using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamDependencyRemovedDomainEvent(Guid ExamDependencyId, Guid ExamPackageId) : IDomainEvent;
