namespace Dbase;


/// <summary>
/// Supported database processors types
/// </summary>
public static class ProcessorTypes
{
    public const string MsSql = "mssql";
    public const string Postgre = "postgresql";
}

/// <summary>
/// Common additional server options
/// </summary>
public static class ServerOptions
{
    public const string NoPreprocessing = "noPreprocessing";
    public const string DataPath = "dataPath";
}

public class ProcessorsFactory
{
    private static ProcessorsFactory? _instance;
    
    private static readonly object Lock = new();

    private ProcessorsFactory() { }

    public static ProcessorsFactory Instance
    {
        get
        {
            lock (Lock)
                _instance ??= new ProcessorsFactory();
            
            return _instance;
        }
    }

    public ErrorOr<DatabaseProcessorBase> GetProcessor(ServerDescription serverConfig, string databaseName) => serverConfig.Type switch
    {
        ProcessorTypes.MsSql => ErrorOr<DatabaseProcessorBase>.Success(new MSSqlDatabaseProcessor(serverConfig, databaseName)),
        ProcessorTypes.Postgre => ErrorOr<DatabaseProcessorBase>.Success(new PostgreSqlDatabaseProcessor(serverConfig, databaseName)),
        _ => ErrorOr<DatabaseProcessorBase>.Fail($"Processor type \"{serverConfig.Type}\" is not supported (typo?)")
    };
}
