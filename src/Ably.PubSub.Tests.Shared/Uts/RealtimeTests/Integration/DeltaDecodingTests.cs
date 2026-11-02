// Delta Decoding Integration Tests (PC3, PC3a, RTL18, RTL18b, RTL18c, RTL19b, RTL20)
// Derived from uts/realtime/integration/delta_decoding_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture); plugins: { vcdiff: decoder } -> ClientOptions.DeltaDecoder (hook H2, internal; the SDK has no plugin option and a built-in decoder), wrapping the real vcdiff decoder with an invocation counter

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class DeltaDecodingTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 90000;

        public DeltaDecodingTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // All tests that publish multiple messages use the same dataset:
        // The data is intentionally similar between messages so that the server generates
        // small vcdiff deltas rather than sending full messages.
        private static List<JObject> TestData()
        {
            return new List<JObject>
            {
                new JObject { ["foo"] = "bar", ["count"] = 1, ["status"] = "active" },
                new JObject { ["foo"] = "bar", ["count"] = 2, ["status"] = "active" },
                new JObject { ["foo"] = "bar", ["count"] = 2, ["status"] = "inactive" },
                new JObject { ["foo"] = "bar", ["count"] = 3, ["status"] = "inactive" },
                new JObject { ["foo"] = "bar", ["count"] = 3, ["status"] = "active" },
            };
        }

        // Use a wrapping decoder that counts invocations: it calls the real vcdiff decoder after onDecode.
        private static Func<byte[], byte[], byte[]> CountingDecoder(Action<byte[], byte[]> onDecode)
        {
            return (@base, delta) =>
            {
                onDecode(delta, @base);
                return global::IO.Ably.DeltaCodec.DeltaDecoder.ApplyDelta(@base, delta).AsByteArray();
            };
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/PC3/delta-decode-end-to-end-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "PC3")]
        public async Task PC3_DeltaDecodeEndToEnd()
        {
            // Setup
            var testData = TestData();
            var channelName = "delta-PC3-" + RandomId();

            // Use a wrapping decoder that counts invocations
            var decodeCount = 0;
            var countingDecoder = CountingDecoder((delta, @base) =>
            {
                Interlocked.Increment(ref decodeCount);
            });

            var client = Realtime(new ClientOptions { DeltaDecoder = countingDecoder });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, new ChannelOptions(channelParams: new ChannelParams { ["delta"] = "vcdiff" }));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            var receivedMessages = new CaptureList<Message>();

            // Fail the test if the channel reattaches (decode failure)
            // NOTE: FAIL inside an SDK callback would not fail the test, so the reattach is recorded and asserted empty after the wait
            var reattaches = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Attaching, change =>
            {
                reattaches.Add(change);
            });

            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Publish all messages sequentially
            for (var i = 0; i <= testData.Count - 1; i++)
            {
                AssertSucceeded(await channel.PublishAsync(i.ToString(), testData[i]), "channel.publish()");
            }

            // Wait for all messages to be received
            await UtsWait.PollUntil(
                () => receivedMessages.Count == testData.Count,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "all messages received");

            // Assertions
            reattaches.Count.Should().Be(0, "the channel must not reattach due to a decode failure");
            for (var i = 0; i <= testData.Count - 1; i++)
            {
                receivedMessages[i].Name.Should().Be(i.ToString());
                JToken.DeepEquals((JToken)receivedMessages[i].Data, testData[i]).Should().BeTrue("message {0} data should equal the published data", i);
            }

            // First message is sent as full payload, rest as deltas
            Volatile.Read(ref decodeCount).Should().Be(testData.Count - 1);

            // Cleanup
            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL19b/dissimilar-payloads-no-delta-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL19b")]
        public async Task RTL19b_DissimilarPayloadsNoDelta()
        {
            // Setup
            var channelName = "delta-dissimilar-" + RandomId();
            var messageCount = 5;

            var decodeCount = 0;
            var countingDecoder = CountingDecoder((delta, @base) =>
            {
                Interlocked.Increment(ref decodeCount);
            });

            var client = Realtime(new ClientOptions { DeltaDecoder = countingDecoder });

            // Generate random binary payloads — 1KB each, completely dissimilar
            var payloads = new List<byte[]>();
            for (var i = 0; i <= messageCount - 1; i++)
            {
                var payload = new byte[1024];
                new Random().NextBytes(payload);
                payloads.Add(payload);
            }

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, new ChannelOptions(channelParams: new ChannelParams { ["delta"] = "vcdiff" }));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            var receivedMessages = new CaptureList<Message>();

            // Fail the test if the channel reattaches (decode failure)
            // NOTE: FAIL inside an SDK callback would not fail the test, so the reattach is recorded and asserted empty after the wait
            var reattaches = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Attaching, change =>
            {
                reattaches.Add(change);
            });

            channel.Subscribe(msg => receivedMessages.Add(msg));

            for (var i = 0; i <= messageCount - 1; i++)
            {
                AssertSucceeded(await channel.PublishAsync(i.ToString(), payloads[i]), "channel.publish()");
            }

            await UtsWait.PollUntil(
                () => receivedMessages.Count == messageCount,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "all messages received");

            // Assertions
            reattaches.Count.Should().Be(0, "the channel must not reattach due to a decode failure");

            // All messages received with correct data
            for (var i = 0; i <= messageCount - 1; i++)
            {
                receivedMessages[i].Name.Should().Be(i.ToString());
                ((byte[])receivedMessages[i].Data).Should().Equal(payloads[i]);
            }

            // The server is expected to send full messages (no deltas) for dissimilar
            // random binary payloads. If so, the decoder should not have been called.
            // However, the server may still choose to generate deltas, so we only log
            // the decode count rather than asserting it is zero.
            Output.WriteLine("Decoder was called " + Volatile.Read(ref decodeCount) + " times for " + messageCount + " dissimilar messages");

            // Cleanup
            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/PC3/no-deltas-without-param-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "PC3")]
        public async Task PC3_NoDeltasWithoutParam()
        {
            // Setup
            var testData = TestData();
            var channelName = "delta-no-param-" + RandomId();

            var decodeCount = 0;
            var countingDecoder = CountingDecoder((delta, @base) =>
            {
                Interlocked.Increment(ref decodeCount);
            });

            var client = Realtime(new ClientOptions { DeltaDecoder = countingDecoder });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach WITHOUT delta params
            var channel = client.Channels.Get(channelName);

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            var receivedMessages = new CaptureList<Message>();
            channel.Subscribe(msg => receivedMessages.Add(msg));

            for (var i = 0; i <= testData.Count - 1; i++)
            {
                AssertSucceeded(await channel.PublishAsync(i.ToString(), testData[i]), "channel.publish()");
            }

            await UtsWait.PollUntil(
                () => receivedMessages.Count == testData.Count,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "all messages received");

            // Assertions
            for (var i = 0; i <= testData.Count - 1; i++)
            {
                receivedMessages[i].Name.Should().Be(i.ToString());
                JToken.DeepEquals((JToken)receivedMessages[i].Data, testData[i]).Should().BeTrue("message {0} data should equal the published data", i);
            }

            // No deltas — decoder was never called
            Volatile.Read(ref decodeCount).Should().Be(0);

            // Cleanup
            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL18/recovery-message-id-mismatch-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL18")]
        public async Task RTL18_RecoveryMessageIdMismatch()
        {
            // Setup
            var testData = TestData();
            var channelName = "delta-recovery-mismatch-" + RandomId();

            var decodeCount = 0;
            var countingDecoder = CountingDecoder((delta, @base) =>
            {
                Interlocked.Increment(ref decodeCount);
            });

            var client = Realtime(new ClientOptions { DeltaDecoder = countingDecoder });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, new ChannelOptions(channelParams: new ChannelParams { ["delta"] = "vcdiff" }));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            var receivedMessages = new CaptureList<Message>();
            var attachingReasons = new CaptureList<ErrorInfo>();

            channel.On(ChannelEvent.Attaching, change =>
            {
                attachingReasons.Add(change.Error);
            });

            channel.Subscribe(msg => receivedMessages.Add(msg));

            // Publish first batch of messages and wait for them to arrive.
            // Publishing in two batches ensures the server has sent and the client has
            // processed the first batch before we clear the stored ID. If all messages
            // were published at once, they could all arrive in a single ProtocolMessage
            // before clearLastPayloadMessageId takes effect.
            for (var i = 0; i <= 2; i++)
            {
                AssertSucceeded(await channel.PublishAsync(i.ToString(), testData[i]), "channel.publish()");
            }

            await UtsWait.PollUntil(
                () => receivedMessages.Count >= 3,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "first three messages received");

            // Simulate a message gap by clearing the stored last message ID.
            // The next delta will fail the RTL20 check.
            // (Implementation-specific: access internal _lastPayload.messageId or equivalent)
            // NOTE: the spec's channel._lastPayload.messageId is this SDK's RealtimeChannel.LastSuccessfulMessageIds.LastMessageId (internal setter), which the RTL20 check reads
            ((RealtimeChannel)channel).LastSuccessfulMessageIds.LastMessageId = null;

            // Publish remaining messages — the server should send these as deltas,
            // which will fail the RTL20 check and trigger recovery
            for (var i = 3; i <= testData.Count - 1; i++)
            {
                AssertSucceeded(await channel.PublishAsync(i.ToString(), testData[i]), "channel.publish()");
            }

            // Wait for all messages to be received — recovery will reattach and
            // the server will resend from the channelSerial
            await UtsWait.PollUntil(
                () =>
                {
                    var names = receivedMessages.Snapshot().Select(m => m.Name).Distinct().ToList();
                    return Enumerable.Range(0, testData.Count).All(i => names.Contains(i.ToString()));
                },
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                description: "unique message names cover all published messages");

            // Assertions
            // All messages were eventually received with correct data (may have duplicates
            // from the server resending after recovery)
            for (var i = 0; i <= testData.Count - 1; i++)
            {
                var msg = receivedMessages.Snapshot().FirstOrDefault(m => m.Name == i.ToString());
                msg.Should().NotBeNull();
                JToken.DeepEquals((JToken)msg.Data, testData[i]).Should().BeTrue("message {0} data should equal the published data", i);
            }

            // RTL18c: Recovery was triggered with error code 40018
            attachingReasons.Count.Should().BeGreaterThanOrEqualTo(1);
            attachingReasons[0].Should().NotBeNull();
            attachingReasons[0].Code.Should().Be(40018);

            // Cleanup
            await CloseClient(client);
        }

        // NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
        // UTS: realtime/integration/RTL18/recovery-decode-failure-1
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL18")]
        public async Task RTL18_RecoveryDecodeFailure()
        {
            // Setup
            var testData = TestData();
            var channelName = "delta-recovery-decode-" + RandomId();

            // Decoder that always fails
            var failingDecoder = new FailingMockVCDiffDecoder();

            var client = Realtime(new ClientOptions { DeltaDecoder = failingDecoder.AsDeltaDecoder() });

            // Test Steps
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, new ChannelOptions(channelParams: new ChannelParams { ["delta"] = "vcdiff" }));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");

            var receivedMessages = new CaptureList<Message>();
            var attachingReasons = new CaptureList<ErrorInfo>();

            channel.On(ChannelEvent.Attaching, change =>
            {
                attachingReasons.Add(change.Error);
            });

            channel.Subscribe(msg => receivedMessages.Add(msg));

            for (var i = 0; i <= testData.Count - 1; i++)
            {
                AssertSucceeded(await channel.PublishAsync(i.ToString(), testData[i]), "channel.publish()");
            }

            // Wait for all messages — first arrives as non-delta, second triggers decode
            // failure and recovery, then remaining messages arrive after reattach
            await UtsWait.PollUntil(
                () => receivedMessages.Count >= testData.Count,
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(500),
                description: "at least as many messages received as published");

            // Assertions
            // All messages eventually received with correct data
            for (var i = 0; i <= testData.Count - 1; i++)
            {
                var msg = receivedMessages.Snapshot().FirstOrDefault(m => m.Name == i.ToString());
                msg.Should().NotBeNull();
                JToken.DeepEquals((JToken)msg.Data, testData[i]).Should().BeTrue("message {0} data should equal the published data", i);
            }

            // RTL18c: At least one recovery was triggered
            attachingReasons.Count.Should().BeGreaterThanOrEqualTo(1);
            attachingReasons[0].Should().NotBeNull();
            attachingReasons[0].Code.Should().Be(40018);

            // Cleanup
            await CloseClient(client);
        }

        // UTS-NA: realtime/integration/PC3/no-plugin-causes-failed-2 — not applicable: no plugin mechanism in this SDK
        // PC3 needs a client without a vcdiff decoder; the decoder is built in and cannot be configured away (default)
    }
}
