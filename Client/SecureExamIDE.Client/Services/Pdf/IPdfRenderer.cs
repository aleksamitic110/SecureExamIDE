namespace SecureExamIDE.Client.Services.Pdf;

// Turns a PDF task into pictures of its pages, from the bytes in memory. The decrypted task never
// touches the disk, so a viewer that wants a file path would be no use here.
public interface IPdfRenderer
{
    // The scale is how much larger than the page's own size it is drawn: 2 gives 144 dots per inch,
    // which stays sharp when the task panel is widened.
    IReadOnlyList<PdfPage> Render(byte[] pdf, int scale);
}

// One page as raw BGRA pixels, which is what an Avalonia bitmap is written from.
public sealed record PdfPage(int Width, int Height, byte[] Pixels);
