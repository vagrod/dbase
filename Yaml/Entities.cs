using YamlDotNet.Serialization;

namespace Dbase.Yaml;

public interface IYamlEntry { public int? order { get; set; } }

[Serializable]
public class DbaseYaml
{
    public Storage storage { get; set; } = new();
    public Data data { get; set; } = new();
}

[Serializable]
public class Storage
{
    public List<StorageAdd> add { get; set; } = new();
    public List<StorageAlter> alter { get; set; } = new();
    public List<StorageRemove> remove { get; set; } = new();
}

[Serializable]
public class StorageAdd : IYamlEntry
{
    public string name { get; set; }
    public int? order { get; set; }
    public Dictionary<string, string> fields { get; set; } = new();
}

[Serializable]
public class StorageRemove : IYamlEntry
{
    public string name { get; set; }
    public int? order { get; set; }
}

[Serializable]
public class StorageAlter : IYamlEntry
{
    public string name { get; set; }
    [YamlMember(Alias = "new-name", ApplyNamingConventions = false)]
    public string newName { get; set; }
    public int? order { get; set; }
    public Dictionary<string, string> fields { get; set; } = new();
}

[Serializable]
public class Data
{
    public List<DataAdd> add { get; set; } = new();
    public List<DataAlter> alter { get; set; } = new();
    public List<DataRemove> remove { get; set; } = new();
}

[Serializable]
public class DataAdd : IYamlEntry
{
    public string storage { get; set; }
    public int? order { get; set; }
    public Dictionary<string, string> fields { get; set; } = new();
}

[Serializable]
public class DataRemove : IYamlEntry
{
    public string storage { get; set; }
    public string clause { get; set; }
    public int? order { get; set; }
}

[Serializable]
public class DataAlter : IYamlEntry
{
    public string storage { get; set; }
    public string clause { get; set; }
    public int? order { get; set; }
    public Dictionary<string, string> fields { get; set; } = new();
}