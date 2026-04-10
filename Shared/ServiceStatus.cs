namespace ServiceManager.Shared;

public class ServiceStatus
{
    public string Name { get; set; } = "";
    public bool IsRunning { get; set; }
    public int? Pid { get; set; }
    public string Uptime { get; set; } = "-";
    public bool AutoStart { get; set; }
}
