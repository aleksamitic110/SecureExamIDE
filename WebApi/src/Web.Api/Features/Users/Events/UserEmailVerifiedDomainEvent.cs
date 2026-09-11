using Web.Api.Common;

namespace Web.Api.Features.Users;

public sealed record UserEmailVerifiedDomainEvent(Guid UserId) : IDomainEvent;
