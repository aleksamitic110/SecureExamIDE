using Web.Api.Common;

namespace Web.Api.Features.Users;

public sealed record EmailVerificationCodeIssuedDomainEvent(Guid UserId) : IDomainEvent;
