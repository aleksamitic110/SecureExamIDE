using CommunityToolkit.Mvvm.ComponentModel;
using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.ViewModels.ExamDay;

namespace SecureExamIDE.Client.ViewModels.Home;

// One sitting on the exam screen: when it runs, whether it is still to come, and whether its
// package is already on this computer.
public sealed partial class SittingItemViewModel : ObservableObject
{
    public SittingItemViewModel(ExamSitting sitting, bool isDownloaded, TimeProvider timeProvider)
    {
        Sitting = sitting;
        _isDownloaded = isDownloaded;

        DateTimeOffset now = timeProvider.GetUtcNow();
        IsEnded = now > sitting.EndsAt;

        if (IsEnded)
        {
            Status = "Ended";
        }
        else
        {
            Status = now < sitting.StartsAt ? "Upcoming" : "In progress";
        }

        When = SittingTimes.Describe(sitting.StartsAt, sitting.EndsAt, timeProvider.LocalTimeZone);

        PackageSize = ByteSize.Format(sitting.PackageSizeBytes);
    }

    public ExamSitting Sitting { get; }

    public string When { get; }

    public string Status { get; }

    public string PackageSize { get; }

    // Nothing is gained by preparing for a sitting that is over.
    public bool IsEnded { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(CanEnter))]
    private bool _isDownloaded;

    public bool CanDownload => !IsEnded && !IsDownloaded;

    // Once a sitting is on this computer the exam can be started from here, where the student already
    // is, instead of through the separate list of downloaded exams.
    public bool CanEnter => IsDownloaded;
}
