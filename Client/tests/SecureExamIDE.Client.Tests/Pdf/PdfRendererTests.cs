using System.Text;
using SecureExamIDE.Client.Services.Pdf;

namespace SecureExamIDE.Client.Tests.Pdf;

public sealed class PdfRendererTests
{
    // The one-page PDF the seeding tool builds for the demo exam, checked with pdfinfo and pdftotext.
    private const string OnePage = """
%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>
endobj
4 0 obj
<< /Length 258 >>
stream
BT /F1 16 Tf 72 720 Td (Operating Systems - January exam) Tj ET
BT /F1 12 Tf 72 690 Td (Task 1: implement a bounded buffer for one producer and one consumer.) Tj ET
BT /F1 12 Tf 72 670 Td (Task 2: explain in comments why your solution cannot deadlock.) Tj ET
endstream
endobj
5 0 obj
<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
endobj
xref
0 6
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000241 00000 n 
0000000550 00000 n 
trailer
<< /Size 6 /Root 1 0 R >>
startxref
620
%%EOF
""";

    // A PDF task is drawn from the bytes in memory: nothing decrypted is written to disk for a viewer.
    [Fact]
    public void Render_Should_DrawThePagesOfAPdf()
    {
        // Arrange
        byte[] pdf = Encoding.ASCII.GetBytes(OnePage);
        var renderer = new PdfRenderer();

        // Act
        IReadOnlyList<PdfPage> pages = renderer.Render(pdf, scale: 2);

        // Assert
        PdfPage page = pages.ShouldHaveSingleItem();

        // A4 at twice its own size, which is 595 by 842 points.
        page.Width.ShouldBe(1190);

        // A4 is taller than it is wide, and four bytes to a pixel.
        page.Height.ShouldBeGreaterThan(page.Width);
        page.Pixels.Length.ShouldBe(page.Width * page.Height * 4);

        // Something was really drawn: a blank page would be one colour throughout.
        page.Pixels.Distinct().Count().ShouldBeGreaterThan(1);

        // The paper is opaque white, not transparent: a transparent page shows as a black rectangle
        // on a dark theme, which is exactly what it looked like the first time.
        page.Pixels[0].ShouldBeGreaterThan((byte)240);
        page.Pixels[1].ShouldBeGreaterThan((byte)240);
        page.Pixels[2].ShouldBeGreaterThan((byte)240);
        page.Pixels[3].ShouldBe((byte)255);
    }
}
