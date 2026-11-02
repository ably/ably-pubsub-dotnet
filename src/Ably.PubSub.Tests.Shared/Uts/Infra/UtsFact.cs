using Xunit;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>The per-test hang guard.</summary>
    public static class UtsTestTimeouts
    {
        /// <summary>
        /// The default bound on one async UTS unit test: well above any spec wait (10 s state waits, 10 s bounded
        /// SDK operations) and far below a stalled CI job.
        /// </summary>
        public const int DefaultTestTimeoutMs = 30000;
    }

    /// <summary>
    /// A <see cref="FactAttribute"/> with the default UTS hang guard (<see
    /// cref="UtsTestTimeouts.DefaultTestTimeoutMs"/>).
    /// </summary>
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
