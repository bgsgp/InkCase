using System.Windows;
using Lmm.Format;
using Lmm.Plugins.Abstractions;
using Lmm.Plugins.English;

namespace ExamReview.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only ManualModuleCatalog for now: one shipped module, so scanning
        // assemblies would be over-engineering. The shell talks to
        // IModuleCatalog, so swapping in an AssemblyLoadContext catalog later
        // doesn't touch MainWindow.
        var catalog = new ManualModuleCatalog([new EnglishSubjectModule()]);
        var window = new MainWindow(catalog, new LmmPackageReader(), new LmmPackageWriter());
        MainWindow = window;
        window.Show();
    }
}
