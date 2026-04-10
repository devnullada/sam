using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ServiceManager.Server;
using ServiceManager.Shared;

using System.Diagnostics;

var configArg = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "services.yaml";
var configPath = ResolveConfigPath(configArg);

if (args.Contains("--install"))
{
    StartupInstaller.Install(Environment.ProcessPath!, configPath);
    return;
}
if (args.Contains("--uninstall"))
{
    StartupInstaller.Uninstall();
    return;
}
if (args.Contains("--stop"))
{
    // Graceful: tell server to stop all services, then kill
    var stopPort = 14040;
    if (File.Exists(configPath)) { try { stopPort = ServiceConfig.LoadFromFile(configPath).Port; } catch { } }
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        http.PostAsync($"http://localhost:{stopPort}/services/stop-all", null).Wait();
        Thread.Sleep(500);
    }
    catch { }

    var self = Process.GetCurrentProcess();
    var procs = Process.GetProcessesByName(self.ProcessName)
        .Where(p => p.Id != self.Id)
        .ToArray();
    if (procs.Length == 0) { Console.WriteLine("No running server found."); return; }
    foreach (var p in procs)
    {
        Console.WriteLine($"Stopping server (PID {p.Id})...");
        try { p.Kill(); } catch { }
    }
    return;
}

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Config file not found: {configPath}");
    return;
}

var config0 = ServiceConfig.LoadFromFile(configPath);

// Check if server is already running
if (!args.Contains("--foreground"))
{
    try
    {
        using var http = new HttpClient();
        var resp = http.GetAsync($"http://localhost:{config0.Port}/health").Result;
        if (resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"Server is already running on port {config0.Port}.");
            return;
        }
    }
    catch { }
}

// Default: launch a detached background process and exit
// Use --foreground to run in the current terminal (for debugging)
if (!args.Contains("--foreground"))
{
    var exe = Environment.ProcessPath!;
    var psi = new ProcessStartInfo
    {
        FileName = exe,
        Arguments = $"\"{configPath}\" --foreground",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
    };
    var proc = Process.Start(psi);
    Console.WriteLine($"Service Manager server started (PID {proc?.Id}) on config: {configPath}");
    return;
}

var config = ServiceConfig.LoadFromFile(configPath);
var manager = new ServiceManagerCore
{
    BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))
};
manager.LoadConfig(config);
manager.StartAll(config);

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://localhost:{config.Port}");
builder.Logging.ClearProviders();
var app = builder.Build();
app.UseWebSockets();

var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

app.MapGet("/health", () => Results.Json(new { status = "ok" }, jsonOpts));

app.MapGet("/services", () =>
{
    var list = manager.ServiceNames
        .Where(n => manager.Services.ContainsKey(n))
        .Select(n => manager.Services[n].ToStatus())
        .ToList();
    return Results.Json(list, jsonOpts);
});

app.MapPost("/services/{name}/start", (string name) =>
{
    if (!manager.Services.TryGetValue(name, out var svc))
        return Results.NotFound(new { error = $"Service '{name}' not found" });
    manager.TryStart(svc);
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapPost("/services/{name}/stop", (string name) =>
{
    if (!manager.Services.TryGetValue(name, out var svc))
        return Results.NotFound(new { error = $"Service '{name}' not found" });
    svc.Stop();
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapPost("/services/{name}/restart", (string name) =>
{
    if (!manager.Services.TryGetValue(name, out var svc))
        return Results.NotFound(new { error = $"Service '{name}' not found" });
    svc.Restart();
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapPost("/services/start-all", () =>
{
    manager.StartAll(config);
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapPost("/services/stop-all", () =>
{
    manager.StopAll();
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapGet("/services/{name}/output", (string name, int? lines) =>
{
    if (!manager.Services.TryGetValue(name, out var svc))
        return Results.NotFound(new { error = $"Service '{name}' not found" });
    var count = lines ?? 100;
    var output = svc.OutputLines;
    var result = output.Count > count ? output.Skip(output.Count - count).ToList() : output.ToList();
    return Results.Json(result, jsonOpts);
});

app.MapPost("/services/{name}/output/clear", (string name) =>
{
    if (!manager.Services.TryGetValue(name, out var svc))
        return Results.NotFound(new { error = $"Service '{name}' not found" });
    svc.ClearOutput();
    return Results.Json(new { ok = true }, jsonOpts);
});

app.Map("/services/{name}/ws", async (HttpContext ctx, string name) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    if (!manager.Services.TryGetValue(name, out var svc)) { ctx.Response.StatusCode = 404; return; }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();

    void OnOutput(object? sender, string line)
    {
        if (ws.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            _ = ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    svc.OutputReceived += OnOutput;
    var buffer = new byte[256];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;
        }
    }
    catch { }
    finally
    {
        svc.OutputReceived -= OnOutput;
        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }
});

var fullConfigPath = Path.GetFullPath(configPath);

app.MapGet("/config-path", () => Results.Json(new { path = fullConfigPath }, jsonOpts));

app.MapPost("/services", async (HttpContext ctx) =>
{
    var entry = await ctx.Request.ReadFromJsonAsync<ServiceEntry>(jsonOpts);
    if (entry is null || string.IsNullOrWhiteSpace(entry.Name))
        return Results.BadRequest(new { error = "Name and command are required" });

    var cfg = ServiceConfig.LoadFromFile(fullConfigPath);
    if (cfg.Services.Any(s => s.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = $"Service '{entry.Name}' already exists" });

    cfg.Services.Add(entry);
    cfg.SaveToFile(fullConfigPath);
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapDelete("/services/{name}", (string name) =>
{
    var cfg = ServiceConfig.LoadFromFile(fullConfigPath);
    var idx = cfg.Services.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (idx < 0) return Results.NotFound(new { error = $"Service '{name}' not found" });

    // Stop if running
    if (manager.Services.TryGetValue(name, out var svc)) svc.Stop();

    cfg.Services.RemoveAt(idx);
    cfg.SaveToFile(fullConfigPath);
    return Results.Json(new { ok = true }, jsonOpts);
});

app.MapGet("/services/{name}/config", (string name) =>
{
    var cfg = ServiceConfig.LoadFromFile(fullConfigPath);
    var entry = cfg.Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (entry is null) return Results.NotFound(new { error = $"Service '{name}' not found" });
    return Results.Json(entry, jsonOpts);
});

app.MapPut("/services/{name}/config", async (string name, HttpContext ctx) =>
{
    var updated = await ctx.Request.ReadFromJsonAsync<ServiceEntry>(jsonOpts);
    if (updated is null) return Results.BadRequest(new { error = "Invalid request body" });

    var cfg = ServiceConfig.LoadFromFile(fullConfigPath);
    var idx = cfg.Services.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (idx < 0) return Results.NotFound(new { error = $"Service '{name}' not found" });

    updated.Name = cfg.Services[idx].Name; // preserve original name
    cfg.Services[idx] = updated;
    cfg.SaveToFile(fullConfigPath);
    return Results.Json(new { ok = true }, jsonOpts);
});

// Watch services.yaml for changes
var watcher = new FileSystemWatcher(Path.GetDirectoryName(fullConfigPath)!, Path.GetFileName(fullConfigPath))
{
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
    EnableRaisingEvents = true,
};

var lastReload = DateTime.MinValue;
watcher.Changed += (_, _) =>
{
    // Debounce — editors trigger multiple write events
    if ((DateTime.Now - lastReload).TotalSeconds < 1) return;
    lastReload = DateTime.Now;

    Thread.Sleep(200); // Let the editor finish writing
    try
    {
        var newConfig = ServiceConfig.LoadFromFile(fullConfigPath);
        manager.ReloadConfig(newConfig);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Config reloaded: {newConfig.Services.Count} services");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Config reload failed (invalid YAML?): {ex.Message}");
    }
};

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    watcher.EnableRaisingEvents = false;
    manager.StopAll();
    app.StopAsync().Wait();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    manager.StopAll();
};

Console.WriteLine($"Service Manager server listening on http://localhost:{config.Port}");
app.Run();
manager.StopAll();

static string ResolveConfigPath(string path)
{
    if (Path.IsPathRooted(path) || File.Exists(path)) return path;

    // Walk up from current directory looking for the config file
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
