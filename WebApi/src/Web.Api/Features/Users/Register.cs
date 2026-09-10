using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.Devices;
using Web.Api.Notifications;

namespace Web.Api.Features.Users;

public static class Register
{
    public sealed record Command(
        string Email,
        string FirstName,
        string LastName,
        string Password,
        Role Role,
        string? IndexNumber,
        string? ProfessorRegistrationCode,
        string DeviceName) : ICommand<Response>;

    // DeviceCredential is the plaintext device secret. It is returned here once and
    // never again - the client must place it in OS secure storage immediately.
    public sealed record Response(Guid UserId, Guid DeviceId, string DeviceCredential);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            // Each rule defers to its value object, so the definition of a valid name, address or
            // index number exists in exactly one place and cannot drift from what the domain accepts.
            RuleFor(c => c.FirstName)
                .Must(v => PersonName.Create(v).IsSuccess)
                .WithMessage($"First name is required and must not exceed {PersonName.MaxLength} characters.");

            RuleFor(c => c.LastName)
                .Must(v => PersonName.Create(v).IsSuccess)
                .WithMessage($"Last name is required and must not exceed {PersonName.MaxLength} characters.");

            // No faculty-domain restriction yet - any syntactically valid address is accepted.
            RuleFor(c => c.Email)
                .Must(v => Email.Create(v).IsSuccess)
                .WithMessage("Email is required and must be a valid address.");

            RuleFor(c => c.Password).NotEmpty().MinimumLength(PasswordMinLength);
            RuleFor(c => c.Role).IsInEnum();

            RuleFor(c => c.DeviceName)
                .Must(v => DeviceName.Create(v).IsSuccess)
                .WithMessage($"Device name is required and must not exceed {DeviceName.MaxLength} characters.");

            When(c => c.Role == Role.Student, () =>
            {
                RuleFor(c => c.IndexNumber)
                    .Must(v => IndexNumber.Create(v).IsSuccess)
                    .WithMessage("Index number must consist of 4 to 6 digits.");

                RuleFor(c => c.ProfessorRegistrationCode)
                    .Empty()
                    .WithMessage("A professor registration code must not be supplied for a student.");
            });

            When(c => c.Role == Role.Professor, () =>
            {
                RuleFor(c => c.IndexNumber)
                    .Empty()
                    .WithMessage("A professor must not supply an index number.");

                RuleFor(c => c.ProfessorRegistrationCode)
                    .NotEmpty()
                    .WithMessage("A professor registration code is required to register as a professor.");
            });
        }

        private const int PasswordMinLength = 8;
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IDeviceCredentialProvider deviceCredentialProvider,
        IDateTimeProvider dateTimeProvider,
        IOptions<RegistrationOptions> registrationOptions) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.Role == Role.Professor &&
                !IsProfessorRegistrationCodeValid(command.ProfessorRegistrationCode))
            {
                return Result.Failure<Response>(UserErrors.InvalidProfessorRegistrationCode);
            }

            // The validator has already checked these shapes, but parsing them here is what makes
            // the invariant structural: a User simply cannot be built from an invalid value, on any
            // path in, including ones that never pass through an HTTP request.
            Result<Email> emailResult = Email.Create(command.Email);

            if (emailResult.IsFailure)
            {
                return Result.Failure<Response>(emailResult.Error);
            }

            Result<PersonName> firstNameResult = PersonName.Create(command.FirstName);

            if (firstNameResult.IsFailure)
            {
                return Result.Failure<Response>(firstNameResult.Error);
            }

            Result<PersonName> lastNameResult = PersonName.Create(command.LastName);

            if (lastNameResult.IsFailure)
            {
                return Result.Failure<Response>(lastNameResult.Error);
            }

            Result<DeviceName> deviceNameResult = DeviceName.Create(command.DeviceName);

            if (deviceNameResult.IsFailure)
            {
                return Result.Failure<Response>(deviceNameResult.Error);
            }

            IndexNumber? indexNumber = null;

            if (command.Role == Role.Student)
            {
                Result<IndexNumber> indexNumberResult = IndexNumber.Create(command.IndexNumber);

                if (indexNumberResult.IsFailure)
                {
                    return Result.Failure<Response>(indexNumberResult.Error);
                }

                indexNumber = indexNumberResult.Value;
            }

            Email email = emailResult.Value;

            if (await context.Users.AnyAsync(u => u.Email == email, cancellationToken))
            {
                return Result.Failure<Response>(UserErrors.EmailNotUnique);
            }

            if (indexNumber is not null &&
                await context.Users.AnyAsync(u => u.IndexNumber == indexNumber, cancellationToken))
            {
                return Result.Failure<Response>(UserErrors.IndexNumberNotUnique);
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FirstName = firstNameResult.Value,
                LastName = lastNameResult.Value,
                PasswordHash = passwordHasher.Hash(command.Password),
                Role = command.Role,
                IndexNumber = indexNumber,
                IsActive = true
            };

            // Registration is the one moment the student is guaranteed to be online, so the machine
            // is bound to the account here. From now on the client submits with this credential
            // instead of logging in.
            string secret = deviceCredentialProvider.Generate();

            var deviceCredential = new DeviceCredential
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DeviceName = deviceNameResult.Value,
                SecretHash = deviceCredentialProvider.Hash(secret),
                CreatedAt = dateTimeProvider.UtcNow
            };

            user.Raise(new UserRegisteredDomainEvent(user.Id, user.Email.Value));
            deviceCredential.Raise(new DeviceCredentialIssuedDomainEvent(deviceCredential.Id, user.Id));

            context.Users.Add(user);
            context.DeviceCredentials.Add(deviceCredential);

            await context.SaveChangesAsync(cancellationToken);

            return new Response(user.Id, deviceCredential.Id, secret);
        }

        private bool IsProfessorRegistrationCodeValid(string? providedCode)
        {
            string expectedCode = registrationOptions.Value.ProfessorRegistrationCode;

            // An unconfigured code disables professor registration rather than accepting anything.
            if (string.IsNullOrEmpty(expectedCode) || string.IsNullOrEmpty(providedCode))
            {
                return false;
            }

            // Compared in constant time so that a caller cannot recover the code one character at
            // a time by measuring how long the comparison takes.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedCode),
                Encoding.UTF8.GetBytes(expectedCode));
        }
    }

    internal sealed class UserRegisteredDomainEventHandler(IEmailSender emailSender)
        : IDomainEventHandler<UserRegisteredDomainEvent>
    {
        public async Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            await emailSender.SendAsync(
                domainEvent.Email,
                "Welcome!",
                "Thanks for registering. We're glad to have you on board.",
                cancellationToken);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(
            string Email,
            string FirstName,
            string LastName,
            string Password,
            Role Role,
            string? IndexNumber,
            string? ProfessorRegistrationCode,
            string DeviceName);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("users/register", async (
                Request request,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(
                    request.Email,
                    request.FirstName,
                    request.LastName,
                    request.Password,
                    request.Role,
                    request.IndexNumber,
                    request.ProfessorRegistrationCode,
                    request.DeviceName);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Users)
            .RequireRateLimiting(RateLimitingPolicies.Authentication);
        }
    }
}
