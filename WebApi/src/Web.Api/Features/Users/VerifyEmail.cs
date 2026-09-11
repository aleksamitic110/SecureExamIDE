using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;

namespace Web.Api.Features.Users;

// Confirms an address with the code that was sent to it. No token is needed - an unverified account
// cannot get one - so the caller proves ownership of the address by knowing the code, and every
// failure looks the same from outside.
public static class VerifyEmail
{
    public sealed record Command(string Email, string Code) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .Must(v => Email.Create(v).IsSuccess)
                .WithMessage("Email is required and must be a valid address.");

            RuleFor(c => c.Code)
                .NotEmpty()
                .Matches(@"^\d{6}$")
                .WithMessage("The code is the six digits from the e-mail.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IOptions<EmailVerificationOptions> options) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            Result<Email> emailResult = Email.Create(command.Email);

            if (emailResult.IsFailure)
            {
                return Result.Failure(emailResult.Error);
            }

            Email email = emailResult.Value;

            User? user = await context.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

            EmailVerificationCode? stored = user is null
                ? null
                : await context.EmailVerificationCodes.SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

            DateTime now = dateTimeProvider.UtcNow;

            // An unknown address, an address that is already verified, and a code that has run out
            // of time or of attempts all get the same answer as a wrong code.
            if (user is null ||
                user.EmailVerifiedAt is not null ||
                stored is null ||
                stored.ExpiresAt <= now ||
                stored.FailedAttempts >= options.Value.MaxFailedAttempts)
            {
                return Result.Failure(UserErrors.InvalidVerificationCode);
            }

            if (!EmailVerificationCodes.Matches(stored, command.Code))
            {
                stored.FailedAttempts++;
                await context.SaveChangesAsync(cancellationToken);

                return Result.Failure(UserErrors.InvalidVerificationCode);
            }

            user.EmailVerifiedAt = now;
            context.EmailVerificationCodes.Remove(stored);

            user.Raise(new UserEmailVerifiedDomainEvent(user.Id));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string Email, string Code);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("users/verify-email", async (
                Request request,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                Result result = await handler.Handle(new Command(request.Email, request.Code), cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags(Tags.Users)
            .RequireRateLimiting(RateLimitingPolicies.Authentication);
        }
    }
}
