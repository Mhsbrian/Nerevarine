namespace Mri.Core.OpenMw;

public static class OpenMwCfgWriter
{
    /// <summary>
    /// Quote a path value for openmw.cfg. Inside quotes OpenMW's escape
    /// character is '&amp;': '&amp;&amp;' is a literal ampersand, '&amp;"' a literal quote.
    /// Order matters — escape ampersands first.
    /// </summary>
    public static string QuotePath(string path) =>
        "\"" + path.Replace("&", "&&").Replace("\"", "&\"") + "\"";
}
