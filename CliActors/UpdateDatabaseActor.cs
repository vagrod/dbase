namespace Dbase.CliActors;

public class UpdateDatabaseActor : CliActorBase
{
    public UpdateDatabaseActor(Configuration appConfig) : base(appConfig) { }

    public override async Task<int> ExecuteAsync(List<string> args) {
        if (args.Count < 2)
        {
            Console.WriteColorLine($"Not enough parameters for [blue]\"{UpdateCommand}\"[/blue] ([cyan]database name[/cyan] and [cyan]configuration name[/cyan]).");
            return -1;
        }

        var dbName = args[0];
        var configName = args[1];

        var config = AppConfig.Servers!.FirstOrDefault(x => x.ConfigurationName.ToLowerInvariant() == configName.ToLowerInvariant());
        if (config == null) {
            Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
            return -1;
        }

        var maybeProcessor = ProcessorsFactory.Instance.GetProcessor(config, dbName);
        if (maybeProcessor.Failed) {
            System.Console.WriteLine(maybeProcessor.Error);
            return -1;
        }
        var processor = maybeProcessor.Unwrap();

        var maybeMeta = await processor.GetMetaAsync();
        if (maybeMeta.Failed) {
            System.Console.WriteLine(maybeMeta.Error);
            return -1;
        }
        var meta = maybeMeta.Unwrap();

        if (!meta.IsInitialized)
        {
            Console.WriteColorLine($"Database [cyan]\"{dbName}\"[/cyan] [red]was not initialized[/red] for [green]dbase[/green]. Run \"[green]dbase[/green] -[blue]{InitCommand}[/blue] [cyan]{configName}[/cyan] [cyan]{dbName}[/cyan]\". Press any key");
            System.Console.ReadLine();
        }
        else
        {
            var maybeUpdate = await processor.UpdateDatabaseAsync();
            if (maybeUpdate.Failed) {
                System.Console.WriteLine(maybeUpdate.Error);
                return -1;
            }

            Console.WriteColorLine($"Database [cyan]\"{dbName}\"[/cyan] [green]was updated[/green] to the [green]dbase[/green] version [cyan]{Program.Version}[/cyan]. Press any key.");
            System.Console.ReadLine();
        }

        return 0;
    }
}
