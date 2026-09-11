using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Common.ValueObjects;
using Web.Api.Database;
using Web.Api.Notifications;

namespace Web.Api.Features.Users;

// Sends a fresh code - for a code that expired, ran out of attempts, or never arrived. It always
// answers 204, whether or not the address is registered or already verified, so it cannot be used
// to find out which addresses have accounts.
public static class ResendVerificationCode
{
    public sealed record Command(string Email) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .Must(v => Email.Create(v).IsSuccess)
                .WithMessage("Email is required and must be a valid address.");
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IEmailSender emailSender,
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
            EmailVerificationOptions settings = options.Value;

            User? user = await context.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user is null || user.EmailVerifiedAt is not null)
            {
                return Result.Success();
            }

            DateTime now = dateTimeProvider.UtcNow;

            EmailVerificationCode? stored = await context.EmailVerificationCodes
                .SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

            // Too soon after the last one: nothing is sent and the code already on its way stays
            // the one to use.
            if (stored is not null && stored.CreatedAt > now.AddSeconds(-settings.ResendCooldownSeconds))
            {
                return Result.Success();
            }

            string code = EmailVerificationCodes.Generate();

            // The single row is overwritten rather than replaced, so a user never has two codes.
            if (stored is null)
            {
                stored = new EmailVerificationCode { Id = Guid.NewGuid(), UserId = user.Id };
                context.EmailVerificationCodes.Add(stored);
            }

            stored.CodeHash = EmailVerificationCodes.Hash(user.Id, code);
            stored.ExpiresAt = now.AddMinutes(settings.CodeLifetimeMinutes);
            stored.FailedAttempts = 0;
            stored.CreatedAt = now;

            user.Raise(new EmailVerificationCodeIssuedDomainEvent(user.Id));

            await context.SaveChangesAsync(cancellationToken);

            await emailSender.SendAsync(
                user.Email.Value,
                EmailVerificationCodes.Subject,
                EmailVerificationCodes.Body(code, settings.CodeLifetimeMinutes),
                cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public sealed record Request(string Email);

        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("users/verify-email/resend", async (
                Request request,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                Result result = await handler.Handle(new Command(request.Email), cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .WithTags(Tags.Users)
            .RequireRateLimiting(RateLimitingPolicies.Authentication);
        }
    }
}
