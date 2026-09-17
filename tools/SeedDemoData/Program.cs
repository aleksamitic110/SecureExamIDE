using System.Text.Json;
using SeedDemoData;

// Fills a fresh database with the same demo data on every machine, so the same exams can be tried on
// the Linux desktop and on the Windows laptop. Everything goes through the API: exam files and
// toolchains live in MinIO, and only the server can seal a sitting's package, so there is nothing an
// SQL init script could have done here.
//
//   dotnet run --project tools/SeedDemoData
//
// It is safe to run again: accounts and exams that already exist are reused, and each run adds one
// fresh sitting - which is what gives you a new one-time code to test with.

try
{
    SeedOptions options = SeedOptions.Read(args);

    using var apiHttp = new HttpClient { BaseAddress = Absolute(options.ApiBaseUrl), Timeout = TimeSpan.FromMinutes(5) };
    using var mailHttp = new HttpClient { BaseAddress = Absolute(options.MailpitBaseUrl) };

    var api = new ExamApi(apiHttp, new Mailbox(mailHttp), options);

    Console.WriteLine($"API      {options.ApiBaseUrl}");
    Console.WriteLine($"Mailpit  {options.MailpitBaseUrl}");
    Console.WriteLine();

    // A look at what the seed builds, without a server: handy when checking the PDF or an archive.
    if (SeedOptions.DumpDirectory(args) is { } dumpDirectory)
    {
        Dump(dumpDirectory);

        return 0;
    }

    await api.WaitForApiAsync();

    // The student is seeded so signing in on a new machine needs no registration; registering by hand
    // still works, and is the only way to test registration itself.
    var professor = new DemoAccount("demo.professor@example.com", "Milena", "Frtunic", "Password123!", "Professor", null);
    var student = new DemoAccount("demo.student@example.com", "Ana", "Anic", "Password123!", "Student", "19252");

    Console.WriteLine("Accounts");
    await api.SignInAsync(student);
    Console.WriteLine($"  student    {student.Email} / {student.Password}");

    string professorToken = await api.SignInAsync(professor);
    api.Authenticate(professorToken);
    Console.WriteLine($"  professor  {professor.Email} / {professor.Password}");
    Console.WriteLine();

    DateTime now = DateTime.UtcNow;
    List<string> codes = [];

    foreach (DemoExam exam in DemoExams())
    {
        Guid? existing = await api.FindExamAsync(exam.Title);
        Guid examId;

        if (existing is { } found)
        {
            examId = found;
            Console.WriteLine($"{exam.Title}: already there");
        }
        else
        {
            examId = await api.CreateExamAsync(exam.Title, exam.Description, exam.Subject);
            Console.WriteLine($"{exam.Title}");

            foreach ((string fileName, byte[] content, string contentType) in exam.Files)
            {
                await api.AddFileAsync(examId, fileName, content, contentType);
                Console.WriteLine($"  task       {fileName} ({content.Length} bytes)");
            }

            foreach ((string name, string version, string platform, byte[] archive) in exam.Dependencies)
            {
                await api.AddDependencyAsync(examId, name, version, platform, archive);
                Console.WriteLine($"  toolchain  {name} {version} for {platform} ({archive.Length / 1024} KiB)");
            }

            await api.PublishAsync(examId);
            Console.WriteLine("  published");
        }

        // Starting five minutes ago, so the sitting is in progress the moment the seed finishes.
        SittingResponse sitting = await api.CreateSittingAsync(examId, now.AddMinutes(-5), now.AddHours(exam.Hours));

        Console.WriteLine($"  sitting    {sitting.StartsAt:yyyy-MM-dd HH:mm} - {sitting.EndsAt:HH:mm} UTC");
        Console.WriteLine($"  code       {sitting.OneTimeCode}");
        Console.WriteLine();

        codes.Add($"{exam.Title}: {sitting.OneTimeCode}");
    }

    Console.WriteLine("One-time codes for the sittings just created (they are shown only here):");

    foreach (string code in codes)
    {
        Console.WriteLine($"  {code}");
    }

    return 0;
}
catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or JsonException)
{
    Console.Error.WriteLine(exception.Message);

    return 1;
}

static void Dump(string directory)
{
    Directory.CreateDirectory(directory);

    File.WriteAllBytes(Path.Combine(directory, "tasks.pdf"), DemoContent.TasksPdf());
    File.WriteAllBytes(Path.Combine(directory, "toolchain-windows.zip"), DemoContent.ToolchainArchive("WindowsX64"));
    File.WriteAllBytes(Path.Combine(directory, "toolchain-linux.zip"), DemoContent.ToolchainArchive("LinuxX64"));
    File.WriteAllBytes(Path.Combine(directory, "headers.zip"), DemoContent.HeadersArchive());

    Console.WriteLine($"Wrote the demo files to {directory}");
}

static Uri Absolute(Uri url) => new($"{url.ToString().TrimEnd('/')}/");

static IEnumerable<DemoExam> DemoExams()
{
    yield return new DemoExam(
        "Algorithms - September exam",
        "Sorting and shortest paths. Write your solution in C or C++.",
        "Algorithms and Data Structures",
        3,
        [
            ("tasks.txt", System.Text.Encoding.UTF8.GetBytes(DemoContent.AlgorithmsTasks), "text/plain")
        ],
        [
            ("GCC (MinGW-w64)", "14.2.0", "WindowsX64", DemoContent.ToolchainArchive("WindowsX64")),
            ("GCC", "14.2.0", "LinuxX64", DemoContent.ToolchainArchive("LinuxX64")),
            ("Course headers", "1.0", "Any", DemoContent.HeadersArchive())
        ]);

    yield return new DemoExam(
        "Operating Systems - January exam",
        "Producer and consumer with a bounded buffer. The tasks are in tasks.pdf.",
        "Operating Systems",
        2,
        [
            ("tasks.pdf", DemoContent.TasksPdf(), "application/pdf"),
            ("tasks.txt", System.Text.Encoding.UTF8.GetBytes(DemoContent.OperatingSystemsTasks), "text/plain")
        ],
        [
            ("GCC (MinGW-w64)", "14.2.0", "WindowsX64", DemoContent.ToolchainArchive("WindowsX64")),
            ("GCC", "14.2.0", "LinuxX64", DemoContent.ToolchainArchive("LinuxX64"))
        ]);
}

internal sealed record DemoExam(
    string Title,
    string Description,
    string Subject,
    int Hours,
    (string FileName, byte[] Content, string ContentType)[] Files,
    (string Name, string Version, string Platform, byte[] Archive)[] Dependencies);
