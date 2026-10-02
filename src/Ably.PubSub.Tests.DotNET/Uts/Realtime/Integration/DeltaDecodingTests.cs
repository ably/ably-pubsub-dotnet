using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration
{
    /// <summary>
    /// Derived from uts/realtime/integration/delta_decoding_test.md in ably/specification.
    ///
    /// Spec points: PC3, RTL18, RTL18c, RTL19b, RTL20
    ///
    /// <b>The decoder is built in, not a plugin.</b> The spec models vcdiff as a plugin registered
    /// through <c>ClientOptions.plugins</c>, and every test in the file wraps that plugin to count or
    /// to fail its invocations. This SDK has no such seam: <c>MessageEncoders/VCDiffEncoder.cs</c> is
    /// always in the default encoder chain and calls the codec statically, so delta decoding is
    /// testable end to end but cannot be replaced, instrumented or removed.
    ///
    /// Two consequences follow.
    ///
    /// Where the spec asserts on <c>decode_count</c>, this file asserts on <c>message.Extras.Delta</c>
    /// — the delta extras the server stamps on each deltified message, which decoding does not strip.
    /// A message carrying delta extras is exactly a message the decoder was invoked for, so it is the
    /// same number by a different witness.
    ///
    /// <c>realtime/integration/RTL18/recovery-decode-failure-1</c> and
    /// <c>realtime/integration/PC3/no-plugin-causes-failed-2</c> are <b>not translated</b>. The first
    /// needs a decoder that fails on demand, the second needs it to be absent, and neither is
    /// expressible without the plugin seam. (The second also expects error code 40019, which this SDK
    /// never raises: a vcdiff decode failure is 40018 and starts the RTL18 reattach rather than
    /// failing the channel.)
    ///
    /// msgpack is compiled out of this build, so only the json half of the spec's Protocol Variants
    /// section is runnable. Every channel name carries a <see cref="UtsSandbox.RandomId"/> suffix
    /// because the sandbox app is shared.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class DeltaDecodingTests : UtsRealtimeIntegrationTestBase
    {
        /// <summary>
        /// The spec's <c>test_data</c>. Held as JSON text so the expected value and the published
        /// value are parsed from the same source and compared as JSON rather than as CLR objects.
        /// The payloads are deliberately similar so the server emits small vcdiff deltas.
        /// </summary>
        private static readonly string[] TestDataJson =
        {
            "{\"foo\":\"bar\",\"count\":1,\"status\":\"active\"}",
            "{\"foo\":\"bar\",\"count\":2,\"status\":\"active\"}",
            "{\"foo\":\"bar\",\"count\":2,\"status\":\"inactive\"}",
            "{\"foo\":\"bar\",\"count\":3,\"status\":\"inactive\"}",
            "{\"foo\":\"bar\",\"count\":3,\"status\":\"active\"}",
        };

        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);

        private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);

        public DeltaDecodingTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/PC3/delta-decode-end-to-end-0
        [Fact]
        public async Task PC3_DeltaDecodeEndToEnd()
        {
            var channelName = "delta-PC3-" + UtsSandbox.RandomId();

            var client = await SandboxRealtimeClient();

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, DeltaChannelOptions());

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue("the channel must attach: {0}", attachResult.Error);

            var received = new ConcurrentQueue<Message>();
            var attachingReasons = new ConcurrentQueue<ErrorInfo>();

            // The spec FAILs from inside the attaching handler. An exception thrown on an SDK callback
            // thread cannot fail an xUnit test — the emitter swallows it — so the reasons are recorded
            // here and asserted empty once the messages have arrived. That fails the test for the same
            // cause, at a point where the failure is reportable.
            channel.On(ChannelEvent.Attaching, change => attachingReasons.Enqueue(change.Error));

            channel.Subscribe(message => received.Enqueue(message));

            // Publish all messages sequentially.
            for (var i = 0; i < TestDataJson.Length; i++)
            {
                var publishResult = await channel.PublishAsync(
                    i.ToString(),
                    JObject.Parse(TestDataJson[i]));
                publishResult.IsSuccess.Should().BeTrue(
                    "publish {0} must succeed: {1}",
                    i,
                    publishResult.Error);
            }

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(received.Count == TestDataJson.Length),
                $"all {TestDataJson.Length} messages to be received",
                ReceiveTimeout);

            var messages = received.ToArray();

            attachingReasons.Should().BeEmpty(
                "a reattach here would mean the channel hit a decode failure");

            for (var i = 0; i < TestDataJson.Length; i++)
            {
                messages[i].Name.Should().Be(i.ToString());
                JToken.DeepEquals(AsJson(messages[i].Data), JObject.Parse(TestDataJson[i]))
                    .Should().BeTrue("message {0} should decode to the payload that was published", i);
            }

            // The spec's "decode_count == length(test_data) - 1": the first message is sent as a full
            // payload and the rest as deltas. See the class summary for why this counts delta extras
            // rather than decoder invocations.
            DeltaMessageCount(messages).Should().Be(TestDataJson.Length - 1);

            client.Close();
        }

        // UTS: realtime/integration/RTL19b/dissimilar-payloads-no-delta-0
        [Fact]
        public async Task RTL19b_DissimilarPayloadsNoDelta()
        {
            const int messageCount = 5;
            var channelName = "delta-dissimilar-" + UtsSandbox.RandomId();

            // Random binary payloads — 1KB each, completely dissimilar.
            var payloads = new byte[messageCount][];
            using (var rng = RandomNumberGenerator.Create())
            {
                for (var i = 0; i < messageCount; i++)
                {
                    payloads[i] = new byte[1024];
                    rng.GetBytes(payloads[i]);
                }
            }

            var client = await SandboxRealtimeClient();

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, DeltaChannelOptions());

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue("the channel must attach: {0}", attachResult.Error);

            var received = new ConcurrentQueue<Message>();
            var attachingReasons = new ConcurrentQueue<ErrorInfo>();

            // See PC3_DeltaDecodeEndToEnd for why the spec's FAIL becomes a recorded reason.
            channel.On(ChannelEvent.Attaching, change => attachingReasons.Enqueue(change.Error));

            channel.Subscribe(message => received.Enqueue(message));

            for (var i = 0; i < messageCount; i++)
            {
                var publishResult = await channel.PublishAsync(i.ToString(), payloads[i]);
                publishResult.IsSuccess.Should().BeTrue(
                    "publish {0} must succeed: {1}",
                    i,
                    publishResult.Error);
            }

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(received.Count == messageCount),
                $"all {messageCount} dissimilar messages to be received",
                ReceiveTimeout);

            var messages = received.ToArray();

            attachingReasons.Should().BeEmpty(
                "a reattach here would mean the channel hit a decode failure");

            // All messages received with correct data. Each non-delta message must have updated the
            // stored base payload (RTL19b), which is what makes the next one decodable.
            for (var i = 0; i < messageCount; i++)
            {
                messages[i].Name.Should().Be(i.ToString());
                messages[i].Data.Should().BeOfType<byte[]>();
                ((byte[])messages[i].Data).Should().Equal(payloads[i]);
            }

            // The server is expected to send full messages (no deltas) for dissimilar random binary
            // payloads, but it may still choose to generate deltas, so the spec logs the count rather
            // than asserting it is zero.
            Output.WriteLine(
                $"Deltas were generated for {DeltaMessageCount(messages)} of {messageCount} " +
                "dissimilar messages");

            client.Close();
        }

        // UTS: realtime/integration/PC3/no-deltas-without-param-1
        [Fact]
        public async Task PC3_NoDeltasWithoutParam()
        {
            var channelName = "delta-no-param-" + UtsSandbox.RandomId();

            var client = await SandboxRealtimeClient();

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attach WITHOUT delta params.
            var channel = client.Channels.Get(channelName);

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue("the channel must attach: {0}", attachResult.Error);

            var received = new ConcurrentQueue<Message>();
            channel.Subscribe(message => received.Enqueue(message));

            for (var i = 0; i < TestDataJson.Length; i++)
            {
                var publishResult = await channel.PublishAsync(
                    i.ToString(),
                    JObject.Parse(TestDataJson[i]));
                publishResult.IsSuccess.Should().BeTrue(
                    "publish {0} must succeed: {1}",
                    i,
                    publishResult.Error);
            }

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(received.Count == TestDataJson.Length),
                $"all {TestDataJson.Length} messages to be received",
                ReceiveTimeout);

            var messages = received.ToArray();

            for (var i = 0; i < TestDataJson.Length; i++)
            {
                messages[i].Name.Should().Be(i.ToString());
                JToken.DeepEquals(AsJson(messages[i].Data), JObject.Parse(TestDataJson[i]))
                    .Should().BeTrue("message {0} should decode to the payload that was published", i);
            }

            // The spec's "decode_count == 0": with no delta channel param the server sends full
            // messages, so no message carries delta extras and the decoder is never reached.
            DeltaMessageCount(messages).Should().Be(0);

            client.Close();
        }

        // UTS: realtime/integration/RTL18/recovery-message-id-mismatch-0
        [Fact]
        public async Task RTL18_RecoveryMessageIdMismatch()
        {
            var channelName = "delta-recovery-mismatch-" + UtsSandbox.RandomId();

            var client = await SandboxRealtimeClient();

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName, DeltaChannelOptions());

            var attachResult = await channel.AttachAsync();
            attachResult.IsSuccess.Should().BeTrue("the channel must attach: {0}", attachResult.Error);

            var received = new ConcurrentQueue<Message>();
            var attachingReasons = new ConcurrentQueue<ErrorInfo>();

            // The spec's change.reason is ChannelStateChange.Error here.
            channel.On(ChannelEvent.Attaching, change => attachingReasons.Enqueue(change.Error));

            channel.Subscribe(message => received.Enqueue(message));

            // Publish the first batch and wait for it to arrive. Publishing in two batches ensures the
            // server has sent and the client has processed the first batch before the stored id is
            // cleared; published all at once they could arrive in a single ProtocolMessage first.
            for (var i = 0; i < 3; i++)
            {
                var publishResult = await channel.PublishAsync(
                    i.ToString(),
                    JObject.Parse(TestDataJson[i]));
                publishResult.IsSuccess.Should().BeTrue(
                    "publish {0} must succeed: {1}",
                    i,
                    publishResult.Error);
            }

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(received.Count >= 3),
                "the first three messages to be received",
                ReceiveTimeout);

            // The spec's "CLEAR channel._lastPayload.messageId", whose mechanism it says is
            // implementation-specific. .NET keeps that state on
            // RealtimeChannel.LastSuccessfulMessageIds, and LastMessageIds.LastMessageId carries an
            // internal setter put there for exactly this simulation; the test assembly has
            // InternalsVisibleTo. Nulling it makes the next delta's extras.delta.from fail the RTL20
            // base-reference check.
            var internalChannel = (RealtimeChannel)channel;
            internalChannel.LastSuccessfulMessageIds.Should().NotBeSameAs(
                LastMessageIds.Empty,
                "the received batch must have replaced the shared Empty singleton before it is mutated");
            internalChannel.LastSuccessfulMessageIds.LastMessageId = null;

            // Publish the remaining messages — the server should send these as deltas, which will fail
            // the RTL20 check and trigger recovery.
            for (var i = 3; i < TestDataJson.Length; i++)
            {
                var publishResult = await channel.PublishAsync(
                    i.ToString(),
                    JObject.Parse(TestDataJson[i]));
                publishResult.IsSuccess.Should().BeTrue(
                    "publish {0} must succeed: {1}",
                    i,
                    publishResult.Error);
            }

            // Recovery reattaches and the server resends from the channelSerial, so the same name can
            // arrive twice. The spec waits for every name rather than for a count.
            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(
                    Enumerable.Range(0, TestDataJson.Length)
                        .All(index => received.Any(candidate => candidate.Name == index.ToString()))),
                "every published message name to be received at least once",
                RecoveryTimeout);

            var messages = received.ToArray();

            // All messages were eventually received with correct data (there may be duplicates from
            // the server resending after recovery).
            for (var i = 0; i < TestDataJson.Length; i++)
            {
                var expectedName = i.ToString();
                var receivedMessage = messages.FirstOrDefault(m => m.Name == expectedName);
                receivedMessage.Should().NotBeNull($"message {expectedName} should have been received");
                JToken.DeepEquals(AsJson(receivedMessage.Data), JObject.Parse(TestDataJson[i]))
                    .Should().BeTrue("message {0} should decode to the payload that was published", i);
            }

            // RTL18c: recovery was triggered with error code 40018.
            var reasons = attachingReasons.ToArray();
            reasons.Should().NotBeEmpty();
            reasons[0].Should().NotBeNull("the recovery ATTACHING must carry a reason");
            reasons[0].Code.Should().Be(40018);

            client.Close();
        }

        private static ChannelOptions DeltaChannelOptions()
            => new ChannelOptions(channelParams: new ChannelParams { { "delta", "vcdiff" } });

        /// <summary>
        /// The number of received messages the server deltified, which is the number of messages the
        /// vcdiff decoder was invoked for. See the class summary: this SDK's decoder is not pluggable,
        /// so the spec's <c>decode_count</c> is read off the delta extras instead.
        /// </summary>
        private static int DeltaMessageCount(IEnumerable<Message> messages)
            => messages.Count(message => message.Extras?.Delta != null);

        /// <summary>
        /// The spec's <c>message.data</c> as a JSON value, whichever shape the decode chain left.
        /// A full JSON payload arrives with encoding <c>json</c> and decodes to a JToken; a deltified
        /// one arrives as <c>…/vcdiff/base64</c> and the vcdiff step hands back the bytes of the
        /// original payload, so depending on the encoding the server stamped the final value can be
        /// those bytes or the JSON text. All three are the same logical value, and the spec asserts on
        /// the value, so this normalises the representation without weakening the comparison.
        /// </summary>
        private static JToken AsJson(object data)
        {
            switch (data)
            {
                case JToken token:
                    return token;
                case byte[] bytes:
                    return JToken.Parse(Encoding.UTF8.GetString(bytes));
                case string text:
                    return JToken.Parse(text);
                default:
                    throw new InvalidOperationException(
                        $"Unexpected message data of type {data?.GetType().Name ?? "null"}.");
            }
        }
    }
}
