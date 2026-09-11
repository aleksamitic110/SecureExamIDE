using System.Net;
using System.Net.Http.Json;

namespace IntegrationTests.Users;

// A professor can find any account by its e-mail address; nobody else can look anyone up.
public sealed class UserLookupTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record FoundUser(
        Guid Id,
        string Email,
        string FirstName,
        string LastName,
        string Role,
        string? IndexNumber,
        bool IsEmailVerified);

    private async Task AuthenticateAsProfessorAsync()
    {
        (Guid _, AccessTokens tokens) = await RegisterAndLoginProfessorAsync();
        Authenticate(tokens.AccessToken);
    }

    private async Task<HttpResponseMessage> LookUpAsync(string email) =>
        await HttpClient.GetAsync($"users?email={Uri.EscapeDataString(email)}");

    [Fact]
    public async Task AProfessor_Should_FindAStudentByEmail()
    {
        // Arrange
        string email = UniqueEmail();
        string indexNumber = UniqueIndexNumber();
        Registration student = await RegisterStudentAsync(email, indexNumber);

        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await LookUpAsync(email.ToUpperInvariant());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        FoundUser found = (await response.Content.ReadFromJsonAsync<FoundUser>())!;
        found.Id.ShouldBe(student.UserId);
        found.Email.ShouldBe(email);
        found.Role.ShouldBe("Student");
        found.IndexNumber.ShouldBe(indexNumber);
        found.IsEmailVerified.ShouldBeTrue();
    }

    // Useful exactly when a student says they cannot log in.
    [Fact]
    public async Task AProfessor_Should_SeeThatAStudentHasNotVerifiedTheirAddress()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email, verifyEmail: false);

        await AuthenticateAsProfessorAsync();

        // Act
        FoundUser found = (await (await LookUpAsync(email)).Content.ReadFromJsonAsync<FoundUser>())!;

        // Assert
        found.IsEmailVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task Lookup_Should_ReturnNotFound_ForAnUnknownAddress()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await LookUpAsync(UniqueEmail());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Looking an address up answers whether it is registered, so students cannot do it at all.
    [Fact]
    public async Task AStudent_Should_BeForbiddenFromLookingAnyoneUp()
    {
        // Arrange
        string otherEmail = UniqueEmail();
        await RegisterStudentAsync(otherEmail);

        (Guid _, AccessTokens tokens) = await RegisterAndLoginAsync();
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await LookUpAsync(otherEmail);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lookup_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Act
        HttpResponseMessage response = await LookUpAsync(UniqueEmail());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lookup_Should_ReturnBadRequest_WhenNoAddressIsGiven()
    {
        // Arrange
        await AuthenticateAsProfessorAsync();

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync("users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
