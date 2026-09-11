using System.Net;
using System.Net.Http.Json;

namespace IntegrationTests.Users;

// A new account is locked until the code mailed to its address comes back: it can neither log in
// nor use the device credential registration gave it. The endpoints that handle codes never reveal
// whether an address is registered.
public sealed class EmailVerificationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private async Task<HttpResponseMessage> LoginRawAsync(string email) =>
        await HttpClient.PostAsJsonAsync("users/login", new { email, password = "Password123", deviceName = "Another laptop" });

    private async Task<HttpResponseMessage> ResendAsync(string email) =>
        await HttpClient.PostAsJsonAsync("users/verify-email/resend", new { email });

    // Any six digits other than the real code.
    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task ANewAccount_Should_BeLockedUntilTheMailedCodeIsEntered()
    {
        // Arrange
        string email = UniqueEmail();
        Registration registration = await RegisterStudentAsync(email, verifyEmail: false);
        string code = LatestVerificationCodeFor(email);

        // Act & Assert - locked: no password login, no device token.
        (await LoginRawAsync(email)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await IssueDeviceTokenAsync(registration.DeviceCredential)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A wrong code does nothing...
        (await VerifyEmailAsync(email, WrongCode(code))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoginRawAsync(email)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ...the mailed one unlocks the account.
        (await VerifyEmailAsync(email, code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginRawAsync(email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IssueDeviceTokenAsync(registration.DeviceCredential)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registration_Should_MailASixDigitCodeToTheAddress()
    {
        // Arrange
        string email = UniqueEmail();

        // Act
        await RegisterStudentAsync(email, verifyEmail: false);

        // Assert
        CapturingEmailSender.SentEmail mail = Mailbox.SentTo(email).ShouldHaveSingleItem();
        mail.Subject.ShouldBe("Your SecureExamIDE verification code");
        LatestVerificationCodeFor(email).Length.ShouldBe(6);
    }

    // The cap on wrong guesses is what protects a six-digit code: once it is reached, even the
    // right code no longer works.
    [Fact]
    public async Task TheCode_Should_StopWorking_AfterFiveWrongGuesses()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email, verifyEmail: false);
        string code = LatestVerificationCodeFor(email);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            (await VerifyEmailAsync(email, WrongCode(code))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Act
        HttpResponseMessage response = await VerifyEmailAsync(email, code);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoginRawAsync(email)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // A new code is the way out of an expired or exhausted one, and it retires the old code.
    [Fact]
    public async Task Resend_Should_MailANewCode_AndRetireTheOldOne()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email, verifyEmail: false);
        string oldCode = LatestVerificationCodeFor(email);

        // Act
        HttpResponseMessage response = await ResendAsync(email);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Mailbox.SentTo(email).Count.ShouldBe(2);

        string newCode = LatestVerificationCodeFor(email);

        if (newCode != oldCode)
        {
            (await VerifyEmailAsync(email, oldCode)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await VerifyEmailAsync(email, newCode)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // Neither endpoint may be used to find out which addresses have accounts.
    [Fact]
    public async Task AnUnknownAddress_Should_GetTheSameAnswersAsAnyOther()
    {
        // Arrange
        string email = UniqueEmail();

        // Act
        HttpResponseMessage resend = await ResendAsync(email);
        HttpResponseMessage verify = await VerifyEmailAsync(email, "123456");

        // Assert
        resend.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        verify.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Mailbox.SentTo(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task Verify_Should_RejectACodeThatIsNotSixDigits()
    {
        // Arrange
        string email = UniqueEmail();
        await RegisterStudentAsync(email, verifyEmail: false);

        // Act
        HttpResponseMessage response = await VerifyEmailAsync(email, "12ab");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
