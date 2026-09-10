using System.Net;
using System.Net.Http.Json;

namespace IntegrationTests.Users;

public sealed class UsersTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private sealed record UserResponse(
        Guid Id,
        string Email,
        string FirstName,
        string LastName,
        string Role,
        string? IndexNumber);

    [Fact]
    public async Task Register_Should_ReturnUserId()
    {
        // Act
        Guid userId = (await RegisterStudentAsync(UniqueEmail())).UserId;

        // Assert
        userId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Register_Should_ReturnConflict_WhenIndexNumberIsAlreadyRegistered()
    {
        // Arrange
        string indexNumber = UniqueIndexNumber();
        await RegisterStudentAsync(UniqueEmail(), indexNumber);

        // Act
        HttpResponseMessage response = await RegisterAsync(new
        {
            email = UniqueEmail(),
            firstName = "Another",
            lastName = "Student",
            password = "Password123",
            role = "Student",
            indexNumber,
            deviceName = "Test laptop"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_Should_ReturnBadRequest_WhenStudentHasNoIndexNumber()
    {
        // Act
        HttpResponseMessage response = await RegisterAsync(new
        {
            email = UniqueEmail(),
            firstName = "Test",
            lastName = "Student",
            password = "Password123",
            role = "Student",
            deviceName = "Test laptop"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_Should_ReturnBadRequest_WhenProfessorCodeIsWrong()
    {
        // Act
        HttpResponseMessage response = await RegisterAsync(new
        {
            email = UniqueEmail(),
            firstName = "Test",
            lastName = "Professor",
            password = "Password123",
            role = "Professor",
            professorRegistrationCode = "not-the-configured-code",
            deviceName = "Test office PC"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_Should_CreateProfessor_WhenCodeIsCorrect()
    {
        // Act
        Guid userId = (await RegisterProfessorAsync(UniqueEmail())).UserId;

        // Assert
        userId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Login_Should_ReturnAccessAndRefreshTokens()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email);

        // Act
        AccessTokens tokens = await LoginAsync(email);

        // Assert
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
    }

    // Before Email became a value object, registration stored the address exactly as typed and
    // login matched it exactly, so differing casing locked the student out of their own account.
    // Normalising inside the value object is what makes this pass.
    [Fact]
    public async Task Login_Should_Succeed_WhenCasingDiffersFromRegistration()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email.ToUpperInvariant());

        // Act
        AccessTokens tokens = await LoginAsync(email);

        // Assert
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_Should_ReturnConflict_WhenEmailDiffersOnlyByCasing()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email);

        // Act
        HttpResponseMessage response = await RegisterAsync(new
        {
            email = email.ToUpperInvariant(),
            firstName = "Test",
            lastName = "Student",
            password = "Password123",
            role = "Student",
            indexNumber = UniqueIndexNumber(),
            deviceName = "Test laptop"
        });

        // Assert - the same person, so the unique index must see the same value.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_Should_ReturnProblem_WhenPasswordIsInvalid()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email);

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/login",
            new { email, password = "WrongPassword1" });

        // Assert
        response.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task RefreshToken_Should_ReturnNewTokens()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email);
        AccessTokens tokens = await LoginAsync(email);

        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/refresh-token",
            new { refreshToken = tokens.RefreshToken });

        // Assert
        response.EnsureSuccessStatusCode();
        AccessTokens? rotated = await response.Content.ReadFromJsonAsync<AccessTokens>();
        rotated!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        rotated.RefreshToken.ShouldNotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_Should_ReturnProblem_WhenTokenIsInvalid()
    {
        // Act
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/refresh-token",
            new { refreshToken = "this-token-does-not-exist" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetUserById_Should_ReturnRoleAndIndexNumber_ForStudent()
    {
        // Arrange
        string email = UniqueEmail();
        string indexNumber = UniqueIndexNumber();
        Guid userId = (await RegisterStudentAsync(email, indexNumber)).UserId;
        AccessTokens tokens = await LoginAsync(email);
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"users/{userId}");

        // Assert
        response.EnsureSuccessStatusCode();
        UserResponse? user = await response.Content.ReadFromJsonAsync<UserResponse>();
        user!.Role.ShouldBe("Student");
        user.IndexNumber.ShouldBe(indexNumber);
    }

    [Fact]
    public async Task GetUserById_Should_ReturnNoIndexNumber_ForProfessor()
    {
        // Arrange
        string email = UniqueEmail();
        Guid userId = (await RegisterProfessorAsync(email)).UserId;
        AccessTokens tokens = await LoginAsync(email);
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"users/{userId}");

        // Assert
        response.EnsureSuccessStatusCode();
        UserResponse? user = await response.Content.ReadFromJsonAsync<UserResponse>();
        user!.Role.ShouldBe("Professor");
        user.IndexNumber.ShouldBeNull();
    }

    [Fact]
    public async Task GetUserById_Should_ReturnForbidden_WhenReadingAnotherUser()
    {
        // Arrange
        Guid otherUserId = (await RegisterStudentAsync(UniqueEmail())).UserId;

        string email = UniqueEmail();
        await RegisterStudentAsync(email);
        AccessTokens tokens = await LoginAsync(email);
        Authenticate(tokens.AccessToken);

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"users/{otherUserId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserById_Should_ReturnUnauthorized_WhenTokenIsMissing()
    {
        // Arrange
        Guid userId = (await RegisterStudentAsync(UniqueEmail())).UserId;

        // Act
        HttpResponseMessage response = await HttpClient.GetAsync($"users/{userId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
