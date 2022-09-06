namespace Dbase.CliActors;

public class VersionActor : CliActorBase
{
    public VersionActor(Configuration appConfig) : base(appConfig) { }

    public override Task<int> ExecuteAsync(List<string> args) {
        Console.WriteLine($"dbase version {Program.Version}");
        Console.WriteLine("Press any key");
        
        System.Console.ReadKey();
        
        return Task.FromResult(0);
    }
}
