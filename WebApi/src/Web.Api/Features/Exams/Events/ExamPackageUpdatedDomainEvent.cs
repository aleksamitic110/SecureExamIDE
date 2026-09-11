using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed record ExamPackageUpdatedDomainEvent(Guid ExamPackageId) : IDomainEvent;
