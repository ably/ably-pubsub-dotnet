using System;
using Xunit;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The env-gated skip of <c>writing-derived-tests.md</c>, for a test that carries the spec-correct
    /// assertion against behaviour the SDK gets wrong.
    ///
    /// xUnit resolves <c>Skip</c> at discovery time and offers no runtime conditional skip, so the gate
    /// has to be decided in the attribute's constructor. Normal runs stay green; a single deviation is
    /// reproducible on demand, which is what makes an issue filed from it actionable:
    ///
    /// <code>RUN_DEVIATIONS=1 dotnet test --filter "FullyQualifiedName~RSA7b"</code>
    ///
    /// Use this only for non-compliance you expect to be fixed. Where the divergence is stable or
    /// intentional, prefer an adapted assertion — a test that runs and asserts the real behaviour
    /// catches regressions, whereas a spec-correct assertion skipped indefinitely verifies nothing.
    /// </summary>
    public sealed class DeviationFactAttribute : FactAttribute
    {
        public DeviationFactAttribute()
        {
            if (!Deviations.Enabled)
            {
                Skip = Deviations.SkipReason;
            }
        }
    }

    /// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="DeviationFactAttribute"/>.</summary>
    public sealed class DeviationTheoryAttribute : TheoryAttribute
    {
        public DeviationTheoryAttribute()
        {
            if (!Deviations.Enabled)
            {
                Skip = Deviations.SkipReason;
            }
        }
    }

    /// <summary>
    /// The gate itself, so the env var name is written once. Also used by a test that needs to branch
    /// mid-body rather than skip wholesale.
    /// </summary>
    public static class Deviations
    {
        public const string EnvironmentVariable = "RUN_DEVIATIONS";

        public const string SkipReason =
            "SDK deviation — see Uts/deviations.md. Enable with RUN_DEVIATIONS=1.";

        public static bool Enabled =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable));
    }
}
