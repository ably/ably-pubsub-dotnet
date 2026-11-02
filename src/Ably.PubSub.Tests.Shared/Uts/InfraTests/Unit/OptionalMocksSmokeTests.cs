using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Unit
{
    /// <summary>
    /// The optional unit mocks each drive the SDK once: <see cref="MockNetworkListener"/> (RTN20) and the
    /// mock vcdiff decoders through hook H2 (PC3, RTL18). Plus the protocol tripwire of decision log #6.
    /// </summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "unit")]
    public class OptionalMocksSmokeTests : UtsUnitTestBase
    {
        public OptionalMocksSmokeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task NetworkListenerLossDisconnectsAConnectedClientWhichReconnects()
        {
            var attempts = 0;
            var mock = new MockWebSocket(attempt =>
            {
                attempts++;
                attempt.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id-" + attempts, "connection-key-" + attempts));
            });
            InstallMock(mock);
            var network = new MockNetworkListener();
            InstallMock(network);
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var changes = UtsRecord.ConnectionStateChanges(client.Connection);
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            network.Clients.Should().ContainSingle().Which.Should().BeSameAs(client);

            network.SimulateNetworkLost();
            await UtsWait.PollUntil(() => changes.Count(c => c.Current == ConnectionState.Connected) == 2, description: "the reconnection");

            UtsAssert.ContainsInOrder(
                changes.Select(c => c.Current),
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                });
            changes.First(c => c.Current == ConnectionState.Disconnected).Reason.Should().NotBeNull("RTN20a: an appropriate reason");
            attempts.Should().Be(2);
            client.Connection.Id.Should().Be("connection-id-2");
        }

        [Fact]
        public async Task NetworkListenerAvailableReconnectsADisconnectedClientAtOnce()
        {
            EnableFakeTimers();
            var attempts = 0;
            var mock = new MockWebSocket(attempt =>
            {
                // The first attempt and the SDK's instant retry fail; then it waits for its retry timer.
                if (++attempts <= 2)
                {
                    attempt.RespondWithRefused();
                }
                else
                {
                    attempt.RespondWithConnected();
                }
            });
            InstallMock(mock);
            var network = new MockNetworkListener();
            InstallMock(network);
            var client = Realtime(new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] });
            await UtsWait.PollUntil(() => attempts == 2 && client.Connection.State == ConnectionState.Disconnected);
            await ProcessPendingEvents();
            attempts.Should().Be(2);

            // RTN20b: no ADVANCE_TIME, so the DISCONNECTED retry timer can't be what reconnects.
            network.SimulateNetworkAvailable();

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            attempts.Should().Be(3);
        }

        [Fact]
        public async Task MockVCDiffDecoderDecodesADeltaThroughTheSdk()
        {
            const string name = "vcdiff-smoke";
            var encoder = new MockVCDiffEncoder();
            var decoder = new MockVCDiffDecoder();
            InstallMock(decoder);
            var mock = AttachingMock(name);
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            var channel = client.Channels.Get(name);
            var received = new CaptureList<Message>();
            channel.Subscribe(received.Add);
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();

            mock.SendToClient(MessageWith(name, "msg-1:0", "hello world", null, null));
            await UtsWait.PollUntil(() => received.Count == 1);

            // JSON transport: the delta travels base64-encoded (channel_delta_decoding.md, transport encoding note).
            var delta = encoder.Encode("hello world", "goodbye world");
            mock.SendToClient(MessageWith(name, "msg-2:0", Convert.ToBase64String(Encoding.UTF8.GetBytes(delta)), "utf-8/vcdiff/base64", "msg-1:0"));
            await UtsWait.PollUntil(() => received.Count == 2);

            received[1].Data.Should().Be("goodbye world");
            decoder.NumberOfCalls.Should().Be(1);
            decoder.LastBase.Should().Equal(Encoding.UTF8.GetBytes("hello world"), "PC3a: a string base reaches the decoder as UTF-8");
            decoder.LastDelta.Should().Equal(Encoding.UTF8.GetBytes(delta));
        }

        [Fact]
        public async Task FailingMockVCDiffDecoderDrivesTheDecodeFailureRecovery()
        {
            const string name = "vcdiff-failing";
            var decoder = new FailingMockVCDiffDecoder();
            InstallMock(decoder);
            var mock = AttachingMock(name);
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            var channel = client.Channels.Get(name);
            var received = new CaptureList<Message>();
            channel.Subscribe(received.Add);
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();
            var first = MessageWith(name, "msg-1:0", "base payload", null, null);
            first["channelSerial"] = "serial-100";
            mock.SendToClient(first);
            await UtsWait.PollUntil(() => received.Count == 1);
            var changes = UtsRecord.ChannelStateChanges(channel);

            var failing = MessageWith(name, "msg-2:0", Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-delta-payload")), "vcdiff/base64", "msg-1:0");
            failing["channelSerial"] = "serial-200";
            mock.SendToClient(failing);

            // RTL18c: recovery reattaches; the mock answers the new ATTACH.
            await UtsWait.PollUntil(() => mock.MessagesFromClientOfAction(ProtocolMessages.Attach).Count == 2, description: "the recovery ATTACH");
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            decoder.NumberOfCalls.Should().Be(1);
            received.Should().ContainSingle("RTL18b: the message that failed to decode is discarded");
            var attaching = changes.First(c => c.Current == ChannelState.Attaching);
            attaching.Error.Code.Should().Be(40018);
            mock.MessagesFromClientOfAction(ProtocolMessages.Attach).Last().ChannelSerial.Should().Be("serial-100");
        }

        [Fact]
        public void BinaryProtocolIsCompiledOutSoTheInfraHasNoMsgpackVariant()
        {
            var options = new ClientOptions { UseBinaryProtocol = true };

            options.UseBinaryProtocol.Should().BeFalse(
                "decision log #6: this SDK build is JSON-only, so the UTS unit infra speaks JSON only. If MSGPACK is ever " +
                "enabled this fails: the infra must then add the msgpack variant (MockWebSocket.OnBinaryDataFrame, " +
                "binary frames in MockTransport, msgpack bodies in MockHttpClient) and the translator a protocol variant");
        }

        private static MockWebSocket AttachingMock(string channel)
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            mock.OnMessage(ProtocolMessages.Attach, ProtocolMessages.AttachedMessage(channel));
            return mock;
        }

        private static JObject MessageWith(string channel, string id, string data, string encoding, string deltaFrom)
        {
            var message = new JObject { ["id"] = id, ["data"] = data };
            if (encoding != null)
            {
                message["encoding"] = encoding;
            }

            if (deltaFrom != null)
            {
                message["extras"] = new JObject { ["delta"] = new JObject { ["from"] = deltaFrom, ["format"] = "vcdiff" } };
            }

            var protocolMessage = ProtocolMessages.MessageProtocolMessage(channel, new JArray(message));
            protocolMessage["id"] = id;
            return protocolMessage;
        }
    }
}
