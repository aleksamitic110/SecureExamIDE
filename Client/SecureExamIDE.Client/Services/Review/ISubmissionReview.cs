using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Review;

// The professor's side of a sitting: who handed in, and opening what they handed in with the sitting's
// one-time code.
//
// The code is asked for **once per sitting** and kept only while the review screen is open - a
// professor marking thirty students should not retype twenty characters thirty times, and nothing is
// written to disk, so closing the screen forgets it.
public interface ISubmissionReview
{
    Task<ApiResult<PagedList<SubmissionSummary>>> GetSubmissionsAsync(
        Guid sittingId,
        int page,
        CancellationToken cancellationToken = default);

    // Fetches the sitting's package header and derives the hand-in key from the code. Called once,
    // before the first submission is opened; the returned key belongs to the caller, which wipes it.
    Task<ApiResult<byte[]>> DeriveHandInKeyAsync(
        Guid sittingId,
        string typedCode,
        CancellationToken cancellationToken = default);

    // Downloads the sealed solution and the log, checks both against the digests the server recorded,
    // and opens them with the key.
    Task<ApiResult<OpenedSubmission>> OpenAsync(
        Guid sittingId,
        SubmissionSummary submission,
        byte[] handInKey,
        CancellationToken cancellationToken = default);
}
