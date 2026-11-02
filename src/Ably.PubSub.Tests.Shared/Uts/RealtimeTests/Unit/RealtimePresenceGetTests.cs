// RealtimePresence Get Tests (RTP11)
// Derived from uts/realtime/unit/presence/realtime_presence_get.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key stand-in "fake.key:secret" as the spec sets it; RTP11d tests use the fake clock (infra-driving: the spec's note describes reaching SUSPENDED through refused reconnects and advanced timers); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimePresenceGetTests : UtsUnitTestBase
    {
        public RealtimePresenceGetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static JObject Present(string clientId, string connectionId, string id, long timestamp, object data = null)
            => ProtocolMessages.PresenceEntry((int)PresenceAction.Present, clientId, connectionId, id, timestamp, data);

        // Spec handler shape: ATTACH answered with ATTACHED (optionally HAS_PRESENCE) and, when members are given, a complete SYNC.
        private static MockWebSocket AttachingMock(string channelName, bool hasPresence, JArray syncMembers, Func<JObject> connected = null)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(connected == null ? ProtocolMessages.ConnectedMessage() : connected()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(hasPresence ? ProtocolMessages.AttachedWithPresenceMessage(channelName) : ProtocolMessages.AttachedMessage(channelName));
                        if (syncMembers != null)
                        {
                            mockWs.SendToClient(ProtocolMessages.SyncMessage(channelName, "seq1:", syncMembers));
                        }
                    }
                });
            return mockWs;
        }

        // UTS: realtime/unit/RTP11a/get-returns-members-single-sync-0
        [UtsFact]
        [Trait("spec", "RTP11a")]
        public async Task RTP11a_GetReturnsMembersSingleSync()
        {
            // Setup
            var channelName = "test-RTP11a-single-" + Guid.NewGuid().ToString("N");

            // Send ATTACHED with HAS_PRESENCE but do NOT send SYNC yet
            var mockWs = AttachingMock(channelName, true, null, () => ProtocolMessages.ConnectedMessage("conn-1"));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Start get() — sync has not arrived yet, so this must wait
            var getTask = channel.Presence.GetAsync();

            // Verify the get has not resolved yet (sync still pending)
            await ProcessPendingEvents();
            getTask.IsCompleted.Should().BeFalse();

            // Now send a single-message SYNC (channelSerial with empty cursor = complete)
            mockWs.SendToClient(ProtocolMessages.SyncMessage(
                channelName,
                "seq1:",
                new JArray(
                    Present("alice", "c1", "c1:0:0", 100, "a"),
                    Present("bob", "c2", "c2:0:0", 100, "b"))));

            var members = (await UtsWait.WithTimeout(getTask, TimeSpan.FromSeconds(10), "getTask")).ToList();

            // Assertions
            members.Should().HaveCount(2);
            var clientIds = members.Select(m => m.ClientId).OrderBy(c => c, StringComparer.Ordinal).ToList();
            clientIds.Should().Equal("alice", "bob");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11a/get-waits-for-multi-sync-1
        [UtsFact]
        [Trait("spec", "RTP11a")]
        public async Task RTP11a_GetWaitsForMultiSync()
        {
            // Setup
            var channelName = "test-RTP11c1-multi-" + Guid.NewGuid().ToString("N");

            // Send ATTACHED with HAS_PRESENCE but do NOT send SYNC yet
            var mockWs = AttachingMock(channelName, true, null, () => ProtocolMessages.ConnectedMessage("conn-1"));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Start get() — sync has not arrived yet
            var getTask = channel.Presence.GetAsync();

            // Verify the get has not resolved yet
            await ProcessPendingEvents();
            getTask.IsCompleted.Should().BeFalse();

            // Send first SYNC message (non-empty cursor = more to come)
            mockWs.SendToClient(ProtocolMessages.SyncMessage(
                channelName,
                "seq1:cursor1",
                new JArray(Present("alice", "c1", "c1:0:0", 100))));

            // get() should still be waiting — sync not complete
            await ProcessPendingEvents();
            getTask.IsCompleted.Should().BeFalse();

            // Send final SYNC message (empty cursor = sync complete)
            mockWs.SendToClient(ProtocolMessages.SyncMessage(
                channelName,
                "seq1:",
                new JArray(Present("bob", "c2", "c2:0:0", 100))));

            var members = (await UtsWait.WithTimeout(getTask, TimeSpan.FromSeconds(10), "getTask")).ToList();

            // Assertions
            // Both alice (from first SYNC message) and bob (from second) are present
            members.Should().HaveCount(2);
            var clientIds = members.Select(m => m.ClientId).OrderBy(c => c, StringComparer.Ordinal).ToList();
            clientIds.Should().Equal("alice", "bob");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11c1/get-no-wait-returns-immediately-0
        [UtsFact]
        [Trait("spec", "RTP11c1")]
        public async Task RTP11c1_GetNoWaitReturnsImmediately()
        {
            // Setup
            var channelName = "test-RTP11c1-nowait-" + Guid.NewGuid().ToString("N");

            // Start SYNC but don't complete it (cursor is non-empty)
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-1")),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(
                            channelName,
                            "seq1:cursor1",
                            new JArray(Present("alice", "c1", "c1:0:0", 100))));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // NOTE: RTP11c1 only promises the members known so far, and the SYNC follows the ATTACHED in the queue, so it is drained here (process_pending_events) to make "known" mean alice
            await ProcessPendingEvents();

            // Sync is in progress but we don't wait
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { WaitForSync = false }), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            // Returns what's available so far (may be incomplete)
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11c2/get-filtered-by-clientid-0
        [UtsFact]
        [Trait("spec", "RTP11c2")]
        public async Task RTP11c2_GetFilteredByClientid()
        {
            // Setup
            var channelName = "test-RTP11c2-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(
                channelName,
                true,
                new JArray(
                    Present("alice", "c1", "c1:0:0", 100),
                    Present("bob", "c2", "c2:0:0", 100),
                    Present("alice", "c3", "c3:0:0", 100)));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { ClientId = "alice" }), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            // Only alice entries returned (from two different connections)
            members.Should().HaveCount(2);
            members.Should().NotContain(m => m.ClientId != "alice");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11c3/get-filtered-by-connectionid-0
        [UtsFact]
        [Trait("spec", "RTP11c3")]
        public async Task RTP11c3_GetFilteredByConnectionid()
        {
            // Setup
            var channelName = "test-RTP11c3-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(
                channelName,
                true,
                new JArray(
                    Present("alice", "c1", "c1:0:0", 100),
                    Present("bob", "c2", "c2:0:0", 100),
                    Present("carol", "c1", "c1:0:1", 100)));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { ConnectionId = "c1" }), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            // Only members from connection c1 (alice and carol)
            members.Should().HaveCount(2);
            members.Should().NotContain(m => m.ConnectionId != "c1");

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11b/get-implicitly-attaches-0
        [UtsFact]
        [Trait("spec", "RTP11b")]
        public async Task RTP11b_GetImplicitlyAttaches()
        {
            // Setup
            var channelName = "test-RTP11b-" + Guid.NewGuid().ToString("N");

            var mockWs = AttachingMock(channelName, false, null);
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            var members = await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { WaitForSync = false }), TimeSpan.FromSeconds(10), "GetAsync");

            // Assertions
            // NOTE: GetAsync returns once the channel is ATTACHING (waitForSync false), so the ATTACHED is awaited before asserting the state
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            channel.State.Should().Be(ChannelState.Attached);
            members.Should().NotBeNull();

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11d/get-suspended-errors-default-0
        [UtsFact]
        [Trait("spec", "RTP11d")]
        public async Task RTP11d_GetSuspendedErrorsDefault()
        {
            // Setup
            var channelName = "test-RTP11d-" + Guid.NewGuid().ToString("N");

            // The mock connectionDetails include an explicit connectionStateTtl, ClientOptions sets a small disconnectedRetryTimeout and
            // every reconnection attempt is refused, so the connection (and then the channel) reaches SUSPENDED once the fake clock passes the TTL.
            EnableFakeTimers();
            var connectionAttempts = 0;
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref connectionAttempts) == 1)
                    {
                        conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-1", connectionStateTtl: 5000));
                    }
                    else
                    {
                        conn.RespondWithRefused();
                    }
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));

                        // Deliver a member via SYNC
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(
                            channelName,
                            "seq1:",
                            new JArray(Present("alice", "c1", "c1:0:0", 100))));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false, DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500) });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Simulate channel becoming SUSPENDED (e.g., connection drops)
            mockWs.ActiveConnection.SimulateDisconnect();

            // NOTE: the spec's one-line wait for SUSPENDED needs the fake clock advanced past connectionStateTtl (see the spec's note on reaching SUSPENDED); the loop advances time until the channel is SUSPENDED
            for (var i = 0; i < 20; i++)
            {
                await AdvanceTime(1000);
                if (channel.State == ChannelState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // Default get (waitForSync=true) should error
            Func<Task> act = () => channel.Presence.GetAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            error.Should().NotBeNull();
            error.Code.Should().Be(91005);

            await CloseClient(client);
        }

        // UTS: realtime/unit/RTP11d/get-suspended-no-wait-returns-1
        [UtsFact]
        [Trait("spec", "RTP11d")]
        public async Task RTP11d_GetSuspendedNoWaitReturns()
        {
            // Setup
            var channelName = "test-RTP11d-nowait-" + Guid.NewGuid().ToString("N");

            // The mock connectionDetails include an explicit connectionStateTtl, ClientOptions sets a small disconnectedRetryTimeout and
            // every reconnection attempt is refused, so the connection (and then the channel) reaches SUSPENDED once the fake clock passes the TTL.
            EnableFakeTimers();
            var connectionAttempts = 0;
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    if (Interlocked.Increment(ref connectionAttempts) == 1)
                    {
                        conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("conn-1", connectionStateTtl: 5000));
                    }
                    else
                    {
                        conn.RespondWithRefused();
                    }
                },
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedWithPresenceMessage(channelName));

                        // Deliver a member via SYNC
                        mockWs.SendToClient(ProtocolMessages.SyncMessage(
                            channelName,
                            "seq1:",
                            new JArray(Present("alice", "c1", "c1:0:0", 100))));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "fake.key:secret", AutoConnect = false, DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(500) });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Simulate channel becoming SUSPENDED (e.g., connection drops)
            mockWs.ActiveConnection.SimulateDisconnect();

            // NOTE: the spec's one-line wait for SUSPENDED needs the fake clock advanced past connectionStateTtl (see the spec's note on reaching SUSPENDED); the loop advances time until the channel is SUSPENDED
            for (var i = 0; i < 20; i++)
            {
                await AdvanceTime(1000);
                if (channel.State == ChannelState.Suspended)
                {
                    break;
                }
            }

            await UtsWait.AwaitChannelState(channel, ChannelState.Suspended);

            // waitForSync=false returns what's in the PresenceMap
            var members = (await UtsWait.WithTimeout(channel.Presence.GetAsync(new Presence.GetParams { WaitForSync = false }), TimeSpan.FromSeconds(10), "GetAsync")).ToList();

            // Assertions
            members.Should().HaveCount(1);
            members[0].ClientId.Should().Be("alice");

            await CloseClient(client);
        }
    }
}
