namespace Dbase.CliActors;

public class InitDatabaseActor : CliActorBase
{
    public InitDatabaseActor(Configuration appConfig) : base(appConfig) { }

    public override async Task<int> ExecuteAsync(List<string> args) {
        if (args.Count < 2)
        {
            Console.WriteColorLine($"Not enough parameters for [blue]\"{InitCommand}\"[/blue]: ([cyan]database name[/cyan] and [cyan]configuration name[/cyan]).");
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

        if (meta.IsInitialized)
        {
            Console.WriteColorLine($"Database [cyan]\"{dbName}\"[/cyan] [green]has already been initialized[/green] with [green]dbase[/green] version [cyan]{meta.DBaseVersion} {meta.LastUpdated:dd.MM.yyyy at HH:mm}[/cyan].");
            System.Console.ReadLine();
        }
        else
        {
            var maybeInit = await processor.InitializeDatabaseAsync();
            if (maybeInit.Failed) {
                System.Console.WriteLine(maybeInit.Error);
                return -1;
            }

            Console.WriteColorLine($"Database [cyan]\"{dbName}\"[/cyan] [green]was initialized[/green]. Press any key.");
            System.Console.ReadLine();
        }

        return 0;
    }
}
