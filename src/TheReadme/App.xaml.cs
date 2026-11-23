using System.IO;
using System.Windows;

namespace TheReadme;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 1 && e.Args[0] is "--installer-add-path" or "--installer-remove-path")
        {
            try
            {
                WindowsIntegration.SetPath(e.Args[0] == "--installer-add-path");
                Shutdown(0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Shutdown(1);
            }
            return;
        }

        RenderPolicy.Initialize();
        var window = new MainWindow(e.Args.Length == 2 && e.Args[0] == "--" ? e.Args[1] : e.Args.FirstOrDefault());
        MainWindow = window;
        window.Show();
    }
}
