namespace SecureExamIDE.Client.Configuration;

// Bound from the "Api" configuration section. The committed appsettings.json leaves the address
// empty; it comes from the git-ignored appsettings.Local.json or the SECUREEXAMIDE_Api__BaseUrl
// environment variable, the same rule the server follows for its own endpoints.
public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}
