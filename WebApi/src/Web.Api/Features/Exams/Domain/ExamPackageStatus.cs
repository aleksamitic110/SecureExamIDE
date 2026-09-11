namespace Web.Api.Features.Exams;

// Persisted as text, like Role, so the column stays readable
// and reordering the members cannot silently repurpose existing rows.
public enum ExamPackageStatus
{
    // Being assembled by its owner. The only state in which files may be added.
    Draft = 1,

    // Visible in the student catalog and downloadable.
    Published = 2,

    // Retired; kept for the record but no longer offered.
    Archived = 3
}
