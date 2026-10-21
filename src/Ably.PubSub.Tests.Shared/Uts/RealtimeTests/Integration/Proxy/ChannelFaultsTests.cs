// Channel Fault Proxy Integration Tests (RTL4f, RTL5f, RTL13a, RTL14, RTL12, RTL3d)
// Derived from uts/realtime/integration/proxy/channel_faults.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: proxy
// Unit spec: uts/realtime/unit/channels/channel_attach.md, uts/realtime/unit/channels/channel_detach.md, uts/realtime/unit/channels/channel_server_initiated_detach.md, uts/realtime/unit/channels/channel_error.md
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
using Ably.PubSub.Tests.Uts.Infra.Integration.Proxy;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Integration.Proxy
{
    /// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
    [Collection(UtsProxyCollection.Name)]
    [Trait("uts", "proxy")]
    [Trait("type", "integration")]
    public class ChannelFaultsTests : UtsProxyTestBase
    {
        private const int TestTimeoutMs = 60000;

        public ChannelFaultsTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // The spec's authCallback: generate_jwt(keyName: key_name, keySecret: key_secret)
        // NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails
        private Func<TokenParams, Task<object>> JwtAuthCallback()
        {
            var keyName = ExtractKeyName(ApiKey);
            var keySecret = ExtractKeySecret(ApiKey);
            return tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(keyName, keySecret)));
        }

        // UTS: realtime/proxy/RTL4f/attach-timeout-suppressed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL4f")]
        public async Task RTL4f_AttachTimeoutSuppressed()
        {
            // Setup
            var channelName = "test-RTL4f-" + RandomId();

            // Create proxy session that suppresses ATTACH messages for our channel
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToServer(ProtocolMessages.Attach, channelName),
                    ProxyRules.Suppress(),
                    comment: "RTL4f: Suppress ATTACH so server never responds"),
            });

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(3000),
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Record channel state changes for sequence verification
            var channelStateChanges = UtsRecord.ChannelStates(channel);

            // Connect through proxy -- connection itself is not faulted
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Start attach -- proxy will suppress the ATTACH, so server never responds
            var attachFuture = channel.AttachAsync();

            // Channel should enter ATTACHING immediately
            await AwaitChannelState(channel, ChannelState.Attaching, TimeSpan.FromSeconds(5));

            // Wait for the channel to transition to SUSPENDED after realtimeRequestTimeout
            await AwaitChannelState(channel, ChannelState.Suspended, TimeSpan.FromSeconds(15));

            // The attach() call should have failed with a timeout error
            var attachResult = await attachFuture;
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Assertions
            // Channel transitioned to SUSPENDED
            channel.State.Should().Be(ChannelState.Suspended);

            // Error indicates timeout
            error.Should().NotBeNull();

            // State sequence: ATTACHING -> SUSPENDED
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot(), new[] { ChannelState.Attaching, ChannelState.Suspended });

            // Connection remains CONNECTED (attach timeout is channel-scoped)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Proxy log confirms the ATTACH frames were received but suppressed
            // Note: The proxy logs frames before applying rules, so suppressed frames still
            // appear in the log with `ruleMatched` set.
            var log = await session.GetLogAsync();
            var attachFrames = log.FramesToServer(ProtocolMessages.Attach).Where(e => (string)e.Message["channel"] == channelName).ToList();
            attachFrames.Count.Should().BeGreaterThanOrEqualTo(1);

            // All ATTACH frames were caught by the suppress rule
            foreach (var frame in attachFrames)
            {
                frame.RuleMatched.Should().NotBeNull();
            }

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTL14/error-on-attach-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL14")]
        public async Task RTL14_ErrorOnAttach()
        {
            // Setup
            var channelName = "test-RTL14-error-on-attach-" + RandomId();

            // Create proxy session that replaces ATTACHED with channel ERROR
            var session = await CreateProxySession(rules: new[]
            {
                ProxyRules.Rule(
                    ProxyRules.WsFrameToClient(ProtocolMessages.Attached, channelName),
                    ProxyRules.Replace(new JObject
                    {
                        ["action"] = ProtocolMessages.Error,
                        ["channel"] = channelName,
                        ["error"] = new JObject { ["code"] = 40160, ["statusCode"] = 403, ["message"] = "Not permitted" },
                    }),
                    times: 1,
                    comment: "RTL14: Replace ATTACHED with channel ERROR"),
            });

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Record channel state changes for sequence verification
            var channelStateChanges = UtsRecord.ChannelStates(channel);

            // Connect through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            // Attach -- proxy replaces ATTACHED with ERROR
            var attachResult = await channel.AttachAsync();
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Channel should be in FAILED state
            await AwaitChannelState(channel, ChannelState.Failed, TimeSpan.FromSeconds(10));

            // Assertions
            // Channel transitioned to FAILED
            channel.State.Should().Be(ChannelState.Failed);

            // Error reason matches the injected error
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            ((int?)channel.ErrorReason.StatusCode).Should().Be(403);

            // The error returned from attach() matches
            error.Should().NotBeNull();
            error.Code.Should().Be(40160);

            // State sequence: ATTACHING -> FAILED
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot(), new[] { ChannelState.Attaching, ChannelState.Failed });

            // Connection remains CONNECTED (channel error does not affect connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTL5f/detach-timeout-suppressed-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL5f")]
        public async Task RTL5f_DetachTimeoutSuppressed()
        {
            // Setup
            var channelName = "test-RTL5f-" + RandomId();

            // Phase 1: Create proxy session with NO fault rules (clean passthrough)
            var session = await CreateProxySession();

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(3000),
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Record channel state changes for sequence verification
            var channelStateChanges = UtsRecord.ChannelStates(channel);

            // Phase 1: Connect and attach normally through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            channel.State.Should().Be(ChannelState.Attached);

            // Clear state change history from the attach phase
            channelStateChanges.Clear();

            // Phase 2: Add rule to suppress DETACH messages
            await session.AddRulesAsync(
                new[]
                {
                    ProxyRules.Rule(
                        ProxyRules.WsFrameToServer(ProtocolMessages.Detach, channelName),
                        ProxyRules.Suppress(),
                        comment: "RTL5f: Suppress DETACH so server never responds"),
                },
                ProxyRules.Prepend);

            // Phase 3: Try to detach -- proxy suppresses DETACH, so server never sends DETACHED
            var detachFuture = channel.DetachAsync();

            // Channel should enter DETACHING
            await AwaitChannelState(channel, ChannelState.Detaching, TimeSpan.FromSeconds(5));

            // Wait for the channel to revert to ATTACHED after realtimeRequestTimeout
            await AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(15));

            // The detach() call should have failed with a timeout error
            var detachResult = await detachFuture;
            detachResult.IsFailure.Should().BeTrue();
            var error = detachResult.Error;

            // Assertions
            // Channel reverted to ATTACHED (previous state)
            channel.State.Should().Be(ChannelState.Attached);

            // Error indicates timeout
            error.Should().NotBeNull();

            // State sequence: DETACHING -> ATTACHED (revert)
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot(), new[] { ChannelState.Detaching, ChannelState.Attached });

            // Connection remains CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTL13a/unsolicited-detach-reattach-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL13a")]
        public async Task RTL13a_UnsolicitedDetachReattach()
        {
            // Setup
            var channelName = "test-RTL13a-" + RandomId();

            // Create proxy session with clean passthrough (no fault rules initially)
            var session = await CreateProxySession();

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Connect and attach normally through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            channel.State.Should().Be(ChannelState.Attached);

            // Record channel state changes from this point
            var channelStateChanges = UtsRecord.ChannelStates(channel);

            // Inject an unsolicited DETACHED message with error via imperative action
            await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject
            {
                ["action"] = ProtocolMessages.Detached,
                ["channel"] = channelName,
                ["error"] = new JObject { ["code"] = 90198, ["statusCode"] = 500, ["message"] = "Channel detached by server" },
            }));

            // Channel should transition ATTACHING (reattach) -> ATTACHED (reattach succeeds)
            // NOTE: AWAIT_STATE would return at once (already ATTACHED); the record is polled until the reattach cycle completes
            await UtsWait.PollUntil(
                () => channelStateChanges.Snapshot().Any(s => s == ChannelState.Attaching) && channelStateChanges.Snapshot().Any(s => s == ChannelState.Attached),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(500),
                description: "channel re-entered ATTACHING and ATTACHED");
            await AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(15));

            // Assertions
            // Channel re-attached successfully
            channel.State.Should().Be(ChannelState.Attached);

            // State sequence: ATTACHING (with error from DETACHED) -> ATTACHED
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot(), new[] { ChannelState.Attaching, ChannelState.Attached });

            // Connection remains CONNECTED throughout
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Proxy log shows the re-attach ATTACH message from the client
            var log = await session.GetLogAsync();
            var attachFrames = log.FramesToServer(ProtocolMessages.Attach).Where(e => (string)e.Message["channel"] == channelName).ToList();

            // At least 2 ATTACH frames: initial attach + reattach after injected DETACHED
            attachFrames.Count.Should().BeGreaterThanOrEqualTo(2);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTL14/channel-error-goes-failed-1
        // DEVIATION: see deviations.md
        [DeviationFact("RTL14", Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL14")]
        public async Task RTL14_ChannelErrorGoesFailed()
        {
            // Setup
            var channelName = "test-RTL14-" + RandomId();

            // Create proxy session with clean passthrough
            var session = await CreateProxySession();

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Connect and attach normally through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            channel.State.Should().Be(ChannelState.Attached);

            // Record channel state changes from this point
            var channelStateChanges = UtsRecord.ChannelStates(channel);

            // Inject a channel-scoped ERROR message via imperative action
            await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject
            {
                ["action"] = ProtocolMessages.Error,
                ["channel"] = channelName,
                ["error"] = new JObject { ["code"] = 40160, ["statusCode"] = 403, ["message"] = "Not permitted" },
            }));

            // Channel should transition to FAILED
            await AwaitChannelState(channel, ChannelState.Failed, TimeSpan.FromSeconds(10));

            // Assertions
            // Channel transitioned to FAILED
            channel.State.Should().Be(ChannelState.Failed);

            // errorReason is set from the injected ERROR
            channel.ErrorReason.Should().NotBeNull();
            channel.ErrorReason.Code.Should().Be(40160);
            ((int?)channel.ErrorReason.StatusCode).Should().Be(403);
            channel.ErrorReason.Message.Should().Contain("Not permitted");

            // State change event shows ATTACHED -> FAILED
            UtsAssert.ContainsInOrder(channelStateChanges.Snapshot(), new[] { ChannelState.Failed });
            channelStateChanges.Count.Should().Be(1);

            // Connection remains CONNECTED (channel-scoped ERROR does not close connection)
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTL12/attached-non-resumed-update-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL12")]
        public async Task RTL12_AttachedNonResumedUpdate()
        {
            // Setup
            var channelName = "test-RTL12-" + RandomId();

            // Create proxy session with clean passthrough
            var session = await CreateProxySession();

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
            });

            var channel = client.Channels.Get(channelName);

            // Test Steps
            // Connect and attach normally through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channel.AttachAsync(), "channel.attach()");
            channel.State.Should().Be(ChannelState.Attached);

            // Listen for both 'update' and 'attached' events
            var updateEvents = new CaptureList<ChannelStateChange>();
            var attachedEvents = new CaptureList<ChannelStateChange>();
            channel.On(ChannelEvent.Update, change => updateEvents.Add(change));
            channel.On(ChannelEvent.Attached, change => attachedEvents.Add(change));

            // Inject an ATTACHED message with resumed=false and an error via imperative action
            await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject
            {
                ["action"] = ProtocolMessages.Attached,
                ["channel"] = channelName,
                ["flags"] = 0,
                ["error"] = new JObject { ["code"] = 91001, ["statusCode"] = 500, ["message"] = "Continuity lost" },
            }));

            // Wait for the update event to be emitted
            await UtsWait.PollUntil(
                () => updateEvents.Count >= 1,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "an update event was emitted");

            // Assertions
            // Channel emitted an UPDATE event
            updateEvents.Count.Should().Be(1);

            // The ChannelStateChange has correct fields
            updateEvents[0].Current.Should().Be(ChannelState.Attached);
            updateEvents[0].Previous.Should().Be(ChannelState.Attached);
            updateEvents[0].Resumed.Should().BeFalse();
            updateEvents[0].Error.Should().NotBeNull();
            updateEvents[0].Error.Code.Should().Be(91001);
            ((int?)updateEvents[0].Error.StatusCode).Should().Be(500);
            updateEvents[0].Error.Message.Should().Contain("Continuity lost");

            // No 'attached' event was emitted (RTL2g)
            attachedEvents.Count.Should().Be(0);

            // Channel state remains ATTACHED
            channel.State.Should().Be(ChannelState.Attached);

            // Connection remains CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }

        // UTS: realtime/proxy/RTL3d/channels-reattach-on-reconnect-0
        [Fact(Timeout = TestTimeoutMs)]
        [Trait("spec", "RTL3d")]
        public async Task RTL3d_ChannelsReattachOnReconnect()
        {
            // Setup
            var channelAName = "test-RTL3d-a-" + RandomId();
            var channelBName = "test-RTL3d-b-" + RandomId();

            // Create proxy session with clean passthrough
            var session = await CreateProxySession();

            var client = ProxyRealtime(session, new ClientOptions
            {
                AuthCallback = JwtAuthCallback(),
                AutoConnect = false,
            });

            var channelA = client.Channels.Get(channelAName);
            var channelB = client.Channels.Get(channelBName);

            // Test Steps
            // Connect and attach both channels normally through proxy
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(15));

            AssertSucceeded(await channelA.AttachAsync(), "channel_a.attach()");
            AssertSucceeded(await channelB.AttachAsync(), "channel_b.attach()");
            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);

            // Record channel state changes from this point
            var channelAStateChanges = UtsRecord.ChannelStates(channelA);
            var channelBStateChanges = UtsRecord.ChannelStates(channelB);

            // Record connection states so the DISCONNECTED -> CONNECTED cycle is observable
            var connectionStates = UtsRecord.ConnectionStates(client.Connection);

            // Trigger disconnect via imperative action (close the WebSocket)
            await session.TriggerActionAsync(ProxyRules.Close());

            // Wait for connection to reach DISCONNECTED
            // NOTE: the DISCONNECTED state may be left before a wait could see it, so the recorded states are polled
            await UtsWait.PollUntil(
                () => connectionStates.Snapshot().Any(s => s == ConnectionState.Disconnected),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500),
                description: "connection reached DISCONNECTED");

            // Wait for connection to recover to CONNECTED
            await AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(30));

            // Wait for both channels to re-attach
            await AwaitChannelState(channelA, ChannelState.Attached, TimeSpan.FromSeconds(15));
            await AwaitChannelState(channelB, ChannelState.Attached, TimeSpan.FromSeconds(15));

            // Assertions
            // Both channels end in ATTACHED state
            channelA.State.Should().Be(ChannelState.Attached);
            channelB.State.Should().Be(ChannelState.Attached);

            // Both channels transitioned through ATTACHING -> ATTACHED after reconnection
            UtsAssert.ContainsInOrder(channelAStateChanges.Snapshot(), new[] { ChannelState.Attaching, ChannelState.Attached });
            UtsAssert.ContainsInOrder(channelBStateChanges.Snapshot(), new[] { ChannelState.Attaching, ChannelState.Attached });

            // Connection is CONNECTED
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Proxy log shows ATTACH messages for both channels on the second WS connection
            var log = await session.GetLogAsync();
            var attachFramesA = log.FramesToServer(ProtocolMessages.Attach).Where(e => (string)e.Message["channel"] == channelAName).ToList();
            var attachFramesB = log.FramesToServer(ProtocolMessages.Attach).Where(e => (string)e.Message["channel"] == channelBName).ToList();

            // At least 2 ATTACH frames each: initial attach + reattach after reconnection
            attachFramesA.Count.Should().BeGreaterThanOrEqualTo(2);
            attachFramesB.Count.Should().BeGreaterThanOrEqualTo(2);

            // AFTER EACH TEST: the client and the session are closed by the base teardown
        }
    }
}
