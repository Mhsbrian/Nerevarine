using Mri.Core.Umo;

namespace Mri.Core.Tests.Umo;

/// <summary>
/// Fixture lines are verbatim umo 0.11.x output captured from real installer
/// runs (logs of 2026-08-10/11) — the parser is tested against reality, not
/// against what we imagine umo prints.
/// </summary>
public class UmoProgressParserTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void SyncingLineIsDownloadWithModName()
    {
        var evt = UmoProgressParser.Parse($"{Esc}[32msyncing Patch for Purists{Esc}[0m");
        Assert.Equal(UmoEventKind.Download, evt.Kind);
        Assert.Equal("Patch for Purists", evt.ModName);
    }

    [Fact]
    public void RedFailureHeaderCarriesTheModName()
    {
        var evt = UmoProgressParser.Parse($"{Esc}[31m152 - Traveling Merchants:{Esc}[0m");
        Assert.Equal(UmoEventKind.ModFailed, evt.Kind);
        Assert.Equal("Traveling Merchants", evt.ModName);
    }

    [Fact]
    public void RedDetailLineIsErrorDetailNotAFailureName()
    {
        // The old parser harvested this junk as a "mod name".
        var evt = UmoProgressParser.Parse(
            $"{Esc}[31m- error received - skipping: RetryError[<Future at 0x1b317ec4ef0 state=finished raised error>]{Esc}[0m");
        Assert.Equal(UmoEventKind.ErrorDetail, evt.Kind);
        Assert.Null(evt.ModName);
    }

    [Fact]
    public void NormalizeErrorStripsThePreambleSoIdenticalRootsCompareEqual()
    {
        // 500 of these must tally as ONE dominant cause.
        var a = UmoProgressParser.NormalizeError(
            "- error received - skipping: Status Code 401 - b'{\"message\":\"Please provide an authentication method\"}'");
        var b = UmoProgressParser.NormalizeError(
            "  - error received - skipping: Status Code 401 - b'{\"message\":\"Please provide an authentication method\"}'");
        Assert.Equal(a, b);
        Assert.StartsWith("Status Code 401", a);
    }

    [Fact]
    public void NoModFileFoundExtractsTheModName()
    {
        var evt = UmoProgressParser.Parse(
            $"{Esc}[31mNo mod file found for \"Kezyma's Voices of Vvardenfell/Kezyma's Voices of Vvardenfell\"" +
            $" - MOMW data may be out of date :( - skipping{Esc}[0m");
        Assert.Equal(UmoEventKind.ModFailed, evt.Kind);
        Assert.Equal("Kezyma's Voices of Vvardenfell", evt.ModName);
    }

    [Fact]
    public void PlainPycurlErrorLineIsInfo()
    {
        // Not red — context only; the red header above it named the mod.
        var evt = UmoProgressParser.Parse(
            "pycurl.error: (28, 'Failed to connect to mw.modhistory.com port 443 after 21106 ms: Could not connect to server')");
        Assert.Equal(UmoEventKind.Info, evt.Kind);
    }

    [Fact]
    public void ModDescriptionProseContainingErrorIsInfo()
    {
        // Description text once polluted state.json's failed-mod list.
        var evt = UmoProgressParser.Parse(
            "    \"description\": \"fixes an error when fighting with spell casters who don't have an active spell\",");
        Assert.Equal(UmoEventKind.Info, evt.Kind);
        Assert.Null(evt.ModName);
    }

    [Fact]
    public void AdminRefusalIsRedButNotAModFailure()
    {
        var evt = UmoProgressParser.Parse($"{Esc}[31mDon't run umo with admin rights!{Esc}[0m");
        Assert.Equal(UmoEventKind.ErrorDetail, evt.Kind);
        Assert.Null(evt.ModName);
    }

    [Fact]
    public void CountersAreExtracted()
    {
        var evt = UmoProgressParser.Parse("Downloading Patch for Purists (3/617)");
        Assert.Equal(UmoEventKind.Download, evt.Kind);
        Assert.Equal(3, evt.Current);
        Assert.Equal(617, evt.Total);
    }

    [Fact]
    public void BareCounterIsProgress()
    {
        var evt = UmoProgressParser.Parse("[42/617] syncing");
        Assert.Equal(UmoEventKind.Progress, evt.Kind);
        Assert.Equal(42, evt.Current);
    }

    [Fact]
    public void StripAnsiRemovesColorCodes() =>
        Assert.Equal("syncing Mass Cure",
            UmoProgressParser.StripAnsi($"{Esc}[32msyncing Mass Cure{Esc}[0m"));

    [Fact]
    public void UnknownLinesDegradeToInfo() =>
        Assert.Equal(UmoEventKind.Info,
            UmoProgressParser.Parse("some future umo output we have never seen").Kind);
}
