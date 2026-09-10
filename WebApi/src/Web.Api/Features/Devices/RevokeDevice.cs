using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Devices;

// Server-side revocation is the only way to withdraw a device credential, since credentials never
// expire on their own. Revoking is irreversible - a machine that loses its credential must be
// bound again through login.
public static class RevokeDevice
{
    public sealed record Command(Guid DeviceId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.DeviceId).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IDateTimeProvider dateTimeProvider) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            // Filtering on the caller's id in the query means another account's device is reported
            // as missing rather than as forbidden, which keeps device ids from being probed.
            DeviceCredential? device = await context.DeviceCredentials
                .SingleOrDefaultAsync(
                    d => d.Id == command.DeviceId && d.UserId == userContext.UserId,
                    cancellationToken);

            if (device is null)
            {
                return Result.Failure(DeviceErrors.NotFound(command.DeviceId));
            }

            if (device.RevokedAt is not null)
            {
                return Result.Failure(DeviceErrors.AlreadyRevoked);
            }

            device.RevokedAt = dateTimeProvider.UtcNow;

            device.Raise(new DeviceCredentialRevokedDomainEvent(device.Id, device.UserId));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapDelete("devices/{deviceId:guid}", async (
                Guid deviceId,
                ICommandHandler<Command> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(deviceId);

                Result result = await handler.Handle(command, cancellationToken);

                return result.Match(Results.NoContent, CustomResults.Problem);
            })
            .HasPermission(Permissions.DevicesManage)
            .WithTags(Tags.Devices);
        }
    }
}
