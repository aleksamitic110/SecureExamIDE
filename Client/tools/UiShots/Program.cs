using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SecureExamIDE.Client;
using SecureExamIDE.Client.Services.Navigation;
using SecureExamIDE.Client.ViewModels;
using SecureExamIDE.Client.ViewModels.Account;
using SecureExamIDE.Client.ViewModels.ExamDay;
using SecureExamIDE.Client.ViewModels.Home;
using SecureExamIDE.Client.ViewModels.Professor;
using SecureExamIDE.Client.Views;

namespace UiShots;

// Walks the client the way a student and then a professor would, against a running server holding
// the demo data, and saves a picture of every screen on the way. Nothing appears on the desktop.
//
//   dotnet run --project tools/UiShots -- --out <folder> --theme light|dark --code <one-time code>
//
// The code is the Algorithms sitting's, printed by the seeding tool. The walk hands that sitting in,
// so the demo data should be seeded again afterwards. It uses this computer's own client data folder
// and signs out when it is done.
internal static class Program
{
    private const string StudentEmail = "demo.student@example.com";
    private const string ProfessorEmail = "demo.professor@example.com";
    private const string Password = "Password123!";

    private static string _out = "ui-shots";
    private static string _code = string.Empty;
    private static bool _dark;
    private static int _number;

    private static Window _window = null!;
    private static MainWindowViewModel _shell = null!;
    private static INavigationService _navigation = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();

    public static async Task<int> Main(string[] args)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            switch (args[index])
            {
                case "--out": _out = args[index + 1]; break;
                case "--code": _code = args[index + 1]; break;
                case "--theme": _dark = args[index + 1] == "dark"; break;
                default: break;
            }
        }

        Directory.CreateDirectory(_out);
        Environment.SetEnvironmentVariable("SECUREEXAMIDE_Api__BaseUrl", Environment.GetEnvironmentVariable("SECUREEXAMIDE_Api__BaseUrl") ?? "http://localhost:5000");

        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(Program));

        return await session.Dispatch(WalkAsync, CancellationToken.None);
    }

    private static async Task<int> WalkAsync()
    {
        Application.Current!.RequestedThemeVariant = _dark ? ThemeVariant.Dark : ThemeVariant.Light;

        using ServiceProvider services = ClientServices.Build();
        _shell = services.GetRequiredService<MainWindowViewModel>();
        _navigation = services.GetRequiredService<INavigationService>();

        _window = new MainWindow { DataContext = _shell, Width = 1280, Height = 800 };
        _window.Show();

        await Step("student", StudentAsync);
        await Step("professor", ProfessorAsync);

        return 0;
    }

    private static async Task Step(string name, Func<Task> walk)
    {
        try
        {
            await walk();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"!! {name} stopped: {exception.GetType().Name}: {exception.Message}");
            await Shot($"{name}-stopped-here");
        }
    }

    private static async Task StudentAsync()
    {
        _navigation.NavigateTo<WelcomeViewModel>();
        await Shot("welcome");

        RegisterViewModel register = _navigation.NavigateTo<RegisterViewModel>();
        register.FirstName = "Ana";
        register.LastName = "Anic";
        register.Email = "ana.anic@example.com";
        register.IndexNumber = "19252";
        register.ErrorMessage = "The two passwords are not the same.";
        await Shot("register");

        _navigation.NavigateTo<VerifyEmailViewModel>(page => page.Initialize(
            "ana.anic@example.com", _ => Task.FromResult(SecureExamIDE.Client.Services.Api.ApiResult.Success()), codeWasJustSent: true));
        await Shot("verify-email");

        await SignInAsync(StudentEmail, "login");

        StudentHomeViewModel home = await Wait<StudentHomeViewModel>();
        await WaitFor(() => home.Exams.Count > 0);
        await Shot("student-catalog");

        CatalogExamItem algorithms = home.Exams.First(exam => exam.Title.StartsWith("Algorithms", StringComparison.Ordinal));
        home.OpenExamCommand.Execute(algorithms);

        ExamDetailsViewModel details = await Wait<ExamDetailsViewModel>();
        await WaitFor(() => details.Sittings.Count > 0);
        await Shot("exam-details");

        SittingItemViewModel sitting = details.Sittings[^1];
        Task downloading = details.DownloadCommand.ExecuteAsync(sitting);
        await Pump(250);
        await Shot("exam-downloading");
        await downloading;
        await Shot("exam-downloaded");

        _navigation.NavigateTo<DownloadedExamsViewModel>();
        await Pump(600);
        await Shot("exams-on-this-computer");

        _navigation.NavigateTo<StudentHomeViewModel>();
        home = await Wait<StudentHomeViewModel>();
        await WaitFor(() => home.Exams.Count > 0);
        home.OpenExamCommand.Execute(home.Exams.First(exam => exam.Title.StartsWith("Algorithms", StringComparison.Ordinal)));
        details = await Wait<ExamDetailsViewModel>();
        await WaitFor(() => details.Sittings.Count > 0);
        await details.EnterCommand.ExecuteAsync(details.Sittings[^1]);

        UnlockSittingViewModel unlock = await Wait<UnlockSittingViewModel>();
        unlock.Code = _code;
        await Shot("unlock");
        await unlock.UnlockCommand.ExecuteAsync(null);

        _window.Width = 1440;
        _window.Height = 900;

        WorkspaceViewModel workspace = await Wait<WorkspaceViewModel>();
        await Shot("workspace-empty");

        AddFile(workspace, "main.c", """
            #include <stdio.h>
            #include "util.h"

            /* Task 1: the sum of the first n natural numbers. */
            int main(void)
            {
                int n = 10;

                printf("Sum of 1..%d is %d\n", n, sum_to(n));
                printf("Largest of 3, 9, 4 is %d\n", largest(3, 9, 4));

                return 0;
            }
            """);
        AddFile(workspace, "util.h", """
            #ifndef UTIL_H
            #define UTIL_H

            static int sum_to(int n)
            {
                int total = 0;

                for (int i = 1; i <= n; i++)
                {
                    total += i;
                }

                return total;
            }

            static int largest(int a, int b, int c)
            {
                int best = a > b ? a : b;

                return best > c ? best : c;
            }

            #endif
            """);
        workspace.OpenCommand.Execute(workspace.Files.First(file => file.Name == "main.c"));
        await Pump(1500);
        Console.WriteLine("files added, running");

        // Never waited on for ever: a program that hangs must not hang the walk with it.
        Task running = workspace.RunCommand.ExecuteAsync(null);

        for (int attempt = 0; attempt < 600 && !running.IsCompleted; attempt++)
        {
            await Pump(100);
        }

        Console.WriteLine(running.IsCompleted ? "run finished" : "run still going after a minute");
        await Shot("workspace");

        if (!running.IsCompleted)
        {
            workspace.StopCommand.Execute(null);
            await Pump(1000);
        }

        workspace.ToggleTasksPanelCommand.Execute(null);
        workspace.ToggleFilesPanelCommand.Execute(null);
        await Shot("workspace-editor-only");
        workspace.ToggleTasksPanelCommand.Execute(null);
        workspace.ToggleFilesPanelCommand.Execute(null);

        workspace.AskToFinishCommand.Execute(null);
        await Shot("workspace-finish");
        await workspace.ConfirmFinishCommand.ExecuteAsync(null);

        _window.Width = 1280;
        _window.Height = 800;

        await Wait<DownloadedExamsViewModel>();
        await Pump(2500);
        await Shot("handed-in");

        await SignOutAsync();
    }

    private static async Task ProfessorAsync()
    {
        await SignInAsync(ProfessorEmail, null);

        ProfessorHomeViewModel home = await Wait<ProfessorHomeViewModel>();
        await WaitFor(() => home.Exams.Count > 0);
        await Shot("professor-exams");

        home.NewExamCommand.Execute(null);
        ExamEditorViewModel editor = await Wait<ExamEditorViewModel>();
        editor.Title = "Databases - February exam";
        editor.Subject = "Databases";
        editor.Description = "Two tasks: a schema for a small library, and five queries over it. Written answers go in answers.txt.";
        await Shot("exam-editor");
        await editor.SaveCommand.ExecuteAsync(null);

        home = await Wait<ProfessorHomeViewModel>();
        await WaitFor(() => home.Exams.Any(exam => exam.IsDraft));
        await Shot("professor-exams-with-draft");
        home.OpenExamCommand.Execute(home.Exams.First(exam => exam.IsDraft));

        ExamContentsViewModel draft = await Wait<ExamContentsViewModel>();
        await Pump(600);
        await Shot("exam-draft");
        draft.BeginAddToolchainCommand.Execute(null);
        draft.ToolchainName = "GCC (MinGW-w64)";
        draft.ToolchainVersion = "16.2.0";
        await Shot("add-toolchain");
        draft.CancelAddToolchainCommand.Execute(null);
        draft.BackCommand.Execute(null);

        home = await Wait<ProfessorHomeViewModel>();
        await WaitFor(() => home.Exams.Count > 1);

        MyExamItem algorithms = home.Exams.First(exam => exam.Title.StartsWith("Algorithms", StringComparison.Ordinal));
        home.OpenExamCommand.Execute(algorithms);

        ExamContentsViewModel contents = await Wait<ExamContentsViewModel>();
        await Pump(800);
        await Shot("exam-contents");

        contents.OpenSittingsCommand.Execute(null);
        SittingsViewModel sittings = await Wait<SittingsViewModel>();
        await WaitFor(() => sittings.Sittings.Count > 0);
        await Shot("sittings");

        sittings.BeginScheduleCommand.Execute(null);
        await Shot("schedule-sitting");
        await sittings.ScheduleCommand.ExecuteAsync(null);
        await Shot("one-time-code");
        sittings.DismissCodeCommand.Execute(null);
        await Pump(600);

        MySittingItem handedIn = sittings.Sittings.First(sitting => sitting.Handed.StartsWith('1'));
        sittings.OpenSubmissionsCommand.Execute(handedIn);

        SubmissionsViewModel submissions = await Wait<SubmissionsViewModel>();
        await WaitFor(() => submissions.Submissions.Count > 0);
        await Shot("submissions");

        Task opening = submissions.OpenCommand.ExecuteAsync(submissions.Submissions[0]);
        await Pump(200);
        submissions.Code = _code;
        await Shot("review-code");
        await opening;
        await submissions.ConfirmCodeCommand.ExecuteAsync(null);

        _window.Width = 1440;
        _window.Height = 900;

        await Wait<SubmissionReviewViewModel>();
        await Pump(1500);
        await Shot("review");

        _window.Width = 1280;
        _window.Height = 800;

        _navigation.NavigateTo<ProfessorHomeViewModel>();
        home = await Wait<ProfessorHomeViewModel>();
        await WaitFor(() => home.Exams.Count > 0);

        // The draft made above is thrown away again, so the walk leaves the exams as it found them.
        if (home.Exams.FirstOrDefault(exam => exam.IsDraft) is { } leftover)
        {
            home.AskToDeleteCommand.Execute(leftover);
            await Shot("delete-draft");
            await home.ConfirmDeleteCommand.ExecuteAsync(null);
        }

        await SignOutAsync();
    }

    private static void AddFile(WorkspaceViewModel workspace, string name, string text)
    {
        workspace.StartNewFileCommand.Execute(null);
        workspace.FileName = name;
        workspace.ConfirmFileNameCommand.Execute(null);
        workspace.ActiveFile!.Document.Text = text;
    }

    private static async Task SignInAsync(string email, string? shot)
    {
        LoginViewModel login = _navigation.NavigateTo<LoginViewModel>();
        login.Email = email;
        login.Password = Password;
        login.DeviceName = "demo-laptop";

        if (shot is not null)
        {
            await Shot(shot);
        }

        await login.SignInCommand.ExecuteAsync(null);

        // The server allows ten sign-ins a minute from one address, and seeding has just used some.
        if (login.ErrorMessage?.Contains("Too many", StringComparison.Ordinal) == true)
        {
            Console.WriteLine("waiting a minute for the sign-in limit");
            await Task.Delay(TimeSpan.FromSeconds(62));
            login.ErrorMessage = null;
            await login.SignInCommand.ExecuteAsync(null);
        }

        if (login.ErrorMessage is { } error)
        {
            throw new InvalidOperationException($"Sign-in as {email} failed: {error}");
        }
    }

    private static async Task SignOutAsync()
    {
        if (_shell.CurrentPage is SignedInViewModelBase signedIn)
        {
            await signedIn.SignOutCommand.ExecuteAsync(null);
        }
        else
        {
            StudentHomeViewModel home = _navigation.NavigateTo<StudentHomeViewModel>();
            await Pump(400);
            await home.SignOutCommand.ExecuteAsync(null);
        }

        await Pump(300);
    }

    private static async Task<T> Wait<T>() where T : ViewModelBase
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (_shell.CurrentPage is T page)
            {
                await Pump(150);

                return page;
            }

            await Pump(50);
        }

        throw new TimeoutException($"Expected the {typeof(T).Name} page, but the window shows {_shell.CurrentPage?.GetType().Name}.");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Pump(50);
        }

        await Pump(150);
    }

    private static async Task Pump(int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Shot(string name)
    {
        await Pump(250);

        string path = Path.Combine(_out, $"{++_number:00}-{name}.png");

        using WriteableBitmap? frame = _window.CaptureRenderedFrame();
#pragma warning disable CS0618 // The plain overload is all a PNG needs.
        frame?.Save(path);
#pragma warning restore CS0618

        Console.WriteLine(frame is null ? $"(nothing drawn for {name})" : path);
    }
}
