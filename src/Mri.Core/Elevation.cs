using System.Security.Principal;

namespace Mri.Core;

public static class Elevation
{
    /// <summary>
    /// True when the current process runs with administrator rights. umo
    /// hard-refuses elevated runs (exit 3, "Don't run umo with admin
    /// rights!") because admin-owned extracted files cause permission chaos
    /// later — so the installer must refuse up front, with a message that
    /// says what to do about it.
    /// </summary>
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
