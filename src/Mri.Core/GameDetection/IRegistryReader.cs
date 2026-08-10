namespace Mri.Core.GameDetection;

public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
}

public enum RegistryWidth
{
    Registry32,
    Registry64,
}

/// <summary>
/// Thin seam over the Windows registry so detection logic is unit-testable on
/// any OS. Implementations must return null/empty rather than throw when a key
/// is missing or inaccessible.
/// </summary>
public interface IRegistryReader
{
    string? GetString(RegistryRoot root, RegistryWidth width, string subKey, string valueName);

    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryWidth width, string subKey);
}

/// <summary>No-op reader for non-Windows hosts (dev on Linux).</summary>
public sealed class NullRegistryReader : IRegistryReader
{
    public string? GetString(RegistryRoot root, RegistryWidth width, string subKey, string valueName) => null;

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, RegistryWidth width, string subKey) =>
        Array.Empty<string>();
}
