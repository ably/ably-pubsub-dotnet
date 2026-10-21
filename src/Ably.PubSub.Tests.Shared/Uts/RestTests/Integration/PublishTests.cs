// REST Channel Publish Integration Tests (RSL1d, RSL1n, RSL1k5, RSL1l1, RSL1m4)
// Derived from uts/rest/integration/publish.md
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
    public class PublishTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public PublishTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The specs' full_access_key (app_config.keys[0].key_str).
        private string FullAccessKey => App.Keys[0].KeyStr;

        // The specs' restricted_key (app_config.keys[2].key_str): per-channel capabilities.
        private string RestrictedKey => App.Keys[2].KeyStr;

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL1d/publish-failure-error-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1d")]
        public async Task RSL1d_PublishFailureError()
        {
            // Setup
            var channelName = "forbidden-channel-" + RandomId(); // Not in restricted key's capability

            var restrictedClient = Rest(new ClientOptions { Key = RestrictedKey }); // Key without publish capability for this channel
            var restrictedChannel = restrictedClient.Channels.Get(channelName);

            // Test Steps
            Func<Task> publish = () => restrictedChannel.PublishAsync("event", "data");
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40160); // Not permitted
            ((int?)error.StatusCode).Should().Be(401);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL1n/publish-result-serials-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1n")]
        public async Task RSL1n_PublishResultSerials()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "test-serials-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Single message
            var result1 = await channel.PublishAsync("event1", "data1");

            // ASSERT result1.serials IS List: guaranteed at compile time (Serials is an IReadOnlyList<string>)
            result1.Serials.Should().NotBeNull();
            result1.Serials.Should().HaveCount(1);

            // ASSERT result1.serials[0] IS String: guaranteed at compile time (string elements)
            result1.Serials[0].Should().NotBeNull();
            result1.Serials[0].Length.Should().BeGreaterThan(0);

            // Multiple messages
            var result2 = await channel.PublishAsync(new[]
            {
                new Message("event2", "data2"),
                new Message("event3", "data3"),
                new Message("event4", "data4"),
            });

            result2.Serials.Should().HaveCount(3);

            // ALL serial IN result2.serials: serial IS String (compile-time) AND serial.length > 0
            result2.Serials.Should().NotContain(serial => !(serial != null && serial.Length > 0));
            result2.Serials.Should().OnlyHaveUniqueItems();
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL1k5/idempotent-client-ids-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1k5")]
        public async Task RSL1k5_IdempotentClientIds()
        {
            // Setup
            var client = Rest(new ClientOptions { Key = FullAccessKey });
            var channelName = "idempotent-explicit-" + RandomId();
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var fixedId = "client-supplied-id-" + RandomId();

            // Publish same message ID multiple times
            for (var i = 1; i <= 3; i++)
            {
                await channel.PublishAsync(new Message("event", "data-" + i) { Id = fixedId });
            }

            // Poll history until message appears (avoid fixed wait)
            var history = await UtsWait.PollUntil(
                async () =>
                {
                    var result = await channel.HistoryAsync();
                    return result.Items.Count > 0 ? result : null;
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "history has a message");

            // Verify only one message in history
            history.Items.Should().HaveCount(1);
            history.Items[0].Id.Should().Be(fixedId);

            // The data should be from the first publish (subsequent ones are no-ops)
            history.Items[0].Data.Should().Be("data-1");
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL1l1/publish-params-force-nack-0
        [Fact(Skip = "Missing SDK public API: RestChannel#publish(params) (RSL1l); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1l1")]
        public Task RSL1l1_PublishParamsForceNack()
        {
            // NOTE: IHttpChannel.PublishAsync has no overload taking publish params (RSL1l), so the _forceNack
            // param can't be sent and the whole test is a stub. Spec lines:
            // client = Rest(options: ClientOptions(key: full_access_key, endpoint: "nonprod:sandbox"))
            // channel_name = "force-nack-test-" + random_id()
            // channel = client.channels.get(channel_name)
            // AWAIT channel.publish(
            // message: Message(name: "event", data: "data"),
            // params: { "_forceNack": "true" }
            // ) FAILS WITH error
            // ASSERT error.code == 40099 # Specific code for forced nack
            return Task.CompletedTask;
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: rest/integration/RSL1m4/clientid-mismatch-rejected-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RSL1m4")]
        public async Task RSL1m4_ClientidMismatchRejected()
        {
            // Setup
            // Create a token with a specific clientId
            var keyClient = Rest(new ClientOptions { Key = FullAccessKey });

            var tokenDetails = await keyClient.Auth.RequestTokenAsync(new TokenParams { ClientId = "authenticated-client-id" });

            // Client using token with clientId
            var tokenClient = Rest(new ClientOptions { Token = tokenDetails.Token });

            var channelName = "clientid-mismatch-" + RandomId();
            var channel = tokenClient.Channels.Get(channelName);

            // Test Steps
            Func<Task> publish = () => channel.PublishAsync(new Message("event", "data", "different-client-id")); // Doesn't match authenticated clientId
            var error = (await publish.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40012); // Incompatible clientId
            ((int?)error.StatusCode).Should().Be(400);
        }
    }
}
