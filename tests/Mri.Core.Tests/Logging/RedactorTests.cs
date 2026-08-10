using System.Text.Json;
using Mri.Core.Logging;

namespace Mri.Core.Tests.Logging;

public class RedactorTests
{
    // Same shape as a real Nexus API key: base64 segments with '+' and '/'.
    private const string Secret = "FaKeKeyAAAA1111bbbb2222cccc+/dd/EEEE3333ffff+GgG/Hh4/Ii==--JjKk9LmN--OoPpQq==";

    [Fact]
    public void RedactsRawForm()
    {
        var variants = Redactor.VariantsOf(Secret);
        Assert.Equal(Redactor.Mask, Redactor.Apply(Secret, variants));
    }

    [Fact]
    public void RedactsJsonEscapedForm()
    {
        // This is the exact leak from the field: System.Text.Json writes '+'
        // as +, so the file content never contains the raw secret.
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["apikey"] = Secret });
        Assert.DoesNotContain(Secret, json); // proves the escaping happens

        var cleaned = Redactor.Apply(json, Redactor.VariantsOf(Secret));
        Assert.Contains(Redactor.Mask, cleaned);
        Assert.DoesNotContain("FaKeKeyAAAA1111bbbb2222cccc", cleaned);
    }

    [Fact]
    public void RedactsUrlEncodedForm()
    {
        var url = $"https://api.example.com/?key={Uri.EscapeDataString(Secret)}";
        var cleaned = Redactor.Apply(url, Redactor.VariantsOf(Secret));

        Assert.Contains(Redactor.Mask, cleaned);
        Assert.DoesNotContain("FaKeKeyAAAA1111bbbb2222cccc", cleaned);
    }

    [Fact]
    public void PlainSecretYieldsSingleVariant()
    {
        // No special characters → no extra variants to scan for.
        Assert.Single(Redactor.VariantsOf("plain-alphanumeric-key-123"));
    }
}
