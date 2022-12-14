using System.IO;
using System.Text;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Dbase;

/// <summary>
/// Class that describes the options of a single server (its connection string, its name, etc.)
/// </summary>
[Serializable]
public class ServerDescription
{
    public ServerDescription()
    {
        ConfigurationName = string.Empty;
        DnsName = string.Empty;
        Type = string.Empty;
        Aliases = new();
    }
    
    /// <summary>
    /// Name of the configuration
    /// </summary>
    public string ConfigurationName { get; set; }
    /// <summary>
    /// Name of the server in the network (or its IP)
    /// </summary>
    public string DnsName { get; set; }
    /// <summary>
    /// Type of the server (see <see cref="ProcessorTypes"/>)
    /// </summary>
    public string Type { get; set; }
    /// <summary>
    /// Database user
    /// </summary>
    public string? DatabaseUser { get; set; }
    /// <summary>
    /// Database user password
    /// </summary>
    public string? Password { get; set; }
    /// <summary>
    /// Server port
    /// </summary>
    public int? Port  { get; set; }
    /// <summary>
    /// Folder for storing dbase backups for this server
    /// </summary>
    public string? BackupFolder { get; set; }
    /// <summary>
    /// Whether to use strict versioning on this server
    /// </summary>
    public bool? StrictVersioning { get; set; }
    /// <summary>
    /// Other server options (type-dependent)
    /// </summary>
    public Dictionary<string,string>? Options { get; set; }
    /// <summary>
    /// Aliases list for this server (variables list with their values)
    /// </summary>
    public Dictionary<string,string> Aliases { get; set; }
}

[Serializable]
public class Configuration
{
    /// <summary>
    /// A list of registered servers
    /// </summary>
    public List<ServerDescription>? Servers { get; set; } = new();

    public void Save()
    {
        try
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            File.WriteAllText("config.yaml", serializer.Serialize(this));
        }
        catch (Exception ex)
        {
            throw new Exception($"Error saving \"config.yaml\" file\n{ex}");  
        }
    }

    public static async Task<Configuration> Load()
    {
        try {
            if (!File.Exists("config.yaml"))
                await File.WriteAllTextAsync("config.yaml", "servers:");
        }
        catch (Exception ex) {
            throw new Exception($"Error creating default \"config.yaml\" file\n{ex}");
        }
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var config = deserializer.Deserialize<Configuration>(await File.ReadAllTextAsync("config.yaml", Encoding.UTF8));
            
            config.Servers ??= new();
            
            return config;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error reading \"config.yaml\" file\n{ex}");
        }
    }
    
}
