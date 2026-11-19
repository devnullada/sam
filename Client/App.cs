using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;
using ServiceManager.Shared;

namespace ServiceManager.Client;

public class ServiceManagerWindow : Window
{
    private readonly IApplication _app;
    private readonly ServerClient _client;
    private readonly ListView _serviceList;
    private readonly Label _infoLabel;
#pragma warning disable CS0618 // TextView superseded by tui-cs/Editor; fine for a read-only log
    private readonly TextView _outputView;
#pragma warning restore CS0618
    private List<ServiceStatus> _services = new();
    private int _selectedIndex;
    private bool _refreshing;
    private string? _subscribedService;

    public ServiceManagerWindow(IApplication app, ServerClient client)
    {
        _app = app;
        _client = client;
        Title = "Service Manager";

        var dark = DarkScheme();
        SetScheme(dark);

        var leftWidth = Dim.Percent(30);

        var servicesFrame = new FrameView
        { Title = "Services", X = 0, Y = 0, Width = leftWidth, Height = Dim.Fill() - 8 };

        _serviceList = new ListView
        { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        _serviceList.ValueChanged += (_, _) => OnSelectedItemChanged();
        _serviceList.KeyDown += OnKeyDown;
        servicesFrame.Add(_serviceList);

        var keysFrame = new FrameView
        { Title = "Keys", X = 0, Y = Pos.Bottom(servicesFrame), Width = leftWidth, Height = Dim.Fill() };
        keysFrame.Add(new Label
        {
            Text =
            " s  start       S  start all\n" +
            " x  stop        X  stop all\n" +
            " r  restart\n" +
            " q  quit",
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
        });

        _infoLabel = new Label { Text = "", X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        var infoFrame = new FrameView
        { Title = "Info", X = Pos.Right(servicesFrame), Y = 0, Width = Dim.Fill(), Height = 3 };
        infoFrame.Add(_infoLabel);

        var outputFrame = new FrameView
        { Title = "Output", X = Pos.Right(servicesFrame), Y = Pos.Bottom(infoFrame), Width = Dim.Fill(), Height = Dim.Fill() };
#pragma warning disable CS0618
        _outputView = new TextView()
        { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = false, CanFocus = false };
#pragma warning restore CS0618
        outputFrame.Add(_outputView);

        Add(servicesFrame, keysFrame, infoFrame, outputFrame);

        _client.OutputLineReceived += line =>
        {
            _app.Invoke(() =>
            {
                var current = _outputView.Text?.ToString() ?? "";
                var clean = Ansi.Strip(line);
                _outputView.Text = current.Length > 0 ? current + "\n" + clean : clean;
                ScrollToBottom();
            });
        };

        _app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            _ = RefreshServicesAsync();
            return true;
        });

        _serviceList.SetFocus();
        _ = RefreshServicesAsync();
    }

    private async Task RefreshServicesAsync()
    {
        var services = await _client.GetServices();
        _app.Invoke(() =>
        {
            _services = services;
            _refreshing = true;
            var items = _services.Select(s =>
            {
                var indicator = s.IsRunning ? "+" : "-";
                return $" {indicator} {s.Name}";
            }).ToList();
            _serviceList.SetSource(new ObservableCollection<string>(items));
            if (_selectedIndex < _services.Count)
                _serviceList.SelectedItem = _selectedIndex;
            _refreshing = false;
            RefreshInfo();
        });
    }

    private void OnSelectedItemChanged()
    {
        if (_refreshing || _serviceList.SelectedItem is not int index) return;
        _selectedIndex = index;
        RefreshInfo();
        _ = SwitchOutputSubscription();
    }

    private async Task SwitchOutputSubscription()
    {
        var svc = GetSelectedService();
        if (svc is null || svc.Name == _subscribedService) return;
        _subscribedService = svc.Name;

        var lines = (await _client.GetOutput(svc.Name)).Select(Ansi.Strip).ToList();
        _app.Invoke(() =>
        {
            var height = Math.Max(1, _outputView.Frame.Height);
            var visible = lines.Count > height ? lines.Skip(lines.Count - height).ToList() : lines;
            _outputView.Text = string.Join("\n", visible);
            ScrollToBottom();
        });

        await _client.SubscribeOutput(svc.Name);
    }

    private void OnKeyDown(object? sender, Key e)
    {
        var svc = GetSelectedService();
        var ch = (char)e.AsRune.Value;
        var handled = true;

        switch (ch)
        {
            case 's': if (svc is not null) _ = _client.StartService(svc.Name); break;
            case 'S': _ = _client.StartAll(); break;
            case 'x': if (svc is not null) _ = _client.StopService(svc.Name); break;
            case 'X': _ = _client.StopAll(); break;
            case 'r': if (svc is not null) _ = _client.RestartService(svc.Name); break;
            case 'q': RequestStop(); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private void RefreshInfo()
    {
        var svc = GetSelectedService();
        if (svc is null) { _infoLabel.Text = ""; return; }
        var status = svc.IsRunning ? "RUNNING" : "STOPPED";
        var pid = svc.Pid?.ToString() ?? "-";
        _infoLabel.Text = $"PID: {pid,-8}  Uptime: {svc.Uptime,-10}  Status: {status}"
            + (string.IsNullOrWhiteSpace(svc.Description) ? "" : $"  {svc.Description}");
        _infoLabel.SetScheme(DarkScheme() with
        {
            Normal = new Attribute(svc.IsRunning ? Color.Green : Color.Red, Color.Black),
        });
    }

    private void ScrollToBottom() => _outputView.MoveEnd();

    public static Scheme DarkScheme() => new()
    {
        Normal = new Attribute(Color.Gray, Color.Black),
        Focus = new Attribute(Color.White, Color.DarkGray),
        HotNormal = new Attribute(Color.Cyan, Color.Black),
        HotFocus = new Attribute(Color.Cyan, Color.DarkGray),
        Disabled = new Attribute(Color.DarkGray, Color.Black),
    };

    private ServiceStatus? GetSelectedService()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _services.Count) return null;
        return _services[_selectedIndex];
    }
}
