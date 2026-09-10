using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Users;

public static class GetUserById
{
    public sealed record Query(Guid UserId) : IQuery<Response>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Email { get; init; }

        public string FirstName { get; init; }

        public string LastName { get; init; }

        public Role Role { get; init; }

        public string? IndexNumber { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            if (query.UserId != userContext.UserId)
            {
                return Result.Failure<Response>(UserErrors.Forbidden());
            }

            Response? user = await context.Users
                .Where(u => u.Id == query.UserId)
                .Select(u => new Response
                {
                    Id = u.Id,
                    FirstName = u.FirstName.Value,
                    LastName = u.LastName.Value,
                    Email = u.Email.Value,
                    Role = u.Role,
                    IndexNumber = u.IndexNumber == null ? null : u.IndexNumber.Value
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                return Result.Failure<Response>(UserErrors.NotFound(query.UserId));
            }

            return user;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("users/{userId}", async (
                Guid userId,
                IQueryHandler<Query, Response> handler,
                CancellationToken cancellationToken) =>
            {
                var query = new Query(userId);

                Result<Response> result = await handler.Handle(query, cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.UsersRead)
            .WithTags(Tags.Users);
        }
    }
}
