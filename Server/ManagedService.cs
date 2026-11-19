using System.Diagnostics;
using ServiceManager.Shared;

namespace ServiceManager.Server;

public class ManagedService
{
    private ServiceEntry _entry;
    private readonly List<string> _outputLines = new();
    private readonly object _outputLock = new();
    private const int MaxOutputLines = 1000;
    private Process? _process;

    public string Name => _entry.Name;
    public bool IsRunning { get { try { var p = _process; return p is not null && !p.HasExited; } catch { return false; } } }
    public DateTime? StartedAt { get; private set; }
    public int? ProcessId { get { try { return _process?.Id; } catch { return null; } } }

    public IReadOnlyList<string> OutputLines
    {
        get { lock (_outputLock) return _outputLines.ToList(); }
    }

    public event EventHandler<string>? OutputReceived;

    public ManagedService(ServiceEntry entry) { _entry = entry; }

    public void UpdateEntry(ServiceEntry entry) { _entry = entry; }

    public void Rename(string newName)
    {
        _entry = new ServiceEntry
        {
            Name = newName,
            Command = _entry.Command,
            Description = _entry.Description,
            WorkingDirectory = _entry.WorkingDirectory,
            AutoStart = _entry.AutoStart,
        };
    }

    public string? BaseDirectory { get; set; }

    public void ClearOutput()
    {
        lock (_outputLock) _outputLines.Clear();
    }

    private void AddOutputLine(string line)
    {
        // Keep color codes for the dashboard; drop cursor/title/etc. sequences
        var clean = Ansi.StripNonSgr(line);
        lock (_outputLock)
        {
            _outputLines.Add(clean);
            if (_outputLines.Count > MaxOutputLines)
                _outputLines.RemoveAt(0);
        }
        OutputReceived?.Invoke(this, clean);
    }

    public void Start()
    {
        if (IsRunning) return;
        var workDir = _entry.WorkingDirectory;
        if (workDir is not null && BaseDirectory is not null && !Path.IsPathRooted(workDir))
            workDir = Path.GetFullPath(Path.Combine(BaseDirectory, workDir));
        workDir ??= Directory.GetCurrentDirectory();

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c chcp 65001 >nul & {_entry.Command}",
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        // Output goes to a pipe, so most tools disable color unless forced
        psi.Environment["FORCE_COLOR"] = "1";
        psi.Environment["CLICOLOR_FORCE"] = "1";
        psi.Environment.Remove("NO_COLOR");
        psi.Environment["PATH"] = FreshPath();

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) AddOutputLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) AddOutputLine(e.Data); };
        // Only this process's own exit clears the field: on a restart the old one's Exited fires
        // after Start has already put the new one there, and must not drop or dispose it
        proc.Exited += (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _process, null, proc) != proc) return;
            StartedAt = null;
            proc.Dispose();
        };
        _process = proc;
        proc.Start();
        StartedAt = DateTime.Now;
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
    }

    // PATH as saved in the registry now, so tools installed after sam started are found;
    // entries only this process had (from the shell that launched sam) are kept at the end
    private static string FreshPath()
    {
        var saved = new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User }
            .Select(t => Environment.GetEnvironmentVariable("Path", t) ?? "");
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        var entries = saved.Append(current)
            .SelectMany(p => p.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(';', entries);
    }

    public void Stop()
    {
        // Take the process out of the field first, so its Exited handler (which fires while
        // taskkill is waiting) neither nulls nor disposes it under us
        var proc = Interlocked.Exchange(ref _process, null);
        if (proc is null) return;
        StartedAt = null;
        int pid;
        try { if (proc.HasExited) { proc.Dispose(); return; } pid = proc.Id; }
        catch { proc.Dispose(); return; }

        // Use taskkill /T to kill the entire process tree — more reliable
        // than Process.Kill(entireProcessTree) for apps like Electron
        // that spawn detached child processes
        try
        {
            var taskkill = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/T /F /PID {pid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            taskkill.Start();
            taskkill.WaitForExit(5000);
        }
        catch { }

        // Fallback
        try { proc.Kill(entireProcessTree: true); } catch { }

        proc.Dispose();
    }

    public void Restart() { Stop(); Start(); }

    public ServiceStatus ToStatus() => new()
    {
        Name = Name,
        Description = _entry.Description,
        IsRunning = IsRunning,
        Pid = ProcessId,
        Uptime = StartedAt.HasValue ? (DateTime.Now - StartedAt.Value).ToString(@"hh\:mm\:ss") : "-",
        AutoStart = _entry.AutoStart,
    };
}
