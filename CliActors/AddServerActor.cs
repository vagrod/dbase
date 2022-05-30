namespace Dbase.CliActors;

public class AddServerActor : CliActorBase
{
    public AddServerActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        if (args.Count == 0 || string.IsNullOrEmpty(args[0])) {
            Console.WriteColorLine($"Not enough parameters for [blue]\"{AddServerCommand}\"[/blue]: ([cyan]configuration name[/cyan]).");
            return Task.FromResult(-1);
        }

        var configOptions = new Dictionary<string, string>();
        var name = args[0];

ReDoType:
        Console.WriteColor($"Source type [[cyan]{ProcessorTypes.MsSql}[/cyan], [cyan]{ProcessorTypes.Postgre}[/cyan]]: ");
        var type = System.Console.ReadLine() ?? string.Empty;
        if (type == string.Empty || !new[] { ProcessorTypes.MsSql, ProcessorTypes.Postgre }.Contains(type.ToLowerInvariant())) {
            Console.WriteColorLine("[red]Invalid source type[/red]");
            goto ReDoType;
        }

ReDoHost:
        Console.WriteColor("Server address ([cyan]DNS[/cyan] name, or [cyan]IP[/cyan], without port): ");
        var dnsName = System.Console.ReadLine() ?? string.Empty;
        if (dnsName == string.Empty) {
            Console.WriteColorLine("[red]Invalid server address[/red]");
            goto ReDoHost;
        }

ReDoPort:
        Console.WriteColor("Port [gray](optional)[/gray]: ");
        var port = System.Console.ReadLine() ?? string.Empty;
        if (!string.IsNullOrEmpty(port) && !int.TryParse(port, out _)) {
            Console.WriteColorLine("[red]Invalid port[/red]");
            goto ReDoPort;
        }

ReDoUser:
        var user = String.Empty;
        if (type == ProcessorTypes.MsSql) {
            Console.WriteColor("User name [gray](optional, if not set, Integrated Security will be used)[/gray]: ");
            user = System.Console.ReadLine() ?? string.Empty;
        }
        else {
            System.Console.Write("User name: ");
            user = System.Console.ReadLine() ?? string.Empty;
            if (user == string.Empty) {
                Console.WriteColorLine("[red]Empty user name[/red]");
                goto ReDoUser;
            }
        }

        var password = string.Empty;
        if (user != string.Empty) {
            Console.WriteColor("User password ([blue]enter[/blue] = no password): ");
            password = System.Console.ReadLine() ?? string.Empty;
        }

        Console.WriteColor("Backup folder ([blue]enter[/blue] = [cyan]no backups[/cyan]): ");
        var backupPath = System.Console.ReadLine() ?? string.Empty;

        if (!string.IsNullOrEmpty(backupPath) && type == ProcessorTypes.MsSql) {
            ReDoDataPath:
            Console.WriteColor("SQL storage folder [cyan]MSSQL (LOG and DATA)[/cyan]: ");
            var dataPath = System.Console.ReadLine() ?? string.Empty;

            if (string.IsNullOrEmpty(dataPath)) {
                Console.WriteColorLine($"With backup folder set, storage folder must set as well");
                goto ReDoDataPath;
            }

            configOptions.Add("dataPath", dataPath);
        }

        Console.WriteColor("Disable patch preprocessing? ([blue]enter[/blue] = [cyan]enable[/cyan]): ");
        var noPreprocessing = !string.IsNullOrEmpty(System.Console.ReadLine() ?? string.Empty);

        if (noPreprocessing)
            configOptions.Add(ServerOptions.NoPreprocessing, "true");

        Console.WriteColor("Disable strict versioning? ([blue]enter[/blue] = [cyan]enable[/cyan]): ");
        var strictVersioning = string.IsNullOrEmpty(System.Console.ReadLine() ?? string.Empty);

        if(AppConfig.Servers is null)
            AppConfig.Servers = new List<ServerDescription>();
        
        AppConfig.Servers.Add(new ServerDescription
        {
            ConfigurationName = name,
            Type = type.ToLower(),
            DnsName = dnsName,
            Port = !string.IsNullOrEmpty(port) ? int.Parse(port) : null,
            DatabaseUser = user,
            Password = password,
            BackupFolder = backupPath,
            StrictVersioning = strictVersioning,
            Options = configOptions
        });

        AppConfig.Save();

        Console.WriteColorLine("[green]New configuration was saved.[/green] Press any key.");
        System.Console.ReadLine();

        return Task.FromResult(0);
    }
}
