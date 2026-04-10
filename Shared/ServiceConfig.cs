using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ServiceManager.Shared;

public class ServiceEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string? WorkingDirectory { get; set; }
    public bool AutoStart { get; set; }
}

public class ServiceConfig
{
    public int Port { get; set; } = 14040;
    public List<ServiceEntry> Services { get; set; } = new();

    public static ServiceConfig Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<ServiceConfig>(yaml);
    }

    public static ServiceConfig LoadFromFile(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public string ToYaml()
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return serializer.Serialize(this);
    }

    public void SaveToFile(string path)
    {
        File.WriteAllText(path, ToYaml());
    }
}
