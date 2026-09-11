using Web.Api.Common;

namespace Web.Api.Features.Devices;

public sealed record DeviceCredentialIssuedDomainEvent(Guid DeviceCredentialId, Guid UserId) : IDomainEvent;
