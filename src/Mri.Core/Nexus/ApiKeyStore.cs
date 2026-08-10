using System.Security.Cryptography;
using System.Text;

namespace Mri.Core.Nexus;

/// <summary>
/// Persists the user's Nexus API key. On Windows the key is DPAPI-protected
/// (CurrentUser scope); elsewhere (Linux dev machines only — the shipped app
/// is Windows-only) it falls back to a base64 file with owner-only permissions.
/// </summary>
public sealed class ApiKeyStore(string filePath)
{
    private static readonly byte[] Entropy = "MorrowindRemakeInstaller.nexus"u8.ToArray();

    public void Save(string apiKey)
    {
        var plaintext = Encoding.UTF8.GetBytes(apiKey);
        byte[] payload;
        if (OperatingSystem.IsWindows())
        {
            payload = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        }
        else
        {
            payload = plaintext;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        File.WriteAllText(filePath, Convert.ToBase64String(payload));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public string? Load()
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            var payload = Convert.FromBase64String(File.ReadAllText(filePath).Trim());
            var plaintext = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser)
                : payload;
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null; // Corrupt or foreign blob — treat as absent.
        }
    }

    public void Clear()
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }
}
