using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/channel_attach.md in ably/specification.
    ///
    /// Spec points: RTL4a, RTL4b, RTL4c, RTL4c1, RTL4f, RTL4g, RTL4h, RTL4i, RTL4j, RTL4k, RTL4l,
    /// RTL4m, RTL16a
    ///
    /// <para>
    /// <c>setOptions</c> is a callback method here rather than a task, so the tests that reattach
    /// through it wrap it in a <c>TaskCompletionSource</c>.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>AWAIT channel.attach() FAILS WITH error</c> is a returned <c>Result</c> - see
    /// the note on <see cref="ChannelAttributesTests"/>.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelAttachTests : UtsTestBase
    {
        private const int PublishFlag = 1 << 17;
        private const int SubscribeFlag = 1 << 18;

        public ChannelAttachTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL4a/already-attached-noop-0
        [Fact]
        public async Task RTL4a_AttachWhenAlreadyAttachedIsNoOp()
        {
            const string ChannelName = "test-RTL4a";

            var attachCount = 0;
            var (_, client) = ConnectingClient(msg =>
            {
                attachCount = attachCount + 1;
                return ProtocolMessages.AttachedMessage(ChannelName);
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(1);

            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(1, "RTL4a - nothing is sent for a channel already attached");
        }

        // UTS: realtime/unit/RTL4h/attach-while-attaching-0
        [Fact]
        public async Task RTL4h_AttachWhileAttachingWaitsForCompletion()
        {
            const string ChannelName = "test-RTL4h-attaching";

            var attachCount = 0;
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var first = channel.AttachAsync();
            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            var second = channel.AttachAsync();

            mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));

            await first;
            await second;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(1, "RTL4h - the second attach joins the first");
        }

        // UTS: realtime/unit/RTL4h/attach-while-detaching-1
        //
        // DEVIATION, D39. RTL4h (features.md:704) is explicit: "If the channel is in a pending
        // state DETACHING or ATTACHING, do the attach operation **after the completion of the
        // pending request**." The ATTACHING half works - the test above passes - but a channel in
        // DETACHING attaches immediately. Measured: states went Detaching then straight to
        // Attaching with the second ATTACH already on the wire, before the server had answered the
        // DETACH. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL4h_AttachWhileDetachingWaitsThenAttaches()
        {
            const string ChannelName = "test-RTL4h-detaching";

            var attachMessages = new List<ProtocolMessage>();
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var detachSeen = false;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachMessages.Add(msg);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    detachSeen = true;

                    // Held, so the channel sits in DETACHING while the attach below is issued.
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            var detach = channel.DetachAsync();
            await UtsClients.AwaitChannelState(channel, ChannelState.Detaching);
            await UtsClients.PollUntil(() => detachSeen, "the DETACH went out");

            var attach = channel.AttachAsync();

            mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));

            await detach;
            await attach;

            channel.State.Should().Be(ChannelState.Attached);
            attachMessages.Should().HaveCount(2, "ATTACH, DETACH, then ATTACH again");
        }

        // UTS: realtime/unit/RTL4g/attach-from-failed-0
        [Fact]
        public async Task RTL4g_AttachFromFailedStateProceeds()
        {
            const string ChannelName = "test-RTL4g";

            var attachCount = 0;
            var (mockWs, client) = ConnectingClient(msg =>
            {
                attachCount = attachCount + 1;
                return attachCount == 1
                    ? ProtocolMessages.ChannelErrorMessage(ChannelName, 40160, "Denied", 401)
                    : ProtocolMessages.AttachedMessage(ChannelName);
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var first = await channel.AttachAsync();
            first.IsSuccess.Should().BeFalse();
            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();

            await channel.AttachAsync();

            channel.State.Should().Be(ChannelState.Attached, "RTL4g");
            channel.ErrorReason.Should().BeNull("RTL4c");
        }

        // UTS: realtime/unit/RTL4c/clears-error-reason-0
        [Fact]
        public async Task RTL4c_SuccessfulAttachClearsErrorReasonAfterSuspension()
        {
            const string ChannelName = "test-RTL4c-suspended";

            var clock = new TestClock();
            var refuseConnections = false;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (refuseConnections)
                {
                    conn.RespondWithRefused();
                    return;
                }

                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage(
                    connectionStateTtl: 5000,
                    maxIdleInterval: 0));
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(100);
            });

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            refuseConnections = true;
            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(30));

            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(30));

            channel.ErrorReason.Should().NotBeNull("the suspension carried an error");

            refuseConnections = false;

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(30));

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(30));

            channel.State.Should().Be(ChannelState.Attached);
            channel.ErrorReason.Should().BeNull("RTL4c - the successful attach clears it");
        }

        // UTS: realtime/unit/RTL4b/fails-connection-closed-0
        [Fact]
        public async Task RTL4b_AttachFailsWhenConnectionClosed()
        {
            const string ChannelName = "test-RTL4b-closed";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);
            client.Close();
            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());
            await closed;

            client.Connection.State.Should().Be(ConnectionState.Closed);

            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeFalse("RTL4b");
            result.Error.Should().NotBeNull();
            channel.State.Should().NotBe(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTL4b/fails-connection-failed-1
        [Fact]
        public async Task RTL4b_AttachFailsWhenConnectionFailed()
        {
            const string ChannelName = "test-RTL4b-failed";

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClientAndClose(
                    ProtocolMessages.ErrorMessage(40005, "Invalid request", 400));
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Failed,
                TimeSpan.FromSeconds(5));

            var channel = client.Channels.Get(ChannelName);

            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeFalse("RTL4b");
            result.Error.Should().NotBeNull();
            channel.State.Should().NotBe(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTL4b/fails-connection-suspended-2
        [Fact]
        public async Task RTL4b_AttachFailsWhenConnectionSuspended()
        {
            const string ChannelName = "test-RTL4b-suspended";

            var clock = new TestClock();
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount = connectionAttemptCount + 1;
                if (connectionAttemptCount == 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage(
                        connectionStateTtl: 5000,
                        maxIdleInterval: 0));
                }
                else
                {
                    conn.RespondWithRefused();
                }
            });

            var client = RealtimeClient(mockWs, clock: clock, configure: options =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(100);
                options.SuspendedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 2,
                "the first reconnection attempt");

            clock.Advance(6000);

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Suspended,
                TimeSpan.FromSeconds(30));

            var channel = client.Channels.Get(ChannelName);

            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeFalse("RTL4b");
            result.Error.Should().NotBeNull();
            channel.State.Should().NotBe(ChannelState.Attached);
        }

        // UTS: realtime/unit/RTL4i/queued-while-connecting-0
        [Fact]
        public async Task RTL4i_AttachQueuedWhileConnecting()
        {
            const string ChannelName = "test-RTL4i-queued";

            var attachSeen = false;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                // Never answered, so the connection stays CONNECTING.
            });

            var client = RealtimeClient(mockWs, configure: options =>
                options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachSeen = true;
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connecting);

            var channel = client.Channels.Get(ChannelName);
            channel.Attach();

            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            channel.State.Should().Be(ChannelState.Attaching, "RTL4i");
            attachSeen.Should().BeFalse("nothing can be sent while the connection is still opening");
        }

        // UTS: realtime/unit/RTL4i/completes-on-connected-1
        [Fact]
        public async Task RTL4i_AttachCompletesWhenConnectionBecomesConnected()
        {
            const string ChannelName = "test-RTL4i-completes";

            var attachSeen = false;
            PendingWebSocketConnection pending = null;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn => pending = conn);

            var client = RealtimeClient(mockWs, configure: options =>
                options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachSeen = true;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connecting);
            await UtsClients.PollUntil(() => pending != null, "the connection attempt");

            var channel = client.Channels.Get(ChannelName);
            var attach = channel.AttachAsync();

            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching);
            attachSeen.Should().BeFalse();

            pending.RespondWithSuccess();
            mockWs.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());

            await attach;

            channel.State.Should().Be(ChannelState.Attached);
            attachSeen.Should().BeTrue();
        }

        // UTS: realtime/unit/RTL4c/sends-attach-message-1
        [Fact]
        public async Task RTL4c_SendsAttachMessageAndTransitionsToAttaching()
        {
            const string ChannelName = "test-RTL4c-sends";

            ProtocolMessage capturedAttach = null;
            var (_, client) = ConnectingClient(msg =>
            {
                capturedAttach = msg;
                return ProtocolMessages.AttachedMessage(ChannelName);
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            ChannelState? stateDuringAttach = null;
            channel.On(change =>
            {
                if (stateDuringAttach == null)
                {
                    stateDuringAttach = change.Current;
                }
            });

            await channel.AttachAsync();

            stateDuringAttach.Should().Be(ChannelState.Attaching);
            channel.State.Should().Be(ChannelState.Attached);

            capturedAttach.Should().NotBeNull();
            capturedAttach.Action.Should().Be(ProtocolMessage.MessageAction.Attach);
            capturedAttach.Channel.Should().Be(ChannelName);
        }

        // UTS: realtime/unit/RTL4c1/includes-channel-serial-0
        [Fact]
        public async Task RTL4c1_AttachIncludesChannelSerialWhenAvailable()
        {
            const string ChannelName = "test-RTL4c1";

            var attachMessages = new List<ProtocolMessage>();
            var (_, client) = ConnectingClient(msg =>
            {
                attachMessages.Add(msg);
                return ProtocolMessages.AttachedMessage(
                    ChannelName,
                    new Dictionary<string, JToken> { ["channelSerial"] = "serial-from-server-1" });
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            // RTL16a reattaches without going through DETACHED, so the serial survives.
            await SetOptionsAsync(channel, new ChannelOptions
            {
                Modes = new ChannelModes(ChannelMode.Subscribe),
            });

            attachMessages.Should().HaveCount(2);
            attachMessages[0].ChannelSerial.Should().BeNullOrEmpty("nothing had been attached yet");
            attachMessages[1].ChannelSerial.Should().Be("serial-from-server-1");
        }

        // UTS: realtime/unit/RTL4f/timeout-to-suspended-0
        [Fact]
        public async Task RTL4f_AttachTimesOutToSuspended()
        {
            const string ChannelName = "test-RTL4f";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200);
                options.ChannelRetryTimeout = TimeSpan.FromMinutes(10);
            });

            // The ATTACH is never answered.
            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);

            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            channel.State.Should().Be(ChannelState.Suspended, "RTL4f");
        }

        // UTS: realtime/unit/RTL4k/includes-channel-params-0
        [Fact]
        public async Task RTL4k_AttachIncludesChannelParams()
        {
            const string ChannelName = "test-RTL4k";

            ProtocolMessage capturedAttach = null;
            var (_, client) = ConnectingClient(msg =>
            {
                capturedAttach = msg;
                return ProtocolMessages.AttachedMessage(ChannelName);
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName, new ChannelOptions
            {
                Params = new ChannelParams { { "rewind", "1" }, { "delta", "vcdiff" } },
            });

            await channel.AttachAsync();

            capturedAttach.Should().NotBeNull();
            capturedAttach.Params.Should().NotBeNull();
            capturedAttach.Params["rewind"].Should().Be("1");
            capturedAttach.Params["delta"].Should().Be("vcdiff");
        }

        // UTS: realtime/unit/RTL4l/modes-encoded-as-flags-0
        [Fact]
        public async Task RTL4l_AttachEncodesModesAsFlags()
        {
            const string ChannelName = "test-RTL4l";

            ProtocolMessage capturedAttach = null;
            var (_, client) = ConnectingClient(msg =>
            {
                capturedAttach = msg;
                return ProtocolMessages.AttachedMessage(ChannelName);
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName, new ChannelOptions
            {
                Modes = new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe),
            });

            await channel.AttachAsync();

            capturedAttach.Should().NotBeNull();
            capturedAttach.Flags.Should().NotBeNull();
            (capturedAttach.Flags.Value & PublishFlag).Should().NotBe(0, "TR3r");
            (capturedAttach.Flags.Value & SubscribeFlag).Should().NotBe(0, "TR3s");
        }

        // UTS: realtime/unit/RTL4m/modes-from-attached-0
        [Fact]
        public async Task RTL4m_ModesPopulatedFromAttachedResponse()
        {
            const string ChannelName = "test-RTL4m";

            var (_, client) = ConnectingClient(msg => ProtocolMessages.AttachedMessage(
                ChannelName,
                new Dictionary<string, JToken> { ["flags"] = PublishFlag | SubscribeFlag }));

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            channel.Modes.Should().NotBeNull();
            channel.Modes.Should().Contain(ChannelMode.Publish);
            channel.Modes.Should().Contain(ChannelMode.Subscribe);
        }

        // UTS: realtime/unit/RTL4j/attach-resume-flag-not-set-0
        [Fact]
        public async Task RTL4j_AttachResumeFlagNotSet()
        {
            const string ChannelName = "test-RTL4j";
            const int AttachResumeFlag = 1 << 5;

            var attachMessages = new List<ProtocolMessage>();
            var (_, client) = ConnectingClient(msg =>
            {
                attachMessages.Add(msg);
                return ProtocolMessages.AttachedMessage(ChannelName);
            });

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            await SetOptionsAsync(channel, new ChannelOptions
            {
                Params = new ChannelParams { { "rewind", "1" } },
            });

            attachMessages.Should().HaveCount(2);
            attachMessages.Should().OnlyContain(
                msg => msg.Flags == null || (msg.Flags.Value & AttachResumeFlag) == 0,
                "RTL4j - ATTACH_RESUME is no longer set on any attach");
        }

        /// <summary>
        /// The spec's <c>AWAIT channel.setOptions(...)</c>. <c>SetOptions</c> is a callback method
        /// here, so it is wrapped to be awaited.
        /// </summary>
        private static Task<bool> SetOptionsAsync(IRealtimeChannel channel, ChannelOptions options)
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            channel.SetOptions(options, (success, error) => completion.TrySetResult(success));

            return completion.Task;
        }

        private (MockWebSocket MockWs, PubSubRealtimeClient Client) ConnectingClient(
            Func<ProtocolMessage, JObject> onAttach)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    var response = onAttach(msg);
                    if (response != null)
                    {
                        mockWs.SendToClient(response);
                    }
                }
            };

            client.Connect();
            return (mockWs, client);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
