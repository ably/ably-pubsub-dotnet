using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Proxy
{
    /// <summary>
    /// Derived from uts/realtime/integration/proxy/channel_faults.md in ably/specification.
    ///
    /// Spec points: RTL3d, RTL4f, RTL5f, RTL12, RTL13a, RTL14
    ///
    /// <para>
    /// Everything that can go wrong with one channel while the connection underneath it is fine:
    /// a request the server never answers (RTL4f, RTL5f), an answer that is an error (RTL14), an
    /// unprompted DETACHED or ATTACHED (RTL13a, RTL12), and the reattach that follows a real
    /// reconnection (RTL3d). The recurring assertion is the second one in each test - that the
    /// connection is still CONNECTED, because none of this is supposed to reach it.
    /// </para>
    /// </summary>
    public class ChannelFaultsTests : UtsProxyTestBase
    {
        private const int AttachAction = 10;
        private const int AttachedAction = 11;
        private const int DetachedAction = 13;
        private const int ErrorAction = 9;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ChannelTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The spec's 3000ms. Short enough to sit out, long enough that a real round trip to the
        /// sandbox cannot beat it by accident.
        /// </summary>
        private static readonly TimeSpan ShortRequestTimeout = TimeSpan.FromMilliseconds(3000);

        public ChannelFaultsTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTL4f/attach-timeout-suppressed-0
        [ProxyFact]
        public async Task RTL4f_AnAttachTheServerNeverSeesSuspendsTheChannel()
        {
            var channelName = "test-RTL4f-" + UtsSandbox.RandomId();

            var session = await ProxySession(new JArray
            {
                SuppressToServer("ATTACH", channelName, "RTL4f: the server never sees the ATTACH"),
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback(), options =>
                options.RealtimeRequestTimeout = ShortRequestTimeout);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            var states = UtsClients.RecordChannelStates(channel);

            var attaching = channel.AttachAsync();

            await UtsClients.AwaitChannelState(channel, ChannelState.Attaching, ChannelTimeout);
            await UtsClients.AwaitChannelState(channel, ChannelState.Suspended, ChannelTimeout);

            var result = await attaching;

            channel.State.Should().Be(ChannelState.Suspended);
            result.IsSuccess.Should().BeFalse("RTL4f - the attach failed");
            result.Error.Should().NotBeNull();

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ChannelState.Attaching,
                ChannelState.Suspended)
                .Should().BeTrue();

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "RTL4f is channel-scoped");

            var attaches = ChannelFrames(await session.GetLog(), AttachAction, channelName);
            attaches.Should().NotBeEmpty("the frames are logged before the rule suppresses them");
            attaches.Should().OnlyContain(
                frame => ProxyLog.RuleMatched(frame) != null,
                "every ATTACH was caught by the suppress rule, which is why none was answered");
        }

        // UTS: realtime/proxy/RTL14/error-on-attach-0
        [ProxyFact]
        public async Task RTL14_AnErrorInsteadOfTheAttachedFailsTheChannel()
        {
            var channelName = "test-RTL14-error-on-attach-" + UtsSandbox.RandomId();

            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "ws_frame_to_client",
                        ["action"] = "ATTACHED",
                        ["channel"] = channelName,
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "replace",
                        ["message"] = ChannelError(channelName, 40160, 403, "Not permitted"),
                    },
                    ["times"] = 1,
                    ["comment"] = "RTL14: answer the ATTACH with a channel ERROR",
                },
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            var states = UtsClients.RecordChannelStates(channel);

            var result = await channel.AttachAsync();

            await UtsClients.AwaitChannelState(channel, ChannelState.Failed, ChannelTimeout);

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            channel.ErrorReason.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

            result.IsSuccess.Should().BeFalse();
            result.Error.Code.Should().Be(40160);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ChannelState.Attaching,
                ChannelState.Failed)
                .Should().BeTrue();

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/proxy/RTL5f/detach-timeout-suppressed-0
        [ProxyFact]
        public async Task RTL5f_ADetachTheServerNeverSeesRevertsToAttached()
        {
            var channelName = "test-RTL5f-" + UtsSandbox.RandomId();

            // Clean to start with: the channel has to attach normally before the detach can be
            // the thing that fails.
            var session = await ProxySession();

            var client = ProxyRealtimeClient(session, await JwtAuthCallback(), options =>
                options.RealtimeRequestTimeout = ShortRequestTimeout);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var states = UtsClients.RecordChannelStates(channel);

            await session.AddRules(
                new JArray
                {
                    SuppressToServer(
                        "DETACH", channelName, "RTL5f: the server never sees the DETACH"),
                },
                position: "prepend");

            var detaching = channel.DetachAsync();

            await UtsClients.AwaitChannelState(channel, ChannelState.Detaching, ChannelTimeout);
            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, ChannelTimeout);

            var result = await detaching;

            channel.State.Should().Be(
                ChannelState.Attached,
                "RTL5f - a failed detach returns the channel to where it was");

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ChannelState.Detaching,
                ChannelState.Attached)
                .Should().BeTrue();

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/proxy/RTL13a/unsolicited-detach-reattach-0
        [ProxyFact]
        public async Task RTL13a_AnUnsolicitedDetachedReattachesTheChannel()
        {
            var channelName = "test-RTL13a-" + UtsSandbox.RandomId();

            var session = await ProxySession();
            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var states = UtsClients.RecordChannelStates(channel);
            var reattached = UtsClients.NextChannelState(
                channel, ChannelState.Attached, ChannelTimeout);

            await session.TriggerAction(new JObject
            {
                ["type"] = "inject_to_client",
                ["message"] = new JObject
                {
                    ["action"] = DetachedAction,
                    ["channel"] = channelName,
                    ["error"] = new JObject
                    {
                        ["code"] = 90198,
                        ["statusCode"] = 500,
                        ["message"] = "Channel detached by server",
                    },
                },
            });

            await reattached;

            channel.State.Should().Be(ChannelState.Attached);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue("RTL13a - the channel reattached on its own");

            client.Connection.State.Should().Be(ConnectionState.Connected);

            await UtsSandbox.WallClockPollUntil(
                async () => ChannelFrames(await session.GetLog(), AttachAction, channelName).Count >= 2,
                "the reattach ATTACH on the wire");
        }

        // UTS: realtime/proxy/RTL14/channel-error-goes-failed-1
        [ProxyFact]
        public async Task RTL14_AnInjectedChannelErrorFailsTheChannelOnly()
        {
            var channelName = "test-RTL14-" + UtsSandbox.RandomId();

            var session = await ProxySession();
            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var states = UtsClients.RecordChannelStates(channel);
            var failed = UtsClients.NextChannelState(channel, ChannelState.Failed, ChannelTimeout);

            await session.TriggerAction(new JObject
            {
                ["type"] = "inject_to_client",
                ["message"] = ChannelError(channelName, 40160, 403, "Not permitted"),
            });

            await failed;

            channel.State.Should().Be(ChannelState.Failed);
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            channel.ErrorReason.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
            channel.ErrorReason.Message.Should().Contain("Not permitted");

            UtsClients.Snapshot(states).Should().Equal(
                new List<ChannelState> { ChannelState.Failed },
                "RTL14 - straight to FAILED, with nothing in between");

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "RTL14 - a channel error is not a connection error");
        }

        // UTS: realtime/proxy/RTL12/attached-non-resumed-update-0
        [ProxyFact]
        public async Task RTL12_ANonResumedAttachedEmitsUpdateAndNotAttached()
        {
            var channelName = "test-RTL12-" + UtsSandbox.RandomId();

            var session = await ProxySession();
            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var updates = new List<ChannelStateChange>();
            var attachedEvents = new List<ChannelStateChange>();

            channel.On(ChannelEvent.Update, change => Append(updates, change));
            channel.On(ChannelEvent.Attached, change => Append(attachedEvents, change));

            await session.TriggerAction(new JObject
            {
                ["type"] = "inject_to_client",
                ["message"] = new JObject
                {
                    ["action"] = AttachedAction,
                    ["channel"] = channelName,
                    ["flags"] = 0,
                    ["error"] = new JObject
                    {
                        ["code"] = 91001,
                        ["statusCode"] = 500,
                        ["message"] = "Continuity lost",
                    },
                },
            });

            await UtsSandbox.WallClockPollUntil(
                () => Task.FromResult(Count(updates) >= 1),
                "RTL12 - the UPDATE event");

            var observed = Snapshot(updates);
            observed.Should().HaveCount(1);
            observed[0].Current.Should().Be(ChannelState.Attached);
            observed[0].Previous.Should().Be(ChannelState.Attached);
            observed[0].Resumed.Should().BeFalse();
            observed[0].Error.Should().NotBeNull();
            observed[0].Error.Code.Should().Be(91001);
            observed[0].Error.StatusCode.Should().Be(
                System.Net.HttpStatusCode.InternalServerError);
            observed[0].Error.Message.Should().Contain("Continuity lost");

            Snapshot(attachedEvents).Should().BeEmpty(
                "RTL2g - a second ATTACHED is not a state change, so no ATTACHED event");

            channel.State.Should().Be(ChannelState.Attached);
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/proxy/RTL3d/channels-reattach-on-reconnect-0
        [ProxyFact]
        public async Task RTL3d_EveryAttachedChannelReattachesAfterAReconnect()
        {
            var channelAName = "test-RTL3d-a-" + UtsSandbox.RandomId();
            var channelBName = "test-RTL3d-b-" + UtsSandbox.RandomId();

            var session = await ProxySession();
            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channelA = client.Channels.Get(channelAName);
            var channelB = client.Channels.Get(channelBName);

            await channelA.AttachAsync();
            await channelB.AttachAsync();

            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);

            var statesA = UtsClients.RecordChannelStates(channelA);
            var statesB = UtsClients.RecordChannelStates(channelB);

            var reattachedA = UtsClients.NextChannelState(
                channelA, ChannelState.Attached, ReconnectTimeout);
            var reattachedB = UtsClients.NextChannelState(
                channelB, ChannelState.Attached, ReconnectTimeout);

            await session.TriggerAction(new JObject { ["type"] = "close" });

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, ChannelTimeout);
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            await reattachedA;
            await reattachedB;

            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(statesA),
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue("RTL3d - channel A reattached");

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(statesB),
                ChannelState.Attaching,
                ChannelState.Attached)
                .Should().BeTrue("RTL3d - and so did channel B");

            client.Connection.State.Should().Be(ConnectionState.Connected);

            var log = await session.GetLog();
            ChannelFrames(log, AttachAction, channelAName).Count.Should().BeGreaterOrEqualTo(2);
            ChannelFrames(log, AttachAction, channelBName).Count.Should().BeGreaterOrEqualTo(2);
        }

        private static JObject SuppressToServer(string action, string channelName, string comment)
            => new JObject
            {
                ["match"] = new JObject
                {
                    ["type"] = "ws_frame_to_server",
                    ["action"] = action,
                    ["channel"] = channelName,
                },
                ["action"] = new JObject { ["type"] = "suppress" },
                ["comment"] = comment,
            };

        private static JObject ChannelError(
            string channelName,
            int code,
            int statusCode,
            string message)
            => new JObject
            {
                ["action"] = ErrorAction,
                ["channel"] = channelName,
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["statusCode"] = statusCode,
                    ["message"] = message,
                },
            };

        private static List<JObject> ChannelFrames(
            IEnumerable<JObject> log,
            int action,
            string channelName)
        {
            var frames = new List<JObject>();
            foreach (var frame in ProxyLog.FramesToServer(log, action))
            {
                if ((string)frame["message"]?["channel"] == channelName)
                {
                    frames.Add(frame);
                }
            }

            return frames;
        }

        private static void Append(List<ChannelStateChange> changes, ChannelStateChange change)
        {
            lock (changes)
            {
                changes.Add(change);
            }
        }

        private static int Count(List<ChannelStateChange> changes)
        {
            lock (changes)
            {
                return changes.Count;
            }
        }

        private static List<ChannelStateChange> Snapshot(List<ChannelStateChange> changes)
        {
            lock (changes)
            {
                return new List<ChannelStateChange>(changes);
            }
        }
    }
}
