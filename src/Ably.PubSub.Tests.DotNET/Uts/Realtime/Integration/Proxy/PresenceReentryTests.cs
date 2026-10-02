using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Proxy
{
    /// <summary>
    /// Derived from uts/realtime/integration/proxy/presence_reentry.md in ably/specification.
    ///
    /// Spec points: RTP17i, RTP17g
    ///
    /// <para>
    /// When a channel re-attaches without continuity the server has forgotten who was present, so
    /// the client has to enter them again from its own record. These two tests reach that moment
    /// two different ways - one injects the non-resumed ATTACHED, one provokes a real disconnect
    /// and rewrites the ATTACHED that follows - and both read the answer off the wire rather than
    /// out of the SDK.
    /// </para>
    ///
    /// <para>
    /// Between them they located D32 precisely. The injected one passes: an ATTACHED arriving on
    /// a channel that is already ATTACHED re-enters correctly. The disconnect one fails: a
    /// channel coming back from ATTACHING does not. That is the whole of the defect, and it is
    /// one line of ordering at the call site rather than anything wrong with the re-entry itself.
    /// See D32 in Uts/deviations.md.
    /// </para>
    /// </summary>
    public class PresenceReentryTests : UtsProxyTestBase
    {
        /// <summary>The PRESENCE protocol action.</summary>
        private const int PresenceAction = 14;

        /// <summary>The ATTACHED protocol action.</summary>
        private const int AttachedAction = 11;

        /// <summary>The ENTER presence action, PA2's 2.</summary>
        private const int EnterAction = 2;

        private const string MemberClientId = "client-a";

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan LogPollTimeout = TimeSpan.FromSeconds(10);

        public PresenceReentryTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTP17i/reenter-on-non-resumed-0
        //
        // This one passes, and that is the finding: the SDK re-enters correctly when the ATTACHED
        // arrives on a channel that is already ATTACHED, which is the RTL12 branch of
        // ChannelMessageProcessor. Only the reattach branch below is broken. See D32.
        [ProxyFact]
        public async Task RTP17i_ANonResumedAttachedReEntersPresenceMembers()
        {
            var channelName = "test-rtp17i-" + UtsSandbox.RandomId();

            var session = await ProxySession();
            var client = ProxyRealtimeClient(
                session,
                await JwtAuthCallback(clientId: MemberClientId));

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();
            await channel.Presence.EnterAsync("hello");

            // The log lags the wire slightly, so wait for the ENTER itself to appear before
            // counting. Reading the count too early makes the original ENTER look like a re-entry
            // and the test pass for the wrong reason - measured.
            await UtsSandbox.WallClockPollUntil(
                async () => (await PresenceFrames(session)).Count >= 1,
                "the ENTER to reach the proxy log",
                LogPollTimeout);

            var before = (await PresenceFrames(session)).Count;

            // A non-resumed ATTACHED is what tells the client the server has forgotten its
            // members. Injecting it reaches RTP17i without needing the connection to drop.
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
                async () => (await PresenceFrames(session)).Count > before,
                "RTP17i - a presence frame re-entering the member",
                LogPollTimeout);

            var frames = await PresenceFrames(session);
            frames.Count.Should().BeGreaterThan(before);

            var reentry = (JArray)frames[frames.Count - 1]["message"]["presence"];
            reentry.Should().NotBeNull();
            reentry.Count.Should().BeGreaterOrEqualTo(1);

            var member = (JObject)reentry[0];
            ((string)member["clientId"]).Should().Be(MemberClientId, "RTP17g - the stored clientId");
            ((string)member["data"]).Should().Be("hello", "RTP17g - and the stored data");
            ((int)member["action"]).Should().Be(EnterAction, "RTP17g - re-entry is an ENTER");

            channel.State.Should().Be(ChannelState.Attached);
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/proxy/RTP17i/reenter-after-disconnect-1
        //
        // DEVIATION, D32. A real disconnect, a real reattach, and an ATTACHED rewritten to say
        // continuity was lost - and no PRESENCE frame on the new transport at all. The difference
        // from the test above is only which branch of ChannelMessageProcessor handled the
        // ATTACHED. See Uts/deviations.md.
        [ProxyDeviationFact]
        public async Task RTP17i_PresenceIsReEnteredAfterARealDisconnect()
        {
            var channelName = "test-rtp17i-real-" + UtsSandbox.RandomId();

            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "delay_after_ws_connect",
                        ["delayMs"] = 3000,
                    },
                    ["action"] = new JObject { ["type"] = "close" },
                    ["times"] = 1,
                    ["comment"] = "RTP17i: close the WebSocket after 3s, leaving time to enter",
                },
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "ws_frame_to_client",
                        ["action"] = "ATTACHED",
                        ["channel"] = channelName,
                        ["count"] = 2,
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "replace",
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
                    },
                    ["times"] = 1,
                    ["comment"] = "RTP17i: make the reattach non-resumed",
                },
            });

            var client = ProxyRealtimeClient(
                session,
                await JwtAuthCallback(clientId: MemberClientId));

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();
            await channel.Presence.EnterAsync("hello");

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, LogPollTimeout);

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            await UtsClients.AwaitChannelState(channel, ChannelState.Attached, ConnectTimeout);

            await UtsSandbox.WallClockPollUntil(
                async () => (await PresenceFramesAfterSecondConnect(session)).Count > 0,
                "RTP17i - a presence frame on the new transport",
                LogPollTimeout);

            var reentries = await PresenceFramesAfterSecondConnect(session);
            reentries.Should().NotBeEmpty();

            var presence = (JArray)reentries[0]["message"]["presence"];
            presence.Should().NotBeNull();
            presence.Count.Should().BeGreaterOrEqualTo(1);

            var member = (JObject)presence[0];
            ((string)member["clientId"]).Should().Be(MemberClientId);
            ((int)member["action"]).Should().Be(EnterAction);
        }

        private static async Task<List<JObject>> PresenceFrames(UtsProxySession session)
            => ProxyLog.FramesToServer(await session.GetLog(), PresenceAction);

        /// <summary>
        /// Presence frames the client sent after the second WebSocket opened — the ones that can
        /// only be re-entries, because the first connection's are before it.
        /// </summary>
        private static async Task<List<JObject>> PresenceFramesAfterSecondConnect(
            UtsProxySession session)
        {
            var log = await session.GetLog();
            var connects = ProxyLog.WsConnects(log);

            if (connects.Count < 2)
            {
                return new List<JObject>();
            }

            var secondConnect = (string)connects[1]["timestamp"];

            return ProxyLog.FramesToServer(log, PresenceAction)
                .Where(e => string.CompareOrdinal((string)e["timestamp"], secondConnect) > 0)
                .ToList();
        }
    }
}
