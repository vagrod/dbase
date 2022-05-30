using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Dbase.Yaml;

public class DbaseYamlReader
{
    public ErrorOr<DbaseYaml> Read(string data) {
        try {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            return ErrorOr<DbaseYaml>.Success(deserializer.Deserialize<DbaseYaml>(data));
        }
        catch (Exception ex) {
            return ErrorOr<DbaseYaml>.Fail(ex.Message);
        }
    }     
}