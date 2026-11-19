using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ServiceManager.Shared;

namespace ServiceManager.Client;

public class ServerClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _wsBaseUrl;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public event Action<string>? OutputLineReceived;

    public ServerClient(int port)
    {
        _wsBaseUrl = $"ws://localhost:{port}";
        _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
    }

    public async Task<bool> HealthCheck()
    {
        try { var r = await _http.GetAsync("/health"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<List<ServiceStatus>> GetServices()
    {
        try
        {
            var json = await _http.GetStringAsync("/services");
            return JsonSerializer.Deserialize<List<ServiceStatus>>(json, _jsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public async Task StartService(string name) { try { await _http.PostAsync($"/services/{name}/start", null); } catch { } }
    public async Task StopService(string name) { try { await _http.PostAsync($"/services/{name}/stop", null); } catch { } }
    public async Task RestartService(string name) { try { await _http.PostAsync($"/services/{name}/restart", null); } catch { } }
    public async Task StartAll() { try { await _http.PostAsync("/services/start-all", null); } catch { } }
    public async Task StopAll() { try { await _http.PostAsync("/services/stop-all", null); } catch { } }

    public async Task<List<string>> GetOutput(string name, int lines = 100)
    {
        try
        {
            var json = await _http.GetStringAsync($"/services/{name}/output?lines={lines}");
            return JsonSerializer.Deserialize<List<string>>(json, _jsonOpts) ?? new();
        }
        catch { return new(); }
    }

    public async Task SubscribeOutput(string serviceName)
    {
        UnsubscribeOutput();
        _wsCts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        try
        {
            await _ws.ConnectAsync(new Uri($"{_wsBaseUrl}/services/{serviceName}/ws"), _wsCts.Token);
            _ = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                while (_ws.State == WebSocketState.Open && !_wsCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _ws.ReceiveAsync(buffer, _wsCts.Token);
                        if (result.MessageType == WebSocketMessageType.Text)
                            OutputLineReceived?.Invoke(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        else if (result.MessageType == WebSocketMessageType.Close) break;
                    }
                    catch { break; }
                }
            });
        }
        catch { }
    }

    public void UnsubscribeOutput()
    {
        _wsCts?.Cancel();
        _ws?.Dispose();
        _ws = null;
        _wsCts = null;
    }

    public void Dispose()
    {
        UnsubscribeOutput();
        _http.Dispose();
    }
}
