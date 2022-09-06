using System.IO;

namespace Dbase.CliActors;

public class PrintActor : CliActorBase
{
    public PrintActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        if (args.Count == 0 || string.IsNullOrEmpty(args[0])) {
            Console.WriteColorLine($"Missing parameter for [blue]\"{PrintCommand}\"[/blue]: [cyan]patch file path[/cyan].");
            return Task.FromResult(-1);
        }
        if (args.Count == 1 || string.IsNullOrEmpty(args[1])) {
            Console.WriteColorLine($"Missing parameter for [blue]\"{PrintCommand}\"[/blue]: [cyan]processor type ({ProcessorTypes.MsSql}, {ProcessorTypes.Postgre})[/cyan] or [cyan]configuration name[/cyan].");
            return Task.FromResult(-1);
        }
        
        var filePath = args[0];
        var processorTypeOrConfig = args[1];
        
        DatabaseProcessorBase processor;
        
        if (processorTypeOrConfig == ProcessorTypes.MsSql) 
            processor = new MSSqlDatabaseProcessor(new ServerDescription(), "dummy");
        else if (processorTypeOrConfig == ProcessorTypes.Postgre)
            processor = new PostgreSqlDatabaseProcessor(new ServerDescription(), "dummy");
        else {
            // Maybe config name was given?
            if (AppConfig.Servers!.All(x => x.ConfigurationName.ToLowerInvariant() != processorTypeOrConfig)) {
                System.Console.Write($"No such processor, neither configuration name \"{processorTypeOrConfig}\"");
                return Task.FromResult(-1);
            }

            var server = AppConfig.Servers!.FirstOrDefault(x => x.ConfigurationName.ToLowerInvariant() == processorTypeOrConfig);
        
            if (server == null) {
                Console.WriteColorLine($"Configuration with name [cyan]\"{processorTypeOrConfig}\"[/cyan] [red]not found[red]");
                return Task.FromResult(-1);
            }

            var maybeProcessor = ProcessorsFactory.Instance.GetProcessor(server, "dummy");
            if (maybeProcessor.Failed) {
                System.Console.Write($"Processor [red]cannot be acquired[/red] [cyan]\"{args[0]}\"[/cyan].");
                return Task.FromResult(-1);
            }
            
            processor = maybeProcessor.Unwrap();
        }

        string fileContents;
        try {
            fileContents = File.ReadAllText(filePath);
        }
        catch (Exception ex) {
            Console.WriteColorLine($"Error reading file [cyan]\"{filePath}\"[/cyan]: {ex.Message}");
            return Task.FromResult(-1);
        }

        var maybePatch = processor.ParsePatch( System.IO.Path.GetFileName(filePath), fileContents);
        if (maybePatch.Failed) {
            Console.WriteColorLine($"Error parsing file [cyan]{filePath}[/cyan]: [red]{maybePatch.Error}[/red]");
            return Task.FromResult(-1);
        }
        
        System.Console.Write(maybePatch.Unwrap().Code);
        return Task.FromResult(0);
    }
}
