using ServiceManager.Client;
using ServiceManager.Shared;
using Terminal.Gui;

var configPath = ResolveConfigPath(args.Length > 0 ? args[0] : "services.yaml");
var port = 14040;

if (File.Exists(configPath))
{
    var config = ServiceConfig.LoadFromFile(configPath);
    port = config.Port;
}

using var client = new ServerClient(port);

if (!await client.HealthCheck())
{
    Console.Error.WriteLine($"Cannot connect to Service Manager server on port {port}.");
    Console.Error.WriteLine("Start the server first: dotnet run --project Server");
    return 1;
}

try
{
    Application.Init();
    Application.Top.ColorScheme = new ColorScheme
    {
        Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black),
        Focus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
        HotNormal = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
        HotFocus = Application.Driver.MakeAttribute(Color.Cyan, Color.DarkGray),
        Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
    };
    var win = new ServiceManagerWindow(client);
    Application.Run(win);
}
finally
{
    Application.Shutdown();
}

return 0;

static string ResolveConfigPath(string path)
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
