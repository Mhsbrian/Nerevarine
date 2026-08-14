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
    public required IReadOnlyList<string> MomwContentOrder { get; init; }

    private static readonly string[] FixupResources =
        ["MRI_AbeceanGreetingFix.esp", "script-patches.json"];

    public static AppData LoadEmbedded() => new()
    {
        Modlist = ModlistLoader.Load(ReadResource("modlist.json")),
        ToolManifest = ToolManifest.Load(ReadResource("tools.json")),
        SettingsTemplate = ReadResource("settings.template.cfg"),
        ShadersTemplate = ReadResource("shaders.template.yaml"),
        MomwContentOrder = ReadResource("momw-content-order.txt")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };

    /// <summary>
    /// Writes the embedded record-fixup payload (repair plugin + script
    /// patches) to a directory the install-fixups step can consume.
    /// </summary>
    public static string MaterializeFixups(string installDir)
    {
        var dir = Path.Combine(installDir, "fixups-src");
        Directory.CreateDirectory(dir);
        foreach (var name in FixupResources)
        {
            using var stream = OpenResource($"fixups.{name}");
            using var file = File.Create(Path.Combine(dir, name));
            stream.CopyTo(file);
        }
        return dir;
    }

    private static Stream OpenResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"Mri.App.Data.{name}")
        ?? throw new InvalidOperationException($"Embedded resource 'Mri.App.Data.{name}' missing.");

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
