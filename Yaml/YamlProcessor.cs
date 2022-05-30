namespace Dbase.Yaml;

public class YamlProcessor
{

    public enum EntryKind
    {
        Undefined,
        
        StorageAdd,
        StorageRemove,
        StorageAlter,
        
        DataAdd,
        DataRemove,
        DataAlter
    }

    public class YamlEntry
    {
        public EntryKind Kind { get; init; }
        public IYamlEntry Entry { get; init; }
        public int Order { get; init; }
    }

    private EntryKind SelectEntryKind(IYamlEntry entry) => entry switch
    {
        StorageAdd => EntryKind.StorageAdd,
        StorageRemove => EntryKind.StorageRemove,
        StorageAlter => EntryKind.StorageAlter,
        DataAdd => EntryKind.DataAdd,
        DataRemove => EntryKind.DataRemove,
        DataAlter => EntryKind.DataAlter,
        _ => EntryKind.Undefined
    };

    public IEnumerable<YamlEntry> Process(DbaseYaml yaml) {
        var entries = 
                (yaml.storage.add).Union((IEnumerable<IYamlEntry>)yaml.storage.alter).Union(yaml.storage.remove)
            .Union(
                (yaml.data.add).Union((IEnumerable<IYamlEntry>)yaml.data.alter).Union(yaml.data.remove)
            );

        return entries.Select(entry => new YamlEntry
        {
            Order = entry.order ?? 0, 
            Kind = SelectEntryKind(entry), 
            Entry = entry
        }).OrderBy(x => x.Order);
    }

}