using Web.Api.Common;

namespace Web.Api.Features.Exams;

public sealed class ExamPackage : Entity
{
    public Guid Id { get; set; }
    public ExamTitle Title { get; set; }
    public ExamDescription Description { get; set; }
    public ExamSubject Subject { get; set; }

    // The professor who owns this exam. Only they may change it.
    public Guid OwnerProfessorId { get; set; }

    public ExamPackageStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    // Null until the exam is published; set once and never cleared, since a published exam is
    // never returned to draft.
    public DateTime? PublishedAt { get; set; }
}
