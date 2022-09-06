namespace Dbase.CliActors;

public static class ActorsFactory
{
    public static CliActorBase? Get(string argName, Configuration appConfig) => argName switch
    {
        CliActorBase.HelpCommand => new HelpActor(appConfig),
        CliActorBase.InitCommand => new InitDatabaseActor(appConfig),
        CliActorBase.UpdateCommand => new UpdateDatabaseActor(appConfig),
        CliActorBase.RunCommand => new RunActor(appConfig),
        CliActorBase.AddServerCommand => new AddServerActor(appConfig),
        CliActorBase.EditServerCommand => new EditServerActor(appConfig),
        CliActorBase.RemoveServerCommand => new RemoveServerActor(appConfig),
        CliActorBase.AliasesCommand => new AliasesActor(appConfig),
        CliActorBase.LsCommand => new LsActor(appConfig),
        CliActorBase.PrintCommand => new PrintActor(appConfig),
        CliActorBase.VersionCommand => new VersionActor(appConfig),
        _ => null
    };
}