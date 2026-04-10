using System.Diagnostics;
using System.Text.RegularExpressions;
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

    public string? BaseDirectory { get; set; }

    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*[a-zA-Z]|\x1B\].*?(\x07|\x1B\\)", RegexOptions.Compiled);
    private static string StripAnsi(string text) => AnsiRegex.Replace(text, "");

    public void ClearOutput()
    {
        lock (_outputLock) _outputLines.Clear();
    }

    private void AddOutputLine(string line)
    {
        var clean = StripAnsi(line);
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

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) AddOutputLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AddOutputLine(e.Data); };
        _process.Exited += (_, _) =>
        {
            var proc = _process;
            _process = null;
            StartedAt = null;
            proc?.Dispose();
        };
        _process.Start();
        StartedAt = DateTime.Now;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
        if (!IsRunning || _process is null) return;
        var pid = _process.Id;

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
        try { _process.Kill(entireProcessTree: true); } catch { }

        _process.Dispose();
        _process = null;
        StartedAt = null;
    }

    public void Restart() { Stop(); Start(); }

    public ServiceStatus ToStatus() => new()
    {
        Name = Name,
        IsRunning = IsRunning,
        Pid = ProcessId,
        Uptime = StartedAt.HasValue ? (DateTime.Now - StartedAt.Value).ToString(@"hh\:mm\:ss") : "-",
        AutoStart = _entry.AutoStart,
    };
}
