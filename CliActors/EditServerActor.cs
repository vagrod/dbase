namespace Dbase.CliActors;

public class EditServerActor : CliActorBase
{
    public EditServerActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        if (args.Count == 0 || string.IsNullOrEmpty(args[0]))
        {
            Console.WriteColorLine($"Not enough parameters for [blue]\"{EditServerCommand}\"[/blue]: ([cyan]configuration name[/cyan]).");
            return Task.FromResult(-1);
        }

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
        var optionsCount = 9;
        
        System.Console.WriteLine("Choose a setting to change:");
        Console.WriteColorLine($"\t1. Configuration name [cyan]({server.ConfigurationName})[/cyan]");
        Console.WriteColorLine($"\t2. Type [cyan]({server.Type})[/cyan]");
        Console.WriteColorLine($"\t3. Server name [cyan]({server.DnsName})[/cyan]");
        Console.WriteColorLine($"\t4. Port [cyan]({(server.Port?.ToString() ?? "not set")})[/cyan]");
        Console.WriteColorLine($"\t5. User name [cyan]({server.DatabaseUser})[/cyan]");
        Console.WriteColorLine($"\t6. User password [cyan]({(string.IsNullOrEmpty(server.Password) ? "not set" : server.Password)})[/cyan]");
        Console.WriteColorLine($"\t7. Strict versioning [cyan]({((server.StrictVersioning.HasValue && server.StrictVersioning.Value) || !server.StrictVersioning.HasValue  ? "enabled" : "disabled")})[/cyan]");
        Console.WriteColorLine($"\t8. Patch preprocessing [cyan]({(preprocessingOn ? "enabled" : "disabled")})[/cyan]");
        Console.WriteColorLine($"\t9. Backup folder [cyan]({server.BackupFolder})[/cyan]");

        if (server.Type == ProcessorTypes.MsSql) {
            optionsCount++;
            var dataFolder = server.Options != null ? server.Options[ServerOptions.DataPath] : string.Empty;
            
            Console.WriteColorLine($"\t10. MSSQL storage folder (LOG and DATA) [cyan]({dataFolder})[/cyan]");
        }
        
        System.Console.WriteLine();
        Console.WriteColorLine($"\t0. Cancel and quit");
        Console.WriteColorLine($"\tс. Save and quit");
        
ReSelect:
        System.Console.Write("Choice: ");
        var choiceString = System.Console.ReadLine();

        if (!int.TryParse(choiceString, out var choice)) {
            if(!string.IsNullOrEmpty(choiceString) && (choiceString.ToLowerInvariant() == "c" || choiceString.ToLowerInvariant() == "с")) {
                AppConfig.Save();
                goto Saved;
            }
            
            Console.WriteColorLine($"Enter options from listed above.");
            goto ReSelect;
        }
        
        if (choice < 0 || choice > optionsCount) {
            Console.WriteColorLine($"Enter options from listed above.");
            goto ReSelect;
        }

        switch (choice) {
            case 0:
                return Task.FromResult(0);

            case 1:
            {
ReTryConfigName:
                var value = PromptInput("New configuration name", true);

                if (AppConfig.Servers!.Any(x =>
                        x.ConfigurationName.ToLowerInvariant() == value.ToLowerInvariant())) {
                    Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
                    goto ReTryConfigName;
                }
                server.ConfigurationName = value;

                goto ReSelect;
            }
            case 2:
            {
ReTryType:
                var value = PromptInput($"Configuration type [[cyan]{ProcessorTypes.MsSql}[/cyan], [cyan]{ProcessorTypes.Postgre}[/cyan]]", true);

                if (value == string.Empty || !new[] { ProcessorTypes.MsSql, ProcessorTypes.Postgre }.Contains(value.ToLowerInvariant())) {
                    Console.WriteColorLine($"Unknown configuration type [cyan]\"{value}\"[/cyan].");
                    goto ReTryType;
                }
                server.Type = value;

                goto ReSelect;
            }
            case 3:
            {
                var value = PromptInput("New server name", true);
                server.DnsName = value;
                
                goto ReSelect;
            }
            case 4:
            {
ReTryPort:
                var value = PromptInput("Port ([blue]enter[/blue] = not set)");
                if (string.IsNullOrEmpty(value)) 
                    server.Port = null;
                else {
                    if(!int.TryParse(value, out var port)) {
                        Console.WriteColorLine($"Invalid port [cyan]\"{value}\"[/cyan].");
                        goto ReTryPort;
                    } 
                    server.Port = port;
                }
                
                goto ReSelect;
            }
            case 5:
            {
                var value = PromptInput("New user name", true);
                server.DatabaseUser = value;
                
                goto ReSelect;
            }
            case 6:
            {
                var value = PromptInput("New password ([blue]enter[/blue] = empty password)");
                server.Password = value;
                
                goto ReSelect;
            }
            case 7:
            {
                var value = PromptInput("Strict versioning ([blue]enter[/blue] = [cyan]enable[/cyan])");
                server.StrictVersioning = string.IsNullOrEmpty(value);
                
                goto ReSelect;
            }
            case 8:
            {
                var value = PromptInput("Patch preprocessing ([blue]enter[/blue] = [cyan]enable[/cyan])");
                server.Options ??= new();
                
                if (server.Options.ContainsKey(ServerOptions.NoPreprocessing))
                    server.Options.Remove(ServerOptions.NoPreprocessing);
                
                server.Options.Add(ServerOptions.NoPreprocessing, string.IsNullOrEmpty(value) ? "false" : "true");
                
                goto ReSelect;
            }
            case 9:
            {
                var value = PromptInput("Backup folder ([blue]enter[/blue] = [cyan]no backups[/cyan])");
                server.BackupFolder = value;
                
                goto ReSelect;
            }
            case 10:
            {
ReTryDataFolder:
                var value = PromptInput("MSSQL storage folder (LOG and DATA)");

                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(server.BackupFolder)) {
                    Console.WriteColorLine($"With backup folder set, storage folder must set as well");
                    goto ReTryDataFolder;
                }
                
                server.Options ??= new();
                
                if (server.Options.ContainsKey(ServerOptions.DataPath))
                    server.Options.Remove(ServerOptions.DataPath);
                
                server.Options.Add(ServerOptions.DataPath, value);
                
                goto ReSelect;
            }
        }
        
Saved:
        Console.WriteColorLine("[green]Configuration was saved.[/green] Press any key.");
        System.Console.ReadLine();

        return Task.FromResult(0);
    }

    private static string PromptInput(string message, bool isRequired = false) {
        System.Console.Write($"{message}: ");
        
ReQuery:
        var value = System.Console.ReadLine();

        if (isRequired && string.IsNullOrEmpty(value)) {
            Console.WriteColor($"{message}: ");
            goto ReQuery;
        }

        return value ?? string.Empty;
    }
    
}
