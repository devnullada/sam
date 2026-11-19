using System.ComponentModel;

namespace ServiceManager.Dashboard;

/// <summary>
/// Lightweight view-model for a row in the service list. Held in a stable
/// ObservableCollection so periodic refreshes update state in place rather than
/// rebuilding the list (which would reset hover/selection every tick).
/// </summary>
public sealed class ServiceItemVm : INotifyPropertyChanged
{
    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }
    }

    private string? _description;
    public string? Description
    {
        get => _description;
        set
        {
            if (_description == value) return;
            _description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (_autoStart == value) return;
            _autoStart = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoStart)));
        }
    }

    public ServiceItemVm(string name, bool isRunning, string? description, bool autoStart)
    {
        _name = name;
        _isRunning = isRunning;
        _description = string.IsNullOrWhiteSpace(description) ? null : description;
        _autoStart = autoStart;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
