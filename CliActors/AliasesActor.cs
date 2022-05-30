namespace Dbase.CliActors;

public class AliasesActor : CliActorBase
{
    public AliasesActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        if (args.Count == 0 || string.IsNullOrEmpty(args[0]))
        {
            Console.WriteColorLine($"Not enough parameters for [blue]\"{AliasesCommand}\"[/blue]: ([cyan]configuration name[/cyan]).");
            return Task.FromResult(-1);
        }

        var configName = args[0].ToLowerInvariant();
        var config = AppConfig.Servers!.FirstOrDefault(x => x.ConfigurationName.ToLowerInvariant() == configName);
        
        if (config == null) {
            Console.WriteColorLine($"Configuration with name [cyan]\"{configName}\"[/cyan] [red]not found[red]");
            return Task.FromResult(-1);
        }
        
        if(config.Aliases.Count == 0) 
            Console.WriteColorLine($"Configuration [cyan]\"{config.ConfigurationName}\"[/cyan] does not contain any aliases yet.");

Home:
        Console.WriteColorLine("[blue](+)[/blue] = new alias");
        Console.WriteColorLine("[blue](c)[/blue] = save all changes");
        Console.WriteColorLine("[blue](x)[/blue] = quit without saving");

ReSelectAliases:
        var i = 0;
        foreach (var alias in config.Aliases) {
            i++;
            Console.WriteColorLine($"[blue]({i})[/blue] = change [cyan]{alias.Key}[/cyan] [gray]=>[/gray] [green]{alias.Value}[/green]");
        }

        Console.WriteLine();
        
        int choiceInt;

ReEnterAliasChoice:
        System.Console.Write("Ваш выбор: ");
        var choice = System.Console.ReadLine();
        if (choice == "+")
            goto AddAlias;
        
        if (choice == "c" || choice == "с")
            goto SaveAll;
        
        if (choice == "x")
            return Task.FromResult(0);
        
        if(!int.TryParse(choice, out choiceInt)) {
            System.Console.WriteLine("Expected a number.");
            goto ReEnterAliasChoice;
        }
        
        var selectedAlias = config.Aliases.ElementAtOrDefault(choiceInt - 1);
        if (selectedAlias.Equals(default)) {
            System.Console.WriteLine("Alias not found.");
            goto ReSelectAliases;
        }
AliasHome:
        Console.WriteColorLine($"[cyan]{selectedAlias.Key}[/cyan] = [green]{selectedAlias.Value}[/green]");
        Console.WriteColorLine($"[blue](1)[/blue] = edit the name");
        Console.WriteColorLine($"[blue](2)[/blue] = edit value");
        Console.WriteColorLine($"[blue](3)[/blue] = remove alias");
        Console.WriteColorLine($"[blue](4)[/blue] = go back");
        
ReEnterActionChoice:
        System.Console.Write("Choice: ");
        choice = System.Console.ReadLine();
        if(!int.TryParse(choice, out choiceInt)) {
            System.Console.WriteLine("Expected a number.");
            goto ReEnterActionChoice;
        }

        switch (choiceInt) {
            case 1:
ReEnterName:
                System.Console.Write("New name (should be unique): ");
                var newName = System.Console.ReadLine();
                if (string.IsNullOrEmpty(newName)) {
                    System.Console.WriteLine("Empty name.");
                    goto ReEnterName;
                }
                if(config.Aliases.Count(x => x.Key == newName) > 1) {
                    System.Console.WriteLine("This alias already exists.");
                    goto ReEnterName;
                } 

                System.Console.WriteLine();
                
                config.Aliases.Remove(selectedAlias.Key);
                config.Aliases.Add(newName, selectedAlias.Value);
                selectedAlias = new KeyValuePair<string, string>(newName, selectedAlias.Value);

                goto AliasHome;
            
            case 2:
                System.Console.Write("New value: ");
                var newValue = System.Console.ReadLine();
               
                config.Aliases.Remove(selectedAlias.Key);
                config.Aliases.Add(selectedAlias.Key, newValue ?? string.Empty);
                
                selectedAlias = new KeyValuePair<string, string>(selectedAlias.Key, newValue ?? string.Empty);

                System.Console.WriteLine();
                
                goto AliasHome;
                
            case 3:
                Console.WriteColorLine($"Remove [cyan]{selectedAlias.Key}[/cyan]? [gray](yes/no/y/n)[/gray]: ");
                var answer = System.Console.ReadLine()?.ToLower();

                System.Console.WriteLine();
                
                if (answer == "yes" || answer == "y") {
                    config.Aliases.Remove(selectedAlias.Key);
                    goto Home;
                }

                goto AliasHome;
                
            case 4:
                System.Console.WriteLine();
                goto Home;
        }
        
AddAlias:
        System.Console.Write("Alias name: ");
        var aliasName = System.Console.ReadLine();
        if (string.IsNullOrEmpty(aliasName)) {
            System.Console.WriteLine("Empty name.");
            goto AddAlias;
        }
        if(config.Aliases.Count(x => x.Key == aliasName) > 0) {
            System.Console.WriteLine("Alias already exists.");
            goto AddAlias;
        } 
        System.Console.Write("Value: ");
        var aliasValue = System.Console.ReadLine();
        config.Aliases.Add(aliasName, aliasValue ?? string.Empty);
        
        System.Console.WriteLine();

        goto Home;

SaveAll:
        AppConfig.Save();
        
        System.Console.WriteLine("New configuration was saved. Press any key.");
        System.Console.ReadLine();

        return Task.FromResult(0);
    }
}
