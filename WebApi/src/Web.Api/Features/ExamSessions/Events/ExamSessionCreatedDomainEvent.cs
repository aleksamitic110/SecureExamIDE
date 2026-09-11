using Web.Api.Common;

namespace Web.Api.Features.ExamSessions;

public sealed record ExamSessionCreatedDomainEvent(Guid ExamSessionId, Guid ExamPackageId) : IDomainEvent;
