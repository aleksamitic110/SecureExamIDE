using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;
using Web.Api.Features.Users;

namespace Web.Api.Features.Devices;

// Exchanges a long-lived device credential for a short-lived access token. This is what lets the
// client upload a finished exam without the student ever entering a password.
public static class IssueDeviceToken
{
    public sealed record Command(string DeviceCredential) : ICommand<Response>;

    public sealed record Response(string AccessToken);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.DeviceCredential).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IDeviceCredentialProvider deviceCredentialProvider,
        ITokenProvider tokenProvider) : ICommandHandler<Command, Response>
    {
        public async Task<Result<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            string secretHash = deviceCredentialProvider.Hash(command.DeviceCredential);

            // Matching on the hash means the presented secret is never compared against stored
            // plaintext, and the unique index turns verification into a single indexed lookup.
            Match? match = await context.DeviceCredentials
                .AsNoTracking()
                .Where(d => d.SecretHash == secretHash)
                .Join(
                    context.Users,
                    d => d.UserId,
                    u => u.Id,
                    (d, u) => new Match(d.Id, d.RevokedAt, u))
                .SingleOrDefaultAsync(cancellationToken);

            if (match is null || match.RevokedAt is not null || !match.User.IsActive)
            {
                return Result.Failure<Response>(DeviceErrors.InvalidCredential);
            }

            // Registration hands out the device credential before the address is confirmed, so it
            // is refused here until it is. Only a caller holding a valid, unrevoked secret gets this
            // far, which is why it may be told the real reason.
            if (match.User.EmailVerifiedAt is null)
            {
                return Result.Failure<Response>(UserErrors.EmailNotVerified);
            }

            string accessToken = tokenProvider.CreateForDevice(match.User, match.DeviceCredentialId);

            return new Response(accessToken);
        }

        private sealed record Match(Guid DeviceCredentialId, DateTime? RevokedAt, User User);
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string DeviceCredential);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("auth/device-token", async (
                Request request,
                ICommandHandler<Command, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(request.DeviceCredential);

                Result<Response> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Devices)
            .RequireRateLimiting(RateLimitingPolicies.Authentication);
        }
    }
}
