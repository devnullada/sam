using System.IO;
using System.Windows;
using ServiceManager.Shared;

namespace ServiceManager.Dashboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var configPath = ResolveConfigPath(e.Args.Length > 0 ? e.Args[0] : "services.yaml");
            var port = 14040;

            if (File.Exists(configPath))
            {
                var config = ServiceConfig.LoadFromFile(configPath);
                port = config.Port;
            }

            var client = new ServerClient(port);
            var window = new MainWindow(client, configPath);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start Dashboard:\n{ex.Message}", "Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string ResolveConfigPath(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path)) return path;

        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return path;
    }
}
