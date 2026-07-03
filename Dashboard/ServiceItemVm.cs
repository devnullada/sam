using System.ComponentModel;

namespace ServiceManager.Dashboard;

/// <summary>
/// Lightweight view-model for a row in the service list. Held in a stable
/// ObservableCollection so periodic refreshes update state in place rather than
/// rebuilding the list (which would reset hover/selection every tick).
/// </summary>
public sealed class ServiceItemVm : INotifyPropertyChanged
{
    public string Name { get; }

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

    public ServiceItemVm(string name, bool isRunning)
    {
        Name = name;
        _isRunning = isRunning;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
