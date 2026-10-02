using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/channel_server_initiated_detach.md in
    /// ably/specification.
    ///
    /// Spec points: RTL13, RTL13a, RTL13b, RTL13c
    ///
    /// <para>
    /// A DETACHED the client did not ask for means the server has dropped the channel, and RTL13
    /// is the recovery: reattach at once from ATTACHED or SUSPENDED, fall to SUSPENDED and retry on
    /// a slower loop if that fails, and abandon the loop entirely once the connection is no longer
    /// CONNECTED because RTL3 takes over there.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>enable_fake_timers()</c> / <c>ADVANCE_TIME</c> has no equivalent here - there
    /// is no timer seam in this SDK, as set out at length in <c>BackoffJitterTests</c>. The waits
    /// are instead made short enough to sit out in real time, by shortening the two options that
    /// drive them: <c>RealtimeRequestTimeout</c> (the attach deadline) and
    /// <c>ChannelRetryTimeout</c> (the SUSPENDED retry). Where a test needs an attach to stay
    /// pending rather than time out, the deadline goes the other way and is pushed out to ten
    /// minutes, which removes the race instead of narrowing it.
    /// </para>
    ///
    /// <para>
    /// One finding runs through the whole file, D43: this SDK passes through DETACHED on the way to
    /// both RTL13 destinations, and emits that change out of order. The recovery itself is correct,
    /// so the sequence assertions here check the spec's states in order rather than by index, and
    /// the spurious change is pinned down on its own in
    /// <c>RTL13_NoIntermediateDetachedStateChange</c>. See Uts/deviations.md.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelServerInitiatedDetachTests : UtsTestBase
    {
        private const int ServerDetachCode = 90198;

        /// <summary>
        /// Long enough that an attach left unanswered stays ATTACHING for the whole test, so a test
        /// about what a DETACHED does to an attaching channel cannot accidentally measure the
        /// attach deadline instead.
        /// </summary>
        private static readonly TimeSpan NoAttachDeadline = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The attach deadline when a test wants an unanswered attach to fail. Longer than the
        /// spec's 100ms: this is real wall-clock time on a shared build agent, not a fake clock.
        /// </summary>
        private static readonly TimeSpan ShortAttachDeadline = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// The SUSPENDED retry wait. RTB1 scales it by attempt and shaves up to 20% off as jitter,
        /// so with three attempts the longest single wait here is about 500ms.
        /// </summary>
        private static readonly TimeSpan ShortRetryWait = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// Several of these tests sit through an attach deadline and then a retry wait, more than
        /// once. The default five seconds is tight for that; this is not.
        /// </summary>
        private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(20);

        public ChannelServerInitiatedDetachTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTL13a/attached-reattach-triggered-0
        [Fact]
        public async Task RTL13a_ServerDetachedWhileAttachedReattachesImmediately()
        {
            const string ChannelName = "test-RTL13a-attached";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(1);

            var changes = RecordChanges(channel);

            // The channel is already ATTACHED, so the reattach has to be watched for rather than
            // waited on: subscribe before provoking it.
            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Server detached channel", 500));

            await reattached;

            attachCount.Should().Be(2, "RTL13a - the DETACHED provoked a second ATTACH");

            var observed = UtsClients.Snapshot(changes);

            UtsClients.ContainsInOrder(
                States(observed),
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue("RTL13a - the channel reattached");

            var attaching = observed.Find(c => c.Current == ChannelState.Attaching);
            attaching.Should().NotBeNull();
            attaching.Error.Should().NotBeNull("RTL13a - the reattach carries the DETACHED's error");
            attaching.Error.Code.Should().Be(ServerDetachCode);
        }

        // UTS: realtime/unit/RTL13a/suspended-reattach-triggered-1
        [Fact]
        public async Task RTL13a_ServerDetachedWhileSuspendedReattachesImmediately()
        {
            const string ChannelName = "test-RTL13a-suspended";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = ShortAttachDeadline;

                // The second DETACHED is what has to provoke the third ATTACH. Keep the retry wait
                // far enough out that it cannot be the thing that did.
                options.ChannelRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                attachCount = attachCount + 1;

                // The second attach - the RTL13a reattach - goes unanswered, so it times out into
                // SUSPENDED. The first and third succeed.
                if (attachCount != 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var suspended = UtsClients.NextChannelState(channel, ChannelState.Suspended, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Detach 1", 500));

            await suspended;
            attachCount.Should().Be(2, "the reattach was sent and then timed out");

            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, 90199, "Detach 2", 500));

            await reattached;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(3, "RTL13a - SUSPENDED reattaches immediately, like ATTACHED");
        }

        // UTS: realtime/unit/RTL13b/failed-reattach-suspended-retry-0
        [Fact]
        public async Task RTL13b_FailedReattachGoesSuspendedAndRetries()
        {
            const string ChannelName = "test-RTL13b-retry";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = ShortAttachDeadline;
                options.ChannelRetryTimeout = ShortRetryWait;
            });

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                attachCount = attachCount + 1;

                // Attach 2 is the RTL13a reattach and goes unanswered; attach 3 is the RTL13b
                // automatic retry and succeeds.
                if (attachCount != 2)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var changes = RecordChanges(channel);
            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Server detached", 500));

            await reattached;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(3, "RTL13b - the retry after channelRetryTimeout succeeded");

            UtsClients.ContainsInOrder(
                States(UtsClients.Snapshot(changes)),
                ChannelState.Attaching,
                ChannelState.Suspended,
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue("RTL13b - reattach, fail to SUSPENDED, retry, succeed");
        }

        // UTS: realtime/unit/RTL13b/attaching-detached-to-suspended-1
        [Fact]
        public async Task RTL13b_ServerDetachedWhileAttachingGoesStraightToSuspended()
        {
            const string ChannelName = "test-RTL13b-attaching";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                // The first attach is deliberately left pending; it must not time out on its own.
                options.RealtimeRequestTimeout = NoAttachDeadline;
                options.ChannelRetryTimeout = ShortRetryWait;
            });

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                attachCount = attachCount + 1;

                // Only the automatic retry out of SUSPENDED is answered.
                if (attachCount > 1)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attaching = UtsClients.AwaitChannelState(channel, ChannelState.Attaching, StateTimeout);

            // Not awaited: the mock leaves this attach pending.
            _ = channel.AttachAsync();
            await attaching;

            var changes = RecordChanges(channel);
            var suspended = UtsClients.NextChannelState(channel, ChannelState.Suspended, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Server detached", 500));

            await suspended;
            attachCount.Should().Be(
                1,
                "RTL13b - an already-ATTACHING channel is suspended, not reattached immediately");

            var attached = UtsClients.NextChannelState(channel, ChannelState.Attached, StateTimeout);
            await attached;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(2, "RTL13b - the automatic retry out of SUSPENDED succeeded");

            var observed = UtsClients.Snapshot(changes);
            var toSuspended = observed.Find(c => c.Current == ChannelState.Suspended);

            toSuspended.Should().NotBeNull();
            toSuspended.Error.Should().NotBeNull();
            toSuspended.Error.Code.Should().Be(ServerDetachCode);

            // The spec also asserts this was the *first* change and that it came straight from
            // ATTACHING. It was not, and it did not: see RTL13_NoIntermediateDetachedStateChange
            // below and D43 in Uts/deviations.md.
        }

        // UTS: realtime/unit/RTL13a/attached-reattach-triggered-0,
        //      realtime/unit/RTL13b/attaching-detached-to-suspended-1 (the state-sequence halves)
        //
        // DEVIATION, D43. RTL13 lists exactly two destinations for a server-initiated DETACHED -
        // ATTACHING under RTL13a, SUSPENDED under RTL13b - and this SDK passes through DETACHED on
        // the way to both. Measured here, a listener sees ATTACHING (previous: DETACHED), then
        // DETACHED (previous: ATTACHED), then ATTACHED: not just one state change too many, but one
        // that arrives after the transition it supposedly preceded. See Uts/deviations.md.
        [DeviationFact]
        public async Task RTL13_NoIntermediateDetachedStateChange()
        {
            const string ChannelName = "test-RTL13-no-detached";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

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

            var changes = RecordChanges(channel);
            var reattached = UtsClients.NextChannelState(channel, ChannelState.Attached, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Server detached channel", 500));

            await reattached;

            var observed = UtsClients.Snapshot(changes);

            States(observed).Should().NotContain(
                ChannelState.Detached,
                "RTL13a - the channel transitions from ATTACHED to ATTACHING, nowhere else");

            observed[0].Current.Should().Be(ChannelState.Attaching);
            observed[0].Previous.Should().Be(
                ChannelState.Attached,
                "RTL13a - the reattach starts from where the channel was");
        }

        // UTS: realtime/unit/RTL13b/repeated-failure-cycle-2
        [Fact]
        public async Task RTL13b_RepeatedFailuresCycleSuspendedAndAttaching()
        {
            const string ChannelName = "test-RTL13b-repeat";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = ShortAttachDeadline;
                options.ChannelRetryTimeout = ShortRetryWait;
            });

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                attachCount = attachCount + 1;

                // Attaches 2 and 3 go unanswered, so the channel cycles twice. The fourth succeeds.
                if (attachCount == 1 || attachCount >= 4)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            attachCount.Should().Be(1);

            var changes = RecordChanges(channel);
            var attached = UtsClients.NextChannelState(channel, ChannelState.Attached, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Detach", 500));

            await attached;

            channel.State.Should().Be(ChannelState.Attached);
            attachCount.Should().Be(4, "RTL13b - two failed cycles and then a successful one");

            UtsClients.ContainsInOrder(
                States(UtsClients.Snapshot(changes)),
                ChannelState.Attaching,
                ChannelState.Suspended,
                ChannelState.Attaching,
                ChannelState.Suspended,
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue("RTL13b - the SUSPENDED/ATTACHING cycle repeats");
        }

        // UTS: realtime/unit/RTL13c/retry-cancelled-disconnected-0
        [Fact]
        public async Task RTL13c_RetryCancelledWhenConnectionIsNotConnected()
        {
            const string ChannelName = "test-RTL13c";

            // Only the first connection is allowed. RTN15a retries a dropped connection instantly,
            // ignoring disconnectedRetryTimeout, and a client that gets back to CONNECTED is
            // entitled to reattach its channels under RTN15c - measured, that put attachCount at 5.
            // Refusing the reconnect is what isolates RTL13c; the spec's fake clock sidesteps the
            // same problem by never letting the reconnect happen.
            var connectionAttempts = 0;
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempts = connectionAttempts + 1;
                if (connectionAttempts == 1)
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
                }
                else
                {
                    conn.RespondWithRefused();
                }
            });

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.RealtimeRequestTimeout = ShortAttachDeadline;
                options.ChannelRetryTimeout = ShortRetryWait;
                options.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action != ProtocolMessage.MessageAction.Attach)
                {
                    return;
                }

                attachCount = attachCount + 1;

                // Only the first attach is answered; every reattach times out.
                if (attachCount == 1)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            attachCount.Should().Be(1);

            var suspended = UtsClients.NextChannelState(channel, ChannelState.Suspended, StateTimeout);

            mockWs.SendToClient(ProtocolMessages.ServerDetachedMessage(
                ChannelName, ServerDetachCode, "Detach", 500));

            await suspended;
            attachCount.Should().Be(2, "the RTL13a reattach was sent and timed out");

            var disconnected = UtsClients.NextConnectionState(
                client.Connection, ConnectionState.Disconnected, StateTimeout);

            mockWs.SimulateDisconnect();
            await disconnected;

            var attachCountAfterDisconnect = attachCount;

            // Well past the retry wait, which RTB1 scales but never past about 600ms here.
            await Task.Delay(TimeSpan.FromMilliseconds(1500));

            attachCount.Should().Be(
                attachCountAfterDisconnect,
                "RTL13c - the retry is cancelled once the connection is not CONNECTED");

            channel.State.Should().Be(
                ChannelState.Suspended,
                "RTL3e - a DISCONNECTED connection leaves channel state alone");

            client.Connection.State.Should().NotBe(
                ConnectionState.Connected,
                "the whole wait above has to have happened with the connection down");
        }

        // UTS: realtime/unit/RTL13a/detaching-not-server-initiated-2
        [Fact]
        public async Task RTL13a_DetachedWhileDetachingIsNotServerInitiated()
        {
            const string ChannelName = "test-RTL13-detaching";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var attachCount = 0;
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachCount = attachCount + 1;
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
                else if (msg.Action == ProtocolMessage.MessageAction.Detach)
                {
                    mockWs.SendToClient(ProtocolMessages.DetachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            await channel.DetachAsync();

            channel.State.Should().Be(
                ChannelState.Detached,
                "RTL13 covers ATTACHING, ATTACHED and SUSPENDED - a requested detach is RTL5");
            attachCount.Should().Be(1, "nothing reattached");
        }

        private static List<ChannelState> States(List<ChannelStateChange> changes)
        {
            var states = new List<ChannelState>();
            foreach (var change in changes)
            {
                states.Add(change.Current);
            }

            return states;
        }

        private static List<ChannelStateChange> RecordChanges(IRealtimeChannel channel)
        {
            var changes = new List<ChannelStateChange>();
            channel.On(change =>
            {
                lock (changes)
                {
                    changes.Add(change);
                }
            });

            return changes;
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
