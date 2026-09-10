using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Messaging;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;

namespace Web.Api.Features.Users;

public static class GetUserByEmail
{
    public sealed record Query(string Email) : IQuery<Response>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string Email { get; init; }

        public string FirstName { get; init; }

        public string LastName { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            Result<Email> emailResult = Email.Create(query.Email);

            if (emailResult.IsFailure)
            {
                return Result.Failure<Response>(emailResult.Error);
            }

            Email email = emailResult.Value;

            Response? user = await context.Users
                .Where(u => u.Email == email)
                .Select(u => new Response
                {
                    Id = u.Id,
                    FirstName = u.FirstName.Value,
                    LastName = u.LastName.Value,
                    Email = u.Email.Value
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                return Result.Failure<Response>(UserErrors.NotFoundByEmail);
            }

            if (user.Id != userContext.UserId)
            {
                return Result.Failure<Response>(UserErrors.Forbidden());
            }

            return user;
        }
    }
}
