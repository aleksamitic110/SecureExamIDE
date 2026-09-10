using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Submissions;

// A student's finished work for one sitting. The bytes are whatever the client produced - the
// design has the client encrypt them locally so the student cannot alter the solution after
// finishing - and the API treats them as opaque: it stores them, measures them, and never looks
// inside.
//
// There is deliberately one row per student per sitting. "The student cannot alter it afterwards"
// is the property the whole exam mode exists to provide, so a second submission is refused rather
// than allowed to replace the first.
public sealed class Submission : Entity
{
    public Guid Id { get; set; }
    public Guid ExamSessionId { get; set; }
    public Guid StudentId { get; set; }

    // Which bound machine sent it. Submitting requires a device token, so this is always known,
    // and it is the evidence tying a solution to the laptop that sat the exam.
    public Guid DeviceCredentialId { get; set; }

    public ObjectKey SolutionObjectKey { get; set; }
    public long SizeBytes { get; set; }

    // Kept, unlike a dependency's: this is exam material, and the digest is the server's own
    // measurement of what it received.
    public Sha256Hash Sha256 { get; set; }

    // Server time, not the client's. A solution may arrive long after the sitting ended - the
    // student works offline and uploads when a connection returns - so when it landed is a fact
    // worth recording rather than inferring.
    public DateTime SubmittedAt { get; set; }
}
