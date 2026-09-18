using SecureExamIDE.Client.Services.Lockdown;

namespace SecureExamIDE.Client.Tests.Lockdown;

// "Paste into the application from outside is blocked; copy and paste inside the application is
// allowed" - the rule the exam mode exists for.
public sealed class ClipboardGuardTests
{
    private readonly FakeClipboard _clipboard = new();

    private ClipboardGuard CreateGuard() => new(_clipboard);

    [Fact]
    public async Task Text_CopiedSomewhereElse_Should_BeWipedFromTheClipboard()
    {
        // Arrange - written in another editor and copied, while the exam window was not in front.
        _clipboard.Text = "int main(void) { /* somebody else's solution */ }";
        ClipboardGuard guard = CreateGuard();

        // Act
        bool blocked = await guard.RemoveOutsideContentAsync();

        // Assert
        blocked.ShouldBeTrue();
        _clipboard.Text.ShouldBeNull();
    }

    [Fact]
    public async Task Text_CopiedInsideTheExam_Should_StayOnTheClipboard()
    {
        // Arrange
        ClipboardGuard guard = CreateGuard();
        guard.Remember("int total = 0;");
        _clipboard.Text = "int total = 0;";

        // Act
        bool blocked = await guard.RemoveOutsideContentAsync();

        // Assert
        blocked.ShouldBeFalse();
        _clipboard.Text.ShouldBe("int total = 0;");
    }

    // Copied inside, then replaced from outside: the exam's own text is no longer what is there.
    [Fact]
    public async Task Text_ThatReplacedTheExamsOwnCopy_Should_BeWiped()
    {
        // Arrange
        ClipboardGuard guard = CreateGuard();
        guard.Remember("int total = 0;");
        _clipboard.Text = "something brought in from a browser";

        // Act
        bool blocked = await guard.RemoveOutsideContentAsync();

        // Assert
        blocked.ShouldBeTrue();
        _clipboard.Text.ShouldBeNull();
    }

    // Wiping is not enough on its own: a clipboard manager puts back what an application clears, which
    // is how a paste from outside still worked on KDE. So the paste itself is checked as well.
    [Fact]
    public async Task Pasting_Should_BeAllowedOnly_ForWhatTheExamCopied()
    {
        // Arrange
        ClipboardGuard guard = CreateGuard();
        guard.Remember("int total = 0;");

        // Act
        _clipboard.Text = "int total = 0;";
        string? own = await guard.TextAllowedToPasteAsync();

        _clipboard.Text = "a solution from a browser";
        string? outside = await guard.TextAllowedToPasteAsync();

        // Assert
        own.ShouldBe("int total = 0;");
        outside.ShouldBeNull();
    }

    [Fact]
    public async Task AnEmptyClipboard_Should_BeLeftAlone()
    {
        // Act
        bool blocked = await CreateGuard().RemoveOutsideContentAsync();

        // Assert
        blocked.ShouldBeFalse();
        _clipboard.Cleared.ShouldBeFalse();
    }

    // The exam's own copy is forgotten once it has been wiped, so the same text arriving again from
    // outside is not mistaken for it.
    [Fact]
    public async Task AWipedCopy_Should_NotBeTrustedIfItComesBack()
    {
        // Arrange
        ClipboardGuard guard = CreateGuard();
        guard.Remember("int total = 0;");
        _clipboard.Text = "from outside";
        await guard.RemoveOutsideContentAsync();

        // Act
        _clipboard.Text = "int total = 0;";
        bool blocked = await guard.RemoveOutsideContentAsync();

        // Assert
        blocked.ShouldBeTrue();
    }

    private sealed class FakeClipboard : IClipboardAccess
    {
        public string? Text { get; set; }

        public bool Cleared { get; private set; }

        public Task<string?> ReadTextAsync() => Task.FromResult(Text);

        public Task ClearAsync()
        {
            Text = null;
            Cleared = true;

            return Task.CompletedTask;
        }
    }
}
