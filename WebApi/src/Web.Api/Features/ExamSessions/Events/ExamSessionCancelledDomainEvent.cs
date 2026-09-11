using Web.Api.Common;

namespace Web.Api.Features.ExamSessions;

public sealed record ExamSessionCancelledDomainEvent(Guid ExamSessionId, Guid ExamPackageId) : IDomainEvent;
