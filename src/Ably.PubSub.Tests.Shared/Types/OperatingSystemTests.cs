using System.Runtime.InteropServices;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Types
{
    public class OperatingSystemTests
    {
        [Fact]
        public void IsWindows()
        {
            OperatingSystem.IsWindows().Should().Be(IsPlatform(OSPlatform.Windows));
        }

        [Fact]
        public void IsMacOS()
        {
            OperatingSystem.IsMacOS().Should().Be(IsPlatform(OSPlatform.OSX));
        }

        [Fact]
        public void IsLinux()
        {
            OperatingSystem.IsLinux().Should().Be(IsPlatform(OSPlatform.Linux));
        }

        private static bool IsPlatform(OSPlatform osPlatform) => RuntimeInformation.IsOSPlatform(osPlatform);
    }
}
