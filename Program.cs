using System.IO;
using Dbase.CliActors;

namespace Dbase;

class Program
{
    
    /// <summary>
    /// The current version of the dbase utility. Will be compared to the version in the source databases before running patches
    /// </summary>
    public const string Version = "1.0";
    
    /// <summary>
    /// Current revision of the executable
    /// </summary>
    public const string Revision = "b";
    
    /// <summary>
    /// Supported cli commands
    /// </summary>
    private static readonly List<(string name, bool isArgumentOptional, int argsCount, int? optionalCount)> SwitchesConfig = new(new[]
    {
        (name: CliActorBase.InitCommand, 
            isArgumentOptional: false, 
            argsCount: 2,
            optionalCount: (int?)null), // <database-name> <server-config-name>
        (name: CliActorBase.UpdateCommand, 
            isArgumentOptional: false, 
            argsCount: 2,
            optionalCount: null), // <database-name> <server-config-name>
        (name: CliActorBase.AddServerCommand, 
            isArgumentOptional: false, 
            argsCount: 1,
            optionalCount: null), // <server-config-name>
        (name: CliActorBase.EditServerCommand, 
            isArgumentOptional: false, 
            argsCount: 1,
            optionalCount: null), // <server-config-name>
        (name: CliActorBase.RemoveServerCommand,
            isArgumentOptional: false,
            argsCount: 1,
            optionalCount: null), // <server-config-name>
        (name: CliActorBase.RunCommand, 
            isArgumentOptional: false, 
            argsCount: 3,
            optionalCount: 1), // <database-name> <server-config-name> <path-to-batches> [/silent] 
        (name: CliActorBase.AliasesCommand, 
            isArgumentOptional: false, 
            argsCount: 1,
            optionalCount: null), // <server-config-name>
        (name: CliActorBase.LsCommand, 
            isArgumentOptional: true, 
            argsCount: 0,
            optionalCount: 1),
        (name: CliActorBase.PrintCommand, // <file-path> <processor-type_or_config-name> 
            isArgumentOptional: false, 
            argsCount: 2,
            optionalCount: 0),
        (name: CliActorBase.HelpCommand, 
            isArgumentOptional: true, 
            argsCount: 0,
            optionalCount: null),
        (name: CliActorBase.VersionCommand, 
            isArgumentOptional: true, 
            argsCount: 0,
            optionalCount: null)
    });

    static async Task<int> Main(string[] args)
    {
        var processModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
        if (processModule != null) {
            var path = Path.GetDirectoryName(processModule.FileName);

            if (path != null)
                Directory.SetCurrentDirectory(path);
        }

        var appConfig = await Configuration.Load();

#if DEBUG
        var options = ParseCommandLine(new[]
        {
            "--version"
        });
#else
        var options = ParseCommandLine(args);
#endif
        
        if (!options.Any()) {
            Console.WriteColorLine($"[green]dbase[/green] version [cyan]{Version}{Revision}[/cyan]");
            Console.WriteLine();
            Console.WriteLine("Any key to exit");
            System.Console.ReadKey();
            return 0;
        }

        // Our cli supports one verb at a time
        var option = options.First();
        var actor = ActorsFactory.Get(option.Key, appConfig);
        
        // Don't trash the output for Print and Version commands
        if (option.Key is not CliActorBase.PrintCommand and not CliActorBase.VersionCommand) 
            Console.WriteColorLine($"[green]dbase[/green] version [cyan]{Version}{Revision}[/cyan]");

        if(actor == null)
        {
            Console.WriteLine();
            Console.WriteLine($"{option.Key} option is not supported");
            return -1;
        }

        return await actor.ExecuteAsync(option.Value);
    }

    #region Commands Parsing
    
    private static Dictionary<string, List<string>> ParseCommandLine(string[] args)
    {
        Dictionary<string, List<string>> options = new();
        
        string CleanOptionName(string s) => (s.StartsWith('-') ? s[1..] : s).ToLowerInvariant();
        bool HasOption(string s) => SwitchesConfig.Any(x => x.name == CleanOptionName(s));

        (string name, bool isArgumentOptional, int argsCount, int? optionalCount) lastConfig = default;
        
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-"))
            {
                var name = CleanOptionName(args[i]);
                var config = SwitchesConfig.SingleOrDefault(x => x.name == name);
                var optionArgs = new List<string>();
                var actualArgs = 0;

                if (config == default) {
                    throw new Exception($"Unknown parameter \"{name}\"");
                }

                lastConfig = config;

                if (config.argsCount > 0)
                {
                    var overallCount = config.argsCount + (config.optionalCount ?? 0);
                    for (var j = 1; j <= overallCount; j++) {
                        if (i + j > overallCount) 
                            continue;

                        if (i + j >= args.Length)
                            continue;
                        
                        if (i + j >= args.Length || HasOption(args[i + j]))
                        {
                            if(!config.isArgumentOptional)
                                throw new Exception($"Parameter \"{name}\" requires {config.argsCount} arguments");
                        }
                        else
                        {
                            optionArgs.Add(args[i + j]);
                            actualArgs++;
                        }
                    }

                    i += actualArgs;
                }
                
                options.Add(name, optionArgs);
            }
            else
            {
                if(lastConfig == default || !lastConfig.isArgumentOptional || !options.Any())
                    throw new Exception($"Value is not expected to be here (\"{args[i]}\"). Maybe you meant \"-{args[i]}\"?");

                var o = options.Last();
                var newOptions = new List<string>(new[] { args[i] });
                
                options.Remove(o.Key);
                newOptions.AddRange(o.Value);
                
                options.Add(o.Key, newOptions);
            }
        }

        return options;
    }
    
    #endregion
    
}
