using System;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Shared
{
    public class AgentTests
    {
        [Fact]
        public void PlatformRuntime_ShouldDetectCorrectRuntime()
        {
            // Arrange
            var frameworkDescription = RuntimeInformation.FrameworkDescription;

            // Act
            var platformId = IoC.PlatformId;

            // Assert - verify that the platform ID matches the runtime
            if (frameworkDescription.StartsWith(".NET 6.", StringComparison.OrdinalIgnoreCase))
            {
                platformId.Should().Be(Agent.PlatformRuntime.Net6, $"Expected Net6 for framework: {frameworkDescription}");
            }
            else if (frameworkDescription.StartsWith(".NET 7.", StringComparison.OrdinalIgnoreCase))
            {
                platformId.Should().Be(Agent.PlatformRuntime.Net7, $"Expected Net7 for framework: {frameworkDescription}");
            }
            else if (frameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase))
            {
                platformId.Should().Be(Agent.PlatformRuntime.Framework, $"Expected Framework for framework: {frameworkDescription}");
            }
            else
            {
                // For .NET Core 3.x, .NET 5.0, or other runtimes
                platformId.Should().Be(Agent.PlatformRuntime.Netstandard20, $"Expected Netstandard20 for framework: {frameworkDescription}");
            }
        }

        [Fact]
        public void DotnetRuntimeIdentifier_ShouldIncludeCorrectRuntimeName()
        {
            // Arrange & Act
            var runtimeIdentifier = Agent.DotnetRuntimeIdentifier;

            // Assert
            runtimeIdentifier.Should().NotBeNullOrEmpty();
            var platformId = IoC.PlatformId;

            switch (platformId)
            {
                case Agent.PlatformRuntime.Net6:
                    runtimeIdentifier.Should().StartWith("dotnet6/");
                    break;
                case Agent.PlatformRuntime.Net7:
                    runtimeIdentifier.Should().StartWith("dotnet7/");
                    break;
                case Agent.PlatformRuntime.Framework:
                    runtimeIdentifier.Should().StartWith("dotnet-framework/");
                    break;
                case Agent.PlatformRuntime.Netstandard20:
                    runtimeIdentifier.Should().NotBeNullOrEmpty();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
