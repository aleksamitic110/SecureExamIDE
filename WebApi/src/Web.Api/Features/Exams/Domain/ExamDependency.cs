using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

// A tool or library the student must have locally to work on the exam - the reason no faculty
// network is needed on exam day. The row only names bytes that are already in object storage;
// name and version together identify it within its exam.
//
// Unlike ExamFile there is no SHA-256 here, deliberately. A dependency is a publicly available
// archive rather than exam material, and the bytes never pass through the API - so a digest could
// only be produced by reading the whole object back out of storage, for an integrity claim the
// system does not make about third-party artifacts.
public sealed class ExamDependency : Entity
{
    public Guid Id { get; set; }
    public Guid ExamPackageId { get; set; }
    public DependencyName Name { get; set; }
    public DependencyVersion Version { get; set; }
    public ContentType ContentType { get; set; }
    public ObjectKey ObjectKey { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
