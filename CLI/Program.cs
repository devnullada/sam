using System.Diagnostics;
using System.Text.Json;
using ServiceManager.Shared;

var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var configPath = ResolveConfigPath("services.yaml");
var port = 14040;
if (File.Exists(configPath))
{
    try { port = ServiceConfig.LoadFromFile(configPath).Port; } catch { }
}

var subcommand = args.Length > 0 ? args[0].ToLower() : "";

switch (subcommand)
{
    case "server":
        await HandleServer(args.Skip(1).ToArray());
        break;
    case "client":
        LaunchProject("Client", args.Skip(1).ToArray(), wait: true);
        break;
    case "status":
        await ShowServerStatus();
        break;
    case "list":
        await PrintStatus();
        break;
    case "start":
    case "stop":
    case "restart":
    case "output":
        await RunCommand(args);
        break;
    case "":
    case "help":
        PrintHelp();
        break;
    default:
        Console.WriteLine($"Unknown command: {subcommand}");
        Console.WriteLine("Run 'sam help' for usage.");
        break;
}

return;

void PrintHelp()
{
    Console.WriteLine("sam - Service Manager");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  sam                     Show this help");
    Console.WriteLine("  sam status              Show server status");
    Console.WriteLine("  sam list                List services and their status");
    Console.WriteLine("  sam start <name|all>    Start a service");
    Console.WriteLine("  sam stop <name|all>     Stop a service");
    Console.WriteLine("  sam restart <name>      Restart a service");
    Console.WriteLine("  sam output <name> [n]   Show last n output lines (default 30)");
    Console.WriteLine("  sam client              Start TUI client");
    Console.WriteLine("  sam server              Start server in background");
    Console.WriteLine("  sam server stop         Stop the server");
    Console.WriteLine("  sam server restart      Restart the server");
    Console.WriteLine("  sam server foreground   Run server in foreground (debug)");
    Console.WriteLine("  sam server install      Auto-start server at Windows login");
    Console.WriteLine("  sam server uninstall    Remove auto-start");
    Console.WriteLine("  sam help                Show this help");
}

// ── Server subcommand ──────────────────────────────────────────

async Task HandleServer(string[] serverArgs)
{
    var sub = serverArgs.Length > 0 ? serverArgs[0].ToLower() : "";

    switch (sub)
    {
        case "restart":
            LaunchProject("Server", ["--stop"]);
            await Task.Delay(1000);
            LaunchProject("Server", []);
            break;
        case "stop":
            LaunchProject("Server", ["--stop"]);
            break;
        case "install":
            LaunchProject("Server", ["--install"]);
            break;
        case "uninstall":
            LaunchProject("Server", ["--uninstall"]);
            break;
        case "foreground":
            LaunchProject("Server", ["--foreground"], wait: true);
            break;
        default:
            LaunchProject("Server", []);
            break;
    }
}

// ── Commands ───────────────────────────────────────────────────

async Task RunCommand(string[] parts)
{
    var cmd = parts[0].ToLower();
    var arg = parts.Length > 1 ? parts[1].Trim() : null;

    switch (cmd)
    {
        case "start":
            if (arg == "all") { await Post("/services/start-all"); Console.WriteLine("Started all."); }
            else if (arg is not null) { await Post($"/services/{arg}/start"); Console.WriteLine($"Started {arg}."); }
            else Console.WriteLine("Usage: sam start <name|all>");
            break;
        case "stop":
            if (arg == "all") { await Post("/services/stop-all"); Console.WriteLine("Stopped all."); }
            else if (arg is not null) { await Post($"/services/{arg}/stop"); Console.WriteLine($"Stopped {arg}."); }
            else Console.WriteLine("Usage: sam stop <name|all>");
            break;
        case "restart":
            if (arg is not null) { await Post($"/services/{arg}/restart"); Console.WriteLine($"Restarted {arg}."); }
            else Console.WriteLine("Usage: sam restart <name>");
            break;
        case "output":
        {
            var outputParts = arg?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var name = outputParts?.FirstOrDefault();
            var count = outputParts?.Length > 1 && int.TryParse(outputParts[1], out var n) ? n : 30;
            if (name is not null) await PrintOutput(name, count);
            else Console.WriteLine("Usage: sam output <name> [lines]");
            break;
        }
    }
}

async Task PrintStatus()
{
    var services = await GetServices();
    if (services.Count == 0) { Console.WriteLine("No services or server not reachable."); return; }

    Console.WriteLine();
    Console.WriteLine($"  {"Name",-24} {"Status",-10} {"PID",-8} {"Uptime"}");
    Console.WriteLine($"  {"----",-24} {"------",-10} {"---",-8} {"------"}");
    foreach (var s in services)
    {
        var status = s.IsRunning ? "RUNNING" : "STOPPED";
        var pid = s.Pid?.ToString() ?? "-";
        Console.WriteLine($"  {s.Name,-24} {status,-10} {pid,-8} {s.Uptime}");
    }
}

async Task ShowServerStatus()
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var resp = await http.GetAsync($"http://localhost:{port}/health");
        if (resp.IsSuccessStatusCode)
        {
            var services = await GetServices();
            var running = services.Count(s => s.IsRunning);
            Console.WriteLine($"  Server:   running on port {port}");
            Console.WriteLine($"  Services: {running}/{services.Count} running");
        }
        else
        {
            Console.WriteLine($"  Server:   not running (port {port})");
        }
    }
    catch
    {
        Console.WriteLine($"  Server:   not running (port {port})");
    }
}

async Task PrintOutput(string name, int lines)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        var json = await http.GetStringAsync($"http://localhost:{port}/services/{name}/output?lines={lines}");
        var outputLines = JsonSerializer.Deserialize<List<string>>(json, jsonOpts) ?? new();
        Console.WriteLine();
        Console.WriteLine($"  --- {name} (last {outputLines.Count} lines) ---");
        foreach (var l in outputLines) Console.WriteLine($"  {l}");
        Console.WriteLine($"  --- end ---");
    }
    catch { Console.WriteLine($"Failed to get output for '{name}'."); }
}

// ── Helpers ────────────────────────────────────────────────────

async Task<List<ServiceStatus>> GetServices()
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var json = await http.GetStringAsync($"http://localhost:{port}/services");
        return JsonSerializer.Deserialize<List<ServiceStatus>>(json, jsonOpts) ?? new();
    }
    catch { return new(); }
}

async Task Post(string path)
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await http.PostAsync($"http://localhost:{port}{path}", null);
    }
    catch { Console.WriteLine("Server not reachable."); }
}

void LaunchProject(string project, string[] extraArgs, bool wait = false)
{
    var solutionDir = FindSolutionDir();
    var exe = Path.Combine(solutionDir, project, "bin", "Debug", "net9.0", $"{project}.exe");

    if (!File.Exists(exe))
    {
        var buildPsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{Path.Combine(solutionDir, project, $"{project}.csproj")}\" --verbosity quiet",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(buildPsi)?.WaitForExit();
    }

    var arguments = string.Join(" ", extraArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
    var psi = new ProcessStartInfo
    {
        FileName = exe,
        Arguments = arguments,
        WorkingDirectory = solutionDir,
        UseShellExecute = false,
    };

    var proc = Process.Start(psi);
    if (wait) proc?.WaitForExit();
}

string FindSolutionDir()
{
    // Walk up from exe location
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    for (int i = 0; i < 10; i++)
    {
        if (File.Exists(Path.Combine(dir, "ServiceManager.sln"))) return dir;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    // Walk up from cwd
    dir = Directory.GetCurrentDirectory();
    for (int i = 0; i < 10; i++)
    {
        if (File.Exists(Path.Combine(dir, "ServiceManager.sln"))) return dir;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    return Directory.GetCurrentDirectory();
}

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
