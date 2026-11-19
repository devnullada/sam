using ServiceManager.Shared;

namespace ServiceManager.Server;

public class ServiceManagerCore
{
    private readonly Dictionary<string, ManagedService> _services = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ManagedService> Services => _services;
    public List<string> ServiceNames { get; private set; } = new();
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

        Reorder(config.Services.Select(e => e.Name));
    }

    // Swapped in as a new list so a concurrent /services listing never sees it half-built
    public void Reorder(IEnumerable<string> names)
    {
        ServiceNames = names.Where(_services.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Re-keys the live service so its process and output survive the rename
    public void Rename(string oldName, string newName)
    {
        if (!_services.Remove(oldName, out var svc)) return;
        svc.Rename(newName);
        _services[newName] = svc;
        var idx = ServiceNames.FindIndex(n => n.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) ServiceNames[idx] = newName;
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
