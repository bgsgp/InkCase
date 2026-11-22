using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lmm.Format;
using Lmm.Plugins.Abstractions;
using Lmm.Plugins.English;
using Lmm.Translation;

namespace ExamReview.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A WinExe has no console, and the classroom machine may not have WER
        // enabled, so an unexpected exception would vanish. Keep a plain-text
        // crash log next to the user profile instead: the next launch shows it
        // in the status bar.
        DispatcherUnhandledException += (_, args) => LogCrash(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => LogCrash(args.Exception);

        // Only ManualModuleCatalog for now: one shipped module, so scanning
        // assemblies would be over-engineering. The shell talks to
        // IModuleCatalog, so swapping in an AssemblyLoadContext catalog later
        // doesn't touch MainWindow.
        var catalog = new ManualModuleCatalog([new EnglishSubjectModule()]);
        var translator = new MyMemoryTranslationProvider();
        var window = new MainWindow(catalog, new LmmPackageReader(), new LmmPackageWriter(), translator);
        MainWindow = window;
        window.Show();

        // File association / "open with": a path on the command line opens (or
        // imports) straight away instead of asking the user to use the dialog.
        var path = e.Args.FirstOrDefault(a => File.Exists(a));
        if (path is not null) window.OpenExternal(path);
    }

    /// <summary>Path of the crash log; the shell surfaces it when it exists.</summary>
    public static string CrashLogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ExamReview", "crash.log");

    private static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down a second time.
        }
    }
}
