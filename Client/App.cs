using Terminal.Gui;
using ServiceManager.Shared;

namespace ServiceManager.Client;

public class ServiceManagerWindow : Window
{
    private readonly ServerClient _client;
    private readonly ListView _serviceList;
    private readonly Label _infoLabel;
    private readonly TextView _outputView;
    private List<ServiceStatus> _services = new();
    private int _selectedIndex;
    private bool _refreshing;
    private string? _subscribedService;

    public ServiceManagerWindow(ServerClient client)
    {
        _client = client;
        Title = "Service Manager";

        var dark = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.White, Color.DarkGray),
            HotNormal = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.Cyan, Color.DarkGray),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
        };

        ColorScheme = dark;

        var leftWidth = Dim.Percent(30);

        var servicesFrame = new FrameView("Services")
        { X = 0, Y = 0, Width = leftWidth, Height = Dim.Fill() - 8, ColorScheme = dark };

        _serviceList = new ListView(new List<string>())
        { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), AllowsMarking = false, CanFocus = true, ColorScheme = dark };
        _serviceList.SelectedItemChanged += OnSelectedItemChanged;
        _serviceList.KeyPress += OnKeyPress;
        servicesFrame.Add(_serviceList);

        var keysFrame = new FrameView("Keys")
        { X = 0, Y = Pos.Bottom(servicesFrame), Width = leftWidth, Height = Dim.Fill(), ColorScheme = dark };
        keysFrame.Add(new Label(
            " s  start       S  start all\n" +
            " x  stop        X  stop all\n" +
            " r  restart\n" +
            " q  quit")
        { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() });

        _infoLabel = new Label("") { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        var infoFrame = new FrameView("Info")
        { X = Pos.Right(servicesFrame), Y = 0, Width = Dim.Fill(), Height = 3, ColorScheme = dark };
        infoFrame.Add(_infoLabel);

        var outputFrame = new FrameView("Output")
        { X = Pos.Right(servicesFrame), Y = Pos.Bottom(infoFrame), Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = dark };
        _outputView = new TextView()
        { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = false, CanFocus = false, ColorScheme = dark };
        outputFrame.Add(_outputView);

        Add(servicesFrame, keysFrame, infoFrame, outputFrame);

        _client.OutputLineReceived += line =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                var current = _outputView.Text?.ToString() ?? "";
                _outputView.Text = current.Length > 0 ? current + "\n" + line : line;
                ScrollToBottom();
            });
        };

        Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(1), (MainLoop loop) =>
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
        Application.MainLoop?.Invoke(() =>
        {
            _services = services;
            _refreshing = true;
            var items = _services.Select(s =>
            {
                var indicator = s.IsRunning ? "+" : "-";
                return $" {indicator} {s.Name}";
            }).ToList();
            _serviceList.SetSource(items);
            if (_selectedIndex < _services.Count)
                _serviceList.SelectedItem = _selectedIndex;
            _refreshing = false;
            RefreshInfo();
        });
    }

    private void OnSelectedItemChanged(ListViewItemEventArgs args)
    {
        if (_refreshing) return;
        _selectedIndex = args.Item;
        RefreshInfo();
        _ = SwitchOutputSubscription();
    }

    private async Task SwitchOutputSubscription()
    {
        var svc = GetSelectedService();
        if (svc is null || svc.Name == _subscribedService) return;
        _subscribedService = svc.Name;

        var lines = await _client.GetOutput(svc.Name);
        Application.MainLoop?.Invoke(() =>
        {
            var height = Math.Max(1, _outputView.Frame.Height);
            var visible = lines.Count > height ? lines.Skip(lines.Count - height).ToList() : lines;
            _outputView.Text = string.Join("\n", visible);
            ScrollToBottom();
        });

        await _client.SubscribeOutput(svc.Name);
    }

    private void OnKeyPress(View.KeyEventEventArgs e)
    {
        var svc = GetSelectedService();
        var ch = (char)e.KeyEvent.KeyValue;
        var handled = true;

        switch (ch)
        {
            case 's': if (svc is not null) _ = _client.StartService(svc.Name); break;
            case 'S': _ = _client.StartAll(); break;
            case 'x': if (svc is not null) _ = _client.StopService(svc.Name); break;
            case 'X': _ = _client.StopAll(); break;
            case 'r': if (svc is not null) _ = _client.RestartService(svc.Name); break;
            case 'q': Application.RequestStop(); break;
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
        _infoLabel.Text = $"PID: {pid,-8}  Uptime: {svc.Uptime,-10}  Status: {status}";
        _infoLabel.ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(
                svc.IsRunning ? Color.Green : Color.Red, Color.Black),
            Focus = _infoLabel.ColorScheme.Focus,
            HotNormal = _infoLabel.ColorScheme.HotNormal,
            HotFocus = _infoLabel.ColorScheme.HotFocus,
        };
    }

    private void ScrollToBottom()
    {
        var text = _outputView.Text?.ToString() ?? "";
        var lineCount = text.Split('\n').Length;
        var viewportHeight = _outputView.Frame.Height;
        _outputView.ScrollTo(Math.Max(0, lineCount - viewportHeight + 1));
    }

    private ServiceStatus? GetSelectedService()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _services.Count) return null;
        return _services[_selectedIndex];
    }
}
