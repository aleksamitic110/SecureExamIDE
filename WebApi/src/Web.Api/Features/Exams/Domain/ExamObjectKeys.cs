using System.Globalization;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Exams;

// Object keys are minted here and nowhere else. A caller never supplies one, so it cannot traverse
// into another exam's content or overwrite an object it does not own; a committed key is checked
// against the prefix of the exam and of the kind of content being written.
public static class ExamObjectKeys
{
    public static string FilePrefixFor(Guid examId) =>
        string.Create(CultureInfo.InvariantCulture, $"exams/{examId}/files/");

    public static string DependencyPrefixFor(Guid examId) =>
        string.Create(CultureInfo.InvariantCulture, $"exams/{examId}/dependencies/");

    public static ObjectKey NewFileKey(Guid examId) => NewKey(FilePrefixFor(examId));

    public static ObjectKey NewDependencyKey(Guid examId) => NewKey(DependencyPrefixFor(examId));

    // Files and dependencies are kept apart deliberately: an uploaded dependency must not be
    // committable as an exam file, or a student could be handed a task disguised as a compiler.
    public static bool IsFileOf(ObjectKey objectKey, Guid examId) =>
        objectKey.Value.StartsWith(FilePrefixFor(examId), StringComparison.Ordinal);

    public static bool IsDependencyOf(ObjectKey objectKey, Guid examId) =>
        objectKey.Value.StartsWith(DependencyPrefixFor(examId), StringComparison.Ordinal);

    // A session's two objects are named rather than randomised: the session id already makes the
    // path unique, and fixed names let a client find the pair without a second lookup.
    public static ObjectKey PackageKeyFor(Guid examId, Guid sessionId) =>
        ObjectKey.Create(string.Create(
            CultureInfo.InvariantCulture,
            $"exams/{examId}/sessions/{sessionId}/package.bin")).Value;

    public static ObjectKey HeaderKeyFor(Guid examId, Guid sessionId) =>
        ObjectKey.Create(string.Create(
            CultureInfo.InvariantCulture,
            $"exams/{examId}/sessions/{sessionId}/package.hdr")).Value;

    private static ObjectKey NewKey(string prefix) =>
        ObjectKey.Create(string.Create(CultureInfo.InvariantCulture, $"{prefix}{Guid.NewGuid()}")).Value;
}
