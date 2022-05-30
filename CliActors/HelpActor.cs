namespace Dbase.CliActors;

public class HelpActor : CliActorBase
{
    public HelpActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        Console.WriteLine("\tAvailable commands:");
        Console.WriteLine("\t________________________");
        
        Console.WriteColorLine($"\t[blue]-{HelpCommand}[/blue] - print this help");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{HelpCommand}[/blue]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{InitCommand}[/blue] - initialize database with dbase");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{InitCommand}[/blue] [cyan]<database_name>[/cyan] [cyan]<config_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{UpdateCommand}[/blue] - update database to match this dbase version");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{UpdateCommand}[/blue] [cyan]<database_name>[/cyan] [cyan]<config_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{AddServerCommand}[/blue] - add new server configuration [gray](starts the wizard)[/gray]");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{AddServerCommand}[/blue] [cyan]<config_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{EditServerCommand}[/blue] - change server configuration [gray](starts the wizard)[/gray]");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{EditServerCommand}[/blue] [cyan]<config_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{RemoveServerCommand}[/blue] - remove server configuration");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{RemoveServerCommand}[/blue] [cyan]<config_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{RunCommand}[/blue] - run directory with patches, or a single patch file");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{RunCommand}[/blue] [cyan]<database_name>[/cyan] [cyan]<config_name>[/cyan] [cyan]<path>[/cyan] [[cyan]/silent[/cyan]]");
        Console.WriteColorLine($"\t\t[cyan]<path>[/cyan] can either be a folder, or a patch file (.sql, .yaml, etc)");
        Console.WriteColorLine($"\t\t[cyan]/silent[/cyan] optional. If set, dbase won't be waiting for the user input after command completes");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{AliasesCommand}[/blue] - set up aliases for a particular server [gray](starts the wizard)[/gray]");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{AliasesCommand}[/blue] [cyan]<config_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{LsCommand}[/blue] - lists all server configurations. If name is set, will dump configuration defails");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{LsCommand}[/blue] [[cyan]<config_name>[/cyan]]");
        Console.WriteLine();
        
        Console.WriteColorLine($"\t[blue]-{PrintCommand}[/blue] - prints to stdout processed patch code");
        Console.WriteColorLine($"\t\t[green]dbase[/green] [blue]-{PrintCommand}[/blue] [cyan]<file_path>[/cyan] [cyan]<config_name_or_processor_name>[/cyan]");
        Console.WriteLine();
        
        Console.WriteLine("\tPress any key");
        System.Console.ReadKey();
        
        return Task.FromResult(0);
    }
}
