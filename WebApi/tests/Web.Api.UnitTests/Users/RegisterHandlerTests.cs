using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.Features.Users;
using Web.Api.Notifications;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Users;

public sealed class RegisterHandlerTests : BaseHandlerTest
{
    private const string ProfessorCode = "professor-code";
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static Register.Command StudentCommand =>
        new("student@example.com", "Test", "Student", "Password123", Role.Student, "19252", null, "Laptop");

    private static Register.Command ProfessorCommand =>
        new("professor@example.com", "Test", "Professor", "Password123", Role.Professor, null, ProfessorCode, "Office PC");

    private static Register.Handler CreateHandler(
        ApplicationDbContext context,
        IPasswordHasher? passwordHasher = null,
        IDeviceCredentialProvider? deviceCredentialProvider = null,
        string professorCode = ProfessorCode,
        IEmailSender? emailSender = null)
    {
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        return new Register.Handler(
            context,
            passwordHasher ?? Substitute.For<IPasswordHasher>(),
            deviceCredentialProvider ?? Substitute.For<IDeviceCredentialProvider>(),
            dateTimeProvider,
            Options.Create(new RegistrationOptions { ProfessorRegistrationCode = professorCode }),
            Options.Create(new EmailVerificationOptions()),
            emailSender ?? Substitute.For<IEmailSender>());
    }

    // The account starts unverified, the code goes to the address it was registered with, and
    // only a hash of it is stored.
    [Fact]
    public async Task Handle_Should_CreateAnUnverifiedAccountAndMailItACode()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        IEmailSender emailSender = Substitute.For<IEmailSender>();
        string? body = null;
        emailSender
            .When(s => s.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => body = call.ArgAt<string>(2));

        Register.Handler handler = CreateHandler(context, emailSender: emailSender);

        // Act
        Result<Register.Response> result = await handler.Handle(StudentCommand, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        User user = await context.Users.SingleAsync();
        user.EmailVerifiedAt.ShouldBeNull();

        await emailSender.Received(1).SendAsync(
            StudentCommand.Email, EmailVerificationCodes.Subject, Arg.Any<string>(), Arg.Any<CancellationToken>());

        string code = Regex.Match(body!, @"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;
        code.Length.ShouldBe(6);

        EmailVerificationCode stored = await context.EmailVerificationCodes.SingleAsync();
        stored.UserId.ShouldBe(user.Id);
        stored.CodeHash.ShouldBe(EmailVerificationCodes.Hash(user.Id, code));
        stored.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        stored.FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_MailNothing_WhenRegistrationFails()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        IEmailSender emailSender = Substitute.For<IEmailSender>();
        Register.Handler handler = CreateHandler(context, emailSender: emailSender);

        Register.Command command = ProfessorCommand with { ProfessorRegistrationCode = "wrong-code" };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
        (await context.EmailVerificationCodes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenEmailIsNotUnique()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = StudentCommand.Email.AsEmail(),
            FirstName = "Existing".AsPersonName(),
            LastName = "User".AsPersonName(),
            PasswordHash = "hash",
            Role = Role.Student,
            IndexNumber = "10000".AsIndexNumber(),
            IsActive = true
        });
        await context.SaveChangesAsync();

        Register.Handler handler = CreateHandler(context);

        // Act
        Result<Register.Response> result = await handler.Handle(StudentCommand, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.EmailNotUnique);
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenIndexNumberIsAlreadyRegistered()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "other@example.com".AsEmail(),
            FirstName = "Existing".AsPersonName(),
            LastName = "Student".AsPersonName(),
            PasswordHash = "hash",
            Role = Role.Student,
            IndexNumber = StudentCommand.IndexNumber!.AsIndexNumber(),
            IsActive = true
        });
        await context.SaveChangesAsync();

        Register.Handler handler = CreateHandler(context);

        // Act
        Result<Register.Response> result = await handler.Handle(StudentCommand, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.IndexNumberNotUnique);
    }

    [Fact]
    public async Task Handle_Should_ReturnProblem_WhenProfessorRegistrationCodeIsWrong()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Register.Handler handler = CreateHandler(context);

        Register.Command command = ProfessorCommand with { ProfessorRegistrationCode = "wrong-code" };

        // Act
        Result<Register.Response> result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidProfessorRegistrationCode);
        (await context.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_ReturnProblem_WhenProfessorRegistrationCodeIsNotConfigured()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Register.Handler handler = CreateHandler(context, professorCode: string.Empty);

        Register.Command command = ProfessorCommand with { ProfessorRegistrationCode = string.Empty };

        // Act
        Result<Register.Response> result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidProfessorRegistrationCode);
    }

    [Fact]
    public async Task Handle_Should_CreateActiveStudentWithIndexNumberAndDispatchDomainEvent_WhenValid()
    {
        // Arrange
        IDomainEventsDispatcher dispatcher = Substitute.For<IDomainEventsDispatcher>();
        await using ApplicationDbContext context = CreateDbContext(dispatcher);

        IPasswordHasher passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Hash(StudentCommand.Password).Returns("hashed-password");

        IDeviceCredentialProvider deviceCredentialProvider = Substitute.For<IDeviceCredentialProvider>();
        deviceCredentialProvider.Generate().Returns("device-secret");
        deviceCredentialProvider.Hash("device-secret").Returns("device-secret-hash");

        Register.Handler handler = CreateHandler(context, passwordHasher, deviceCredentialProvider);

        // Act
        Result<Register.Response> result = await handler.Handle(StudentCommand, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        User user = await context.Users.SingleAsync(u => u.Id == result.Value.UserId);
        user.Email!.Value.ShouldBe(StudentCommand.Email);
        user.PasswordHash.ShouldBe("hashed-password");
        user.Role.ShouldBe(Role.Student);
        user.IndexNumber!.Value.ShouldBe(StudentCommand.IndexNumber);
        user.IsActive.ShouldBeTrue();
        await dispatcher.Received().DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.Any(e => e is UserRegisteredDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_BindTheRegisteringMachine_WhenValid()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();

        IDeviceCredentialProvider deviceCredentialProvider = Substitute.For<IDeviceCredentialProvider>();
        deviceCredentialProvider.Generate().Returns("device-secret");
        deviceCredentialProvider.Hash("device-secret").Returns("device-secret-hash");

        Register.Handler handler = CreateHandler(context, deviceCredentialProvider: deviceCredentialProvider);

        // Act
        Result<Register.Response> result = await handler.Handle(StudentCommand, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        // The plaintext secret is returned exactly once and only the hash is persisted.
        result.Value.DeviceCredential.ShouldBe("device-secret");

        DeviceCredential device = await context.DeviceCredentials.SingleAsync();
        device.Id.ShouldBe(result.Value.DeviceId);
        device.UserId.ShouldBe(result.Value.UserId);
        device.DeviceName!.Value.ShouldBe(StudentCommand.DeviceName);
        device.SecretHash.ShouldBe("device-secret-hash");
        device.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_Should_NotBindAMachine_WhenRegistrationFails()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Register.Handler handler = CreateHandler(context);

        Register.Command command = ProfessorCommand with { ProfessorRegistrationCode = "wrong-code" };

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        (await context.DeviceCredentials.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_CreateProfessorWithoutIndexNumber_WhenRegistrationCodeMatches()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        Register.Handler handler = CreateHandler(context);

        // Act
        Result<Register.Response> result = await handler.Handle(ProfessorCommand, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        User user = await context.Users.SingleAsync(u => u.Id == result.Value.UserId);
        user.Role.ShouldBe(Role.Professor);
        user.IndexNumber.ShouldBeNull();
        user.IsActive.ShouldBeTrue();
    }
}
