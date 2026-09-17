using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Downloads;
using SecureExamIDE.Client.Services.Session;

namespace SecureExamIDE.Client.Services.Exams;

internal sealed class ExamDownloadService(
    IApiClient apiClient,
    ISessionService session,
    IExamCatalog catalog,
    IFileDownloader downloader,
    ILocalExamLibrary library,
    TimeProvider timeProvider) : IExamDownloadService
{
    public async Task<ApiResult> DownloadSittingAsync(
        CatalogExam exam,
        ExamSitting sitting,
        IProgress<ExamDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ApiResult<IReadOnlyList<ExamDependency>> dependencies = await catalog.GetDependenciesAsync(exam.Id, cancellationToken);

        if (!dependencies.IsSuccess)
        {
            return dependencies;
        }

        var tracker = new ProgressTracker(
            progress,
            itemCount: 2 + dependencies.Value.Count,
            bytesTotal: sitting.PackageSizeBytes + dependencies.Value.Sum(d => d.SizeBytes));

        ApiResult package = await DownloadPackageAsync(exam, sitting, tracker, cancellationToken);

        if (!package.IsSuccess)
        {
            return package;
        }

        List<DownloadedDependency> downloadedDependencies = [];

        foreach (ExamDependency dependency in dependencies.Value)
        {
            ApiResult<DownloadedDependency> downloaded = await DownloadDependencyAsync(exam, dependency, tracker, cancellationToken);

            if (!downloaded.IsSuccess)
            {
                return downloaded;
            }

            downloadedDependencies.Add(downloaded.Value);
        }

        await RecordAsync(exam, sitting, downloadedDependencies, cancellationToken);

        return ApiResult.Success();
    }

    private async Task<ApiResult> DownloadPackageAsync(
        CatalogExam exam,
        ExamSitting sitting,
        ProgressTracker tracker,
        CancellationToken cancellationToken)
    {
        string packagePath = library.PackagePath(exam.Id, sitting.Id);
        string headerPath = library.HeaderPath(exam.Id, sitting.Id);

        tracker.StartItem("Exam package", sitting.PackageSizeBytes);

        if (File.Exists(packagePath) && File.Exists(headerPath) &&
            await library.LoadAsync(exam.Id, cancellationToken) is { } local &&
            local.Sittings.Any(s => s.SittingId == sitting.Id && s.PackageSha256 == sitting.PackageSha256))
        {
            tracker.CompleteItem();
            tracker.StartItem("Exam package header", 0);
            tracker.CompleteItem();
            return ApiResult.Success();
        }

        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return token;
        }

        // Links are asked for right before use: they expire, and the dependencies before them in a
        // slow download could otherwise outlast them.
        ApiResult<SittingPackage> links = await apiClient.GetSittingPackageAsync(sitting.Id, token.Value, cancellationToken);

        if (!links.IsSuccess)
        {
            return links;
        }

        // The digest is the one the sitting was listed with, so a package swapped in storage after
        // the student saw the listing is refused.
        ApiResult packageFile = await downloader.DownloadAsync(
            new DownloadRequest(links.Value.PackageUrl, packagePath, sitting.PackageSizeBytes, sitting.PackageSha256),
            tracker.ItemProgress,
            cancellationToken);

        if (!packageFile.IsSuccess)
        {
            return packageFile;
        }

        tracker.CompleteItem();
        tracker.StartItem("Exam package header", 0);

        // The header has no recorded size or digest. It needs none: it holds the wrapped key, and
        // AES-GCM refuses to unwrap a header that was altered.
        ApiResult headerFile = await downloader.DownloadAsync(
            new DownloadRequest(links.Value.HeaderUrl, headerPath, null, null),
            null,
            cancellationToken);

        if (headerFile.IsSuccess)
        {
            tracker.CompleteItem();
        }

        return headerFile;
    }

    private async Task<ApiResult<DownloadedDependency>> DownloadDependencyAsync(
        CatalogExam exam,
        ExamDependency dependency,
        ProgressTracker tracker,
        CancellationToken cancellationToken)
    {
        string fileName = library.DependencyFileName(dependency.Id, dependency.ContentType);
        string path = library.DependencyPath(exam.Id, fileName);
        var downloaded = new DownloadedDependency(
            dependency.Id, dependency.Name, dependency.Version, dependency.ContentType, dependency.SizeBytes, fileName);

        tracker.StartItem($"{dependency.Name} {dependency.Version}", dependency.SizeBytes);

        // Dependencies are shared by every sitting of the exam, so a second sitting reuses them.
        // With no digest to go on, the size is the check.
        if (File.Exists(path) && new FileInfo(path).Length == dependency.SizeBytes)
        {
            tracker.CompleteItem();
            return ApiResult.Success(downloaded);
        }

        ApiResult<string> token = await session.GetAccessTokenAsync(cancellationToken);

        if (!token.IsSuccess)
        {
            return ApiResult.Failure<DownloadedDependency>(token.Error);
        }

        ApiResult<DependencyDownload> link = await apiClient.GetDependencyDownloadAsync(dependency.Id, token.Value, cancellationToken);

        if (!link.IsSuccess)
        {
            return ApiResult.Failure<DownloadedDependency>(link.Error);
        }

        ApiResult file = await downloader.DownloadAsync(
            new DownloadRequest(link.Value.DownloadUrl, path, dependency.SizeBytes, null),
            tracker.ItemProgress,
            cancellationToken);

        if (!file.IsSuccess)
        {
            return ApiResult.Failure<DownloadedDependency>(file.Error);
        }

        tracker.CompleteItem();

        return ApiResult.Success(downloaded);
    }

    // Merged with what is already recorded: another sitting of the same exam stays listed, and the
    // dependency list is replaced by the current one.
    private async Task RecordAsync(
        CatalogExam exam,
        ExamSitting sitting,
        IReadOnlyList<DownloadedDependency> dependencies,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DownloadedExam? existing = await library.LoadAsync(exam.Id, cancellationToken);

        var downloadedSitting = new DownloadedSitting(
            sitting.Id, sitting.StartsAt, sitting.EndsAt, sitting.PackageSizeBytes, sitting.PackageSha256, now);

        List<DownloadedSitting> sittings =
        [
            .. (existing?.Sittings ?? []).Where(s => s.SittingId != sitting.Id),
            downloadedSitting
        ];

        await library.SaveAsync(
            new DownloadedExam(
                exam.Id,
                exam.Title,
                exam.Subject,
                exam.Description,
                $"{exam.ProfessorFirstName} {exam.ProfessorLastName}",
                [.. sittings.OrderBy(s => s.StartsAt)],
                dependencies,
                now),
            cancellationToken);
    }

    // Turns "bytes of the current file" into progress over the whole sitting.
    private sealed class ProgressTracker(IProgress<ExamDownloadProgress>? progress, int itemCount, long bytesTotal)
    {
        private int _itemNumber;
        private long _bytesBeforeItem;
        private long _itemSize;
        private string _itemName = string.Empty;

        public IProgress<long>? ItemProgress => progress is null ? null : new InlineProgress(Report);

        public void StartItem(string name, long sizeBytes)
        {
            _itemNumber++;
            _itemName = name;
            _itemSize = sizeBytes;
            Report(0);
        }

        public void CompleteItem()
        {
            _bytesBeforeItem += _itemSize;
            _itemSize = 0;
        }

        private void Report(long itemBytes) => progress?.Report(new ExamDownloadProgress(
            _itemName, _itemNumber, itemCount, _bytesBeforeItem + Math.Min(itemBytes, _itemSize), bytesTotal));
    }

    // Reports straight through, so the caller's own Progress<T> is the one place that switches to
    // the UI thread.
    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
