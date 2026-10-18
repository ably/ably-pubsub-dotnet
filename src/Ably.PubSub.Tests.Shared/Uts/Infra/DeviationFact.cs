using System;
using Xunit;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// The runtime skip of <c>writing-derived-tests.md</c>'s "SDK deviation" end state: a test that keeps
    /// the spec-correct assertion against behaviour the SDK gets wrong, skipped unless
    /// <c>RUN_DEVIATIONS</c> is set. xUnit 2.4 has no runtime skip, so the attribute decides <c>Skip</c>
    /// in its constructor, which xUnit evaluates at discovery.
    ///
    /// <code>RUN_DEVIATIONS=1 dotnet test ... --filter "FullyQualifiedName~RTN15h3"</code>
    /// </summary>
    public sealed class DeviationFactAttribute : FactAttribute
    {
        public DeviationFactAttribute(string deviation)
        {
            if (!Deviations.Enabled)
            {
                Skip = $"SDK deviation {deviation}; see deviations.md. Run it with {Deviations.EnvironmentVariable}=1.";
            }
        }
    }

    /// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="DeviationFactAttribute"/>.</summary>
    public sealed class DeviationTheoryAttribute : TheoryAttribute
    {
        public DeviationTheoryAttribute(string deviation)
        {
            if (!Deviations.Enabled)
            {
                Skip = $"SDK deviation {deviation}; see deviations.md. Run it with {Deviations.EnvironmentVariable}=1.";
            }
        }
    }

    /// <summary>The deviation gate, so its variable is named once.</summary>
    public static class Deviations
    {
        public const string EnvironmentVariable = "RUN_DEVIATIONS";

        public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable));
    }
}
