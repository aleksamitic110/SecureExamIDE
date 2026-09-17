using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.Tests.Workspace;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _examId = Guid.NewGuid();
    private readonly Guid _sittingId = Guid.NewGuid();
    private readonly WorkspaceStore _store;

    public WorkspaceStoreTests() => _store = new WorkspaceStore(new LocalExamLibrary(_directory));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Write_Should_KeepTheFileNextToTheSittingsPackage()
    {
        // Act
        _store.WriteFile(_examId, _sittingId, "main.c", "int main(void) { return 0; }");

        // Assert
        string expected = Path.Combine(_directory, "exams", _examId.ToString("N"), "sittings", _sittingId.ToString("N"), "workspace", "files", "main.c");
        File.ReadAllText(expected).ShouldBe("int main(void) { return 0; }");
        _store.ReadFile(_examId, _sittingId, "main.c").ShouldBe("int main(void) { return 0; }");
    }

    [Fact]
    public void List_Should_ReturnTheStudentsFilesByName_AndSkipLeftovers()
    {
        // Arrange
        _store.WriteFile(_examId, _sittingId, "util.h", "");
        _store.WriteFile(_examId, _sittingId, "Main.c", "");
        string files = Path.GetDirectoryName(Path.Combine(_directory, "exams", _examId.ToString("N"), "sittings", _sittingId.ToString("N"), "workspace", "files", "x"))!;
        File.WriteAllText(Path.Combine(files, "main.c.tmp"), "half a save");

        // Act
        IReadOnlyList<string> names = _store.ListFiles(_examId, _sittingId);

        // Assert
        names.ShouldBe(["Main.c", "util.h"]);
    }

    [Fact]
    public void RenameAndDelete_Should_ChangeTheFilesOnDisk()
    {
        // Arrange
        _store.WriteFile(_examId, _sittingId, "draft.c", "x");

        // Act
        _store.RenameFile(_examId, _sittingId, "draft.c", "main.c");
        IReadOnlyList<string> afterRename = _store.ListFiles(_examId, _sittingId);
        _store.DeleteFile(_examId, _sittingId, "main.c");

        // Assert
        afterRename.ShouldBe(["main.c"]);
        _store.ListFiles(_examId, _sittingId).ShouldBeEmpty();
    }

    // The store's own guard, independent of the screen's validation.
    [Fact]
    public void Write_Should_RefuseANameThatLeavesTheFolder()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => _store.WriteFile(_examId, _sittingId, "../escaped.c", "x"));
    }

    [Fact]
    public void MarkFinished_Should_BeRemembered()
    {
        // Arrange
        bool before = _store.IsFinished(_examId, _sittingId);

        // Act
        _store.MarkFinished(_examId, _sittingId, DateTimeOffset.UtcNow);

        // Assert
        before.ShouldBeFalse();
        _store.IsFinished(_examId, _sittingId).ShouldBeTrue();
        _store.ListFiles(_examId, _sittingId).ShouldBeEmpty();
    }
}
