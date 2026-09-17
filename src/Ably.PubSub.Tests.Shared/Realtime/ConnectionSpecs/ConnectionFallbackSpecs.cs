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
        public async Task WithFallbackHostsUseDefault_ConnectionGoesStraightToFailedInsteadOfDisconnected()
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
                await client.RestClient.Channels.Get("boo").PublishAsync("boo", "baa");
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
