namespace ServiceManager.Server;

public static class StartupInstaller
{
    private const string ShortcutName = "ServiceManager.lnk";

    public static void Install(string exePath, string arguments)
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolder, ShortcutName);
        var workDir = Path.GetDirectoryName(exePath)!;

        var script = $@"
$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut('{shortcutPath.Replace("'", "''")}')
$s.TargetPath = '{exePath.Replace("'", "''")}'
$s.Arguments = '{arguments.Replace("'", "''")}'
$s.WorkingDirectory = '{workDir.Replace("'", "''")}'
$s.WindowStyle = 7
$s.Save()";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        System.Diagnostics.Process.Start(psi)?.WaitForExit();
        Console.WriteLine($"Startup shortcut installed: {shortcutPath}");
    }

    public static void Uninstall()
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolder, ShortcutName);
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
            Console.WriteLine($"Startup shortcut removed: {shortcutPath}");
        }
        else Console.WriteLine("No startup shortcut found.");
    }
}
