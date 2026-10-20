// Batch Presence Integration Tests (RSC24, BGR2, BGF2)
// Derived from uts/rest/integration/batch_presence.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class BatchPresenceTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public BatchPresenceTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSC24/batch-presence-multiple-channels-0
        [Fact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC24")]
        public Task RSC24_BatchPresenceMultipleChannels()
        {
            // NOTE: the SDK has no RestClient#batchPresence and no BatchResult / BatchPresenceSuccessResult types
            // (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // channel_a_name = "batch-presence-a-" + random_id()
            // channel_b_name = "batch-presence-b-" + random_id()
            // realtime = Realtime(options: ClientOptions(key: full_access_key, endpoint: "nonprod:sandbox", useBinaryProtocol: PROTOCOL == "msgpack"))
            // # Connect and enter members on two channels
            // realtime.connect()
            // AWAIT_STATE realtime.connection.state == ConnectionState.connected
            // ch_a = realtime.channels.get(channel_a_name)
            // AWAIT ch_a.attach()
            // AWAIT ch_a.presence.enterClient("user-1", data: "data-a1")
            // AWAIT ch_a.presence.enterClient("user-2", data: "data-a2")
            // ch_b = realtime.channels.get(channel_b_name)
            // AWAIT ch_b.attach()
            // AWAIT ch_b.presence.enterClient("user-3", data: "data-b1")
            // # Query via REST batchPresence (keep realtime open so presence persists)
            // rest = Rest(options: ClientOptions(key: full_access_key, endpoint: "nonprod:sandbox", useBinaryProtocol: PROTOCOL == "msgpack"))
            // result = AWAIT rest.batchPresence([channel_a_name, channel_b_name])
            // ASSERT result.successCount == 2
            // ASSERT result.failureCount == 0
            // ASSERT result.results.length == 2
            // # Find results by channel name
            // result_a = result.results.find(r => r.channel == channel_a_name)
            // result_b = result.results.find(r => r.channel == channel_b_name)
            // ASSERT result_a IS BatchPresenceSuccessResult
            // ASSERT result_a.presence.length == 2
            // client_ids_a = [m.clientId FOR m IN result_a.presence]
            // ASSERT "user-1" IN client_ids_a
            // ASSERT "user-2" IN client_ids_a
            // # Verify data round-trips correctly
            // member_1 = result_a.presence.find(m => m.clientId == "user-1")
            // ASSERT member_1.data == "data-a1"
            // ASSERT result_b IS BatchPresenceSuccessResult
            // ASSERT result_b.presence.length == 1
            // ASSERT result_b.presence[0].clientId == "user-3"
            // ASSERT result_b.presence[0].data == "data-b1"
            // AWAIT realtime.close()
            return Task.CompletedTask;
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSC24/restricted-key-channel-failure-1
        [Fact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC24")]
        public Task RSC24_RestrictedKeyChannelFailure()
        {
            // NOTE: the SDK has no RestClient#batchPresence and no BatchPresenceFailureResult type
            // (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // # Use the fixed channel name matching keys[2] capability from ably-common
            // allowed_channel = "channel6"
            // denied_channel = "denied-batch-" + random_id()
            // # Enter members on both channels using the full-access key
            // realtime = Realtime(options: ClientOptions(key: full_access_key, endpoint: "nonprod:sandbox", useBinaryProtocol: PROTOCOL == "msgpack"))
            // realtime.connect()
            // AWAIT_STATE realtime.connection.state == ConnectionState.connected
            // ch_allowed = realtime.channels.get(allowed_channel)
            // AWAIT ch_allowed.attach()
            // AWAIT ch_allowed.presence.enterClient("member-1", data: "hello")
            // ch_denied = realtime.channels.get(denied_channel)
            // AWAIT ch_denied.attach()
            // AWAIT ch_denied.presence.enterClient("member-2", data: "world")
            // AWAIT realtime.close()
            // # Query with restricted key (only has access to "batch-allowed" channel)
            // restricted_rest = Rest(options: ClientOptions(key: restricted_key, endpoint: "nonprod:sandbox", useBinaryProtocol: PROTOCOL == "msgpack"))
            // result = AWAIT restricted_rest.batchPresence([allowed_channel, denied_channel])
            // ASSERT result.successCount == 1
            // ASSERT result.failureCount == 1
            // ASSERT result.results.length == 2
            // # Find results by channel name
            // success = result.results.find(r => r.channel == allowed_channel)
            // failure = result.results.find(r => r.channel == denied_channel)
            // # Allowed channel succeeds with presence data
            // ASSERT success IS BatchPresenceSuccessResult
            // ASSERT success.presence.length == 1
            // ASSERT success.presence[0].clientId == "member-1"
            // # Denied channel fails with capability error
            // ASSERT failure IS BatchPresenceFailureResult
            // ASSERT failure.error IS ErrorInfo
            // ASSERT failure.error.code == 40160
            // ASSERT failure.error.statusCode == 401
            return Task.CompletedTask;
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSC24/empty-channel-presence-2
        [Fact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSC24")]
        public Task RSC24_EmptyChannelPresence()
        {
            // NOTE: the SDK has no RestClient#batchPresence (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // empty_channel = "batch-empty-" + random_id()
            // populated_channel = "batch-populated-" + random_id()
            // # Enter a member on only the populated channel
            // realtime = Realtime(options: ClientOptions(key: full_access_key, endpoint: "nonprod:sandbox", useBinaryProtocol: PROTOCOL == "msgpack"))
            // realtime.connect()
            // AWAIT_STATE realtime.connection.state == ConnectionState.connected
            // ch = realtime.channels.get(populated_channel)
            // AWAIT ch.attach()
            // AWAIT ch.presence.enterClient("someone", data: "here")
            // # NOTE: Keep realtime open during the REST query so the presence member
            // # persists on the server. Closing realtime before the query would cause
            // # the member to leave.
            // rest = Rest(options: ClientOptions(key: full_access_key, endpoint: "nonprod:sandbox", useBinaryProtocol: PROTOCOL == "msgpack"))
            // result = AWAIT rest.batchPresence([empty_channel, populated_channel])
            // ASSERT result.successCount == 2
            // ASSERT result.failureCount == 0
            // ASSERT result.results.length == 2
            // empty_result = result.results.find(r => r.channel == empty_channel)
            // populated_result = result.results.find(r => r.channel == populated_channel)
            // # Empty channel succeeds with no members
            // ASSERT empty_result IS BatchPresenceSuccessResult
            // ASSERT empty_result.presence.length == 0
            // # Populated channel succeeds with the member
            // ASSERT populated_result IS BatchPresenceSuccessResult
            // ASSERT populated_result.presence.length == 1
            // ASSERT populated_result.presence[0].clientId == "someone"
            // AWAIT realtime.close()
            return Task.CompletedTask;
        }
    }
}
