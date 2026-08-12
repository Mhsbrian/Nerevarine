using System.Text;

namespace Mri.Curation.Inspect;

/// <summary>
/// Minimal TES3 plugin header reader: enough to know a plugin's master files
/// (MAST subrecords). Activation is only safe when every master is active —
/// OpenMW refuses to start on a missing master, so the activation planner
/// validates the whole dependency graph before emitting content lists.
/// </summary>
public static class PluginScanner
{
    public sealed record PluginHeader(
        string FileName,
        IReadOnlyList<string> Masters,
        bool IsMaster); // .esm/.omwgame flag semantics aside, extension is what load rules care about

    public static PluginHeader? TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (tag != "TES3")
                return null;
            var recordSize = reader.ReadUInt32();
            reader.ReadUInt32(); // unknown
            reader.ReadUInt32(); // flags

            var masters = new List<string>();
            long recordEnd = stream.Position + recordSize;
            while (stream.Position < recordEnd - 8)
            {
                var subTag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var subSize = reader.ReadUInt32();
                if (stream.Position + subSize > recordEnd)
                    break;
                if (subTag == "MAST")
                {
                    var name = Encoding.GetEncoding("ISO-8859-1")
                        .GetString(reader.ReadBytes((int)subSize)).TrimEnd('\0');
                    masters.Add(name);
                }
                else
                {
                    stream.Seek(subSize, SeekOrigin.Current);
                }
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return new PluginHeader(Path.GetFileName(path), masters, ext is ".esm" or ".omwgame");
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Plugin-ish files (activatable) directly inside a data root.</summary>
    public static readonly string[] PluginExtensions = [".esm", ".esp", ".omwaddon", ".omwscripts", ".omwgame"];

    public static IEnumerable<string> FindPlugins(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
            yield break;
        foreach (var file in Directory.EnumerateFiles(dataRoot)
                     .Where(f => PluginExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            yield return file;
    }
}
