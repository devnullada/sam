using ServiceManager.Shared;

namespace ServiceManager.Server;

public class ServiceManagerCore
{
    private readonly Dictionary<string, ManagedService> _services = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ManagedService> Services => _services;
    public List<string> ServiceNames { get; } = new();
    public string? BaseDirectory { get; set; }

    public void LoadConfig(ServiceConfig config)
    {
        foreach (var entry in config.Services)
        {
            if (_services.ContainsKey(entry.Name)) continue;
            var svc = new ManagedService(entry) { BaseDirectory = BaseDirectory };
            _services[entry.Name] = svc;
            ServiceNames.Add(entry.Name);
        }
    }

    public void ReloadConfig(ServiceConfig config)
    {
        var newNames = new HashSet<string>(config.Services.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

        // Remove services no longer in config
        var toRemove = ServiceNames.Where(n => !newNames.Contains(n)).ToList();
        foreach (var name in toRemove)
        {
            if (_services.TryGetValue(name, out var svc))
            {
                svc.Stop();
                _services.Remove(name);
            }
            ServiceNames.Remove(name);
        }

        // Add new services, update existing ones
        foreach (var entry in config.Services)
        {
            if (_services.TryGetValue(entry.Name, out var existing))
            {
                existing.UpdateEntry(entry);
            }
            else
            {
                var svc = new ManagedService(entry) { BaseDirectory = BaseDirectory };
                _services[entry.Name] = svc;
                ServiceNames.Add(entry.Name);
            }
        }
    }

    public void StartAll(ServiceConfig config)
    {
        foreach (var entry in config.Services)
        {
            if (entry.AutoStart && _services.TryGetValue(entry.Name, out var svc))
                TryStart(svc);
        }
    }

    public void TryStart(ManagedService svc) { try { svc.Start(); } catch { } }
    public void StopAll() { foreach (var svc in _services.Values) svc.Stop(); }
}
