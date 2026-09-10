using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Users;

public static class Refresh
{
    public sealed record Command(string RefreshToken) : ICommand<AccessTokensResponse>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RefreshToken).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        ITokenProvider tokenProvider,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command, AccessTokensResponse>
    {
        public async Task<Result<AccessTokensResponse>> Handle(Command command, CancellationToken cancellationToken)
        {
            RefreshToken? refreshToken = await context.RefreshTokens
                .Include(rt => rt.User)
                .SingleOrDefaultAsync(rt => rt.Token == command.RefreshToken, cancellationToken);

            if (refreshToken is null || refreshToken.ExpiresOnUtc < dateTimeProvider.UtcNow)
            {
                return Result.Failure<AccessTokensResponse>(UserErrors.InvalidRefreshToken);
            }

            string accessToken = tokenProvider.Create(refreshToken.User);
            string newRefreshToken = tokenProvider.GenerateRefreshToken();

            // Rotate the refresh token so a stolen token can only be used once.
            refreshToken.Token = newRefreshToken;
            refreshToken.ExpiresOnUtc = dateTimeProvider.UtcNow.AddDays(RefreshTokenExpirationInDays);

            await context.SaveChangesAsync(cancellationToken);

            return new AccessTokensResponse(accessToken, newRefreshToken);
        }

        private const int RefreshTokenExpirationInDays = 7;
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string RefreshToken);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("users/refresh-token", async (
                Request request,
                ICommandHandler<Command, AccessTokensResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(request.RefreshToken);

                Result<AccessTokensResponse> result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .WithTags(Tags.Users)
            .RequireRateLimiting(RateLimitingPolicies.Authentication);
        }
    }
}
