namespace SecureExamIDE.Client.Services.Exams;

// Where a sitting's download has got to, for the progress bar. Bytes count across every file, so
// a large toolchain moves the bar in proportion to its share of the wait.
public sealed record ExamDownloadProgress(
    string CurrentItem,
    int ItemNumber,
    int ItemCount,
    long BytesDone,
    long BytesTotal);
