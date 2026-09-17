using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Unlock;

internal static class UnlockErrors
{
    // A wrong code and an altered wrapped key fail the same GCM check and cannot be told apart.
    public static readonly ApiError WrongCode = new(
        0,
        "Unlock.WrongCode",
        "This code does not unlock this sitting. Check it and try again.",
        []);

    public static readonly ApiError Damaged = new(
        0,
        "Unlock.Damaged",
        "The downloaded exam package is damaged. Download the sitting again.",
        []);

    public static readonly ApiError Missing = new(
        0,
        "Unlock.Missing",
        "This sitting's package is not on this computer. Download it again.",
        []);

    public static readonly ApiError Unsupported = new(
        0,
        "Unlock.Unsupported",
        "This exam package was made by a newer version of SecureExamIDE. Update the application.",
        []);

    public static readonly ApiError IncompleteCode = new(
        0,
        "Unlock.IncompleteCode",
        $"The code has {OneTimeCode.Length} letters and digits. Check that none are missing.",
        []);
}
