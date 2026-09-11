using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamPackageDeletedDomainEvent(Guid ExamPackageId, Guid OwnerProfessorId) : IDomainEvent;
