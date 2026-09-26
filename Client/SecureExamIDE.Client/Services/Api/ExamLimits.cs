namespace SecureExamIDE.Client.Services.Api;

// The lengths the API's value objects enforce on an exam. Mirrored here so a form can say what is
// allowed and refuse to send a request that cannot succeed; the server stays the authority, and
// whatever it says when it refuses is what the screen shows.
public static class ExamLimits
{
    public const int TitleMaxLength = 200;

    public const int DescriptionMaxLength = 2000;

    public const int SubjectMaxLength = 200;

    public const int FileNameMaxLength = 255;

    public const int DependencyNameMaxLength = 200;

    public const int DependencyVersionMaxLength = 50;

    // The server trims before it measures, so a field of nothing but spaces is empty to it.
    public static bool IsWithin(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;
}
