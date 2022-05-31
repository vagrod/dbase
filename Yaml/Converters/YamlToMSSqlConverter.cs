using System.Text;

namespace Dbase.Yaml.Converters;

public class YamlToMSSqlConverter : YamlConverterBase
{

    private readonly bool _shouldWriteCommits;

    public YamlToMSSqlConverter(ServerDescription server) {
        var preprocessingOn = server.Options == null || (server.Options != null && server.Options.ContainsKey(ServerOptions.NoPreprocessing) && server.Options[ServerOptions.NoPreprocessing].ToLowerInvariant() != "true");
        
        _shouldWriteCommits = preprocessingOn;
    }

    protected override string DefaultSchema => "dbo";

    #region Processing

    /// <summary>
    /// Dbase-to-mssql datatypes mapping
    /// </summary>
    /// <param name="type">Dbase type alias</param>
    /// <param name="size">Additional datatype size (is any)</param>
    /// <returns></returns>
    protected override string MapSqlType(string type, int? size) => type switch
    {
        DataTypes.Uuid => "uniqueidentifier",
        DataTypes.Big => "bigint",
        DataTypes.Binary => "image",
        DataTypes.String => size.HasValue ? $"nvarchar({size})" : "nvarchar(max)",
        DataTypes.Int => "int",
        DataTypes.Float => "float",
        DataTypes.Boolean => "bit",
        DataTypes.Date => "datetime",
        DataTypes.DateTime => "datetime",
        _ => "nvarchar(max)"
    };
    
    private void AppendGoIfNeeded(StringBuilder sb) {
        // If options is set to preprocess patch before applying, add "GO"
        if (_shouldWriteCommits)
            sb.AppendLine("GO");
    }

    private string ProcessStorageAdd(StorageAdd entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.name);
        
        sb.AppendLine(@$"IF (NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{storageData.Schema}'))
BEGIN");
        sb.AppendLine($"  EXEC ('CREATE SCHEMA [{storageData.Schema}] AUTHORIZATION [dbo]')");
        sb.AppendLine("END");
        sb.AppendLine();
        sb.AppendLine(@$"IF (NOT EXISTS (SELECT * 
  FROM INFORMATION_SCHEMA.TABLES 
  WHERE TABLE_SCHEMA = '{storageData.Schema}' 
  AND  TABLE_NAME = '{storageData.Table}'))
BEGIN");
        sb.AppendLine($"  CREATE TABLE {storageData.Schema}.[{storageData.Table}] (");
        
        var foreignKeys = new List<string>();
        var descriptions = new List<string>();
        var guidKey = "";
        var i = 0;
        
        if (storageData.HasDescription) 
            descriptions.Add($@"  EXEC sp_addextendedproperty N'MS_Description', N'{storageData.Description!.Replace('\'', '"')}', N'SCHEMA', N'{storageData.Schema}', N'TABLE', N'{storageData.Table}'");
        
        foreach (var field in entry.fields) {
            var fieldInfo = GetColumnData(field.Key, field.Value);
            var isLast = i == entry.fields.Count - 1 && foreignKeys.Count == 0;

            if (fieldInfo.HasDescription) 
                descriptions.Add($@"  EXEC sp_addextendedproperty N'MS_Description', N'{fieldInfo.Description!.Replace('\'', '"')}', N'SCHEMA', N'{storageData.Schema}', N'TABLE', N'{storageData.Table}', N'COLUMN', N'{fieldInfo.Name}'");

            if (fieldInfo.ForeignKey.HasForeignKey) {
                foreignKeys.Add($"    CONSTRAINT FK_{storageData.Schema}_{storageData.Table}_{field.Key}_{fieldInfo.ForeignKey.Schema}_{fieldInfo.ForeignKey.Table}_{fieldInfo.ForeignKey.Column} FOREIGN KEY ([{field.Key}]) REFERENCES [{fieldInfo.ForeignKey.Schema}].[{fieldInfo.ForeignKey.Table}]([{fieldInfo.ForeignKey.Column}])");
            }
            
            sb.Append($"    [{field.Key}] {fieldInfo.Type} {(fieldInfo.IsRequired || fieldInfo.IsKey ? "NOT NULL" : "NULL")}");

            if(!fieldInfo.IsKey && fieldInfo.IsOrdinal && fieldInfo.TypeAlias is DataTypes.Int or DataTypes.Big)
                sb.Append(" IDENTITY(1,1)");
            
            if(fieldInfo.IsKey && fieldInfo.TypeAlias is DataTypes.Uuid)
                guidKey = field.Key;
            
            if (fieldInfo.IsKey && fieldInfo.TypeAlias is not DataTypes.Uuid) {
                sb.AppendLine();
                sb.Append("      ");

                if (fieldInfo.TypeAlias is DataTypes.Int or DataTypes.Big)
                    sb.Append("IDENTITY(1,1) ");
                
                sb.Append($"PRIMARY KEY");
            }
            
            sb.AppendLine(isLast ? "" : ",");

            i++;
        }

        foreach (var foreignKey in foreignKeys) {
            var isLast = foreignKey == foreignKeys.Last();
            sb.AppendLine(foreignKey + (isLast ? "" : ","));
        }
        
        sb.AppendLine($"  ) ON [PRIMARY]");

        if (!string.IsNullOrEmpty(guidKey)) {
            sb.AppendLine($"  ALTER TABLE {storageData.Schema}.[{storageData.Table}] ADD CONSTRAINT [PK_{storageData.Table}_{guidKey}] PRIMARY KEY CLUSTERED" +
                          $"([{guidKey}]) WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]");
            sb.AppendLine($"  ALTER TABLE {storageData.Schema}.[{storageData.Table}] ADD DEFAULT (newid()) FOR [{guidKey}]");
        }

        sb.AppendLine($"  ALTER TABLE {storageData.Schema}.[{storageData.Table}] SET (LOCK_ESCALATION = TABLE)");

        foreach(var description in descriptions) {
            sb.AppendLine(description);
        }
        
        sb.AppendLine("END");

        return sb.ToString();
    }
    
    private string ProcessStorageAlter(StorageAlter entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.name);

        foreach (var field in entry.fields) {
            var actionData = GetColumnActionData(field.Key, field.Value);

            if (actionData.Action == ColumnActions.Add) {
                sb.AppendLine($"ALTER TABLE {storageData.Schema}.[{storageData.Table}] ADD [{actionData.ColumnData!.Name}] {actionData.ColumnData!.Type} {(actionData.ColumnData!.IsRequired ? "NOT NULL" : "NULL")}");
                
                AppendGoIfNeeded(sb);

                if(actionData.ColumnData!.HasDescription)
                    sb.AppendLine($@"EXEC sp_addextendedproperty N'MS_Description', N'{actionData.ColumnData!.Description}', N'SCHEMA', N'{storageData.Schema}', N'TABLE', N'{storageData.Table}', N'COLUMN', N'{actionData.ColumnData!.Name}'");
                
                if (actionData.ColumnData!.ForeignKey.HasForeignKey) {
                    sb.AppendLine($"ALTER TABLE {storageData.Schema}.[{storageData.Table}] ADD CONSTRAINT FK_{storageData.Schema}_{storageData.Table}_{field.Key}_{actionData.ColumnData!.ForeignKey.Schema}_{actionData.ColumnData!.ForeignKey.Table}_{actionData.ColumnData!.ForeignKey.Column} FOREIGN KEY ([{field.Key}]) REFERENCES [{actionData.ColumnData!.ForeignKey.Schema}].[{actionData.ColumnData!.ForeignKey.Table}]([{actionData.ColumnData!.ForeignKey.Column}])");
                
                    AppendGoIfNeeded(sb);
                }
            }
            if (actionData.Action == ColumnActions.Rename) {
                sb.AppendLine($"EXEC sp_rename '{entry.name}.{field.Key}', {actionData.NewName!}, 'COLUMN'");
                AppendGoIfNeeded(sb);
            }
            if (actionData.Action == ColumnActions.Remove) {
                sb.AppendLine($"ALTER TABLE {storageData.Schema}.[{storageData.Table}] DROP COLUMN [{field.Key}]");
                AppendGoIfNeeded(sb);
            }
            
            sb.AppendLine();
        }
        
        if (!string.IsNullOrWhiteSpace(entry.newName)) {
            sb.AppendLine($"EXEC sp_rename '{entry.name}', '{entry.newName}';");
        }
        
        return sb.ToString();
    }
    
    private string ProcessStorageRemove(StorageRemove entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.name);
        
        sb.AppendLine($"DROP TABLE {storageData.Schema}.[{storageData.Table}]");
        AppendGoIfNeeded(sb);
        
        return sb.ToString();
    }
    
    private string ProcessDataAdd(DataAdd entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.storage);

        sb.AppendLine($"INSERT INTO {storageData.Schema}.[{storageData.Table}] ({string.Join(", ", entry.fields.Keys)})");
        sb.AppendLine($"  VALUES ({string.Join(", ", entry.fields.Values.Select(x => $"{x.Replace("\"", "'")}"))})");
        
        return sb.ToString();
    }
    
    private string ProcessDataAlter(DataAlter entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.storage);

        sb.AppendLine($"UPDATE {storageData.Schema}.[{storageData.Table}] SET {string.Join(", ", entry.fields.Select(x => $"[{x.Key}] = {x.Value.Replace("\"", "'")}"))}");
        if(!string.IsNullOrWhiteSpace(entry.clause))
            sb.AppendLine($"  WHERE {entry.clause.Replace("'''", "'").Replace("\"", "'")}");
        
        return sb.ToString();
    }
    
    private string ProcessDataRemove(DataRemove entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.storage);

        sb.AppendLine($"DELETE FROM {storageData.Schema}.[{storageData.Table}]");
        if(!string.IsNullOrWhiteSpace(entry.clause))
            sb.AppendLine($"  WHERE {entry.clause.Replace("'''", "'").Replace("\"", "'")}");
        
        return sb.ToString();
    }
    
    #endregion 
        
    protected override ErrorOr<string> GenerateSqlCodeFromYamlEntries(IEnumerable<YamlProcessor.YamlEntry> entries)
    {
        try {
            var sb = new StringBuilder();
            foreach (var entry in entries) {
                sb.AppendLine(
                    entry.Kind switch
                    {
                        YamlProcessor.EntryKind.StorageAdd => ProcessStorageAdd((StorageAdd)entry.Entry),
                        YamlProcessor.EntryKind.StorageAlter => ProcessStorageAlter((StorageAlter)entry.Entry),
                        YamlProcessor.EntryKind.StorageRemove => ProcessStorageRemove((StorageRemove)entry.Entry),
                        YamlProcessor.EntryKind.DataAdd => ProcessDataAdd((DataAdd)entry.Entry),
                        YamlProcessor.EntryKind.DataAlter => ProcessDataAlter((DataAlter)entry.Entry),
                        YamlProcessor.EntryKind.DataRemove => ProcessDataRemove((DataRemove)entry.Entry),
                        
                        _ => throw new Exception($"Unknown entry kind: {entry.Kind}")
                    }
                );

                AppendGoIfNeeded(sb);
                
                sb.AppendLine();
            }

            return ErrorOr<string>.Success(sb.ToString());
        }
        catch (Exception ex) {
            return ErrorOr<string>.Fail(ex.Message);
        }
    }
}