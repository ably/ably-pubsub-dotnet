using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Transport;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime.ConnectionSpecs
{
    [Trait("spec", "RTN17")]
    public class ConnectionFallbackSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN17b")]
        public async Task WithCustomHostAndError_ConnectionGoesStraightToFailedInsteadOfDisconnected()
        {
            var client = await GetConnectedClient(opts => opts.RealtimeHost = "test.com");

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Failed);
        }

        [Fact]
        [Trait("spec", "RTN17b")]
        public async Task WithCustomPortAndError_ConnectionGoesStraightToFailedInsteadOfDisconnected()
        {
            var client = await GetConnectedClient(opts => opts.Port = 100);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Failed);
        }

        [Fact]
        [Trait("spec", "RTN17b")]
        public async Task WithCustomEnvironmentAndError_ConnectionGoesStraightToFailedInsteadOfDisconnected()
        {
            var client = await GetConnectedClient(opts => opts.Environment = "sandbox");

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Failed);
        }

        [Fact]
        [Trait("spec", "RTN17a")]
        public async Task WhenPreviousAttemptFailed_ShouldGoToDefaultHostFirst()
        {
            var client = GetClientWithFakeTransport();

            var realtimeHosts = new List<string>();
            FakeTransportFactory.InitialiseFakeTransport = t => realtimeHosts.Add(t.Parameters.Host);

            await client.WaitForState(ConnectionState.Connecting);
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            // We should go through the states - Disconnected and then Connecting with a new RealtimeHost
            await client.WaitForState(ConnectionState.Disconnected);
            await client.WaitForState(ConnectionState.Connecting);
            // We want to wait until the Connecting command is completely finished as the event
            // is triggered during the command firing
            await client.ProcessCommands();
            // Up to now we will have the first connection attempt on the default host and
            // one retry on a fallback host
            realtimeHosts.Should().HaveCount(2);
            realtimeHosts.Last().Should().Be(client.State.Connection.FallbackHosts.First());

            // Fail the client and make sure it is failed
            client.Workflow.QueueCommand(SetFailedStateCommand.Create(ErrorInfo.ReasonFailed));
            await client.WaitForState(ConnectionState.Failed);

            client.Connect();

            await client.ConnectClient();

            realtimeHosts.Last().Should().Be(Defaults.RealtimeHost);
        }

        [Fact]
        [Trait("spec", "RTN17")]
        [Trait("spec", "RTN17j")]
        public async Task WhenTheImmediateRetriesAreSpent_ShouldStillReachAFallbackHost()
        {
            // RTN17 - every attempt considers a fallback, including the timer driven ones. Skipping
            // them would lock a client out entirely once the immediate retry budget is spent, since
            // every remaining attempt is timer driven and would be pinned to the primary.
            var client = await GetConnectedClient(opts => opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10));

            var hostsTried = new List<string>();
            FakeTransportFactory.InitialiseFakeTransport = t => hostsTried.Add(t.Parameters.Host);

            // Spend the immediate retry budget and then some, so the later attempts are all
            // timer driven.
            var domainCount = 1 + client.State.Connection.FallbackHosts.Count;
            for (var i = 0; i < domainCount + 3; i++)
            {
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
                {
                    Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout },
                });

                await client.ProcessCommands();

                if (client.Connection.State != ConnectionState.Connecting)
                {
                    await Task.Delay(50);
                }
            }

            client.State.AttemptsInfo.InstantRetryCount.Should().Be(domainCount);
            hostsTried.Should().Contain(x => client.State.Connection.FallbackHosts.Contains(x));
        }

        [Fact]
        [Trait("spec", "RTN17i")]
        public async Task AfterAnExceptionDropsAConnectedTransport_ShouldTryThePrimaryBeforeAnyFallback()
        {
            // RTN17i - "every connection attempt is first attempted to the primary domain ... even if
            // a previous connection attempt to that endpoint has failed". A transport dropping out of
            // CONNECTED is not grounds to move off the primary: it answered a moment ago. Sending the
            // first reconnect to another datacenter would abandon a healthy primary on a blip, and
            // pay the latency of a distant one to do it.
            var client = await GetConnectedClient(opts =>
                opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10));

            client.State.Connection.FallbackHosts.Should().NotBeEmpty("otherwise there is nothing to prefer the primary over");

            var hostsTried = new List<string>();
            FakeTransportFactory.InitialiseFakeTransport = t => hostsTried.Add(t.Parameters.Host);

            // An ordinary socket error. This is the path that carries an Exception rather than an
            // ErrorInfo, and so the one that used to count as a fallback-worthy failure.
            LastCreatedTransport.Listener.OnTransportEvent(
                LastCreatedTransport.Id,
                TransportState.Closed,
                new Exception("socket closed"));

            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            hostsTried.Should().Equal(Defaults.RealtimeHost);

            // The fallbacks are deferred by one attempt rather than lost. That reconnect to the
            // primary now fails at connect time, which is the RSC15l1 "host unreachable" case RTN17f
            // does admit, so the attempt after it moves off the primary.
            LastCreatedTransport.Listener.OnTransportEvent(
                LastCreatedTransport.Id,
                TransportState.Closed,
                new Exception("connect failed"));

            await client.ProcessCommands();

            // Asserted on the attempt straight after the primary failed, not on the last one: with
            // instant retries and a 10ms timeout several attempts land here, so Last() would read as
            // a fallback even if the primary had been tried twice over first.
            hostsTried.Should().HaveCountGreaterThan(1);
            hostsTried[1].Should().BeOneOf(client.State.Connection.FallbackHosts);
        }

        [Fact]
        [Trait("spec", "RTN17j")]
        public async Task WhenAnImmediateRetryIsGranted_ShouldCheckConnectivityOnceForTheCycle()
        {
            // RTN17j asks for a connectivity check before an alternative host is used, and only
            // there. One decision in the cycle needs the answer - whether this attempt may move off
            // the primary - so one check is taken. Asking again to decide whether to retry at all
            // would hold the workflow's single reader thread for a second MaxHttpOpenTimeout on
            // every failing attempt, and would let the probe veto a retry RTN15h3 requires.
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Defaults.InternetCheckOkMessage),
            };

            var handler = new FakeHttpMessageHandler(response);
            var client = GetClientWithFakeTransportAndMessageHandler(messageHandler: handler);
            client.Options.SkipInternetCheck = false;

            await client.ConnectClient();
            await client.ProcessCommands();

            handler.Requests.Clear();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout },
            });

            await client.ProcessCommands();
            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            var checks = handler.Requests
                .Count(x => x.RequestUri.ToString().EqualsTo(Defaults.InternetCheckUrl));

            checks.Should().Be(1);
        }

        [Fact]
        [Trait("spec", "RTN17j")]
        [Trait("spec", "RTN15h3")]
        public async Task WhenTheCandidateIsThePrimary_ShouldRetryImmediatelyWithoutCheckingConnectivity()
        {
            // RTN17j scopes the check to "the use of an alternative host". An attempt that is going
            // to stay on the primary has nothing to verify, so it should not pay for a probe - and
            // must not be cancelled by one, or a probe failing while the realtime endpoint is fine
            // defers the RTN15h3 reconnect to the RTB1 timer.
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("no"),
            });

            var client = GetClientWithFakeTransportAndMessageHandler(
                opts => opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10),
                handler);
            client.Options.SkipInternetCheck = false;

            await client.ConnectClient();
            await client.ProcessCommands();
            handler.Requests.Clear();

            // A non-token DISCONNECTED carrying no retryable status: RTN15h3 earns it an immediate
            // reconnect, and with nothing fallback-worthy on record the candidate is the primary.
            // Deliberately not a socket drop - that path arrives with retryInstantly already set by
            // the caller, so it never reaches the decision under test.
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo("Something else went wrong", 50000),
            });

            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            handler.Requests
                .Count(x => x.RequestUri.ToString().EqualsTo(Defaults.InternetCheckUrl))
                .Should().Be(0, "the primary needs no RTN17j check");

            // Immediately, not in ten minutes - and the unreachable probe did not veto it.
            client.State.AttemptsInfo.InstantRetryCount.Should().Be(1);
            LastCreatedTransport.Parameters.Host.Should().Be(Defaults.RealtimeHost);
        }

        [Fact]
        [Trait("spec", "RTN17j")]
        [Trait("spec", "RTN15h3")]
        public async Task WhenTheCandidateIsAFallbackAndTheInternetIsDown_ShouldStayOnThePrimaryAndStillRetry()
        {
            // The check governs the host, not whether to reconnect. A failed probe means the fallback
            // cannot be trusted to answer either, so the attempt stays on the primary - but it still
            // happens, because RTN15h3 asks for it unconditionally.
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("no"),
            });

            var client = GetClientWithFakeTransportAndMessageHandler(
                opts => opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10),
                handler);
            client.Options.SkipInternetCheck = false;

            await client.ConnectClient();
            await client.ProcessCommands();
            handler.Requests.Clear();

            // A 500-504 DISCONNECTED is fallback-worthy under RTN17f1, so the next attempt's
            // candidate is a fallback domain.
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout },
            });

            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            handler.Requests
                .Count(x => x.RequestUri.ToString().EqualsTo(Defaults.InternetCheckUrl))
                .Should().Be(1, "a fallback candidate is exactly what RTN17j wants checked");

            client.State.AttemptsInfo.InstantRetryCount.Should().Be(1, "the probe governs the host, not the retry");
            LastCreatedTransport.Parameters.Host.Should().Be(Defaults.RealtimeHost);
        }

        [Fact]
        [Trait("spec", "RTN15h3")]
        [Trait("spec", "RTN17j")]
        public async Task WhenEveryConnectThrows_ShouldSpendTheRetryBudgetAndSettleInDisconnected()
        {
            // The instant retry is queued, not returned. Returned, it is processed inside the same
            // command batch one level deeper, so a transport whose connect throws recurses
            // DISCONNECTED -> CONNECTING -> DISCONNECTED within that batch until the command loop's
            // nesting guard trips. The guard throws, the outer catch logs and swallows it, and the
            // batch is abandoned - leaving the connection in CONNECTING with no transport and no
            // timer, never reaching RTB1 or the RTN14e deadline. Only reachable once the retry is no
            // longer vetoed by the RTN17j probe, which is why nothing caught it before.
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.AutoConnect = false;
                opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10);
            });

            FakeTransportFactory.InitialiseFakeTransport = t => t.ThrowOnConnect = true;

            client.Connect();
            await client.ProcessCommands();

            // Bounded by the budget the retry is meant to be bounded by, not by the nesting guard.
            var domainCount = 1 + client.State.Connection.FallbackHosts.Count;
            client.State.AttemptsInfo.InstantRetryCount.Should().Be(domainCount);

            // And parked on the RTB1 timer rather than stranded mid-attempt.
            client.Connection.State.Should().Be(ConnectionState.Disconnected);
        }

        [Fact]
        [Trait("spec", "RTN17j")]
        public async Task WhenRenewingATokenMidAttempt_ShouldNotReachAFallbackWithoutACheck()
        {
            // The token renewal path builds a transport directly instead of queueing a CONNECTING -
            // the connection is already in that state, and the renewed token has to be picked up by
            // the next transport rather than by a re-transition. So it never inherited the CONNECTING
            // handler's RTN17j gate, and with a fallback-worthy failure on record it would open a
            // transport against another datacenter on the strength of that alone, with no check.
            var renewed = new TokenDetails("renewed") { Expires = TestHelpers.Now().AddHours(1) };

            var client = await GetConnectedClient(
                opts => opts.UseBinaryProtocol = false,
                request => request.Url.Contains("/keys")
                    ? renewed.ToJson().ToAblyJsonResponse()
                    : "no".ToAblyResponse());

            // Set after construction, not through the options action: GetRealtimeClient stamps
            // SkipInternetCheck back to true for unit tests once the action has run.
            client.Options.SkipInternetCheck = false;

            // Seeded after connecting, because entering CONNECTED clears the attempt collection.
            var attempt = new ConnectionAttempt(TestHelpers.Now());
            attempt.FailedStates.Add(new AttemptFailedState(
                ConnectionState.Disconnected,
                new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }));
            client.State.AttemptsInfo.Attempts.Add(attempt);

            AttemptsHelpers.GetHost(client.State, Defaults.RealtimeHost)
                .Should().BeOneOf(client.State.Connection.FallbackHosts, "the candidate must really be a fallback, or this proves nothing");

            await client.Workflow.ProcessCommand(HandleConnectingTokenErrorCommand.Create(
                new ErrorInfo { Code = ErrorCodes.TokenError, StatusCode = HttpStatusCode.Unauthorized }));
            await client.ProcessCommands();

            // The internet is unreachable, so the fallback candidate is declined and the renewed
            // token goes out against the primary.
            LastCreatedTransport.Parameters.Host.Should().Be(Defaults.RealtimeHost);
        }

        [Fact]
        [Trait("spec", "RTN17e")]
        public async Task WithFallbackHost_ShouldMakeRestRequestsOnSameHost()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("[12345678]") };
            var handler = new FakeHttpMessageHandler(response);
            var client = GetClientWithFakeTransportAndMessageHandler(messageHandler: handler);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                ConnectionId = "1"
            });

            await client.WaitForState(ConnectionState.Connected);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Disconnected);
            await client.ProcessCommands();

            Output.WriteLine(client.GetCurrentState());
            await client.TimeAsync();

            var lastRequestUri = handler.Requests.Last().RequestUri.ToString();
            var wasLastRequestAFallback = client.State.Connection.FallbackHosts.Any(x => lastRequestUri.Contains(x));
            wasLastRequestAFallback.Should().BeTrue();

            lastRequestUri.Should().Contain(client.State.Connection.Host);
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RTN17e")]
        [Trait("spec", "RSC15f")]
        public async Task WithRealtimeHostConnectedToFallback_WhenMakingRestRequestThatFails_ShouldRetryUsingAFallback()
        {
            var requestCount = 0;

            HttpResponseMessage GetResponse(HttpRequestMessage request)
            {
                try
                {
                    Output.WriteLine($"Response for request: {request.RequestUri}");
                    switch (requestCount)
                    {
                        case 0:
                            Output.WriteLine("0: Returning BadGateway");
                            return new HttpResponseMessage(HttpStatusCode.BadGateway);
                        case 1:
                            Output.WriteLine("1: Returning Ok");
                            return new HttpResponseMessage(HttpStatusCode.OK);
                        case 2:
                            Output.WriteLine("2: Return BadGateway");
                            return new HttpResponseMessage(HttpStatusCode.BadGateway);
                        default:
                            Output.WriteLine($"{requestCount}. Returning Ok");
                            return new HttpResponseMessage(HttpStatusCode.OK);
                    }
                }
                finally
                {
                    requestCount++;
                }
            }

            var handler = new FakeHttpMessageHandler(GetResponse);

            var client = GetClientWithFakeTransportAndMessageHandler(messageHandler: handler);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                ConnectionId = "1"
            });

            await client.WaitForState(ConnectionState.Connected);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Disconnected);

            await client.ConnectClient();

            await MakeRestRequestRequest(); // Will make 2 requests 1 to the RealtimeFallbackHost and one to another fallback host
            await MakeRestRequestRequest(); // Will make 2 requests 1 to the saved fallback host but no the same as RealtimeFallbackHost and 1 to RealtimeFallbackHost
            await MakeRestRequestRequest(); // Will make 1 request to the RealtimeFallback host

            handler.Requests.Count.Should().Be(5); // First attempt is with rest.ably.io
            var attemptedHosts = handler.Requests.Select(x => x.RequestUri.Host).ToList();
            attemptedHosts[0].Should().Be(client.Connection.Host);
            attemptedHosts[1].Should().BeOneOf(Defaults.FallbackHosts);
            attemptedHosts[2].Should().BeOneOf(Defaults.FallbackHosts);
            attemptedHosts[3].Should().Be(client.Connection.Host);
            attemptedHosts[4].Should().Be(client.Connection.Host);

            async Task MakeRestRequestRequest()
            {
                await client.HttpClient.Channels.Get("boo").PublishAsync("boo", "baa");
            }
        }

        [Fact]
        [Trait("spec", "RTN17e")]
        [Trait("spec", "RTN17a")]
        public async Task WhenRealtimeGoesFromFallbackHostToDefault_RestRequestShouldBeOnDefaultHost()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("[12345678]") };
            var handler = new FakeHttpMessageHandler(response);
            var client = GetClientWithFakeTransportAndMessageHandler(null, handler);

            await client.ConnectClient(); // On the default host
            await client.DisconnectWithRetryableError();
            await client.ConnectClient(); // On fallback host
            LastCreatedTransport.Parameters.Host.Should().NotBe(Defaults.RealtimeHost);
            await client.DisconnectWithRetryableError(); // Disconnect again
            await client.ConnectClient(); // We try the default host first

            await client.TimeAsync();
            var lastRequestUri = handler.Requests.Last().RequestUri.ToString();
            var wasLastRequestAFallback = client.Options.GetFallbackHosts().Any(x => lastRequestUri.Contains(x));
            wasLastRequestAFallback.Should().BeFalse();

            lastRequestUri.Should().Contain(Defaults.RestHost);
        }

        [Fact]
        [Trait("spec", "RTN17c")]
        public async Task WithDefaultHostAndRecoverableError_ConnectionGoesToDisconnectedInsteadOfFailedAndRetryInstantly()
        {
            var client = await GetConnectedClient();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Disconnected);
            await client.WaitForState(ConnectionState.Connecting);
            client.Close();
        }

        [Fact]
        [Trait("spec", "RTN17c")]
        public async Task WhileInDisconnectedStateLoop_ShouldRetryWithMultipleHosts()
        {
            var client = await GetConnectedClient(opts => opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10));

            var states = new List<ConnectionState>();
            client.Connection.On((args) =>
            {
                states.Add(args.Current);
            });

            List<string> retryHosts = new List<string>();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.ProcessCommands();

            for (int i = 0; i < 5; i++)
            {
                if (client.Connection.State != ConnectionState.Connecting)
                {
                    await Task.Delay(50); // wait just enough for the disconnect timer to kick in
                }

                retryHosts.Add(LastCreatedTransport.Parameters.Host);

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
                {
                    Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
                });

                await client.ProcessCommands();
            }

            states.Count.Should().BeGreaterThan(0);
            retryHosts.Count.Should().BeGreaterOrEqualTo(3);
            retryHosts.Distinct().Count().Should().BeGreaterOrEqualTo(3);
        }

        [Fact]
        [Trait("spec", "RTN17c")]
        public async Task WhenItMovesFromDisconnectedToSuspended_ShouldTryDefaultHostAgain()
        {
            var now = new Now();

            var client = await GetConnectedClient(opts =>
            {
                opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10);
                opts.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(10);
                opts.NowFunc = now.ValueFn;
            });

            var realtimeHosts = new List<string>();
            FakeTransportFactory.InitialiseFakeTransport = p => realtimeHosts.Add(p.Parameters.Host);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });
            // The connection manager will move from Disconnected to Connecting on a fallback host
            await client.WaitForState(ConnectionState.Connecting);

            // Add 1 more second than the ConnectionStateTtl
            now.Reset(now.Value.Add(client.State.Connection.ConnectionStateTtl).AddSeconds(1));

            // Return an error which will trip the Suspended state check
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout }
            });

            await client.WaitForState(ConnectionState.Suspended);

            // Shortly after the suspended timer will trigger and retry the connection
            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            realtimeHosts.Should().HaveCount(2);
            realtimeHosts.First().Should().Match(x => client.State.Connection.FallbackHosts.Contains(x));
            realtimeHosts.Last().Should().Be("realtime.ably.io");
        }

        [Fact]
        [Trait("spec", "RTN17f")]
        public async Task WhenNonRetryableError_ShouldAlwaysTryDefaultHostFirst()
        {
            var client = await GetConnectedClient(opts =>
            {
                opts.DisconnectedRetryTimeout = TimeSpan.FromSeconds(2);
                opts.SuspendedRetryTimeout = TimeSpan.FromSeconds(2);
            });

            // Reduced connectionStateTTL for limited disconnected retries upto 20 seconds
            client.State.Connection.ConnectionStateTtl = TimeSpan.FromSeconds(20);

            var realtimeHosts = new List<string>();
            FakeTransportFactory.InitialiseFakeTransport = p => realtimeHosts.Add(p.Parameters.Host);

            client.Connection.On(ConnectionEvent.Connecting, stateChange =>
            {
                if (stateChange.Previous == ConnectionState.Disconnected)
                {
                    client.DisconnectWithNonRetryableError(false);
                }
            });

            // Receive first disconnect message on CONNECTED client, will call above callback after timeout
            await client.DisconnectWithNonRetryableError();

            await new ConditionalAwaiter(() => client.Connection.State == ConnectionState.Suspended, null, 120);

            client.Connection.State.Should().Be(ConnectionState.Suspended);
            await client.WaitForState(ConnectionState.Connecting);

            client.DisconnectWithNonRetryableError(false);

            await client.WaitForState(ConnectionState.Suspended);
            await client.WaitForState(ConnectionState.Connecting);

            realtimeHosts.Should().AllBe("realtime.ably.io");
        }

        [Fact]
        [Trait("spec", "RTN17j")]
        public async Task WhenInternetConnectionIsDown_ShouldAlwaysTryDefaultHostFirst()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("Internet not available")
            };

            var handler = new FakeHttpMessageHandler(response);

            var realtimeHosts = new List<string>();
            FakeTransportFactory.InitialiseFakeTransport = p => realtimeHosts.Add(p.Parameters.Host);

            var client = GetClientWithFakeTransportAndMessageHandler(
                opts =>
                {
                    opts.DisconnectedRetryTimeout = TimeSpan.FromSeconds(2);
                    opts.SuspendedRetryTimeout = TimeSpan.FromSeconds(2);
                },
                handler);
            client.Options.SkipInternetCheck = false;

            // Reduced connectionStateTTL for limited disconnected retries upto 20 seconds
            client.State.Connection.ConnectionStateTtl = TimeSpan.FromSeconds(20);

            await client.ConnectClient(); // On the default host

            client.Connection.On(ConnectionEvent.Connecting, stateChange =>
            {
                if (stateChange.Previous == ConnectionState.Disconnected)
                {
                    client.DisconnectWithRetryableError(false);
                }
            });

            // Receive first disconnect message on CONNECTED client, will call above callback after timeout
            await client.DisconnectWithRetryableError();

            await new ConditionalAwaiter(() => client.Connection.State == ConnectionState.Suspended, null, 120);

            client.Connection.State.Should().Be(ConnectionState.Suspended);
            await client.WaitForState(ConnectionState.Connecting);

            client.DisconnectWithRetryableError(false);

            await client.WaitForState(ConnectionState.Suspended);
            await client.WaitForState(ConnectionState.Connecting);

            realtimeHosts.Should().AllBe("realtime.ably.io");
        }

        public ConnectionFallbackSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
