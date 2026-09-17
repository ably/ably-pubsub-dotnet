using System;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Types
{
    public class SemanticVersionTests
    {
        [Fact]
        public void Construction_Default()
        {
            var semVer = new SemanticVersion();
            semVer.Major.Should().Be(0);
            semVer.Minor.Should().Be(0);
            semVer.Patch.Should().Be(0);
        }

        [Fact]
        public void Construction_ExplicitMajorMinorPatch()
        {
            var semVer = new SemanticVersion(3, 5, 8);

            semVer.Major.Should().Be(3);
            semVer.Minor.Should().Be(5);
            semVer.Patch.Should().Be(8);
        }

        [Fact]
        public void Construction_FromVersion()
        {
            var ver = new Version(3, 5, 8, 13);
            var semVer = new SemanticVersion(ver);

            semVer.Major.Should().Be(ver.Major);
            semVer.Minor.Should().Be(ver.Minor);
            semVer.Patch.Should().Be(ver.Build);
        }

        [Fact]
        public void ToString_FollowsSemVerOrgStandard()
        {
            var ver = new Version(3, 5, 8, 13);
            var semVer = new SemanticVersion(ver);

            semVer.ToString().Should().Be("3.5.8");
        }
    }
}
