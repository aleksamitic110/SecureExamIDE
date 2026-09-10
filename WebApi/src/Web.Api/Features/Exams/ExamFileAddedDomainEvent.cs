using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamFileAddedDomainEvent(Guid ExamFileId, Guid ExamPackageId) : IDomainEvent;
