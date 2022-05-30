using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Dbase.Yaml.Converters;

namespace Dbase;

/// <summary>
/// PostgreSQL database processor.
/// </summary>
public class MSSqlDatabaseProcessor : DatabaseProcessorBase
{
    private string ConnectionString { get; }
    private string MasterConnectionString { get; }
    private bool IsUsingIntegratedSecurity { get; }

    public MSSqlDatabaseProcessor(ServerDescription serverConfig, string databaseName) : base(serverConfig, databaseName) {
        IsUsingIntegratedSecurity = string.IsNullOrEmpty(serverConfig.DatabaseUser);

        if (!IsUsingIntegratedSecurity) {
            ConnectionString = $"Data Source={ServerConfig.DnsName};Initial Catalog={DatabaseName};User Id={ServerConfig.DatabaseUser};Password={ServerConfig.Password};";
            MasterConnectionString = $"Data Source={ServerConfig.DnsName};Initial Catalog=master;User Id={ServerConfig.DatabaseUser};Password={ServerConfig.Password};";
        }
        else {
            ConnectionString = $"Data Source={ServerConfig.DnsName};Initial Catalog={DatabaseName};persist security info=True;Integrated Security=SSPI;";
            MasterConnectionString = $"Data Source={ServerConfig.DnsName};Initial Catalog=master;persist security info=True;Integrated Security=SSPI;";
        }
    }

    public override string ProcessorType => ProcessorTypes.MsSql;

    /// <summary>
    /// Initializes the database to work with dbase
    /// </summary>
    public override async Task<Result> InitializeDatabaseAsync()
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();
            
            // Create dbase schema
            await using var commandSchema = new SqlCommand("CREATE SCHEMA dbase", connection, transaction);
            await commandSchema.ExecuteNonQueryAsync();
            
            // Create history table
            await using var commandHistory = new SqlCommand($"CREATE TABLE dbase.{HistoryTableName} ({HistoryFields.Id} INT IDENTITY(1,1) PRIMARY KEY, [{HistoryFields.Date}] DATETIME NOT NULL, {HistoryFields.Major} INT NOT NULL, {HistoryFields.Minor} INT NOT NULL, {HistoryFields.Code} NTEXT NOT NULL)", connection, transaction);
            await commandHistory.ExecuteNonQueryAsync();
            
            // Create meta table
            await using var commandMeta = new SqlCommand($"CREATE TABLE dbase.{MetaTableName} ({MetaFields.DBaseVersion} NVARCHAR(10) NOT NULL, {MetaFields.LastUpdated} DATETIME NOT NULL)", connection, transaction);
            await commandMeta.ExecuteNonQueryAsync();
            
            // Write the necessary data
            await using var commandMetadata = new SqlCommand($"INSERT INTO dbase.{MetaTableName} ({MetaFields.DBaseVersion}, {MetaFields.LastUpdated}) VALUES (@version, @date)", connection, transaction);
            commandMetadata.Parameters.AddWithValue("@version", Program.Version);
            commandMetadata.Parameters.AddWithValue("@date", DateTime.Now);
            await commandMetadata.ExecuteNonQueryAsync();
            
            transaction.Commit();
            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
        
        return Result.Success;
    }

    public override async Task<Result> UpdateDatabaseAsync() {
        try {
            var maybeMeta = await GetMetaAsync();
            if (maybeMeta.Failed) {
                return Result.Fail(maybeMeta.UnwrapError());
            }
            
            var meta = maybeMeta.Unwrap();
            if(meta.DBaseVersion == new Version(Program.Version)) { 
                // Is not an error
                return Result.Success;
            }
            
            // When a new dbase version will require the schema change, it should be done here
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
        
        return Result.Success;
    }

#pragma warning disable 8602,8604
    public override async Task<SuccessOr<PatchRunErrorInfo>> RunAsync(IEnumerable<Patch> patches) {
        var strictVersioning = ServerConfig.StrictVersioning ?? true;
        
        // Materialize our patches ordered by version
        var arrayPatches = patches as Patch[] ?? patches.OrderBy(x => x.Version).ToArray();

        // If nothing to run, will return the empty version
        if(!arrayPatches.Any())
            return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(Version.Empty, "No patches to run"));
            
        // Read the current info from the database about all the patches applied before
        var history = await GetHistoryAsync();

        // If error, will return a fail with the first patch as a failed patch 
        if (history.Failed)
            return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(arrayPatches.First().Version, history.Error));

        // All good, get history object itself 
        var historySummary = history.Unwrap();
        
        // If strict versioning is set for this server, will cut off all the previous patches
        var newPatches = (strictVersioning ? arrayPatches.Where(x => x.Version > historySummary.LastPatchVersion) : arrayPatches).OrderBy(x => x.Version).ToArray();
        if (newPatches.Any() && strictVersioning)
        {
            var versionToTest = historySummary.LastPatchVersion;
            foreach (var patch in newPatches) {
                if(!GetIsVersionApplicable(patch.Version, versionToTest))
                    return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, "Set of patches cannot be applied to the database: version validation failed. No data was changed."));

                versionToTest = patch.Version;
            }
        }

        string backupFile = String.Empty;
        
        // Backup the database if needed
        if (!string.IsNullOrEmpty(ServerConfig.BackupFolder))
        {
            var backupResult = await BackupAsync();
            if (backupResult.Failed)
                return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(Version.Empty, $"{backupResult.UnwrapError()}"));
            
            backupFile = backupResult.Unwrap();
        }

        SuccessOr<PatchRunErrorInfo>? runError = null; 
        
        // Run every patch we filtered
        foreach (var patch in newPatches)
        {
            var maybeSuccess = await RunPatchAsync(patch, RunPatchOptions.IgnoreBackups);
            
            // Error applying the patch. Return this patch as an error
            if (maybeSuccess.Failed) {
                runError = SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, maybeSuccess.Error.Description));
                break;
            }
        }

        if (runError != null) {
            var err = runError.Value.UnwrapError();
            
            // Restore the database if needed
            if (!string.IsNullOrEmpty(ServerConfig.BackupFolder))
            {
                var restoreResult = await RestoreAsync(backupFile);
                if (restoreResult.Failed) 
                    return SuccessOr<PatchRunErrorInfo>.Fail(err with { Description = $"{err.Description};\n{restoreResult.UnwrapError()}" });
                
                return SuccessOr<PatchRunErrorInfo>.Fail(err with { Description = $"{err.Description}\nБаза данных была восстановлена из бэкапа." });        
            }

            return runError.Value;
        }
        
        // All patches were applied
        return SuccessOr<PatchRunErrorInfo>.Success;   
    }
#pragma warning restore 8602,8604
    
    public override async Task<ErrorOr<Meta>> GetMetaAsync()
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Let's check if we have metadata in the database (whether the database was properly initialized with dbase)
            await using var commandCheck = new SqlCommand("SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName", connection);
            commandCheck.Parameters.AddWithValue("@tableName", MetaTableName);
            var readerCheck = await commandCheck.ExecuteReaderAsync();
            if (!readerCheck.HasRows)
                return ErrorOr<Meta>.Success(new Meta(false, null, null));
            
            await readerCheck.CloseAsync();
            
            await using var command = new SqlCommand($"SELECT TOP 1 {MetaFields.DBaseVersion}, {MetaFields.LastUpdated} FROM dbase.{MetaTableName}", connection);
            await using var reader = await command.ExecuteReaderAsync();
            
            if (!await reader.ReadAsync())
                return ErrorOr<Meta>.Fail("Не удалось получить информацию о версии dbase в источнике");
            
            var versionString = reader.GetString(0);
            var lastUpdated = reader.GetDateTime(1);

            await reader.CloseAsync();
            await connection.CloseAsync();
            
            return ErrorOr<Meta>.Success(new Meta(true, new Version(versionString), lastUpdated));
        }
        catch (Exception ex)
        {
            return ErrorOr<Meta>.Fail(ex.Message);
        }
    }
    
    public override async Task<SuccessOr<PatchRunErrorInfo>> RunPatchAsync(Patch patch, RunPatchOptions? options = null)
    {
        var backupFile = string.Empty;
        
        // Backup the database if needed
        if ((options?.DoBackup ?? true) && !string.IsNullOrEmpty(ServerConfig.BackupFolder))
        {
            var backupResult = await BackupAsync();
            if (backupResult.Failed)
                return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(Version.Empty, $"{backupResult.UnwrapError()}"));
            
            backupFile = backupResult.Unwrap();
        }
        
        try {
            var strictVersioning = ServerConfig.StrictVersioning ?? true;
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            var maybeCheck = await GetHistoryRecordAsync(patch.Version);
            if (maybeCheck.Failed)
                return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, $"Error getting history information about the patch: {maybeCheck.Error}"));
            var historyInfo = maybeCheck.Unwrap();
            
            // If specifically asked for patch version checking
            if ((options?.CheckVersionBeforeRun ?? false) && strictVersioning)
            {
                if (historyInfo.DateApplied != null)
                    return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, $"Patch version {patch.Version} already has been applied {historyInfo.DateApplied:dd.MM.yyyy at HH:mm}."));
                
                var maybeHistory = await GetHistoryAsync();
                if (maybeHistory.Failed)
                    return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, $"Error reading patches history: {maybeHistory.Error}"));
                var historySummary = maybeHistory.Unwrap();
                
                if (!GetIsVersionApplicable(patch.Version, historySummary.LastPatchVersion))
                    return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, "This patch cannot be applied: its version didn't pass the validation against database patches history"));
            }

            if (!strictVersioning) {
                if (historyInfo.DateApplied != null) // Пропускаем уже прогнанный патч в режиме упрощённой версионности
                    return SuccessOr<PatchRunErrorInfo>.Success;
            }
            
            // Start the transaction
            await using var transaction = connection.BeginTransaction();
            
            // Split the code by "COMMIT" and :GO:: we're managing transactions ourselves here
            var commands = SplitByCommitAndReturnCommands(patch.Code, connection, transaction);
            
            // Run all the commands
            foreach (var command in commands)
                await command.ExecuteNonQueryAsync();
            
            await using var commandUpdateHistory = new SqlCommand($"INSERT INTO dbase.{HistoryTableName} ({HistoryFields.Major}, {HistoryFields.Minor}, {HistoryFields.Code}, [{HistoryFields.Date}]) VALUES (@major, @minor, @sqlCode, @date)", connection, transaction); 
            
            commandUpdateHistory.Parameters.AddWithValue("@major", patch.Version.Major);
            commandUpdateHistory.Parameters.AddWithValue("@minor", patch.Version.Minor);
            commandUpdateHistory.Parameters.AddWithValue("@sqlCode", patch.Code);
            commandUpdateHistory.Parameters.AddWithValue("@date", DateTime.Now);

            // Commit the transaction
            await commandUpdateHistory.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            // Restore the database if needed
            if ((options?.DoBackup ?? true) && !string.IsNullOrEmpty(ServerConfig.BackupFolder))
            {
                var restoreResult = await RestoreAsync(backupFile);
                if (restoreResult.Failed) 
                    return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, $"{ex.Message};\n{restoreResult.UnwrapError()}"));
                
                return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, $"{ex.Message}\nБаза данных была восстановлена из бэкапа."));
            }
            
            return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, ex.Message));
        }
        
        return SuccessOr<PatchRunErrorInfo>.Success;
    }

    /// <summary>
    /// Parses mssql code and splits it into N commands by the "COMMIT" and "GO" keywords
    /// </summary>
    /// <param name="patchCode">SQL code</param>
    /// <param name="connection">Active connection</param>
    /// <param name="transaction">Active transaction</param>
    private List<SqlCommand> SplitByCommitAndReturnCommands(string patchCode, SqlConnection connection, SqlTransaction transaction)
    {
        if (ServerConfig.Options != null && ServerConfig.Options.ContainsKey(ServerOptions.NoPreprocessing) && ServerConfig.Options[ServerOptions.NoPreprocessing].ToLower() == "true")
            return new List<SqlCommand> { new (patchCode, connection, transaction) };
        
        var result = new List<SqlCommand>();
        var pattern = @"(\s*[Cc][Oo][Mm][Mm][Ii][Tt]\s+)|(\s*[Gg][Oo]\s+)";
        var substrings = Regex.Split(patchCode, pattern);
        
        foreach (string match in substrings)
        {
            if (!Regex.IsMatch(match, pattern) && !string.IsNullOrWhiteSpace(match))
                result.Add(new SqlCommand(match, connection, transaction));
        }

        return result;
    }

#pragma warning disable 8604
    public override ErrorOr<Patch> ParsePatch(string fileName, string fileContents)
    {
        var maybeFileMeta = ParseMetaFromFilename(fileName);
        
        if(maybeFileMeta.Failed)
            return ErrorOr<Patch>.Fail(maybeFileMeta.Error);

        var meta = maybeFileMeta.Unwrap();
        if (meta.Version == null)
            throw new Exception($"Patch file \"{fileName}\" doesn't contain version information");
        
        var patchCode = fileContents;

        if (meta.Extension.ToLower() == ".yaml") {
            var converter = new YamlToMSSqlConverter(ServerConfig);
            
            var maybeCode = converter.Convert(fileContents);
            if (maybeCode.Failed)
                return ErrorOr<Patch>.Fail(maybeCode.UnwrapError());
            
            patchCode = maybeCode.Unwrap();
        }

        patchCode = ReplaceAliases(patchCode);
        
        return ErrorOr<Patch>.Success(new Patch(patchCode, meta.Version));
    }

    public override async Task<ErrorOr<string>> BackupAsync() {
        if(string.IsNullOrEmpty(ServerConfig.BackupFolder))
            return ErrorOr<string>.Fail("This configuration does not contain information about database backup folder");
        
        if (ServerConfig.Options == null || !ServerConfig.Options.ContainsKey(ServerOptions.DataPath))
            return ErrorOr<string>.Fail($"This configuration does not contain {ServerOptions.DataPath} option. It is required if backup folder is set.");
        
        var fileName = $"{DatabaseName}-dbase-{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        var fullPath = System.IO.Path.Combine(ServerConfig.BackupFolder, fileName);
        
        try {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            await using var commandCheck = new SqlCommand($"SELECT db_id('{DatabaseName}')", connection);
            var hasDatabase = await commandCheck.ExecuteScalarAsync() != DBNull.Value;
            
            if(!hasDatabase)
                return ErrorOr<string>.Success(string.Empty);
            
            await using var command = new SqlCommand($"BACKUP DATABASE [{DatabaseName}] TO DISK = N'{fullPath}' WITH NOFORMAT, NOINIT,  NAME = N'DBase Backup', SKIP, NOREWIND, NOUNLOAD,  STATS = 10", connection);
            await command.ExecuteNonQueryAsync();
            await connection.CloseAsync();
        }
        catch (Exception ex) {
            return ErrorOr<string>.Fail($"{ex.Message}{(IsUsingIntegratedSecurity ? " (integrated security is used)" : string.Empty)}");
        }
        
        return ErrorOr<string>.Success(fileName);
    }

    public override async Task<Result> RestoreAsync(string backupFileName) {
        if(string.IsNullOrEmpty(ServerConfig.BackupFolder))
            return Result.Fail("This configuration does not contain information about database backup folder");
        
        if (ServerConfig.Options == null || !ServerConfig.Options.ContainsKey(ServerOptions.DataPath))
            return Result.Fail($"This configuration does not contain {ServerOptions.DataPath} option. It is required if backup folder is set.");
        
        if(string.IsNullOrEmpty(backupFileName))
            return Result.Success;
        
        try {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            var fileName = backupFileName;
            var backupFilePath = System.IO.Path.Combine(ServerConfig.BackupFolder, fileName);
            
            await using var commandDrop = new SqlCommand(@$"
                IF NOT (select db_id('{DatabaseName}')) IS NULL
				BEGIN
					EXEC msdb.dbo.sp_delete_database_backuphistory @database_name = N'{DatabaseName}';
					ALTER DATABASE [{DatabaseName}] SET  SINGLE_USER WITH ROLLBACK IMMEDIATE;
					ALTER DATABASE [{DatabaseName}] SET  SINGLE_USER;
					DROP DATABASE [{DatabaseName}];
				END;", connection);
            
            await using var commandRestore = new SqlCommand(@$"
                RESTORE DATABASE [{DatabaseName}] 
				FROM  DISK = N'{backupFilePath}' 
				WITH FILE = 1,
					MOVE N'{DatabaseName}' TO N'{ServerConfig.Options[ServerOptions.DataPath]}\{DatabaseName}.mdf', 
					MOVE N'{DatabaseName}_log' TO N'{ServerConfig.Options[ServerOptions.DataPath]}\{DatabaseName}_Log.ldf', NOUNLOAD,  REPLACE", connection);
            
            await using var commandCheck = new SqlCommand($"SELECT db_id('{DatabaseName}')", connection);
            var hasDatabase = await commandCheck.ExecuteScalarAsync() != DBNull.Value;
            
            if(hasDatabase)
                await commandDrop.ExecuteNonQueryAsync();
            
            await commandRestore.ExecuteNonQueryAsync();
            await connection.CloseAsync();
        }
        catch (Exception ex) {
            return Result.Fail($"{ex.Message}{(IsUsingIntegratedSecurity ? $" (integrated security is used). Backup file name \"{backupFileName}\"." : string.Empty)}");
        }
        
        return Result.Success;
    }
    
#pragma warning restore 8604

    protected override async Task<ErrorOr<HistorySummary>> GetHistoryAsync()
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            await using var command = new SqlCommand($"SELECT TOP 1 {HistoryFields.Major}, {HistoryFields.Minor} FROM dbase.{HistoryTableName} ORDER BY {HistoryFields.Major}, {HistoryFields.Minor} desc", connection);
            await using var reader = await command.ExecuteReaderAsync();
            
            if (!await reader.ReadAsync())
                return ErrorOr<HistorySummary>.Success(new HistorySummary(Version.Empty));
            
            var major = reader.GetInt32(0);
            var minor = reader.GetInt32(1);

            await reader.CloseAsync();
            await connection.CloseAsync();
            
            return ErrorOr<HistorySummary>.Success(new HistorySummary(new Version(major, minor)));
        }
        catch (Exception ex)
        {
            return ErrorOr<HistorySummary>.Fail(ex.Message);
        }
    }

    protected override async Task<ErrorOr<PatchHistoryInfo>> GetHistoryRecordAsync(Version version)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            await using var command = new SqlCommand($"SELECT TOP 1 {HistoryFields.Date} FROM dbase.{HistoryTableName} WHERE {HistoryFields.Major}=@major AND {HistoryFields.Minor}=@minor", connection);
            command.Parameters.AddWithValue("@major", version.Major);
            command.Parameters.AddWithValue("@minor", version.Minor);
            
            await using var reader = await command.ExecuteReaderAsync();
            
            if (!await reader.ReadAsync())
                return ErrorOr<PatchHistoryInfo>.Success(new PatchHistoryInfo(version, null));
            
            var date = reader.GetDateTime(0);

            await reader.CloseAsync();
            await connection.CloseAsync();
            
            return ErrorOr<PatchHistoryInfo>.Success(new PatchHistoryInfo(version, date));
        }
        catch (Exception ex)
        {
            return ErrorOr<PatchHistoryInfo>.Fail(ex.Message);
        }
    }
}