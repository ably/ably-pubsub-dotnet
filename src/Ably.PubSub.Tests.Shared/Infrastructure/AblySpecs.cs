using System;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public abstract class AblySpecs
    {
        protected const string ValidKey = "1iZPfA.BjcI_g:wpNhw5RCw6rDjisl";

        protected ITestOutputHelper Output { get; }

        protected DateTimeOffset Now => NowFunc();

        protected Func<DateTimeOffset> NowFunc { get; set; }

        internal ILogger Logger { get; }

        protected void NowAdd(TimeSpan ts)
        {
            DateTimeOffset n = Now.Add(ts);
            SetNowFunc(() => n);
        }

        protected void SetNowFunc(Func<DateTimeOffset> nowFunc) => NowFunc = nowFunc;

        protected AblySpecs(ITestOutputHelper output)
        {
            Logger = DefaultLogger.LoggerInstance;
            NowFunc = TestHelpers.Now;
            Output = output;
        }
    }
}
