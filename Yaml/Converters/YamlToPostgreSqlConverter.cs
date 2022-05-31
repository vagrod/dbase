using System.Text;

namespace Dbase.Yaml.Converters;

public class YamlToPostgreSqlConverter : YamlConverterBase
{
    
    private readonly bool _shouldWriteCommits;

    public YamlToPostgreSqlConverter(ServerDescription server) {
        var preprocessingOn = server.Options == null || (server.Options != null && server.Options.ContainsKey(ServerOptions.NoPreprocessing) && server.Options[ServerOptions.NoPreprocessing].ToLowerInvariant() != "true");
        
        _shouldWriteCommits = preprocessingOn;
    }
    
    protected override string DefaultSchema => "public";

    #region Processing

    /// <summary>
    /// Dbase-to-postgres datatypes mapping
    /// </summary>
    /// <param name="type">Dbase type alias</param>
    /// <param name="size">Additional datatype size (is any)</param>
    /// <returns></returns>
    protected override string MapSqlType(string type, int? size) => type switch
    {
        DataTypes.Uuid => "uuid",
        DataTypes.Big => "bigint",
        DataTypes.Binary => "bytea",
        DataTypes.String => size.HasValue ? $"varchar({size})" : "text",
        DataTypes.Int => "integer",
        DataTypes.Float => "decimal(10, 4)",
        DataTypes.Boolean => "boolean",
        DataTypes.Date => "date",
        DataTypes.DateTime => "timestamp",
        _ => "text"
    };
    
    private void AppendCommitIfNeeded(StringBuilder sb) {
        // If options is set to preprocess patch before applying, add "commit"
        if (_shouldWriteCommits)
            sb.AppendLine("commit;");
    }

    private string ProcessStorageAdd(StorageAdd entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.name);

        sb.AppendLine("do $$");
        sb.AppendLine("  begin");
        sb.AppendLine($"    if not exists(select schema_name from information_schema.schemata where schema_name = '{storageData.Schema}') then");
        sb.AppendLine($"        create schema \"{storageData.Schema}\";");
        sb.AppendLine($"    end if;");
        sb.AppendLine("  end");
        sb.AppendLine("$$;");
        sb.AppendLine();
        
        AppendCommitIfNeeded(sb);

        sb.AppendLine();
        sb.AppendLine("do $$");
        sb.AppendLine("  begin");
        sb.AppendLine(@$"    if not exists (
        select from pg_catalog.pg_class c
        join   pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        where  n.nspname = '{storageData.Schema}'
        and    c.relname = '{storageData.Table}'
    ) then");
        
        sb.AppendLine($"      create table \"{storageData.Schema}\".\"{storageData.Table}\" (");
        
        var foreignKeys = new List<string>();
        var foreignKeysConstraints = new List<string>();
        var descriptions = new List<string>();
        var hasKey = false;
        var keyName = string.Empty;
        var i = 0;
        
        if (storageData.HasDescription) 
            descriptions.Add($"      comment on table \"{storageData.Schema}\".\"{storageData.Table}\" is '{storageData.Description!.Replace('\'', '"')}';");
        
        foreach (var field in entry.fields) {
            var isLast = i == entry.fields.Count - 1 && foreignKeys.Count == 0;
            var fieldInfo = GetColumnData(field.Key, field.Value);
            var guidKey = String.Empty;
            
            if (fieldInfo.HasDescription) 
                descriptions.Add($"      comment on column \"{storageData.Schema}\".\"{storageData.Table}\".\"{fieldInfo.Name}\" is '{fieldInfo.Description}';");

            var calculatedType = fieldInfo.IsOrdinal && fieldInfo.TypeAlias is DataTypes.Int or DataTypes.Big ? "serial" : fieldInfo.Type;
            
            sb.Append($"        \"{field.Key}\" {calculatedType} {(fieldInfo.IsRequired || fieldInfo.IsKey ? "not null" : "null")}");

            if (fieldInfo.IsKey && !hasKey) {
                hasKey = true;
                keyName = field.Key;
            }

            if(fieldInfo.IsKey && fieldInfo.TypeAlias == DataTypes.Uuid)
                guidKey = field.Key;
            
            if (fieldInfo.IsKey && string.IsNullOrEmpty(guidKey)) {
                sb.AppendLine();
                sb.AppendLine($"          generated always as identity{(isLast ? "" : ",")}");
            }
            else {
                if (!string.IsNullOrEmpty(guidKey)) {
                    sb.AppendLine(" default uuid_generate_v4()");
                    sb.Append($"          constraint {storageData.Table.ToLower()}_{field.Key.ToLower()}_pk primary key");
                }
                
                sb.AppendLine(isLast ? "" : ",");
            }
            
            if (fieldInfo.ForeignKey.HasForeignKey) {
                var fkName = $"fk_{storageData.Schema.ToLower()}_{storageData.Table.ToLower()}_{fieldInfo.Name.ToLower()}_{fieldInfo.ForeignKey.Schema.ToLower()}_{fieldInfo.ForeignKey.Table.ToLower()}_{fieldInfo.ForeignKey.Column.ToLower()}";

                foreignKeys.Add($"      create unique index {fkName} " +
                                $"\n        on \"{fieldInfo.ForeignKey.Schema}\".\"{fieldInfo.ForeignKey.Table}\"(\"{fieldInfo.ForeignKey.Column}\");");
                foreignKeysConstraints.Add($"        constraint {fkName} foreign key(\"{field.Key}\") references \"{fieldInfo.ForeignKey.Schema}\".\"{fieldInfo.ForeignKey.Table}\"(\"{fieldInfo.ForeignKey.Column}\")");
            }

            i++;
        }

        foreach (var foreignKeysConstraint in foreignKeysConstraints) {
            var isLast = foreignKeysConstraints.IndexOf(foreignKeysConstraint) == foreignKeysConstraints.Count - 1;
            sb.AppendLine(foreignKeysConstraint + (isLast ? "" : ","));
        }
        
        sb.AppendLine($"      );");

        if (hasKey) {
            sb.AppendLine($@"      create unique index {storageData.Table.ToLower()}_{keyName.ToLower()}_index
        on ""{storageData.Schema}"".""{storageData.Table}""(""{keyName}"");");
        }
        foreach (var foreignKey in foreignKeys) {
            sb.AppendLine(foreignKey);
        }
        
        foreach(var description in descriptions) {
            sb.AppendLine(description);
        }

        sb.AppendLine("    end if;");
        sb.AppendLine("  end");
        sb.AppendLine("$$;");
        sb.AppendLine();

        return sb.ToString();
    }

    private string ProcessStorageAlter(StorageAlter entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.name);

        foreach (var field in entry.fields) {
            var actionData = GetColumnActionData(field.Key, field.Value);
            
            if (actionData.Action == ColumnActions.Add) {
                sb.Append($"alter table \"{storageData.Schema}\".\"{storageData.Table}\" add column \"{actionData.ColumnData!.Name}\" {actionData.ColumnData.Type} {(actionData.ColumnData.IsRequired ? "not null" : "null")}");
                
                if (actionData.ColumnData!.ForeignKey.HasForeignKey) {
                    var fkName = $"fk_{storageData.Schema.ToLower()}_{storageData.Table.ToLower()}_{actionData.ColumnData!.Name.ToLower()}_{actionData.ColumnData!.ForeignKey.Schema.ToLower()}_{actionData.ColumnData!.ForeignKey.Table.ToLower()}_{actionData.ColumnData!.ForeignKey.Column.ToLower()}";
                    
                    sb.Append($"\n  constraint {fkName} references \"{actionData.ColumnData!.ForeignKey.Schema}\".\"{actionData.ColumnData!.ForeignKey.Table}\"(\"{actionData.ColumnData!.ForeignKey.Column}\")");
                }

                sb.AppendLine(";");
                
                if (actionData.ColumnData!.HasDescription) 
                    sb.AppendLine($"comment on column \"{storageData.Schema}\".\"{storageData.Table}\".\"{actionData.ColumnData!.Name}\" is '{actionData.ColumnData!.Description!.Replace('\'', '"')}';");
            }
            if (actionData.Action == ColumnActions.Rename) {
                sb.AppendLine($"alter table \"{storageData.Schema}\".\"{storageData.Table}\" rename column \"{field.Key}\" to \"{actionData.NewName!}\";");
            }
            if (actionData.Action == ColumnActions.Remove) {
                sb.AppendLine($"alter table \"{storageData.Schema}\".\"{storageData.Table}\" drop column \"{field.Key}\";");
            }
            
            sb.AppendLine();
        }

        return sb.ToString();
    }
    
    private string ProcessStorageRemove(StorageRemove entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.name);

        sb.AppendLine($"drop table if exists \"{storageData.Schema}\".\"{storageData.Table}\";");
        
        return sb.ToString();
    }
    
    private string ProcessDataAdd(DataAdd entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.storage);

        sb.AppendLine($"insert into \"{storageData.Schema}\".\"{storageData.Table}\" ({string.Join(", ", entry.fields.Keys)})");
        sb.AppendLine($"  values ({string.Join(", ", entry.fields.Values.Select(x => $"{x.Replace("\"", "'")}"))});");
        
        return sb.ToString();
    }
    
    private string ProcessDataAlter(DataAlter entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.storage);

        sb.AppendLine($"update \"{storageData.Schema}\".\"{storageData.Table}\" set {string.Join(", ", entry.fields.Select(x => $"\"{x.Key}\" = {x.Value.Replace("\"", "'")}"))}");
        if(!string.IsNullOrWhiteSpace(entry.clause))
            sb.AppendLine($"  where {entry.clause.Replace("'''", "'").Replace("\"", "'")};");
        
        return sb.ToString();
    }
    
    private string ProcessDataRemove(DataRemove entry) {
        var sb = new StringBuilder();
        var storageData = GetStorageData(entry.storage);

        sb.Append($"delete from \"{storageData.Schema}\".\"{storageData.Table}\"");
        if (!string.IsNullOrWhiteSpace(entry.clause)) {
            sb.AppendLine();
            sb.Append($"  where {entry.clause.Replace("'''", "'").Replace("\"", "'")}");
        }
        sb.AppendLine(";");

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
               
                AppendCommitIfNeeded(sb);
            }

            return ErrorOr<string>.Success(sb.ToString());
        }
        catch (Exception ex) {
            return ErrorOr<string>.Fail(ex.Message);
        }
    }

}