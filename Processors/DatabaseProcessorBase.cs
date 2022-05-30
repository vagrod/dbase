using System.IO;
using System.Text.RegularExpressions;

namespace Dbase;

/// <summary>
/// Stores information about the last patch applied on this source (database)
/// </summary>
/// <param name="LastPatchVersion">Version of the patch</param>
public record HistorySummary(
    Version LastPatchVersion
);
/// <summary>
/// Stores applied patch information 
/// </summary>
/// <param name="Code">Patch code, in a format of the source (actual sql code being run)</param>
/// <param name="Version">Patch version</param>
public record Patch(
    string Code,
    Version Version
);
/// <summary>
/// Stores information about dbase on this server
/// </summary>
/// <param name="IsInitialized">If `true`, database has already been initialized to work with dbase</param>
/// <param name="DBaseVersion">dbase version, for which which server has been initialized</param>
/// <param name="LastUpdated">When dbase utility has been last updated on this source</param>
public record Meta(
    bool IsInitialized,
    Version? DBaseVersion,
    DateTime? LastUpdated
);
/// <summary>
/// Stores error information about the patch application
/// </summary>
/// <param name="Version">Error patch version</param>
/// <param name="Description">Error description</param>
public record PatchRunErrorInfo(
    Version Version,
    string Description
);
/// <summary>
/// Stores information extracted from the patch file name
/// </summary>
/// <param name="Version">Version, if found</param>
/// <param name="Extension">Extension (with the dot)</param>
public record FileNameMeta(
    Version? Version,
    string Extension
);
/// <summary>
/// Describes a history patch record
/// </summary>
/// <param name="Version">Patch version</param>
/// <param name="DateApplied">Date-time when this patch was applied (`null` if wasn't)</param>
public record PatchHistoryInfo(
    Version Version,
    DateTime? DateApplied
);

/// <summary>
/// Patch application options
/// </summary>
public class RunPatchOptions
{
    public bool CheckVersionBeforeRun { get; private set; }
    public bool DoBackup { get; private set; } = true;
    
    public static RunPatchOptions Default => new()
    {
        CheckVersionBeforeRun = false,
        DoBackup = true
    };
    
    public static RunPatchOptions CheckBeforeRun => new()
    {
        CheckVersionBeforeRun = true
    };
    
    public static RunPatchOptions IgnoreBackups => new()
    {
        DoBackup = false
    };
}

/// <summary>
/// Base class for patch source processors
/// </summary>
public abstract class DatabaseProcessorBase
{
    protected const string HistoryTableName = "history";
    protected const string MetaTableName = "meta";

    protected static class MetaFields
    {
        public const string DBaseVersion = "version";
        public const string LastUpdated = "lastUpdated";
    }
    
    protected static class HistoryFields
    {
        public const string Id = "id";
        public const string Date = "date";
        public const string Major = "major";
        public const string Minor = "minor";
        public const string Code = "code";
    }
    
    /// <summary>
    /// Server configuration
    /// </summary>
    protected ServerDescription ServerConfig { get; }
    /// <summary>
    /// Database name on the server
    /// </summary>
    protected string DatabaseName { get; }
    /// <summary>
    /// Processor type -- see <see cref="ProcessorTypes"/>
    /// </summary>
    public abstract string ProcessorType { get; }

    /// <summary>
    /// Base processor constructor
    /// </summary>
    /// <param name="serverConfig">Server configuration</param>
    /// <param name="databaseName">Database name on the server</param>
    protected DatabaseProcessorBase(ServerDescription serverConfig, string databaseName)
    {
        ServerConfig = serverConfig;
        DatabaseName = databaseName;
    }
    /// <summary>
    /// Retuens the patch history
    /// </summary>
    /// <returns>History object, or error if failed</returns>
    protected abstract Task<ErrorOr<HistorySummary>> GetHistoryAsync();
    /// <summary>
    /// Returns information about the particular patch version from the source.
    /// </summary>
    /// <param name="version">Patch version to check</param>
    /// <returns>Patch history record, or error if failed</returns>
    protected abstract Task<ErrorOr<PatchHistoryInfo>> GetHistoryRecordAsync(Version version);
    /// <summary>
    /// Gets version information from the file name
    /// </summary>
    /// <param name="fileName">File name (without the path)</param>
    /// <returns>Metadata gathered from the file name, or error if failed</returns>
    protected ErrorOr<FileNameMeta> ParseMetaFromFilename(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return ErrorOr<FileNameMeta>.Fail("Пустое имя файла");
        var match = Regex.Match(fileName, @"^(\s*|[a-zA-Z]*)([\-\._])*((?<major>\d+)(\.|-|_)(?<minor>\d+)|\w*)\s*(?<therest>.*)$", RegexOptions.Singleline);
        if (!match.Success)
            return ErrorOr<FileNameMeta>.Fail($"Не удалось прочитать информацию о версии патча из имени файла {fileName}");
        
        if(match.Groups.ContainsKey("major") && match.Groups.ContainsKey("minor") 
                                             && !string.IsNullOrEmpty(match.Groups["major"].Value)
                                             && !string.IsNullOrEmpty(match.Groups["minor"].Value))
            return ErrorOr<FileNameMeta>.Success(new FileNameMeta(new Version($"{match.Groups["major"].Value}.{match.Groups["minor"].Value}"), Path.GetExtension(fileName)));
        
        return ErrorOr<FileNameMeta>.Success(new FileNameMeta(null, Path.GetExtension(fileName)));
    }
    /// <summary>
    /// Checks if the particular version is valid against another version
    /// </summary>
    /// <param name="version">Version to check</param>
    /// <param name="versionToTestAgainst">"Previous" version</param>
    /// <returns>True if passed</returns>
    protected bool GetIsVersionApplicable(Version version, Version versionToTestAgainst)
    {
        if (versionToTestAgainst == Version.Empty) return true;
        
        // Do not allow the same version
        if (version == versionToTestAgainst) return false;
        
        // Do not allow jumps in the version
        if(version.Major > versionToTestAgainst.Major + 1) return false;
        if(version.Major == versionToTestAgainst.Major && version.Minor > versionToTestAgainst.Minor + 1) return false;
        
        // Do not allow older versions
        if(version < versionToTestAgainst) return false;
        
        return true;
    }
    /// <summary>
    ///  Replaces aliases in the patch code
    /// </summary>
    /// <param name="patchCode">Patch sql code</param>
    /// <returns>Altered patch code</returns>
    protected string ReplaceAliases(string patchCode) {
        if (!ServerConfig.Aliases.Any())
            return patchCode;

        var s = patchCode;
        
        foreach (var alias in ServerConfig.Aliases) 
            s = s.Replace($"$({alias.Key})", alias.Value);

        return s;
    }

    /// <summary>
    /// Runs a single patch on a source
    /// </summary>
    /// <param name="patch">Patch to run</param>
    /// <param name="options">Patch options</param>
    /// <returns>If failed, will return error with description</returns>
    public abstract Task<SuccessOr<PatchRunErrorInfo>> RunPatchAsync(Patch patch, RunPatchOptions? options = null);
    /// <summary>
    /// Initializes source (database) to work with dbase (creates schema, tables, etc)
    /// </summary>
    /// <returns>If failed, will return error with description</returns>
    public abstract Task<Result> InitializeDatabaseAsync();
    /// <summary>
    /// Updates dbase on the source (database) to match the current version
    /// </summary>
    /// <returns>If failed, will return error with description</returns>
    public abstract Task<Result> UpdateDatabaseAsync();
    /// <summary>
    /// Runs a set of patches on the source (database)
    /// </summary>
    /// <param name="patches">Patches list</param>
    /// <returns>If failed, will return failed patch and error description</returns>
    public abstract Task<SuccessOr<PatchRunErrorInfo>> RunAsync(IEnumerable<Patch> patches);
    /// <summary>
    /// Gets dbase metadata from the source (database)
    /// </summary>
    /// <returns>Returns metadata object, or error with description if failed</returns>
    public abstract Task<ErrorOr<Meta>> GetMetaAsync();
    /// <summary>
    /// Returns patch objects from the file contents and file name
    /// </summary>
    /// <param name="fileName">File name (with extension and without the path)</param>
    /// <param name="fileContents">String containing the file data</param>
    /// <returns>Patch object, or error with description if failed</returns>
    public abstract ErrorOr<Patch> ParsePatch(string fileName, string fileContents);
    /// <summary>
    /// Creates source (database) backup 
    /// </summary>
    /// <returns>Will return backup file name on success. Error with description if failed</returns>
    public abstract Task<ErrorOr<string>> BackupAsync();
    /// <summary>
    /// Restires source (database) from a backup
    /// </summary>
    /// <param name="backupFileName">Backup file name (without the path)</param>
    /// <returns>Will return error with description if failed</returns>
    public abstract Task<Result> RestoreAsync(string backupFileName);
}
