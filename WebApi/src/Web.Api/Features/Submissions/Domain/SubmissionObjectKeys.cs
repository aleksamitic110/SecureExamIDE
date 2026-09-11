using System.Globalization;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Submissions;

// Keys are minted here and nowhere else. The student id is part of the path, so the prefix check
// at commit time proves the object was uploaded by the same student who is now claiming it - one
// student cannot submit another's work, even by guessing a key. The part is in the path too, so a
// solution cannot be committed as the activity log or the other way round.
public static class SubmissionObjectKeys
{
    public static string SolutionPrefixFor(Guid sessionId, Guid studentId) =>
        $"{PrefixFor(sessionId, studentId)}solution/";

    public static string ActivityLogPrefixFor(Guid sessionId, Guid studentId) =>
        $"{PrefixFor(sessionId, studentId)}activity-log/";

    public static ObjectKey NewSolutionKey(Guid sessionId, Guid studentId) =>
        NewKey(SolutionPrefixFor(sessionId, studentId));

    public static ObjectKey NewActivityLogKey(Guid sessionId, Guid studentId) =>
        NewKey(ActivityLogPrefixFor(sessionId, studentId));

    public static bool IsSolutionOf(ObjectKey objectKey, Guid sessionId, Guid studentId) =>
        objectKey.Value.StartsWith(SolutionPrefixFor(sessionId, studentId), StringComparison.Ordinal);

    public static bool IsActivityLogOf(ObjectKey objectKey, Guid sessionId, Guid studentId) =>
        objectKey.Value.StartsWith(ActivityLogPrefixFor(sessionId, studentId), StringComparison.Ordinal);

    private static string PrefixFor(Guid sessionId, Guid studentId) =>
        string.Create(CultureInfo.InvariantCulture, $"sessions/{sessionId}/submissions/{studentId}/");

    private static ObjectKey NewKey(string prefix) =>
        ObjectKey.Create(string.Create(CultureInfo.InvariantCulture, $"{prefix}{Guid.NewGuid()}")).Value;
}
