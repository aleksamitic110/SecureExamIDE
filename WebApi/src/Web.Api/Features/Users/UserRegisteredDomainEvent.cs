using Web.Api.Common;

namespace Web.Api.Features.Users;

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email) : IDomainEvent;
