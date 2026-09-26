namespace SecureExamIDE.Client.Services.Api;

// Where an exam stands. Sent and received as text, the way the API serialises it, and the filter on
// the professor's own list uses these exact names - the API is case-sensitive about them, so
// "draft" is rejected as an unknown value rather than quietly returning everything.
public enum ExamStatus
{
    // Being assembled by its owner. The only state in which it can still be changed.
    Draft,

    // In the student catalog and downloadable. Nothing about it can be edited any more.
    Published,

    // Retired; kept for the record but no longer offered.
    Archived
}
