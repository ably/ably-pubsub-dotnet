using Xunit;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// The per-test hang guard. A UTS test awaits SDK operations, and in the unit tier any SDK fallback timer
    /// runs on the fake clock, so an operation the SDK never completes would otherwise wait forever and stall
    /// the whole run. xUnit fails an async test that runs past <see cref="FactAttribute.Timeout"/>; these
    /// attributes give every async UTS test that bound by default. A test may still set its own
    /// <c>Timeout</c>. xUnit 2.4 honours <c>Timeout</c> on async tests only, so a synchronous test keeps
    /// <see cref="FactAttribute"/>.
    /// </summary>
    public static class UtsTestTimeouts
    {
        /// <summary>
        /// The default bound on one async UTS unit test: well above any spec wait (10 s state waits, 10 s
        /// bounded SDK operations) and far below a stalled CI job.
        /// </summary>
        public const int DefaultTestTimeoutMs = 30000;
    }

    /// <summary>A <see cref="FactAttribute"/> with the default UTS hang guard (<see cref="UtsTestTimeouts.DefaultTestTimeoutMs"/>).</summary>
    public sealed class UtsFactAttribute : FactAttribute
    {
        public UtsFactAttribute()
        {
            Timeout = UtsTestTimeouts.DefaultTestTimeoutMs;
        }
    }

    /// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="UtsFactAttribute"/>.</summary>
    public sealed class UtsTheoryAttribute : TheoryAttribute
    {
        public UtsTheoryAttribute()
        {
            Timeout = UtsTestTimeouts.DefaultTestTimeoutMs;
        }
    }
}
