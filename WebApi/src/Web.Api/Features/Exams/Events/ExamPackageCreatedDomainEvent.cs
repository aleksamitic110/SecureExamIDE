using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamPackageCreatedDomainEvent(Guid ExamPackageId, Guid OwnerProfessorId) : IDomainEvent;
