using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Shared
{
    public class CoreDoorAccessTests
    {
        [Theory]
        [InlineData("Ably.PubSub.Device")]
        [InlineData("Ably.PubSub.Server")]
        public void Core_GrantsInternalsAccessToEachDoor(string doorAssembly)
        {
            // Both door packages construct the clients through the core's internal constructors,
            // so every core build must grant both doors InternalsVisibleTo. This shared test runs
            // against each core head: the netstandard/net6.0+ core (Tests.DotNET) and the net46
            // core (Tests.NETFramework). The net46 core is what a .NET Framework app gets alongside
            // the netstandard2.0 Device door, and it once granted only Server, so
            // PubSubDevice.CreateClient threw MethodAccessException there. The keyed (Package)
            // grants are covered end to end by the Test.PackageConsumption Cake target.
            var grantedAssemblies = typeof(PubSubRealtimeClient).Assembly
                .GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim());

            grantedAssemblies.Should().Contain(doorAssembly, "the door constructs clients through the core's internal constructors");
        }
    }
}
