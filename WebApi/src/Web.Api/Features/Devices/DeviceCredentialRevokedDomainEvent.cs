using Web.Api.Common;

namespace Web.Api.Features.Devices;

public sealed record DeviceCredentialRevokedDomainEvent(Guid DeviceCredentialId, Guid UserId) : IDomainEvent;
