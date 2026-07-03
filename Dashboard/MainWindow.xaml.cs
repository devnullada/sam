using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceManager.Shared;

namespace ServiceManager.Dashboard;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private readonly ServerClient _client;
    private readonly string _configPath;
    private readonly DispatcherTimer _timer;
    private List<ServiceStatus> _services = [];
    private readonly ObservableCollection<ServiceItemVm> _items = [];
    private string? _subscribedService;
    private int _outputLineCount;
    private const int MaxOutputLines = 1000;
    private bool _serverOnline;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ServiceManager", "dashboard.json");

    public MainWindow(ServerClient client, string configPath)
    {
        InitializeComponent();
        EnableDarkTitleBar();
        _client = client;
        _configPath = configPath;
        ServiceList.ItemsSource = _items;

        RestoreWindowBounds();

        _client.OutputLineReceived += OnOutputLineReceived;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            if (ServiceList.Items.Count > 0)
                ServiceList.SelectedIndex = 0;
        };

        Closed += (_, _) =>
        {
            SaveWindowBounds();
            _timer.Stop();
            _client.Dispose();
        };
    }

    private async Task RefreshAsync()
    {
        var online = await _client.HealthCheck();
        if (online != _serverOnline)
        {
            _serverOnline = online;
            UpdateServerStatus();
        }

        if (_serverOnline)
            await RefreshServicesAsync();
        else
            ClearServices();
    }

    private void UpdateServerStatus()
    {
        if (_serverOnline)
        {
            ServerStatusIcon.Foreground = (Brush)FindResource("RunningText");
            ServerStatusText.Foreground = (Brush)FindResource("RunningText");
            ServerStatusText.Text = "SERVER ONLINE";
        }
        else
        {
            ServerStatusIcon.Foreground = (Brush)FindResource("StoppedText");
            ServerStatusText.Foreground = (Brush)FindResource("StoppedText");
            ServerStatusText.Text = "SERVER OFFLINE";
        }

        BtnStartServer.IsEnabled = !_serverOnline;
        BtnStopServer.IsEnabled = _serverOnline;
        UpdateButtonStates();
    }

    private void ClearServices()
    {
        _services = [];
        ServiceList.SelectionChanged -= OnServiceSelected;
        _items.Clear();
        ServiceList.SelectionChanged += OnServiceSelected;
        _subscribedService = null;
        OutputParagraph.Inlines.Clear();
        _outputLineCount = 0;
        PidText.Text = "-";
        UptimeText.Text = "-";
        StatusText.Text = "-";
        StatusText.Foreground = (Brush)FindResource("DefaultText");
        UpdateButtonStates();
    }

    private async Task RefreshServicesAsync()
    {
        var services = await _client.GetServices();
        var selectedName = GetSelectedServiceName();
        var previousIndex = ServiceList.SelectedIndex;

        _services = services;

        // Reconcile the stable collection in place so periodic refreshes don't
        // rebuild rows (which would reset hover/selection every tick).
        ServiceList.SelectionChanged -= OnServiceSelected;
        for (var i = _items.Count - 1; i >= 0; i--)
            if (!services.Any(s => s.Name == _items[i].Name))
                _items.RemoveAt(i);
        for (var i = 0; i < services.Count; i++)
        {
            var s = services[i];
            var existing = _items.FirstOrDefault(it => it.Name == s.Name);
            if (existing is null)
            {
                _items.Insert(i, new ServiceItemVm(s.Name, s.IsRunning));
            }
            else
            {
                existing.IsRunning = s.IsRunning;
                var curIdx = _items.IndexOf(existing);
                if (curIdx != i) _items.Move(curIdx, i);
            }
        }

        if (selectedName != null)
        {
            var idx = _services.FindIndex(s => s.Name == selectedName);
            if (idx >= 0) ServiceList.SelectedIndex = idx;
        }
        else if (previousIndex >= 0 && previousIndex < _items.Count)
        {
            ServiceList.SelectedIndex = previousIndex;
        }

        ServiceList.SelectionChanged += OnServiceSelected;
        UpdateInfoBar();
        UpdateButtonStates();
    }

    private void OnServiceSelected(object sender, SelectionChangedEventArgs e)
    {
        UpdateInfoBar();
        UpdateButtonStates();
        _ = SwitchOutputSubscription();
    }

    private async Task SwitchOutputSubscription(bool force = false)
    {
        var svc = GetSelectedService();
        if (svc is null) return;
        if (!force && svc.Name == _subscribedService) return;
        _subscribedService = svc.Name;

        var lines = await _client.GetOutput(svc.Name);
        OutputParagraph.Inlines.Clear();
        _outputLineCount = 0;

        foreach (var line in lines)
        {
            AppendOutputLine(line);
        }
        ScrollOutputToBottom();

        await _client.SubscribeOutput(svc.Name);
    }

    private void OnOutputLineReceived(string line)
    {
        Dispatcher.Invoke(() =>
        {
            AppendOutputLine(line);
            TrimOutputLines();

            if (IsScrolledToBottom())
                ScrollOutputToBottom();
        });
    }

    private void AppendOutputLine(string line)
    {
        if (_outputLineCount > 0)
            OutputParagraph.Inlines.Add(new LineBreak());

        var segments = AnsiParser.Parse(line);
        foreach (var seg in segments)
        {
            OutputParagraph.Inlines.Add(new Run(seg.Text) { Foreground = seg.Foreground });
        }

        _outputLineCount++;
    }

    private void TrimOutputLines()
    {
        while (_outputLineCount > MaxOutputLines)
        {
            while (OutputParagraph.Inlines.FirstInline is Inline first)
            {
                OutputParagraph.Inlines.Remove(first);
                if (first is LineBreak) break;
            }
            _outputLineCount--;
        }
    }

    private bool IsScrolledToBottom()
    {
        var offset = OutputBox.VerticalOffset;
        var viewportHeight = OutputBox.ViewportHeight;
        var extentHeight = OutputBox.ExtentHeight;
        return offset + viewportHeight >= extentHeight - 10;
    }

    private void ScrollOutputToBottom()
    {
        OutputBox.ScrollToEnd();
    }

    private void UpdateInfoBar()
    {
        var svc = GetSelectedService();
        if (svc is null)
        {
            PidText.Text = "-";
            UptimeText.Text = "-";
            StatusText.Text = "-";
            StatusText.Foreground = (Brush)FindResource("DefaultText");
            return;
        }

        PidText.Text = svc.Pid?.ToString() ?? "-";
        UptimeText.Text = svc.Uptime;
        StatusText.Text = svc.IsRunning ? "RUNNING" : "STOPPED";
        StatusText.Foreground = svc.IsRunning
            ? (Brush)FindResource("RunningText")
            : (Brush)FindResource("StoppedText");
    }

    private void UpdateButtonStates()
    {
        var svc = GetSelectedService();
        var isRunning = svc?.IsRunning ?? false;
        var hasSelection = svc != null && _serverOnline;
        BtnStart.IsEnabled = hasSelection && !isRunning;
        BtnStop.IsEnabled = hasSelection && isRunning;
        BtnNewService.IsEnabled = _serverOnline;
    }

    private async Task RunWithSpinner(Button button, Func<Task> action)
    {
        var originalContent = button.Content;
        button.Content = new ProgressBar { Style = (Style)FindResource("ButtonSpinner") };
        button.IsEnabled = false;
        try
        {
            await action();
        }
        finally
        {
            button.Content = originalContent;
            // Don't re-enable here — let UpdateButtonStates/UpdateServerStatus handle it
        }
    }

    private async Task WaitForServerState(bool online, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (await _client.HealthCheck() == online) break;
            await Task.Delay(250);
        }
        _serverOnline = online;
        UpdateServerStatus();
        if (online) await RefreshServicesAsync();
        else ClearServices();
    }

    private async void OnStartServer(object sender, RoutedEventArgs e)
    {
        var serverExe = FindServerExe();
        if (serverExe is null)
        {
            MessageBox.Show("Could not find Server executable.", "Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunWithSpinner(BtnStartServer, async () =>
        {
            var fullConfigPath = Path.GetFullPath(_configPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = serverExe,
                Arguments = $"\"{fullConfigPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            await WaitForServerState(true);
        });
    }

    private async void OnStopServer(object sender, RoutedEventArgs e)
    {
        await RunWithSpinner(BtnStopServer, async () =>
        {
            await _client.StopAll();
            var serverExe = FindServerExe();
            if (serverExe is not null)
            {
                var fullConfigPath = Path.GetFullPath(_configPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = serverExe,
                    Arguments = $"\"{fullConfigPath}\" --stop",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            await WaitForServerState(false);
        });
    }

    private string? FindServerExe()
    {
        // Look for Server.exe next to the Dashboard executable
        var dashDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(dashDir, "..", "..", "..", "..", "Server", "bin", "Debug", "net9.0", "Server.exe");
        if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        // Try sibling directory structure
        candidate = Path.Combine(dashDir, "Server.exe");
        if (File.Exists(candidate)) return candidate;

        // Try relative to working directory
        candidate = Path.Combine(Directory.GetCurrentDirectory(), "Server", "bin", "Debug", "net9.0", "Server.exe");
        if (File.Exists(candidate)) return candidate;

        return null;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        var svc = GetSelectedService();
        if (svc is not null)
            await RunWithSpinner(BtnStart, async () =>
            {
                await _client.StartService(svc.Name);
                await Task.Delay(500);
                await SwitchOutputSubscription(force: true);
            });
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        var svc = GetSelectedService();
        if (svc is not null)
            await RunWithSpinner(BtnStop, async () =>
            {
                await _client.StopService(svc.Name);
                await Task.Delay(500);
                await SwitchOutputSubscription(force: true);
            });
    }

    private async void OnClearOutput(object sender, RoutedEventArgs e)
    {
        OutputParagraph.Inlines.Clear();
        _outputLineCount = 0;
        var svc = GetSelectedService();
        if (svc is not null)
            await _client.ClearOutput(svc.Name);
    }

    private void OnCopyOutput(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(OutputBox.Document.ContentStart, OutputBox.Document.ContentEnd).Text.TrimEnd();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private async void OnEditConfig(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServiceItemVm item) return;

        var entry = await _client.GetServiceConfig(item.Name);
        if (entry is null)
        {
            MessageBox.Show("Could not load service config.", "Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new EditServiceDialog(entry) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var ok = await _client.UpdateServiceConfig(item.Name, dialog.Result);
            if (!ok)
                MessageBox.Show("Failed to save config.", "Dashboard",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnNewService(object sender, RoutedEventArgs e)
    {
        var entry = new ServiceEntry();
        var dialog = new EditServiceDialog(entry, isNew: true) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            if (string.IsNullOrWhiteSpace(dialog.Result.Name))
            {
                MessageBox.Show("Service name is required.", "Dashboard",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var ok = await _client.CreateService(dialog.Result);
            if (!ok)
                MessageBox.Show("Failed to create service. Name may already exist.", "Dashboard",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnDeleteService(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServiceItemVm item) return;

        var result = MessageBox.Show($"Delete service '{item.Name}'?", "Dashboard",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var ok = await _client.DeleteService(item.Name);
        if (!ok)
            MessageBox.Show("Failed to delete service.", "Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ServiceStatus? GetSelectedService()
    {
        var idx = ServiceList.SelectedIndex;
        if (idx < 0 || idx >= _services.Count) return null;
        return _services[idx];
    }

    private string? GetSelectedServiceName()
    {
        return GetSelectedService()?.Name;
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var value = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    private void SaveWindowBounds()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new { Left, Top, Width, Height, Maximized = false }
                : new { Left = RestoreBounds.Left, Top = RestoreBounds.Top, Width = RestoreBounds.Width, Height = RestoreBounds.Height, Maximized = WindowState == WindowState.Maximized };
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(bounds));
        }
        catch { }
    }

    private void RestoreWindowBounds()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var root = doc.RootElement;
            Left = root.GetProperty("Left").GetDouble();
            Top = root.GetProperty("Top").GetDouble();
            Width = root.GetProperty("Width").GetDouble();
            Height = root.GetProperty("Height").GetDouble();
            if (root.GetProperty("Maximized").GetBoolean())
                WindowState = WindowState.Maximized;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        catch { }
    }
}
