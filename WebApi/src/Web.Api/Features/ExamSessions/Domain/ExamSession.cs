using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.ExamSessions;

// One sitting of an exam. The package is sealed per session rather than per exam, so a January
// sitting and a September retake of the same exam have different codes and different ciphertext -
// a code that leaks after the first sitting cannot open the second.
public sealed class ExamSession : Entity
{
    public Guid Id { get; set; }
    public Guid ExamPackageId { get; set; }
    public Guid CreatedByProfessorId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    // Cleared when a sitting is called off. The package objects are left in place: a cancelled
    // session must stop being downloadable, not lose its record.
    public bool IsActive { get; set; }

    // SHA-256 of the normalised one-time code. The code itself is shown to the professor once and
    // is never recoverable from here, so the server cannot open a package it sealed.
    public Sha256Hash OneTimeCodeHash { get; set; }

    public ObjectKey PackageObjectKey { get; set; }
    public ObjectKey HeaderObjectKey { get; set; }
    public long PackageSizeBytes { get; set; }
    public Sha256Hash PackageSha256 { get; set; }
    public DateTime CreatedAt { get; set; }
}
