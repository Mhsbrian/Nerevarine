using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Mri.Core.GameDetection;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryReader : IRegistryReader
{
    public string? GetString(RegistryRoot root, RegistryWidth width, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(ToHive(root), ToView(width));
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryWidth width, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(ToHive(root), ToView(width));
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static RegistryHive ToHive(RegistryRoot root) => root switch
    {
        RegistryRoot.CurrentUser => RegistryHive.CurrentUser,
        RegistryRoot.LocalMachine => RegistryHive.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(root)),
    };

    private static RegistryView ToView(RegistryWidth width) => width switch
    {
        RegistryWidth.Registry32 => RegistryView.Registry32,
        RegistryWidth.Registry64 => RegistryView.Registry64,
        _ => throw new ArgumentOutOfRangeException(nameof(width)),
    };
}
