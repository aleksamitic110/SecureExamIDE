using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace IntegrationTests.Users;

// A body the framework cannot bind is the caller's mistake. These used to come back as 500
// "Server failure", which told a client nothing and made a typo look like an outage.
public sealed class MalformedRequestTests(IntegrationTestWebAppFactory factory)
    : BaseIntegrationTest(factory)
{
    private sealed record Problem(string Title, int Status, string? Detail);

    private async Task<HttpResponseMessage> PostRawAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        return await HttpClient.PostAsync(path, content);
    }

    // The exact shape that produced a 500: indexNumber is a string in the contract, and a JSON
    // number cannot be bound into one.
    [Fact]
    public async Task Register_Should_ReturnBadRequest_WhenAFieldHasTheWrongJsonType()
    {
        // Act
        HttpResponseMessage response = await PostRawAsync("users/register", """
            {
              "email": "wrong.type@example.com",
              "password": "Password123!",
              "firstName": "Ana",
              "lastName": "Petrovic",
              "role": "Student",
              "indexNumber": 1000,
              "professorRegistrationCode": null,
              "deviceName": "laptop"
            }
            """);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        Problem problem = (await response.Content.ReadFromJsonAsync<Problem>())!;
        problem.Status.ShouldBe(400);
        problem.Title.ShouldNotBe("Server failure");
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_Should_ReturnBadRequest_WhenTheBodyIsNotValidJson()
    {
        // Act
        HttpResponseMessage response = await PostRawAsync("users/register", "{ this is not json");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Should_ReturnBadRequest_WhenTheBodyIsEmpty()
    {
        // Act
        HttpResponseMessage response = await PostRawAsync("users/login", "");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // The same field spelled correctly still has to work - the fix must not have turned a valid
    // request into a rejected one.
    [Fact]
    public async Task Register_Should_Succeed_WhenTheIndexNumberIsAString()
    {
        // Act
        HttpResponseMessage response = await PostRawAsync("users/register", $$"""
            {
              "email": "{{UniqueEmail()}}",
              "password": "Password123!",
              "firstName": "Ana",
              "lastName": "Petrovic",
              "role": "Student",
              "indexNumber": "{{UniqueIndexNumber()}}",
              "professorRegistrationCode": null,
              "deviceName": "laptop"
            }
            """);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
