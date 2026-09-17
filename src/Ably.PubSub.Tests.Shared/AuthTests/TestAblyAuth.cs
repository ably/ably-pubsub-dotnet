using System;
using System.Threading.Tasks;

namespace IO.Ably.Tests.AuthTests
{
    internal class TestAblyAuth : AblyAuth
    {
        public TestAblyAuth(ClientOptions options, AblyRest rest, Func<Task<DateTimeOffset>> serverTimeFunc = null)
            : base(options, rest)
        {
            if (serverTimeFunc != null)
            {
                ServerTime = serverTimeFunc;
            }
        }

        public bool RequestTokenCalled { get; private set; }

        public TokenParams LastRequestTokenParams { get; private set; }

        public AuthOptions LastRequestAuthOptions { get; private set; }

        public override Task<TokenDetails> RequestTokenAsync(TokenParams tokenParams = null, AuthOptions options = null)
        {
            RequestTokenCalled = true;
            LastRequestTokenParams = tokenParams;
            LastRequestAuthOptions = options;

            return base.RequestTokenAsync(tokenParams, options);
        }

        // Fetch and returns current Ably server time.
        public async Task<DateTimeOffset> GetServerTime()
        {
            return await ServerTime();
        }

        // Returns current server time w.r.t local clock. (By adding/subtracting clock offset).
        public DateTimeOffset? GetServerNow()
        {
            return ServerNow;
        }
    }
}
