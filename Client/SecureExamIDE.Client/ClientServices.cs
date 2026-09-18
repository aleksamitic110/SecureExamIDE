using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureExamIDE.Client.Configuration;
using SecureExamIDE.Client.Services.ActivityLog;
using SecureExamIDE.Client.Services.Api;
using SecureExamIDE.Client.Services.Credentials;
using SecureExamIDE.Client.Services.Downloads;
using SecureExamIDE.Client.Services.Exams;
using SecureExamIDE.Client.Services.Lockdown;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.Services.Pdf;
using SecureExamIDE.Client.Services.Run;
using SecureExamIDE.Client.Services.Session;
using SecureExamIDE.Client.Services.Storage;
using SecureExamIDE.Client.Services.Submission;
using SecureExamIDE.Client.Services.Toolchains;
using SecureExamIDE.Client.Services.Unlock;
using SecureExamIDE.Client.Services.Workspace;
using SecureExamIDE.Client.ViewModels;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.ExamDay;
using SecureExamIDE.Client.ViewModels.Home;

namespace SecureExamIDE.Client;

// The composition root: the only place that knows which implementation stands behind each service.
internal static class ClientServices
{
    public static ServiceProvider Build()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables(prefix: "SECUREEXAMIDE_")
            .Build();

        var services = new ServiceCollection();

        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        services.Configure<LockdownOptions>(configuration.GetSection("Lockdown"));
#if DEBUG
        // A development build always has the escape hatch and can reopen a finished sitting, whatever
        // configuration says. A Release build takes it from configuration only.
        services.PostConfigure<LockdownOptions>(options => options.AllowEmergencyExit = true);
#endif

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMachineIdentity, MachineIdentity>();
        services.AddSingleton(CreateCredentialStore);
        services.AddSingleton(provider => CreateHttpClient(provider.GetRequiredService<IOptions<ApiOptions>>().Value));
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<IProfileCache>(_ => new ProfileCache(ClientPaths.DataDirectory));
        services.AddSingleton<IUiPreferences>(_ => new UiPreferences(ClientPaths.DataDirectory));
        services.AddSingleton<ISessionService, SessionService>();

        services.AddSingleton<ILocalExamLibrary>(_ => new LocalExamLibrary(ClientPaths.DataDirectory));
        services.AddSingleton<IFileDownloader>(_ => new FileDownloader(CreateDownloadHttpClient()));
        services.AddSingleton<IExamCatalog, ExamCatalog>();
        services.AddSingleton<IExamDownloadService, ExamDownloadService>();
        services.AddSingleton<IPackageUnlocker, PackageUnlocker>();
        services.AddSingleton<IWorkspaceStore, WorkspaceStore>();
        services.AddSingleton<IActivityLogStore, ActivityLogStore>();
        services.AddSingleton<ISubmissionService, SubmissionService>();
        services.AddSingleton<IToolchainService, ToolchainService>();
        services.AddSingleton<IProgramRunner, ProgramRunner>();
        services.AddSingleton<IPdfRenderer, PdfRenderer>();
        services.AddSingleton<WindowExamLockdown>();
        services.AddSingleton<IExamLockdown>(provider => provider.GetRequiredService<WindowExamLockdown>());

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddTransient<StartupViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<VerifyEmailViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<StudentHomeViewModel>();
        services.AddTransient<ExamDetailsViewModel>();
        services.AddTransient<DownloadedExamsViewModel>();
        services.AddTransient<UnlockSittingViewModel>();
        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<ProfessorHomeViewModel>();

        return services.BuildServiceProvider();
    }

    // Windows keeps the credential under DPAPI. Everywhere else - Linux while the Windows client is
    // being built - the encrypted-file fallback stands in until the linux-client branch adds libsecret.
    private static ICredentialStore CreateCredentialStore(IServiceProvider provider) =>
        OperatingSystem.IsWindows()
            ? new DpapiCredentialStore(ClientPaths.DataDirectory)
            : new EncryptedFileCredentialStore(ClientPaths.DataDirectory, provider.GetRequiredService<IMachineIdentity>());

    // One HttpClient for the life of the application, with pooled connections recycled every few
    // minutes so a changed DNS entry is eventually noticed. The container disposes it on exit.
    private static HttpClient CreateHttpClient(ApiOptions options)
    {
#pragma warning disable CA2000 // The handler is owned and disposed by the HttpClient.
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
#pragma warning restore CA2000

        // Left without a base address when none is configured; ApiClient then reports that clearly
        // instead of the application failing to start.
        // The trailing slash matters: without it, relative paths such as "users/register" would
        // replace the last segment of a base address that has a path.
        if (Uri.TryCreate($"{options.BaseUrl.Trim().TrimEnd('/')}/", UriKind.Absolute, out Uri? baseAddress) &&
            (baseAddress.Scheme == Uri.UriSchemeHttp || baseAddress.Scheme == Uri.UriSchemeHttps))
        {
            client.BaseAddress = baseAddress;
        }

        return client;
    }

    // Downloads go straight to object storage through presigned links, with no API address and no
    // timeout: the size of a toolchain, not a fixed limit, decides how long one takes.
    private static HttpClient CreateDownloadHttpClient()
    {
#pragma warning disable CA2000 // The handler is owned and disposed by the HttpClient.
        return new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
#pragma warning restore CA2000
    }
}
