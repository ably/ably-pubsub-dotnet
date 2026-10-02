using System;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/publish.md in ably/specification.
    ///
    /// Spec points: RSL1d, RSL1k5, RSL1m4
    ///
    /// Integration tier: a real sandbox app, no mock. No test here needs a cleanup block — a channel
    /// is not a registered resource and the history these tests write expires on its own — but every
    /// channel name and client id still carries a <c>UtsSandbox.RandomId()</c> suffix, because the
    /// app is shared.
    ///
    /// Two of the file's five tests are not translated, because each needs an API that is absent
    /// rather than differently spelled, and in C# that is a compile error rather than a failing
    /// assertion:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     rest/integration/RSL1n/publish-result-serials-0 — there is no <c>PublishResult</c> type at
    ///     all. All three <c>IHttpChannel.PublishAsync</c> overloads return a bare <c>Task</c>
    ///     (<c>Http/HttpChannel.cs:69,75,81</c>), so <c>result.serials</c> has nothing to read from.
    ///   </item>
    ///   <item>
    ///     rest/integration/RSL1l1/publish-params-force-nack-0 — no publish overload takes params.
    ///     The three are <c>PublishAsync(name, data, clientId)</c>, <c>PublishAsync(Message)</c> and
    ///     <c>PublishAsync(IEnumerable&lt;Message&gt;)</c>; all are fixed-arity and none writes a
    ///     query string onto the POST, so <c>_forceNack</c> cannot be transmitted. The unit tier
    ///     records the same gap for RSL1l.
    ///   </item>
    /// </list>
    ///
    /// msgpack is compiled out of this build, so only the JSON protocol variant is runnable.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PublishTests : UtsIntegrationTestBase
    {
        public PublishTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSL1d/publish-failure-error-0
        [Fact]
        public async Task RSL1d_PublishFailureError()
        {
            var sandbox = await Sandbox();

            // keys[2] is the spec's restricted key: its capability names channel0..channel6 only, so a
            // randomly named channel falls outside it and the publish is not permitted.
            var channelName = "forbidden-channel-" + UtsSandbox.RandomId();

            var restrictedClient = await SandboxRestClient(key: sandbox.Key(2).KeyStr);
            var restrictedChannel = restrictedClient.Channels.Get(channelName);

            Func<Task> act = () => restrictedChannel.PublishAsync("event", "data");

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40160);
            ((int)error.StatusCode.Value).Should().Be(401);
        }

        // UTS: rest/integration/RSL1k5/idempotent-client-ids-0
        [Fact]
        public async Task RSL1k5_IdempotentClientIds()
        {
            var client = await SandboxRestClient();
            var channelName = "idempotent-explicit-" + UtsSandbox.RandomId();
            var channel = client.Channels.Get(channelName);

            var fixedId = "client-supplied-id-" + UtsSandbox.RandomId();

            // A client-supplied id leaves IdempotentRestPublishing (RSL1k1, on by default) alone: the
            // library only generates ids when every message in the publish has none, so this id reaches
            // the server verbatim and is what the server dedupes on.
            for (var i = 1; i <= 3; i++)
            {
                await channel.PublishAsync(new Message
                {
                    Id = fixedId,
                    Name = "event",
                    Data = "data-" + i,
                });
            }

            // The spec's poll_until. History lags a publish, so this is a wall-clock poll rather than a
            // fixed wait, with the spec's own interval and timeout.
            Func<Task<PaginatedResult<Message>>> fetchHistory = async () =>
            {
                var page = await channel.HistoryAsync();
                return page.Items.Count > 0 ? page : null;
            };

            var history = await UtsSandbox.WallClockPollUntil(
                fetchHistory,
                "the idempotent publish to appear in channel history",
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500));

            history.Items.Should().HaveCount(1);
            history.Items[0].Id.Should().Be(fixedId);

            // The data is the first publish's; the spec reads the two later publishes of the same id as
            // no-ops rather than as overwrites.
            history.Items[0].Data.Should().Be("data-1");
        }

        // UTS: rest/integration/RSL1m4/clientid-mismatch-rejected-0
        [Fact]
        public async Task RSL1m4_ClientIdMismatchRejected()
        {
            var keyClient = await SandboxRestClient();

            // NOTE: the spec hardcodes both client ids. The sandbox app is shared, so each takes a
            // RandomId() suffix per the integration tier's rule; the test turns on the two differing,
            // which a common suffix preserves.
            var suffix = UtsSandbox.RandomId();
            var authenticatedClientId = "authenticated-client-id-" + suffix;
            var mismatchedClientId = "different-client-id-" + suffix;

            var tokenDetails = await keyClient.Auth.RequestTokenAsync(
                new TokenParams { ClientId = authenticatedClientId });

            var tokenClient = await SandboxRestClient(configure: options =>
            {
                // The spec's client authenticates with the token literal and nothing else. The harness
                // seeds Key from the sandbox, so clear it: left set, this client would have a way to
                // re-authorize that the spec's does not.
                options.Key = null;
                options.Token = tokenDetails.Token;
            });

            var channelName = "clientid-mismatch-" + UtsSandbox.RandomId();
            var channel = tokenClient.Channels.Get(channelName);

            Func<Task> act = () => channel.PublishAsync(new Message
            {
                Name = "event",
                Data = "data",
                ClientId = mismatchedClientId,
            });

            // NOTE: the spec frames this as a server rejection, and here it is one. The library's own
            // check, AblyAuth.ValidateClientIds, reads AblyAuth.ClientId, which is null for a client
            // given a bare token literal — TokenDetails(string) sets only Token, so there is no parsed
            // clientId to compare against — so the check passes and the message reaches the server.
            // Either side would answer 40012/400, so the assertion does not depend on which rejected.
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40012);
            ((int)error.StatusCode.Value).Should().Be(400);
        }
    }
}
