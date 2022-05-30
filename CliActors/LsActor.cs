namespace Dbase.CliActors;

public class LsActor : CliActorBase
{
    public LsActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        if (args.Count > 0) {
            var configName = args[0].ToLowerInvariant();

            if (AppConfig.Servers!.All(x => x.ConfigurationName.ToLowerInvariant() != configName)) {
                Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
                return Task.FromResult(-1);
            }

            var server = AppConfig.Servers!.FirstOrDefault(x => x.ConfigurationName.ToLowerInvariant() == configName);
        
            if (server == null) {
                Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
                return Task.FromResult(-1);
            }
            
            var preprocessingOn = server.Options == null || (server.Options != null && server.Options.ContainsKey(ServerOptions.NoPreprocessing) && server.Options[ServerOptions.NoPreprocessing].ToLowerInvariant() != "true");
            
            Console.WriteLine($"\tSettings {server.ConfigurationName}:");
            Console.WriteLine("\t____________________________");
            Console.WriteColorLine($"\t  - Configuration name [cyan]({server.ConfigurationName})[/cyan]");
            Console.WriteColorLine($"\t  - Type [cyan]({server.Type})[/cyan]");
            Console.WriteColorLine($"\t  - Server name [cyan]({server.DnsName})[/cyan]");
            Console.WriteColorLine($"\t  - Port [cyan]({(server.Port?.ToString() ?? "не указан")})[/cyan]");
            Console.WriteColorLine($"\t  - User name [cyan]({server.DatabaseUser})[/cyan]");
            Console.WriteColorLine($"\t  - User password [cyan]({(string.IsNullOrEmpty(server.Password) ? "не указан" : server.Password)})[/cyan]");
            Console.WriteColorLine($"\t  - Strict versioning [cyan]({((server.StrictVersioning.HasValue && server.StrictVersioning.Value) || !server.StrictVersioning.HasValue  ? "enabled" : "disabled")})[/cyan]");
            Console.WriteColorLine($"\t  - Patch preprocessing [cyan]({(preprocessingOn ? "enabled" : "disabled")})[/cyan]");
            Console.WriteColorLine($"\t  - Backup folder [cyan]({server.BackupFolder})[/cyan]");
            
            if (server.Type == ProcessorTypes.MsSql) {
                var dataFolder = server.Options != null ? server.Options[ServerOptions.DataPath] : string.Empty;
            
                Console.WriteColorLine($"\t  - MSSQL storage folder (LOG and DATA) [cyan]({dataFolder})[/cyan]");
            }
            
            Console.WriteColorLine($"\t  - Aliases ({server.Aliases.Count}):");

            foreach (var alias in server.Aliases) {
                Console.WriteColorLine($"\t      - {alias.Key} = {alias.Value}");
            }
            
            System.Console.WriteLine();
        
            Console.WriteLine("\tAny key to exit.");
            System.Console.ReadKey();
            
            return Task.FromResult(0);
        } 
        
        Console.WriteLine("\tList of servers:");
        Console.WriteLine("\t____________________________");
        foreach (var server in AppConfig.Servers!) {
            Console.WriteColorLine($"\t[blue]{server.ConfigurationName}[/blue]");
            Console.WriteColorLine($"\t\tType: [cyan]{server.Type}[/cyan]");
            Console.WriteColorLine($"\t\tAddress: [cyan]{server.DnsName}[/cyan]");
            Console.WriteColorLine($"\t\tAliases count: [cyan]{server.Aliases.Count}[/cyan]");
            System.Console.WriteLine();
        }
        
        Console.WriteLine("\tAny key to exit.");
        System.Console.ReadKey();
        
        return Task.FromResult(0);
    }
}
