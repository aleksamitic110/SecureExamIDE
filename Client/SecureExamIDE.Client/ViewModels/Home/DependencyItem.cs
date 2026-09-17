using SecureExamIDE.Client.Formatting;
using SecureExamIDE.Client.Services.Api;

namespace SecureExamIDE.Client.ViewModels.Home;

public sealed class DependencyItem(ExamDependency dependency, bool isDownloaded)
{
    public string Name => $"{dependency.Name} {dependency.Version}";

    public string Size => ByteSize.Format(dependency.SizeBytes);

    public bool IsDownloaded => isDownloaded;

    public string State => isDownloaded ? "Downloaded" : "Not downloaded";
}
