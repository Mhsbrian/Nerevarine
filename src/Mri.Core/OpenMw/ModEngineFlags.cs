namespace Mri.Core.OpenMw;

/// <summary>
/// Engine settings that installed mods REQUIRE or strongly recommend (the
/// "enable this in the OpenMW launcher" class). The installer ships them at
/// their mod-friendly defaults; the Nerevarine launcher exposes each as a
/// toggle so the player can deviate knowingly. Sources: the mods' own
/// readmes, swept 2026-08-16.
/// </summary>
public sealed record EngineFlag(
    string Section,
    string Key,
    bool ModDefault,
    string Label,
    string RequiredBy);

public static class ModEngineFlags
{
    public static readonly IReadOnlyList<EngineFlag> All =
    [
        new("Game", "trainers training skills based on base skill", true,
            "Trainers offer skills by base value",
            "Better Merchants Skills, NCGD (required)"),
        new("Game", "weapon sheathing", true,
            "Visible sheathed weapons",
            "Weapon Sheathing, Animated Morrowind patch (required)"),
        new("Game", "shield sheathing", true,
            "Visible carried shields",
            "Weapon Sheathing companion behavior"),
        new("Game", "use additional anim sources", true,
            "Additional animation sources",
            "Bardcraft instruments, RUN sprint animation (required)"),
        new("Game", "graphic herbalism", true,
            "Graphic herbalism (pick plants by hand)",
            "Graphic Herbalism patches"),
        new("Game", "smooth animation transitions", true,
            "Smooth animation blending",
            "Bardcraft (recommended), general animation mods"),
        new("Game", "smooth movement", true,
            "Smooth NPC/player movement",
            "Modern movement feel (reference-build parity)"),
        new("Game", "turn to movement direction", true,
            "Turn toward movement direction",
            "Modern movement feel; pairs with RUN"),
        new("Game", "swim upward correction", true,
            "Swim upward correction",
            "Quality-of-life default"),
        new("Models", "load unsupported nif files", true,
            "Load extended NIF models",
            "Advanced Camera / SkyrimCompat rigs (required)"),
        new("GUI", "keyboard navigation", false,
            "Keyboard menu navigation",
            "OFF recommended by Bardcraft (Tab breaks its UI)"),
    ];

    /// <summary>Reads the current on/off state of a flag from settings.cfg text.</summary>
    public static bool Read(string settingsCfg, EngineFlag flag)
    {
        var doc = IniDocument.Parse(settingsCfg);
        var raw = doc.Get(flag.Section, flag.Key);
        return raw is null ? flag.ModDefault
            : string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string Write(string settingsCfg, EngineFlag flag, bool value)
    {
        var doc = IniDocument.Parse(settingsCfg);
        doc.Set(flag.Section, flag.Key, value ? "true" : "false");
        return doc.ToText();
    }
}
