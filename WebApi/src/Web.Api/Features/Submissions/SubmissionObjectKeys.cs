using System.Globalization;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Submissions;

// Keys are minted here and nowhere else. The student id is part of the path, so the prefix check
// at commit time proves the object was uploaded by the same student who is now claiming it - one
// student cannot submit another's work, even by guessing a key.
public static class SubmissionObjectKeys
{
    public static string PrefixFor(Guid sessionId, Guid studentId) =>
        string.Create(CultureInfo.InvariantCulture, $"sessions/{sessionId}/submissions/{studentId}/");

    public static ObjectKey NewSolutionKey(Guid sessionId, Guid studentId) =>
        ObjectKey.Create(string.Create(
            CultureInfo.InvariantCulture,
            $"{PrefixFor(sessionId, studentId)}{Guid.NewGuid()}")).Value;

    public static bool BelongsTo(ObjectKey objectKey, Guid sessionId, Guid studentId) =>
        objectKey.Value.StartsWith(PrefixFor(sessionId, studentId), StringComparison.Ordinal);
}
