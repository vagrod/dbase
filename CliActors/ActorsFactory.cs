namespace Dbase.CliActors;

public static class ActorsFactory
{
    public static CliActorBase? Get(string argName, Configuration appConfig)
    {
        if(argName == CliActorBase.HelpCommand)
            return new HelpActor(appConfig);
        
        if(argName == CliActorBase.InitCommand)
            return new InitDatabaseActor(appConfig);
        
        if(argName == CliActorBase.UpdateCommand)
            return new UpdateDatabaseActor(appConfig);
        
        if(argName == CliActorBase.RunCommand)
            return new RunActor(appConfig);
        
        if(argName == CliActorBase.AddServerCommand)
            return new AddServerActor(appConfig);
        
        if(argName == CliActorBase.EditServerCommand)
            return new EditServerActor(appConfig);

        if(argName == CliActorBase.RemoveServerCommand)
            return new RemoveServerActor(appConfig);
        
        if(argName == CliActorBase.AliasesCommand)
            return new AliasesActor(appConfig);
        
        if(argName == CliActorBase.LsCommand)
            return new LsActor(appConfig);
        
        if(argName == CliActorBase.PrintCommand)
            return new PrintActor(appConfig);

        return null;
    }
}