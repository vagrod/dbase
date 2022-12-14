using Npgsql;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using Dbase.Yaml.Converters;

namespace Dbase;

/// <summary>
/// PostgreSQL database processor.
/// </summary>
public class PostgreSqlDatabaseProcessor : DatabaseProcessorBase
{
    private string ConnectionString { get; set; }

    public PostgreSqlDatabaseProcessor(ServerDescription serverConfig, string databaseName) : base(serverConfig, databaseName)
    {
        ConnectionString = $"User ID=postgres;Password=;Host={serverConfig.DnsName};Port={serverConfig.Port};Database={databaseName};User Id={serverConfig.DatabaseUser};Password={serverConfig.Password};";
    }

    public override string ProcessorType => ProcessorTypes.Postgre;

    /// <summary>
    /// Initializes the database to work with dbase
    /// </summary>
    public override async Task<Result> InitializeDatabaseAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            
            // Create dbase schema
            await using var commandSchema = new NpgsqlCommand("CREATE SCHEMA \"dbase\";", connection, transaction);
            await commandSchema.ExecuteNonQueryAsync();
            
            // Create history table
            await using var commandHistory = new NpgsqlCommand($"CREATE TABLE \"dbase\".{HistoryTableName} ({HistoryFields.Id} SERIAL PRIMARY KEY, {HistoryFields.Date} timestamp NOT null, {HistoryFields.Major} int NOT null, {HistoryFields.Minor} int NOT null, {HistoryFields.Code} text NOT null)", connection, transaction);
            await commandHistory.ExecuteNonQueryAsync();
            
            // Create meta table
            await using var commandMeta = new NpgsqlCommand($"CREATE TABLE \"dbase\".{MetaTableName} (id SERIAL PRIMARY KEY, {MetaFields.DBaseVersion} varchar(10) NOT null, {MetaFields.LastUpdated} timestamp NOT null)", connection, transaction);
            await commandMeta.ExecuteNonQueryAsync();
            
            // Write the necessary data
            await using var commandMetadata = new NpgsqlCommand($"INSERT INTO \"dbase\".{MetaTableName} ({MetaFields.DBaseVersion}, {MetaFields.LastUpdated}) VALUES (@version, @date)", connection, transaction);
            commandMetadata.Parameters.AddWithValue("@version", Program.Version);
            commandMetadata.Parameters.AddWithValue("@date", DateTime.Now);
            await commandMetadata.ExecuteNonQueryAsync();
            
            await transaction.CommitAsync();
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
    public override async Task<SuccessOr<PatchRunErrorInfo>> RunAsync(IEnumerable<Patch> patches)
    {
        var strictVersioning = ServerConfig.StrictVersioning ?? true;
        
        // Materialize our patches ordered by version
        var arrayPatches = patches as Patch[] ?? patches.ToArray().OrderBy(x => x.Version).ToArray();
        
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
            var maybeSuccess = await RunPatchAsync(patch);
            
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
                
                return SuccessOr<PatchRunErrorInfo>.Fail(err with { Description = $"{err.Description}\nDatabase has been restored from a backup." });        
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Let's check if we have metadata in the database (whether the database was properly initialized with dbase)
            await using var commandCheck = new NpgsqlCommand(@$"SELECT EXISTS (
               SELECT FROM information_schema.tables 
               WHERE  table_schema = 'dbase'
               AND    table_name   = '{MetaTableName}'
               );", connection);
#pragma warning disable 8605
            var exists = (bool)(await commandCheck.ExecuteScalarAsync());
#pragma warning restore 8605
            if (!exists)
                return ErrorOr<Meta>.Success(new Meta(false, null, null));
            
            await using var command = new NpgsqlCommand($"SELECT {MetaFields.DBaseVersion}, {MetaFields.LastUpdated} FROM dbase.{MetaTableName} LIMIT 1", connection);
            await using var reader = await command.ExecuteReaderAsync();
            
            if (!await reader.ReadAsync())
                return ErrorOr<Meta>.Fail("Unable to get dbase information from the source");
            
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
        
        try
        {
            var strictVersioning = ServerConfig.StrictVersioning ?? true;
            await using var connection = new NpgsqlConnection(ConnectionString);
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
                if (historyInfo.DateApplied != null) // Skip already applied patch in a lazy versioning mode
                    return SuccessOr<PatchRunErrorInfo>.Success;
            }
            
            // Start the transaction
            await using var transaction = await connection.BeginTransactionAsync();
            
            // Split the code by "COMMIT": we're managing transactions ourselves here
            var commands = SplitByCommitAndReturnCommands(patch.Code, connection, transaction);
            
            // Run all the commands
            foreach (var command in commands)
                await command.ExecuteNonQueryAsync();
            
            // Add history record
            await using var commandUpdateHistory = new NpgsqlCommand($"INSERT INTO dbase.{HistoryTableName} ({HistoryFields.Major}, {HistoryFields.Minor}, {HistoryFields.Code}, {HistoryFields.Date}) VALUES (@major, @minor, @sqlCode, @date)", connection, transaction); 
            
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
                
                return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, $"{ex.Message}\nDatabase has been restored from a backup."));
            }
            
            return SuccessOr<PatchRunErrorInfo>.Fail(new PatchRunErrorInfo(patch.Version, ex.Message));
        }
        
        return SuccessOr<PatchRunErrorInfo>.Success;
    }

    /// <summary>
    /// Parses postgresql code and splits it into N commands by the "COMMIT" keyword
    /// </summary>
    /// <param name="patchCode">SQL code</param>
    /// <param name="connection">Active connection</param>
    /// <param name="transaction">Active transaction</param>
    private List<NpgsqlCommand> SplitByCommitAndReturnCommands(string patchCode, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var isNoPreprocesing = ServerConfig.Options != null &&
            ServerConfig.Options.ContainsKey(ServerOptions.NoPreprocessing) &&
            ServerConfig.Options[ServerOptions.NoPreprocessing].ToLower() == "true";
        
        if (isNoPreprocesing)
            return new List<NpgsqlCommand> { new (patchCode, connection, transaction) };
        
        var result = new List<NpgsqlCommand>();
        var pattern = @"(\s+[Cc][Oo][Mm][Mm][Ii][Tt]\s*;\s*)";
        var substrings = Regex.Split(patchCode, pattern);
        
        foreach (string match in substrings)
        {
            if (!Regex.IsMatch(match, pattern) && !string.IsNullOrWhiteSpace(match))
                result.Add(new NpgsqlCommand(match, connection, transaction));
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
            var converter = new YamlToPostgreSqlConverter(ServerConfig);
            
            var maybeCode = converter.Convert(fileContents);
            if (maybeCode.Failed)
                return ErrorOr<Patch>.Fail(maybeCode.UnwrapError());
            
            patchCode = maybeCode.Unwrap();
        }
        
        patchCode = ReplaceAliases(patchCode);
        
        return ErrorOr<Patch>.Success(new Patch(patchCode, meta.Version));
    }

    public override async Task<ErrorOr<string>> BackupAsync() {
        var controller = new BackupRestoreController();

        var backupFileName = $"{DatabaseName}-dbase-{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        var result = await controller.PostgreSqlDump(Path.Combine(ServerConfig.BackupFolder, backupFileName), ServerConfig.DnsName, ServerConfig.Port?.ToString(), DatabaseName, ServerConfig.DatabaseUser, ServerConfig.Password);
        if(result.Failed)
            return ErrorOr<string>.Fail(result.UnwrapError());
        
        return ErrorOr<string>.Success(backupFileName);
    }

    public override async Task<Result> RestoreAsync(string backupFileName) {
        var controller = new BackupRestoreController();

        return await controller.PostgreSqlRestore(Path.Combine(ServerConfig.BackupFolder, backupFileName), ServerConfig.DnsName, ServerConfig.Port?.ToString(), DatabaseName, ServerConfig.DatabaseUser, ServerConfig.Password);
    }
    
#pragma warning restore 8604

    protected override async Task<ErrorOr<HistorySummary>> GetHistoryAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            await using var command = new NpgsqlCommand($"SELECT {HistoryFields.Major}, {HistoryFields.Minor} FROM dbase.{HistoryTableName} ORDER BY {HistoryFields.Major} DESC, {HistoryFields.Minor} DESC LIMIT 1", connection);
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            await using var command = new NpgsqlCommand($"SELECT {HistoryFields.Date} FROM \"dbase\".{HistoryTableName} WHERE {HistoryFields.Major}=@major AND {HistoryFields.Minor}=@minor LIMIT 1", connection);
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

/// <summary>
/// Class for managing postgresql database backups
/// </summary>
public class BackupRestoreController
{
    private readonly string _set = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "set " : "export ";

    public async Task<Result> PostgreSqlRestore(
        string inputFile,
        string host,
        string port,
        string database,
        string user,
        string password) {
        string dumpCommand = $"{_set}PGPASSWORD={password}\n" +
                             $"psql -h {host} -p {port} -U {user} -d {database} -c \"select pg_terminate_backend(pid) from pg_stat_activity where datname = '{database}'\"\n" +
                             "dropdb -h " + host + " -p " + port + " -U " + user + $" {database}\n" +
                             "createdb -h " + host + " -p " + port + " -U " + user + $" {database}\n" +
                             "pg_restore -h " + host + " -p " + port + " -d " + database + " -U " + user + "";

        dumpCommand = $"{dumpCommand} {inputFile}";

        return await Execute(dumpCommand);
    }
    
    private async Task<Result> Execute(string dumpCommand)
    {
        string batFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}." + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "bat" : "sh"));
        try
        {
            string batchContent = "";
            batchContent += $"{dumpCommand}";

            await File.WriteAllTextAsync(batFilePath, batchContent, Encoding.ASCII);

            ProcessStartInfo info = ProcessInfoByOS(batFilePath);

            using Process? proc = Process.Start(info);

            if(proc == null)
                return Result.Fail("Could not start the backup/restore process.");
            
            await proc.WaitForExitAsync();

            var code = proc.ExitCode;

            if (code != 0) {
                string? output;
                
                try {
                    output = await proc.StandardError.ReadToEndAsync();
                }
                catch {
                    output = null;
                }

                return Result.Fail($"Error during backup/restore operation. Return code is {code}{(string.IsNullOrEmpty(output) ? string.Empty : $"\n{output}")}");
            }

            proc.Close();
            
            return Result.Success;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
        finally
        {
            if (File.Exists(batFilePath)) File.Delete(batFilePath);
        }
    }
    
    private static ProcessStartInfo ProcessInfoByOS(string batFilePath)
    {
        ProcessStartInfo info;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            info = new ProcessStartInfo(batFilePath);
        }
        else
        {
            info = new ProcessStartInfo("sh")
            {
                Arguments = $"{batFilePath}"
            };
        }

        info.CreateNoWindow = true;
        info.UseShellExecute = false;
        info.WorkingDirectory = Path.GetDirectoryName(batFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;

        return info;
    }
    
    public async Task<Result> PostgreSqlDump(
        string outFile,
        string host,
        string port,
        string database,
        string user,
        string password)
    {
        string dumpCommand =
            $"{_set}PGPASSWORD={password}\n" +
            $"pg_dump" + " -Fc" + " -h " + host + " -p " + port + " -d " + database + " -U " + user + "";

        string batchContent = "" + dumpCommand + " > " + "\"" + outFile + "\"" + "\n";
        if (File.Exists(outFile)) File.Delete(outFile);

        return await Execute(batchContent);
    }
}
