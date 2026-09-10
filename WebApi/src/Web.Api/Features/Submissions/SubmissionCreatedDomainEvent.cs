using Web.Api.Common;

namespace Web.Api.Features.Submissions;

public sealed record SubmissionCreatedDomainEvent(Guid SubmissionId, Guid ExamSessionId, Guid StudentId)
    : IDomainEvent;
