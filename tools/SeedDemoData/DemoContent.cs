using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SeedDemoData;

// The files the demo exams are made of. They are built here rather than committed, so the repository
// carries no binaries and both machines end up with byte-identical content.
internal static class DemoContent
{
    public const string AlgorithmsTasks = """
        Algorithms - September exam

        Task 1 (40 points)
        Read a list of integers from the standard input, one per line, and print them sorted
        in ascending order, separated by single spaces.

        Task 2 (60 points)
        A graph is given as a list of edges. Print the length of the shortest path between the
        first and the last vertex, or -1 when there is no path.

        Write your solution in main.c or main.cpp. You may add as many files as you like.
        """;

    public const string OperatingSystemsTasks = """
        Operating Systems - January exam

        The tasks are in tasks.pdf. This file repeats them in short:

        Task 1  Implement a bounded buffer shared by one producer and one consumer.
        Task 2  Explain, in comments, why your solution cannot deadlock.
        """;

    // A toolchain the way a professor would attach one: an archive with the compilers in bin/.
    // The padding is only there to make the download take a moment, as a real one would.
    public static byte[] ToolchainArchive(string platform)
    {
        bool windows = platform == "WindowsX64";

        return Zip(new Dictionary<string, byte[]>
        {
            [windows ? "bin/gcc.exe" : "bin/gcc"] = Text($"Not a real compiler. Stands in for GCC on {platform}."),
            [windows ? "bin/g++.exe" : "bin/g++"] = Text($"Not a real compiler. Stands in for G++ on {platform}."),
            ["README.txt"] = Text("Demo toolchain created by the SeedDemoData tool."),
            ["lib/padding.bin"] = Padding(2 * 1024 * 1024)
        });
    }

    public static byte[] HeadersArchive() => Zip(new Dictionary<string, byte[]>
    {
        ["include/course.h"] = Text("#pragma once\nint course_helper(int value);\n"),
        ["include/course.c"] = Text("#include \"course.h\"\nint course_helper(int value) { return value + 1; }\n")
    });

    // A minimal one-page PDF, written out by hand: enough for a PDF viewer to open and show a line
    // of text, without pulling a PDF library into a seeding tool.
    public static byte[] TasksPdf()
    {
        string[] lines =
        [
            "BT /F1 16 Tf 72 720 Td (Operating Systems - January exam) Tj ET",
            "BT /F1 12 Tf 72 690 Td (Task 1: implement a bounded buffer for one producer and one consumer.) Tj ET",
            "BT /F1 12 Tf 72 670 Td (Task 2: explain in comments why your solution cannot deadlock.) Tj ET"
        ];

        string content = string.Join('\n', lines);

        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        List<int> offsets = [];

        for (int number = 1; number <= objects.Count; number++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(number).Append(" 0 obj\n").Append(objects[number - 1]).Append("\nendobj\n");
        }

        int xref = pdf.Length;

        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");

        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
           .Append(xref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static byte[] Zip(Dictionary<string, byte[]> entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    // Bytes that do not compress, so the archive really is the size a small toolchain would be, and
    // a download takes a moment. Deterministic on purpose: both machines get byte-identical content.
    private static byte[] Padding(int size)
    {
        byte[] padding = new byte[size];
        byte[] block = SHA256.HashData("SecureExamIDE demo toolchain"u8);

        for (int offset = 0; offset < size; offset += block.Length)
        {
            block = SHA256.HashData(block);
            block.AsSpan(0, Math.Min(block.Length, size - offset)).CopyTo(padding.AsSpan(offset));
        }

        return padding;
    }
}
