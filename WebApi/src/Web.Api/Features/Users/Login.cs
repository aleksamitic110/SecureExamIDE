using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Features.Devices;

namespace Web.Api.Features.Users;

// Binds an additional machine to an existing account. This is the only reason to log in: normal
// use never requires it, because the client already holds a device credential from registration.
// Each successful login issues a credential for the machine that made the call.
public static class Login
{
    public sealed record Command(string Email, string Password, string DeviceName)
        : ICommand<Response>;

    public sealed record Response(
        string AccessToken,
        string RefreshToken,
        Guid DeviceId,
        string DeviceCredential);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .Must(v => Email.Create(v).IsSuccess)
                .WithMessage("Email is required and must be a valid address.");

            RuleFor(c => c.Password).NotEmpty();

            RuleFor(c => c.DeviceName)
                .Must(v => DeviceName.Create(v).IsSuccess)
                .WithMessage($"Device name is required and must not exceed {DeviceName.MaxLength} characters.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenProvider tokenProvider,
        IDeviceCredentialProvider deviceCredentialProvider,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            Result<Email> emailResult = Email.Create(command.Email);

            if (emailResult.IsFailure)
            {
                return Result.Failure<Response>(emailResult.Error);
            }

            Result<DeviceName> deviceNameResult = DeviceName.Create(command.DeviceName);

            if (deviceNameResult.IsFailure)
            {
                return Result.Failure<Response>(deviceNameResult.Error);
            }

            // Because Email normalises on construction, a student who registered as "A@x.com" is
            // found here when they type "a@x.com" - the match no longer depends on exact casing.
            Email email = emailResult.Value;

            User? user = await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user is null)
            {
                return Result.Failure<Response>(UserErrors.NotFoundByEmail);
            }

            bool verified = passwordHasher.Verify(command.Password, user.PasswordHash);

            if (!verified)
            {
                return Result.Failure<Response>(UserErrors.NotFoundByEmail);
            }

            // Checked only after the password matched, so whether an address is verified is never
            // revealed to someone who does not know its password.
            if (user.EmailVerifiedAt is null)
            {
                return Result.Failure<Response>(UserErrors.EmailNotVerified);
            }

            string accessToken = tokenProvider.Create(user);
            string refreshToken = tokenProvider.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                Token = refreshToken,
                UserId = user.Id,
                ExpiresOnUtc = dateTimeProvider.UtcNow.AddDays(RefreshTokenExpirationInDays)
            };

            string secret = deviceCredentialProvider.Generate();

            var deviceCredential = new DeviceCredential
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DeviceName = deviceNameResult.Value,
                SecretHash = deviceCredentialProvider.Hash(secret),
                CreatedAt = dateTimeProvider.UtcNow
            };

            deviceCredential.Raise(new DeviceCredentialIssuedDomainEvent(deviceCredential.Id, user.Id));

            context.RefreshTokens.Add(refreshTokenEntity);
            context.DeviceCredentials.Add(deviceCredential);

            await context.SaveChangesAsync(cancellationToken);

            return new Response(accessToken, refreshToken, deviceCredential.Id, secret);
        }

        private const int RefreshTokenExpirationInDays = 7;
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string Email, string Password, string DeviceName);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("users/login", async (
                Request request,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(request.Email, request.Password, request.DeviceName);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Users)
            .RequireRateLimiting(RateLimitingPolicies.Authentication);
        }
    }
}
