namespace SeedDemoData;

// Where the tool talks to, and the one secret it needs. Nothing is hard-coded into a committed file:
// the professor registration code is read from the same WebApi/.env the stack itself uses, unless it
// is passed on the command line or set in the environment.
internal sealed record SeedOptions(Uri ApiBaseUrl, Uri MailpitBaseUrl, string ProfessorRegistrationCode)
{
    public static SeedOptions Read(string[] arguments)
    {
        string api = Argument(arguments, "--api") ?? "http://localhost:5000";
        string mailpit = Argument(arguments, "--mailpit") ?? "http://localhost:8025";
        string? code = Argument(arguments, "--professor-code")
            ?? Environment.GetEnvironmentVariable("Registration__ProfessorRegistrationCode")
            ?? FromEnvFile("Registration__ProfessorRegistrationCode");

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException(
                "The professor registration code was not found. Pass --professor-code <code>, "
                + "set Registration__ProfessorRegistrationCode, or run this from a clone whose WebApi/.env is filled in.");
        }

        return new SeedOptions(new Uri(api), new Uri(mailpit), code);
    }

    // --dump <folder> writes the generated files out instead of seeding, so they can be inspected.
    public static string? DumpDirectory(string[] arguments) => Argument(arguments, "--dump");

    private static string? Argument(string[] arguments, string name)
    {
        int index = Array.IndexOf(arguments, name);

        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    // Walks up from wherever the tool runs to the WebApi folder of this clone.
    private static string? FromEnvFile(string key)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "WebApi", ".env");

            if (File.Exists(candidate))
            {
                foreach (string line in File.ReadAllLines(candidate))
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    {
                        return line[(key.Length + 1)..].Trim();
                    }
                }

                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
