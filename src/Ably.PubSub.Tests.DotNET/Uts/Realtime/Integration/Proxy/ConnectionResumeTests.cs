using System;
using System.Collections.Generic;
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
    /// Derived from uts/realtime/integration/proxy/connection_resume.md in ably/specification.
    ///
    /// Spec points: RTN14h, RTN15a, RTN15b, RTN15c6, RTN15c7, RTN15h1, RTN15h3, RTN15j, RTN16d,
    /// RTN16k, RTN16l, RTN19a, RTN19a2
    ///
    /// <para>
    /// Resume is the SDK's answer to a transport that disappears: open another one, present the
    /// old connection key, and let the server say whether the connection continues or a new one
    /// has begun. These tests cover both answers, the cases where the client must not even try
    /// (RTN15h1, RTN15j), the long way round through SUSPENDED (RTN14h), and the explicit
    /// recovery of a connection from a key carried over from another client (RTN16).
    /// </para>
    ///
    /// <para>
    /// The spec prefers temporal rules - <c>delay_after_ws_connect</c> then <c>close</c> - to an
    /// imperative disconnect, because the proxy fires them at a known point in the lifecycle
    /// rather than racing the SDK. That preference is kept here.
    /// </para>
    ///
    /// <para>
    /// Ten of the file's eleven tests are here. <c>RTN14h/resume-after-ttl-expiry-0</c> is not:
    /// it needs the client held in DISCONNECTED while a shortened <c>connectionStateTtl</c> runs
    /// out, and neither half of that can be arranged with the proxy build available. See M3 in
    /// Uts/deviations.md.
    /// </para>
    /// </summary>
    public class ConnectionResumeTests : UtsProxyTestBase
    {
        private const int ConnectedAction = 4;
        private const int DisconnectedAction = 6;
        private const int ErrorAction = 9;
        private const int MessageAction = 15;
        private const int AckAction = 1;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ReconnectTimeout = TimeSpan.FromSeconds(30);

        public ConnectionResumeTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/proxy/RTN15a/disconnect-triggers-resume-0
        [ProxyFact]
        public async Task RTN15a_AnUnexpectedCloseFrameTriggersAResume()
            => await AnUnexpectedDisconnectTriggersAResume("close");

        // UTS: realtime/proxy/RTN15a/tcp-close-triggers-resume-1
        //
        // The same test with the TCP connection dropped and no close frame at all, which is the
        // harder of the two for a client to notice.
        [ProxyFact]
        public async Task RTN15a_ATcpCloseWithNoFrameTriggersAResume()
            => await AnUnexpectedDisconnectTriggersAResume("disconnect");

        // UTS: realtime/proxy/RTN15b/resume-preserves-connid-0
        [ProxyFact]
        public async Task RTN15b_ASuccessfulResumeKeepsTheConnectionId()
        {
            var session = await ProxySession(new JArray
            {
                CloseAfter(1000, "close", "RTN15b: close after 1s to provoke a resume"),
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var originalId = client.Connection.Id;
            var originalKey = client.Connection.Key;

            originalId.Should().NotBeNullOrEmpty();
            originalKey.Should().NotBeNullOrEmpty();

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, StateTimeout);
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            client.Connection.Id.Should().Be(
                originalId,
                "RTN15c6 - the same connectionId back means the connection continued");

            var connects = ProxyLog.WsConnects(await session.GetLog());
            connects.Count.Should().BeGreaterOrEqualTo(2);
            ResumeParam(connects[1]).Should().Be(
                originalKey,
                "RTN15b - the resume carries the key of the connection being resumed");

            client.Connection.ErrorReason.Should().BeNull("nothing went wrong");
        }

        // UTS: realtime/proxy/RTN15c7/failed-resume-new-connid-0
        [ProxyFact]
        public async Task RTN15c7_AFailedResumeIsANewConnection()
        {
            var session = await ProxySession(new JArray
            {
                CloseAfter(1000, "close", "RTN15c7: close after 1s to provoke a resume"),
                ReplaceConnected(
                    2,
                    "proxy-injected-new-id",
                    "proxy-injected-new-key",
                    new JObject
                    {
                        ["code"] = 80008,
                        ["statusCode"] = 400,
                        ["message"] = "Unable to recover connection",
                    },
                    "RTN15c7: answer the resume with a different connection"),
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var originalId = client.Connection.Id;
            originalId.Should().NotBeNullOrEmpty();
            originalId.Should().NotBe("proxy-injected-new-id");

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, StateTimeout);
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            client.Connection.Id.Should().Be("proxy-injected-new-id");
            client.Connection.Key.Should().Be("proxy-injected-new-key");

            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80008);

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "RTN15c7 - a failed resume is still a connection, not a failure");

            var connects = ProxyLog.WsConnects(await session.GetLog());
            connects.Count.Should().BeGreaterOrEqualTo(2);
            ResumeParam(connects[1]).Should().NotBeNull("the resume was attempted");
        }

        // UTS: realtime/proxy/RTN15h1/token-error-nonrenewable-failed-0
        [ProxyFact]
        public async Task RTN15h1_ATokenErrorWithNothingToRenewWithFails()
        {
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "delay_after_ws_connect",
                        ["delayMs"] = 1000,
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "inject_to_client_and_close",
                        ["message"] = new JObject
                        {
                            ["action"] = DisconnectedAction,
                            ["error"] = new JObject
                            {
                                ["code"] = 40142,
                                ["statusCode"] = 401,
                                ["message"] = "Token expired",
                            },
                        },
                    },
                    ["times"] = 1,
                    ["comment"] = "RTN15h1: a token error on a connection that cannot renew",
                },
            });

            // A real token, taken directly from the sandbox, and then no way to get another: no
            // key, no callback. That is what makes it non-renewable.
            var sandbox = await Sandbox();
            var direct = new PubSubHttpClient(new ClientOptions
            {
                Key = sandbox.KeyStr,
                Environment = "sandbox",
                Tls = true,
            });

            var token = await direct.Auth.RequestTokenAsync();

            var client = Track(new PubSubRealtimeClient(new ClientOptions
            {
                RestHost = session.ProxyHost,
                RealtimeHost = session.ProxyHost,
                Port = session.ProxyPort,
                TlsPort = session.ProxyPort,
                Tls = false,
                AutoConnect = false,
                Token = token.Token,
            }));

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var states = UtsClients.RecordConnectionStates(client.Connection);

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, StateTimeout);

            client.Connection.State.Should().Be(ConnectionState.Failed);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(
                40171,
                "RTN15h1 - the reported error is 'not renewable', not the token error that "
                + "prompted it");
            client.Connection.ErrorReason.StatusCode.Should().Be(
                System.Net.HttpStatusCode.Unauthorized);

            UtsClients.Snapshot(states).Should().Contain(ConnectionState.Failed);
        }

        // UTS: realtime/proxy/RTN15h3/non-token-error-reconnects-0
        [ProxyFact]
        public async Task RTN15h3_ANonTokenErrorReconnectsRatherThanFailing()
        {
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "delay_after_ws_connect",
                        ["delayMs"] = 1000,
                    },
                    ["action"] = new JObject
                    {
                        ["type"] = "inject_to_client_and_close",
                        ["message"] = new JObject
                        {
                            ["action"] = DisconnectedAction,
                            ["error"] = new JObject
                            {
                                ["code"] = 80003,
                                ["statusCode"] = 500,
                                ["message"] = "Service temporarily unavailable",
                            },
                        },
                    },
                    ["times"] = 1,
                    ["comment"] = "RTN15h3: a DISCONNECTED that is not about the token",
                },
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var states = UtsClients.RecordConnectionStates(client.Connection);

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, StateTimeout);
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            client.Connection.State.Should().Be(ConnectionState.Connected);

            UtsClients.ContainsInOrder(
                UtsClients.Snapshot(states),
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue("RTN15h3 - it reconnects instead of giving up");

            var connects = ProxyLog.WsConnects(await session.GetLog());
            connects.Count.Should().BeGreaterOrEqualTo(2);
            ResumeParam(connects[1]).Should().NotBeNull("and resumes while it is at it");

            client.Connection.ErrorReason.Should().BeNull("the reconnection succeeded");
        }

        // UTS: realtime/proxy/RTN15j/fatal-error-established-conn-0
        [ProxyFact]
        public async Task RTN15j_AFatalErrorOnALiveConnectionFailsItAndEveryChannel()
        {
            var session = await ProxySession();
            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channelA = client.Channels.Get("fatal-error-a-" + UtsSandbox.RandomId());
            var channelB = client.Channels.Get("fatal-error-b-" + UtsSandbox.RandomId());

            await Task.WhenAll(channelA.AttachAsync(), channelB.AttachAsync());

            var connectionStates = UtsClients.RecordConnectionStates(client.Connection);
            var statesA = UtsClients.RecordChannelStates(channelA);
            var statesB = UtsClients.RecordChannelStates(channelB);

            await session.TriggerAction(new JObject
            {
                ["type"] = "inject_to_client",
                ["message"] = new JObject
                {
                    ["action"] = ErrorAction,
                    ["error"] = new JObject
                    {
                        ["code"] = 50000,
                        ["statusCode"] = 500,
                        ["message"] = "Internal server error",
                    },
                },
            });

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Failed, StateTimeout);

            client.Connection.State.Should().Be(ConnectionState.Failed);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(50000);
            client.Connection.ErrorReason.StatusCode.Should().Be(
                System.Net.HttpStatusCode.InternalServerError);

            await UtsClients.AwaitChannelState(channelA, ChannelState.Failed, StateTimeout);
            await UtsClients.AwaitChannelState(channelB, ChannelState.Failed, StateTimeout);

            channelA.ErrorReason.Should().NotBeNull();
            channelA.ErrorReason.Code.Should().Be(50000);
            channelB.ErrorReason.Should().NotBeNull();
            channelB.ErrorReason.Code.Should().Be(50000);

            UtsClients.Snapshot(connectionStates).Should().Contain(ConnectionState.Failed);
            UtsClients.Snapshot(statesA).Should().Contain(ChannelState.Failed);
            UtsClients.Snapshot(statesB).Should().Contain(ChannelState.Failed);

            ProxyLog.WsConnects(await session.GetLog()).Should().HaveCount(
                1,
                "RTN15j - a fatal error is not retried, so no second transport was opened");
        }

        // UTS: realtime/proxy/RTN19a/unacked-resent-on-resume-0
        [ProxyFact]
        public async Task RTN19a_AnUnackedMessageIsResentOnTheResumedTransport()
        {
            var session = await ProxySession(new JArray
            {
                new JObject
                {
                    ["match"] = new JObject
                    {
                        ["type"] = "ws_frame_to_client",
                        ["action"] = "ACK",
                    },
                    ["action"] = new JObject { ["type"] = "suppress" },
                    ["times"] = 1,
                    ["comment"] = "RTN19a: swallow the first ACK so a publish stays pending",
                },
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            var channel = client.Channels.Get("test-resend-unacked-" + UtsSandbox.RandomId());
            await channel.AttachAsync();

            var publishing = channel.PublishAsync("event", "test-data");

            // Waiting for both halves - the message out, the ACK swallowed - is what makes the
            // disconnect land at the right moment without a sleep.
            await UtsSandbox.WallClockPollUntil(
                async () =>
                {
                    var log = await session.GetLog();
                    var sent = ProxyLog.FramesToServer(log, MessageAction).Count > 0;
                    var swallowed = ProxyLog.FramesToClient(log, AckAction)
                        .Exists(frame => ProxyLog.RuleMatched(frame) != null);

                    return sent && swallowed;
                },
                "the publish to be sent and its ACK suppressed");

            await session.TriggerAction(new JObject { ["type"] = "close" });

            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            var result = await publishing;
            result.IsSuccess.Should().BeTrue(
                "RTN19a - the resent message was acknowledged on the new transport");

            var finalLog = await session.GetLog();
            var connects = ProxyLog.WsConnects(finalLog);

            connects.Count.Should().BeGreaterOrEqualTo(2);
            ResumeParam(connects[1]).Should().NotBeNull();

            var messages = ProxyLog.FramesToServer(finalLog, MessageAction);
            messages.Count.Should().BeGreaterOrEqualTo(
                2,
                "RTN19a - the message went out on both transports");

            ((long)messages[0]["message"]["msgSerial"]).Should().Be(
                (long)messages[1]["message"]["msgSerial"],
                "RTN19a2 - a resumed connection keeps the serials it already issued");
        }

        // UTS: realtime/proxy/RTN16d/recovery-preserves-connid-0
        [ProxyFact]
        public async Task RTN16d_ARecoveredConnectionKeepsItsIdAndTakesANewKey()
        {
            var firstSession = await ProxySession();
            var firstClient = ProxyRealtimeClient(firstSession, await JwtAuthCallback());

            firstClient.Connect();
            await UtsClients.AwaitConnectionState(
                firstClient.Connection, ConnectionState.Connected, ConnectTimeout);

            var originalId = firstClient.Connection.Id;
            var originalKey = firstClient.Connection.Key;
            originalId.Should().NotBeNullOrEmpty();

            var channel = firstClient.Channels.Get("recovery-test-" + UtsSandbox.RandomId());
            await channel.AttachAsync();

            var recoveryKey = firstClient.Connection.CreateRecoveryKey();
            recoveryKey.Should().NotBeNullOrEmpty();

            // The transport is killed rather than closed, so the server keeps the connection
            // state alive for the second client to claim.
            //
            // DISCONNECTED is recorded rather than awaited. A killed transport earns an immediate
            // reconnect, so the client can be back in CONNECTING - or CONNECTED - before an awaiter
            // registers, and waiting for it to be the current state then times out on a drop that
            // did happen.
            var firstStates = UtsClients.RecordConnectionStates(firstClient.Connection);

            await firstSession.TriggerAction(new JObject { ["type"] = "close" });
            await UtsClients.PollUntil(
                () => UtsClients.Snapshot(firstStates).Contains(ConnectionState.Disconnected),
                "the first connection to be dropped",
                StateTimeout);

            firstClient.Close();

            // Closing a client whose transport has already been killed has no server to answer
            // it, so CLOSED can take as long as the close request's own deadline. Nothing after
            // this depends on it - the recovery key is already in hand and the first client is
            // not to be used again - so a slow close is waited for but not insisted on.
            try
            {
                await UtsClients.AwaitConnectionState(
                    firstClient.Connection, ConnectionState.Closed, StateTimeout);
            }
            catch (TimeoutException)
            {
                Output.WriteLine(
                    "first client did not reach CLOSED in time; state is "
                    + firstClient.Connection.State);
            }

            // A second session, so the recover parameter can be read out of its own log.
            var secondSession = await ProxySession();
            var secondClient = ProxyRealtimeClient(
                secondSession,
                await JwtAuthCallback(),
                options => options.Recover = recoveryKey);

            secondClient.Connect();
            await UtsClients.AwaitConnectionState(
                secondClient.Connection, ConnectionState.Connected, ConnectTimeout);

            secondClient.Connection.Id.Should().Be(
                originalId,
                "RTN16d - recovering means continuing the same connection");

            secondClient.Connection.Key.Should().NotBeNullOrEmpty();
            secondClient.Connection.Key.Should().NotBe(
                originalKey,
                "RTN16d - with a new key from the CONNECTED that recovered it");

            var connects = ProxyLog.WsConnects(await secondSession.GetLog());
            connects.Should().NotBeEmpty();

            RecoverParam(connects[0]).Should().Be(
                originalKey,
                "RTN16k - the recover parameter is the connectionKey from the recovery key");

            ResumeParam(connects[0]).Should().BeNull("this is a recovery, not a resume");

            secondClient.Connection.ErrorReason.Should().BeNull();
        }

        // UTS: realtime/proxy/RTN16l/recovery-failure-fresh-conn-0
        [ProxyFact]
        public async Task RTN16l_AFailedRecoveryIsAFreshConnection()
        {
            var session = await ProxySession(new JArray
            {
                ReplaceConnected(
                    1,
                    "recovery-failed-new-id",
                    "recovery-failed-new-key",
                    new JObject
                    {
                        ["code"] = 80008,
                        ["statusCode"] = 400,
                        ["message"] = "Unable to recover connection",
                    },
                    "RTN16l: refuse the recovery and hand back a new connection"),
            });

            // The key does not need to be real: the proxy answers before the server would.
            var fabricated = new JObject
            {
                ["connectionKey"] = "stale-old-key",
                ["msgSerial"] = 99,
                ["channelSerials"] = new JObject { ["old-channel"] = "old-serial" },
            }.ToString(Newtonsoft.Json.Formatting.None);

            var client = ProxyRealtimeClient(
                session,
                await JwtAuthCallback(),
                options => options.Recover = fabricated);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);

            client.Connection.Id.Should().Be("recovery-failed-new-id");
            client.Connection.Key.Should().Be("recovery-failed-new-key");

            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(80008);

            client.Connection.State.Should().Be(
                ConnectionState.Connected,
                "RTN16l - a failed recovery behaves like a failed resume, not like a failure");

            var connects = ProxyLog.WsConnects(await session.GetLog());
            connects.Should().NotBeEmpty();
            RecoverParam(connects[0]).Should().Be("stale-old-key");
        }

        private static string ResumeParam(JObject wsConnect)
            => (string)wsConnect["queryParams"]?["resume"];

        private static string RecoverParam(JObject wsConnect)
            => (string)wsConnect["queryParams"]?["recover"];

        private static JObject CloseAfter(int delayMs, string action, string comment)
            => new JObject
            {
                ["match"] = new JObject
                {
                    ["type"] = "delay_after_ws_connect",
                    ["delayMs"] = delayMs,
                },
                ["action"] = new JObject { ["type"] = action },
                ["times"] = 1,
                ["comment"] = comment,
            };

        /// <summary>
        /// The specs' full CONNECTED replacement. <c>connectionDetails</c> has to be complete or
        /// the SDK reads defaults it was not given.
        /// </summary>
        private static JObject ReplaceConnected(
            int count,
            string connectionId,
            string connectionKey,
            JObject error,
            string comment,
            int connectionStateTtlMs = 120000)
        {
            var message = new JObject
            {
                ["action"] = ConnectedAction,
                ["connectionId"] = connectionId,
                ["connectionKey"] = connectionKey,
                ["connectionDetails"] = new JObject
                {
                    ["connectionKey"] = connectionKey,
                    ["clientId"] = null,
                    ["maxMessageSize"] = 65536,
                    ["maxInboundRate"] = 250,
                    ["maxOutboundRate"] = 100,
                    ["maxFrameSize"] = 524288,
                    ["serverId"] = "test-server",
                    ["connectionStateTtl"] = connectionStateTtlMs,
                    ["maxIdleInterval"] = 15000,
                },
            };

            if (error != null)
            {
                message["error"] = error;
            }

            return new JObject
            {
                ["match"] = new JObject
                {
                    ["type"] = "ws_frame_to_client",
                    ["action"] = "CONNECTED",
                    ["count"] = count,
                },
                ["action"] = new JObject
                {
                    ["type"] = "replace",
                    ["message"] = message,
                },
                ["times"] = 1,
                ["comment"] = comment,
            };
        }

        private async Task AnUnexpectedDisconnectTriggersAResume(string closeAction)
        {
            var session = await ProxySession(new JArray
            {
                CloseAfter(
                    1000,
                    closeAction,
                    "RTN15a: " + closeAction + " after 1s to provoke an unexpected disconnect"),
            });

            var client = ProxyRealtimeClient(session, await JwtAuthCallback());
            var states = UtsClients.RecordConnectionStates(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ConnectTimeout);
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Disconnected, StateTimeout);
            await UtsClients.AwaitConnectionState(
                client.Connection, ConnectionState.Connected, ReconnectTimeout);

            var observed = UtsClients.Snapshot(states);

            observed.Should().Contain(ConnectionState.Disconnected);
            UtsClients.ContainsInOrder(
                observed,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected)
                .Should().BeTrue("RTN15a - the disconnect was noticed and answered");

            var connects = ProxyLog.WsConnects(await session.GetLog());
            connects.Count.Should().BeGreaterOrEqualTo(2);
            ResumeParam(connects[1]).Should().NotBeNull(
                "RTN15a - the second transport asks to resume the first");
        }
    }
}
