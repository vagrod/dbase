namespace Dbase.CliActors;

public abstract class CliActorBase
{
    
    protected readonly Configuration AppConfig;

    public const string InitCommand = "init-database";
    public const string AddServerCommand = "add-server";
    public const string EditServerCommand = "edit-server";
    public const string RemoveServerCommand = "remove-server";
    public const string RunCommand = "run";
    public const string AliasesCommand = "aliases";
    public const string UpdateCommand = "update-database";
    public const string LsCommand = "ls";
    public const string HelpCommand = "help";
    public const string PrintCommand = "print";
    public const string VersionCommand = "-version";
    
    protected CliActorBase(Configuration appConfig) {
        AppConfig = appConfig;
    }
    
    public abstract Task<int> ExecuteAsync(List<string> args);
    
}
