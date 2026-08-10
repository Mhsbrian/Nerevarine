using Mri.Core;

namespace Mri.Core.Tests;

public class ElevationTests
{
    [Fact]
    public void DoesNotThrowAndIsFalseOffWindows()
    {
        // On Windows dev boxes this may legitimately be true; the contract we
        // can assert everywhere is "never throws", plus "false on non-Windows".
        var elevated = Elevation.IsElevated();
        if (!OperatingSystem.IsWindows())
            Assert.False(elevated);
    }
}
