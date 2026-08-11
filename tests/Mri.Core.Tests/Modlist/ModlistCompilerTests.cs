using System.Text.Json;
using Mri.Core.Modlist;

namespace Mri.Core.Tests.Modlist;

public class ModlistCompilerTests
{
    private static Mri.Core.Modlist.Modlist SampleList() => new()
    {
        ListVersion = "2026.08.0",
        Name = "morrowind-remake",
        Mods =
        [
            new ModEntry
            {
                Id = "patch-for-purists",
                Name = "Patch for Purists",
                Category = "Bug Fixes / Patches",
                Author = "Half11",
                Source = new ModSource
                {
                    Handler = ModHandler.Nexus,
                    Url = "https://www.nexusmods.com/morrowind/mods/45096",
                    NexusId = 45096,
                },
                Downloads =
                [
                    new ModDownload
                    {
                        FileName = "Patch for Purists-45096-4-0-2.7z",
                        NexusFileId = 1000019704,
                        ExtractTo = "PatchForPurists",
                        Actions =
                        [
                            new FileAction { Type = FileActionType.Remove, Path = "PatchForPurists/bad.esp" },
                            new FileAction
                            {
                                Type = FileActionType.Rename,
                                Src = "PatchForPurists/a.esp",
                                Dst = "PatchForPurists/b.esp",
                            },
                        ],
                    },
                ],
                DataPaths = ["PatchForPurists"],
                Content =
                [
                    new ContentFile { File = "Patch for Purists.esm" },
                    new ContentFile { File = "PfP - Merge Input.esp", Mode = ContentMode.DeltaOnly },
                    new ContentFile { File = "Disabled Thing.esp", Mode = ContentMode.Disabled },
                ],
            },
            new ModEntry
            {
                Id = "aesthesia-groundcover",
                Name = "Aesthesia Groundcover",
                Category = "Groundcover",
                Source = new ModSource
                {
                    Handler = ModHandler.Direct,
                    Url = "https://example.com/grass",
                },
                Downloads =
                [
                    new ModDownload
                    {
                        FileName = "grass.zip",
                        DirectUrl = "https://example.com/grass.zip",
                        ExtractTo = "Aesthesia/00 Core",
                    },
                ],
                DataPaths = ["Aesthesia/00 Core"],
                Groundcover = ["Grass_AC.esp"],
            },
        ],
    };

    [Fact]
    public void UmoProjectionMatchesModDescSchema()
    {
        var json = ModlistCompiler.ToUmoModDescJson(SampleList());
        using var doc = JsonDocument.Parse(json);
        var mods = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, mods.ValueKind);
        Assert.Equal(2, mods.GetArrayLength());

        var pfp = mods[0];
        Assert.Equal("Patch for Purists", pfp.GetProperty("name").GetString());
        Assert.Equal("patch-for-purists", pfp.GetProperty("slug").GetString());
        Assert.Equal("nexus", pfp.GetProperty("handler").GetString());
        // String, not int — umo's Pydantic model requires Optional[str].
        Assert.Equal("45096", pfp.GetProperty("nexus_id").GetString());
        Assert.Equal("morrowind", pfp.GetProperty("nexus_game").GetString());
        Assert.Equal("PatchForPurists", pfp.GetProperty("dir").GetString());
        Assert.Equal("PatchForPurists", pfp.GetProperty("data_paths")[0].GetString());

        var download = pfp.GetProperty("download_info")[0];
        Assert.Equal(1000019704, download.GetProperty("nexus_file_id").GetInt64());
        Assert.True(download.GetProperty("pinned").GetBoolean());

        var actions = download.GetProperty("actions");
        Assert.Equal("remove", actions[0].GetProperty("action").GetString());
        Assert.Equal("PatchForPurists/bad.esp", actions[0].GetProperty("path").GetString());
        Assert.Equal("rename", actions[1].GetProperty("action").GetString());
        Assert.Equal("PatchForPurists/a.esp", actions[1].GetProperty("src").GetString());

        // Disabled plugins must not surface in umo's plugins list.
        var plugins = pfp.GetProperty("plugins").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Equal(["Patch for Purists.esm", "PfP - Merge Input.esp"], plugins);

        var grass = mods[1];
        Assert.Equal("direct", grass.GetProperty("handler").GetString());
        Assert.Equal("Aesthesia", grass.GetProperty("dir").GetString());
        Assert.Equal("https://example.com/grass.zip",
            grass.GetProperty("download_info")[0].GetProperty("direct_download").GetString());
    }

    [Fact]
    public void LoadOrderPlanPhase1IncludesDeltaOnlyPlugins()
    {
        var plan = ModlistCompiler.BuildLoadOrderPlan(SampleList(), new LoadOrderOptions());

        Assert.Equal(["PatchForPurists", "Aesthesia/00 Core"], plan.DataDirs);
        Assert.Equal(["Patch for Purists.esm", "PfP - Merge Input.esp"], plan.ContentFiles);
        Assert.Equal(["Grass_AC.esp"], plan.GroundcoverFiles);
    }

    [Fact]
    public void LoadOrderPlanPhase2SwapsDeltaOnlyForMergedAddon()
    {
        var plan = ModlistCompiler.BuildLoadOrderPlan(
            SampleList(), new LoadOrderOptions { IncludeDelta = true });

        Assert.Equal(["PatchForPurists", "Aesthesia/00 Core", "delta-merged"], plan.DataDirs);
        Assert.Equal(["Patch for Purists.esm", "delta-merged.omwaddon"], plan.ContentFiles);
    }

    [Fact]
    public void SkippedModsAreOmittedEverywhere()
    {
        var plan = ModlistCompiler.BuildLoadOrderPlan(SampleList(), new LoadOrderOptions
        {
            SkippedModIds = new HashSet<string> { "aesthesia-groundcover" },
        });

        Assert.Equal(["PatchForPurists"], plan.DataDirs);
        Assert.Empty(plan.GroundcoverFiles);
    }

    [Fact]
    public void CanonicalModlistRoundTripsThroughJson()
    {
        var original = SampleList();
        var restored = ModlistLoader.Load(ModlistLoader.Serialize(original));

        Assert.Equal(original, restored, ModlistEquality.Instance);
    }

    /// <summary>
    /// Records with collection properties don't get structural equality for
    /// free; compare via serialized form.
    /// </summary>
    private sealed class ModlistEquality : IEqualityComparer<Mri.Core.Modlist.Modlist>
    {
        public static readonly ModlistEquality Instance = new();

        public bool Equals(Mri.Core.Modlist.Modlist? x, Mri.Core.Modlist.Modlist? y) =>
            x is not null && y is not null &&
            ModlistLoader.Serialize(x) == ModlistLoader.Serialize(y);

        public int GetHashCode(Mri.Core.Modlist.Modlist obj) =>
            ModlistLoader.Serialize(obj).GetHashCode();
    }
}
