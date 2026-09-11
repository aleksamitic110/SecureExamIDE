using Microsoft.EntityFrameworkCore;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;

namespace Web.Api.Features.Users;

// Lets a professor find an account by its e-mail address - to check that a student has
// registered and verified before a sitting, or to put a name to an address. Professors only
// (Permissions.UsersLookup): a lookup by address inevitably answers whether that address is
// registered, and professor accounts are themselves gated by the registration code. A user who
// wants their own details uses GetUserById.
public static class GetUserByEmail
{
    public sealed record Query(string Email) : IQuery<Response>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Email { get; init; }

        public string FirstName { get; init; }

        public string LastName { get; init; }

        public string Role { get; init; }

        // Students only; null for a professor.
        public string? IndexNumber { get; init; }

        // Whether the account can be used yet - the first thing to check when a student says
        // they cannot log in.
        public bool IsEmailVerified { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            Result<Email> emailResult = Email.Create(query.Email);

            if (emailResult.IsFailure)
            {
                return Result.Failure<Response>(emailResult.Error);
            }

            // Email normalises on construction, so the lookup does not depend on the casing the
            // professor typed.
            Email email = emailResult.Value;

            Response? user = await context.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .Select(u => new Response
                {
                    Id = u.Id,
                    Email = u.Email.Value,
                    FirstName = u.FirstName.Value,
                    LastName = u.LastName.Value,
                    Role = u.Role.ToString(),
                    IndexNumber = u.IndexNumber == null ? null : u.IndexNumber.Value,
                    IsEmailVerified = u.EmailVerifiedAt != null
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                return Result.Failure<Response>(UserErrors.NotFoundByEmail);
            }

            return user;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            // A missing email parameter is rejected by the framework with 400 before this runs.
            app.MapGet("users", async (
                string email,
                IQueryHandler<Query, Response> handler,
                CancellationToken cancellationToken) =>
            {
                Result<Response> result = await handler.Handle(new Query(email), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.UsersLookup)
            .WithTags(Tags.Users);
        }
    }
}
