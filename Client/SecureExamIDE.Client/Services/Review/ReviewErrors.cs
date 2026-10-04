using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.Services.Review;

internal static class ReviewErrors
{
    // The same message for a wrong code and for a package this version cannot read would be unkind;
    // these are the three the review screen can actually hit.
    public static readonly ApiError WrongCode = new(
        0,
        "Review.WrongCode",
        "This code does not belong to this sitting. The code is the one shown when the sitting was scheduled.",
        []);

    public static readonly ApiError UnsupportedPackage = new(
        0,
        "Review.UnsupportedPackage",
        "This sitting's package was made by a newer version of SecureExamIDE. Update the application.",
        []);

    public static readonly ApiError CannotOpen = new(
        0,
        "Review.CannotOpen",
        "This submission could not be opened with the sitting's code. It may have been handed in by an older version of the application.",
        []);

    public static ApiError CannotExport(string detail) => new(
        0,
        "Review.CannotExport",
        $"The files could not be saved: {detail}",
        []);
}
