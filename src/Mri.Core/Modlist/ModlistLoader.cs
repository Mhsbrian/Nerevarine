using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mri.Core.Modlist;

public static class ModlistLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Modlist Load(string json) =>
        JsonSerializer.Deserialize<Modlist>(json, JsonOptions)
        ?? throw new InvalidDataException("Modlist JSON deserialized to null.");

    public static Modlist LoadFile(string path) => Load(File.ReadAllText(path));

    public static Modlist LoadStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

    public static string Serialize(Modlist modlist) =>
        JsonSerializer.Serialize(modlist, JsonOptions);
}
