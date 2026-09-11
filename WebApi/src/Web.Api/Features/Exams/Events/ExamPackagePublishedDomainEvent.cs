using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamPackagePublishedDomainEvent(Guid ExamPackageId, Guid OwnerProfessorId) : IDomainEvent;
