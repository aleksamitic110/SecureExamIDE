using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamFileRemovedDomainEvent(Guid ExamFileId, Guid ExamPackageId) : IDomainEvent;
