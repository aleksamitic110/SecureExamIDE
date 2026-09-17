using System.Security.Cryptography;
using System.Text;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Workspace;

namespace SecureExamIDE.Client.Tests.Workspace;

public sealed class WorkspaceStoreTests : IDisposable
{
    private const string Code = "int main(void) { return 0; }";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "secureexamide-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _examId = Guid.NewGuid();
    private readonly Guid _sittingId = Guid.NewGuid();
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly WorkspaceStore _store;

    public WorkspaceStoreTests() => _store = new WorkspaceStore(new LocalExamLibrary(_directory));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private IWorkspaceFiles Open(byte[]? key = null) => _store.Open(_examId, _sittingId, key ?? _key);

    private string FilesDirectory => Path.Combine(
        _directory, "exams", _examId.ToString("N"), "sittings", _sittingId.ToString("N"), "workspace", "files");

    [Fact]
    public void Write_Should_SealTheFileNextToTheSittingsPackage()
    {
        // Act
        using (IWorkspaceFiles files = Open())
        {
            files.Write("main.c", Code);
        }

        // Assert - what is on disk is not the student's text.
        byte[] stored = File.ReadAllBytes(Path.Combine(FilesDirectory, "main.c"));
        Encoding.UTF8.GetString(stored).ShouldNotContain("main");
        stored.Length.ShouldBeGreaterThan(Code.Length);

        using IWorkspaceFiles reopened = Open();
        reopened.Read("main.c").ShouldBe(Code);
    }

    // The key comes from the one-time code, so the work cannot be read outside the exam.
    [Fact]
    public void Read_Should_Refuse_WhenTheKeyIsNotTheOneTheFileWasWrittenWith()
    {
        // Arrange
        using (IWorkspaceFiles files = Open())
        {
            files.Write("main.c", Code);
        }

        // Act & Assert
        using IWorkspaceFiles other = Open(RandomNumberGenerator.GetBytes(32));
        Should.Throw<InvalidDataException>(() => other.Read("main.c"));
    }

    // A file cannot be passed off as another one: the name is part of what the tag covers.
    [Fact]
    public void Read_Should_Refuse_WhenAFileWasRenamedBehindTheApplicationsBack()
    {
        // Arrange
        using (IWorkspaceFiles files = Open())
        {
            files.Write("main.c", Code);
        }

        File.Move(Path.Combine(FilesDirectory, "main.c"), Path.Combine(FilesDirectory, "other.c"));

        // Act & Assert
        using IWorkspaceFiles files2 = Open();
        Should.Throw<InvalidDataException>(() => files2.Read("other.c"));
    }

    [Fact]
    public void Read_Should_StillOpenAFileFromBeforeTheFilesWereEncrypted()
    {
        // Arrange
        Directory.CreateDirectory(FilesDirectory);
        File.WriteAllText(Path.Combine(FilesDirectory, "old.c"), Code);

        // Act
        using IWorkspaceFiles files = Open();
        string text = files.Read("old.c");

        // Assert
        text.ShouldBe(Code);
    }

    [Fact]
    public void List_Should_ReturnTheStudentsFilesByName_AndSkipLeftovers()
    {
        // Arrange
        using IWorkspaceFiles files = Open();
        files.Write("util.h", "");
        files.Write("Main.c", "");
        File.WriteAllText(Path.Combine(FilesDirectory, "main.c.tmp"), "half a save");

        // Act
        IReadOnlyList<string> names = files.List();

        // Assert
        names.ShouldBe(["Main.c", "util.h"]);
    }

    [Fact]
    public void RenameAndDelete_Should_ChangeTheFilesOnDisk()
    {
        // Arrange
        using IWorkspaceFiles files = Open();
        files.Write("draft.c", Code);

        // Act
        files.Rename("draft.c", "main.c");
        IReadOnlyList<string> afterRename = files.List();
        string text = files.Read("main.c");
        files.Delete("main.c");

        // Assert
        afterRename.ShouldBe(["main.c"]);
        text.ShouldBe(Code);
        files.List().ShouldBeEmpty();
    }

    // The store's own guard, independent of the screen's validation.
    [Fact]
    public void Write_Should_RefuseANameThatLeavesTheFolder()
    {
        // Arrange
        using IWorkspaceFiles files = Open();

        // Act & Assert
        Should.Throw<ArgumentException>(() => files.Write("../escaped.c", "x"));
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

        using IWorkspaceFiles files = Open();
        files.List().ShouldBeEmpty();
    }
}
