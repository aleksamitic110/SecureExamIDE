using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Authorization;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Devices;

public static class GetDevices
{
    public sealed record Query : IQuery<IReadOnlyCollection<Response>>;

    public sealed record Response
    {
        public Guid Id { get; init; }

        public string DeviceName { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? RevokedAt { get; init; }
    }

    internal sealed class Handler(ApplicationDbContext context, IUserContext userContext)
        : IQueryHandler<Query, IReadOnlyCollection<Response>>
    {
        public async Task<Result<IReadOnlyCollection<Response>>> Handle(
            Query query,
            CancellationToken cancellationToken)
        {
            // Scoped to the caller: a device list is account data, never administrative data.
            List<Response> devices = await context.DeviceCredentials
                .AsNoTracking()
                .Where(d => d.UserId == userContext.UserId)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new Response
                {
                    Id = d.Id,
                    DeviceName = d.DeviceName.Value,
                    CreatedAt = d.CreatedAt,
                    RevokedAt = d.RevokedAt
                })
                .ToListAsync(cancellationToken);

            return devices;
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("devices", async (
                IQueryHandler<Query, IReadOnlyCollection<Response>> handler,
                CancellationToken cancellationToken) =>
            {
                Result<IReadOnlyCollection<Response>> result =
                    await handler.Handle(new Query(), cancellationToken);

                return result.Match(Results.Ok, CustomResults.Problem);
            })
            .HasPermission(Permissions.DevicesManage)
            .WithTags(Tags.Devices);
        }
    }
}
