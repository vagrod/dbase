using System.IO;

namespace Dbase.CliActors;

public class RunActor : CliActorBase
{
    public RunActor(Configuration appConfig) : base(appConfig) { }

    public override async Task<int> ExecuteAsync(List<string> args) {
        if (args.Count < 2) {
            Console.WriteColorLine($"Not enough parameters for [blue]\"{RunCommand}\"[/blue] ([cyan]database name[/cyan], [cyan]configuration name[/cyan] and [cyan]path to patch(es)[/cyan]).");
            return -1;
        }

        var dbName = args[0];
        var configName = args[1];
        var path = args[2];
        var silent = args.Contains("/silent");

        var config = AppConfig.Servers!.FirstOrDefault(x =>
            x.ConfigurationName.ToLowerInvariant() == configName.ToLowerInvariant());
        if (config == null) {
            Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
            return -1;
        }

        var maybeProcessor = ProcessorsFactory.Instance.GetProcessor(config, dbName);
        if (maybeProcessor.Failed) {
            Console.WriteColorLine($"[red]{maybeProcessor.Error}[/red]");
            return -1;
        }

        var processor = maybeProcessor.Unwrap();
        var maybeMeta = await processor.GetMetaAsync();
        if (maybeMeta.Failed) {
            Console.WriteColorLine($"[red]{maybeMeta.Error}[/red]");
            return -1;
        }

        var meta = maybeMeta.Unwrap();

        if (!meta.IsInitialized) {
            Console.WriteColorLine($"Database [cyan]\"{dbName}\"[/cyan] [red]was not[/red] initialized for [green]dbase[/green]. Run \"[green]dbase[/green] -[blue]{InitCommand}[/blue] [cyan]{configName}[/cyan] [cyan]{dbName}[/cyan]\"");
            return -1;
        }

        var isFile = !string.IsNullOrEmpty(Path.GetExtension(path));
        if (isFile)
            return await ProcessSingleFile(path, processor, silent);
        else 
            return await ProcessPatchesFolder(path, processor, silent);
    }

    private async Task<int> ProcessPatchesFolder(string path, DatabaseProcessorBase processor, bool silent) {
        var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly);
        var patches = new List<Patch>();

        foreach (var file in files) {
            var maybePatch = await PatchFromFile(file, processor);
            if (maybePatch.Failed) {
                Console.WriteColorLine($"[red]{maybePatch.Error}[/red]");
                return -1;
            }

            patches.Add(maybePatch.Unwrap());
        }

        var result = await processor.RunAsync(patches);

#pragma warning disable 8602
        if (result.Failed) {
            Console.WriteColorLine($"[red]Error processing patch[/red] version [cyan]{result.Error.Version}[/cyan]: {result.Error.Description}");
            return -1;
        }
#pragma warning restore 8602

        if (!silent) {
            Console.WriteColorLine("[green]All patches were applied.[/green] Press any key..");
            System.Console.ReadLine();
        }
        else {
            Console.WriteColorLine("[green]All patches were applied.[/green]");
        }

        return 0;
    }

    private async Task<int> ProcessSingleFile(string path, DatabaseProcessorBase processor, bool silent) {
        var maybePatch = await PatchFromFile(path, processor);
        if (maybePatch.Failed) {
            Console.WriteColorLine($"[red]{maybePatch.Error}[/red]");
            return -1;
        }
        
        var result = await processor.RunPatchAsync(maybePatch.Unwrap(), RunPatchOptions.CheckBeforeRun);

#pragma warning disable 8602
        if (result.Failed) {
            Console.WriteColorLine($"[red]Error processing patch[/red]: {result.Error.Description}");
            return -1;
        }
#pragma warning restore 8602

        if (!silent) {
            Console.WriteColorLine("[green]All patches were applied.[/green] Press any key.");
            System.Console.ReadLine();
        }
        else {
            Console.WriteColorLine("[green]All patches were applied.[/green]");
        }

        return 0;
    }

    private async Task<ErrorOr<Patch>> PatchFromFile(string path, DatabaseProcessorBase processor) {
        var fileName = Path.GetFileName(path);
        var fileContents = await File.ReadAllTextAsync(path);
        var maybePatch = processor.ParsePatch(fileName, fileContents);

        return maybePatch;
    }

}
