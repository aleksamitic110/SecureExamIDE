using Web.Api.Common;

namespace Web.Api.Features.Exams;

public static class ExamErrors
{
    public static Error NotFound(Guid examId) => Error.NotFound(
        "Exams.NotFound",
        $"The exam with the Id = '{examId}' was not found");

    public static Error NotDraft(ExamPackageStatus status) => Error.Conflict(
        "Exams.NotDraft",
        $"Files can only be added while the exam is a draft, but this exam is '{status}'");

    // Raised when a caller commits an object key whose bytes are not in storage. This is the check
    // that makes the two-phase upload meaningful: without it the database could name an object
    // that was never written.
    public static readonly Error ContentNotUploaded = Error.Problem(
        "Exams.ContentNotUploaded",
        "No uploaded content was found for the provided object key");

    // Raised when an object key does not belong to the exam being written to - the guard that stops
    // one exam from claiming another exam's stored content.
    public static readonly Error ObjectKeyNotOwnedByExam = Error.Forbidden(
        "Exams.ObjectKeyNotOwnedByExam",
        "The provided object key does not belong to this exam");

    public static readonly Error ContentAlreadyCommitted = Error.Conflict(
        "Exams.ContentAlreadyCommitted",
        "The provided object key has already been recorded against this exam");

    public static readonly Error DependencyAlreadyAdded = Error.Conflict(
        "Exams.DependencyAlreadyAdded",
        "This exam already has a dependency with the same name and version");

    // Publishing is what makes an exam visible to students, so it is refused unless there is
    // something for them to download.
    public static readonly Error NoFiles = Error.Conflict(
        "Exams.NoFiles",
        "An exam must contain at least one file before it can be published");

    public static Error CannotPublish(ExamPackageStatus status) => Error.Conflict(
        "Exams.CannotPublish",
        $"Only a draft exam can be published, but this exam is '{status}'");

    public static Error DependencyTooLarge(long sizeBytes, long maxBytes) => Error.Problem(
        "Exams.DependencyTooLarge",
        $"The uploaded dependency is {sizeBytes} bytes, which exceeds the {maxBytes} byte limit");

    public static readonly Error EmptyContent = Error.Problem(
        "Exams.EmptyContent",
        "The uploaded content is empty");
}
