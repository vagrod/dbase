namespace Dbase.CliActors;

public class RemoveServerActor : CliActorBase
{
    public RemoveServerActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        if (args.Count == 0 || string.IsNullOrEmpty(args[0]))
        {
            Console.WriteColorLine($"Missing parameter for [blue]\"{RemoveServerCommand}\"[/blue]: ([cyan]configuration name[/cyan]).");
            return Task.FromResult(-1);
        }

        var configName = args[0].ToLowerInvariant();

        if (AppConfig.Servers!.All(x => x.ConfigurationName.ToLowerInvariant() != configName)) {
            Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
            return Task.FromResult(-1);
        }

        AppConfig.Servers!.RemoveAll(x => x.ConfigurationName.ToLowerInvariant() == configName);
        AppConfig.Save();
        
        Console.WriteColorLine("[green]New configuration was saved.[/green] Press any key.");
        System.Console.ReadLine();

        return Task.FromResult(0);
    }
}
