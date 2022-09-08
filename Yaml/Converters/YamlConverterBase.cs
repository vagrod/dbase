namespace Dbase.Yaml.Converters;

public abstract class YamlConverterBase
{

    protected enum ColumnActions
    {
        Add,
        Rename,
        Remove
    }

    /// <summary>
    /// Dbase type aliases
    /// </summary>
    protected static class DataTypes
    {
        public const string Uuid = "uuid";
        public const string String = "string";
        public const string Int = "int";
        public const string Float = "float";
        public const string Date = "date";
        public const string DateTime = "datetime";
        public const string Boolean = "bool";
        public const string Binary = "binary";
        public const string Big = "big";
    }
    
    protected record ForeignKeyData(
        bool HasForeignKey,
        string Schema,
        string Table,
        string Column
    );

    protected record ColumnData(
        string Name,
        string TypeAlias,
        string Type,
        bool HasDescription,
        string? Description,
        bool IsKey,
        bool IsOrdinal,
        bool IsRequired,
        ForeignKeyData ForeignKey
    );

    protected record ColumnActionData(
        ColumnActions Action,
        ColumnData? ColumnData,
        string? NewName
    );

    protected record StorageData(
        string Schema,
        string Table,
        bool HasDescription,
        string? Description
    );

    protected abstract string MapSqlType(string dataType, int? size);
    protected abstract ErrorOr<string> GenerateSqlCodeFromYamlEntries(IEnumerable<YamlProcessor.YamlEntry> entries);
    protected abstract string DefaultSchema { get; }

    protected StorageData GetStorageData(string data) {
        var mainPart = data;
        var descIndex = DetectDescIndexInAString(data) + 1;
        var hasDescription = descIndex > 0;
        var description = null as string;
        
        if (hasDescription) {
            var descContentIndex = descIndex + "desc ".Length;
            
            mainPart = data[..descIndex].Replace(",","").Trim();
            description = data[descContentIndex..];
        }
        var parts = mainPart.Split('.');
        var schema = parts.Length == 1 ? DefaultSchema : parts[0];
        var cleanName = parts.Length == 1 ? parts[0] : parts[1];
        
        return new StorageData(schema, cleanName, hasDescription, description);
    }
    
    protected ColumnActionData GetColumnActionData(string name, string data) {
        var parts = data.Split(',', ' ');
        var actionContentIndex = parts[0].Length + 1;
        var dataPart = data[actionContentIndex..].Trim();

        return parts[0].ToLower() switch
        {
            "add" => new ColumnActionData(ColumnActions.Add, GetColumnData(name, dataPart), null),
            "rename-to" => new ColumnActionData(ColumnActions.Rename, null, dataPart),
            "remove" => new ColumnActionData(ColumnActions.Remove, null, null),
            _ => throw new Exception($"Unknown column action: {parts[0]}")
        };
    }

    protected ForeignKeyData GetForeignKeyData(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return new ForeignKeyData(false, string.Empty, string.Empty, string.Empty);

        var parts = data.Split(',', ' ');
        var fkIndex = parts.Select(x => x.ToLower()).ToList().IndexOf("fk");
        var hasFk = fkIndex >= 0;
        
        if(!hasFk)
            return new ForeignKeyData(false, string.Empty, string.Empty, string.Empty);

        var fkData = parts[fkIndex + 1];
        var fkParts = fkData.Split('.');
        
        return fkParts.Length switch
        {
            1 => throw new Exception("Invalid FK definition"),
            2 => new ForeignKeyData(true, DefaultSchema, fkParts[0], fkParts[1]),
            3 => new ForeignKeyData(true, fkParts[0], fkParts[1], fkParts[2]),
            _ => throw new ArgumentException($"Invalid foreign key format: {data}")
        };
    }

    protected ColumnData GetColumnData(string name, string data) {
        var mainPart = data;
        var descIndex = DetectDescIndexInAString(data) + 1;
        var hasDescription = descIndex > 0;
        var description = null as string;
        
        if (hasDescription) {
            var descContentIndex = descIndex + "desc ".Length;
            
            mainPart = data[..descIndex];
            description = data[descContentIndex..];
        }
        
        var fieldData = mainPart.Split(' ', ',');
        var isKey = fieldData.Contains("key", StringComparer.OrdinalIgnoreCase);
        var isRequired = fieldData.Contains("required", StringComparer.OrdinalIgnoreCase);
        var isOrdinal = fieldData.Contains("ordinal", StringComparer.OrdinalIgnoreCase);
        var dataTypeString = fieldData[0];
        var dataTypeParts = dataTypeString.Split('-');
        var sqlType = MapSqlType(dataTypeParts[0].ToLower(), dataTypeParts.Length > 1 ? int.Parse(dataTypeParts[1]) : null);
        var fkData = GetForeignKeyData(data);

        return new ColumnData(name, dataTypeParts[0].ToLower(), sqlType, hasDescription, description, isKey, isOrdinal, isRequired, fkData);
    }
    
    private ErrorOr<IEnumerable<YamlProcessor.YamlEntry>> ProcessYamlData(string yamlData)
    {
        try {
            var yaml = new YamlProcessor();
            var reader = new DbaseYamlReader();
            var maybeEntries = reader.Read(yamlData);

            if (maybeEntries.Failed)
                return ErrorOr<IEnumerable<YamlProcessor.YamlEntry>>.Fail($"Error reading dbase yaml instruction: {maybeEntries.UnwrapError()}");

            return ErrorOr<IEnumerable<YamlProcessor.YamlEntry>>.Success(yaml.Process(maybeEntries.Unwrap()));
        }
        catch (Exception ex) {
            return ErrorOr<IEnumerable<YamlProcessor.YamlEntry>>.Fail($"Error reading dbase yaml instruction: {ex.Message}");
        }
    }
    
    public ErrorOr<string> Convert(string yamlData) {
        var maybeYaml = ProcessYamlData(yamlData);
        if(maybeYaml.Failed)
            return ErrorOr<string>.Fail(maybeYaml.UnwrapError());
                
        var entries = maybeYaml.Unwrap();
     
        return GenerateSqlCodeFromYamlEntries(entries);
    }
    
    private int DetectDescIndexInAString(string data) => new[]
    {
        data.IndexOf(" desc ", StringComparison.OrdinalIgnoreCase),
        data.IndexOf(",desc ", StringComparison.OrdinalIgnoreCase),
        data.IndexOf("\tdesc ", StringComparison.OrdinalIgnoreCase)
    }.Max();

}