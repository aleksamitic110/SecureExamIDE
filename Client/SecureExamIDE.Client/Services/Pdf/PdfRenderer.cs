using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace SecureExamIDE.Client.Services.Pdf;

// PDFium (through Docnet) does the drawing - the same engine browsers use - with the native library
// carried per platform by the package.
internal sealed class PdfRenderer : IPdfRenderer
{
    public IReadOnlyList<PdfPage> Render(byte[] pdf, int scale)
    {
        // Scaling rather than a fixed width, so pages of different sizes keep their proportions.
        using IDocReader document = DocLib.Instance.GetDocReader(pdf, new PageDimensions(scale));

        List<PdfPage> pages = [];

        for (int number = 0; number < Math.Min(document.GetPageCount(), MaxPages); number++)
        {
            using IPageReader page = document.GetPageReader(number);

            // Without this the page comes back with a transparent background - black text on
            // nothing - which on a dark theme looks like a black rectangle. The converter paints
            // the paper white, which is what a task sheet should look like.
            pages.Add(new PdfPage(page.GetPageWidth(), page.GetPageHeight(), page.GetImage(TransparencyRemover)));
        }

        return pages;
    }

    // A task sheet is a few pages; this only stops a strange file from filling memory with bitmaps.
    private const int MaxPages = 50;

    private static readonly NaiveTransparencyRemover TransparencyRemover = new();
}
