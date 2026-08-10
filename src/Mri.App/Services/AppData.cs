using System.Reflection;
using Mri.Core.Modlist;
using Mri.Core.Tools;

namespace Mri.App.Services;

/// <summary>Embedded data shipped inside the exe: modlist, tool manifest, config templates.</summary>
public sealed class AppData
{
    public required Modlist Modlist { get; init; }
    public required ToolManifest ToolManifest { get; init; }
    public required string SettingsTemplate { get; init; }
    public required string ShadersTemplate { get; init; }

    public static AppData LoadEmbedded() => new()
    {
        Modlist = ModlistLoader.Load(ReadResource("modlist.json")),
        ToolManifest = ToolManifest.Load(ReadResource("tools.json")),
        SettingsTemplate = ReadResource("settings.template.cfg"),
        ShadersTemplate = ReadResource("shaders.template.yaml"),
    };

    private static string ReadResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = $"Mri.App.Data.{name}";
        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{fullName}' missing — available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
