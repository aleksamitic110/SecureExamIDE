using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamDependencyAddedDomainEvent(Guid ExamDependencyId, Guid ExamPackageId) : IDomainEvent;
