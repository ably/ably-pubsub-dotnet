using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Tests.Shared.Utils;
using IO.Ably.Transport;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime.ConnectionSpecs
{
    public class ConnectionFailureSpecs : AblyRealtimeSpecs
    {
        private readonly TokenDetails _returnedDummyTokenDetails = new TokenDetails("123") { Expires = TestHelpers.Now().AddDays(1), ClientId = "123" };

        public ConnectionFailureSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        [Trait("spec", "RTN14b")]
        public async Task WithTokenErrorAndRenewableToken_ShouldRenewTokenAutomaticallyWithoutEmittingError()
        {
            var tokenDetails = new TokenDetails("id") { Expires = Now.AddHours(1) };
            bool renewTokenCalled = false;
            var client = GetClientWithFakeTransport(
                opts =>
            {
                opts.TokenDetails = tokenDetails;
                opts.UseBinaryProtocol = false;
                opts.AutoConnect = false;
            }, request =>
            {
                if (request.Url.Contains("/keys"))
                {
                    if (renewTokenCalled == false)
                    {
                        renewTokenCalled = true;
                    }

                    return _returnedDummyTokenDetails.ToJson().ToAblyResponse();
                }

                return AblyResponse.EmptyResponse.ToTask();
            });
            client.Connect();
            List<ErrorInfo> raisedErrors = new List<ErrorInfo>();
            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    raisedErrors.Add(args.Reason);
                }
            });

            await client.WaitForState(ConnectionState.Connecting);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = new ErrorInfo("Unauthorised", ErrorCodes.TokenError, HttpStatusCode.Unauthorized) });

            await client.ProcessCommands();
            renewTokenCalled.Should().BeTrue();
            var currentToken = client.RestClient.AblyAuth.CurrentToken;
            currentToken.Token.Should().Be(_returnedDummyTokenDetails.Token);
            currentToken.ClientId.Should().Be(_returnedDummyTokenDetails.ClientId);
            currentToken.Expires.Should().BeCloseTo(_returnedDummyTokenDetails.Expires, TimeSpan.FromMilliseconds(20));
            raisedErrors.Should().BeEmpty("No errors should be raised!");
        }

        [Fact]
        [Trait("spec", "RTN14b")]
        public async Task WithTokenErrorAndTokenRenewalFails_ShouldRaiseErrorAndTransitionToDisconnected()
        {
            var tokenDetails = new TokenDetails("id") { Expires = Now.AddHours(1) };
            var taskAwaiter = new TaskCompletionAwaiter(taskCount: 2);
            var client = GetClientWithFakeTransport(
                opts =>
            {
                opts.TokenDetails = tokenDetails;
                opts.UseBinaryProtocol = false;
            }, request =>
            {
                if (request.Url.Contains("/keys"))
                {
                    throw new AblyException(new ErrorInfo { Code = ErrorCodes.TokenError });
                }

                return AblyResponse.EmptyResponse.ToTask();
            });

            var stateChanges = new List<ConnectionStateChange>();
            client.Connection.On(stateChange =>
            {
                if (stateChange.Current == ConnectionState.Disconnected)
                {
                    taskAwaiter.Tick();
                }

                stateChanges.Add(stateChange);
            });

            await client.WaitForState(ConnectionState.Connecting);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = new ErrorInfo("Unauthorised", ErrorCodes.TokenError, HttpStatusCode.Unauthorized) });

            await taskAwaiter.Task;

            stateChanges.Count.Should().BeGreaterThan(0);
            client.Connection.ErrorReason.Should().NotBeNull();
            client.Connection.ErrorReason.Code.Should().Be(ErrorCodes.TokenError);
        }

        [Fact]
        [Trait("spec", "RTN14b")]
        public async Task WithTokenErrorTwice_ShouldNotRenewAndRaiseErrorAndTransitionToDisconnected()
        {
            var tokenDetails = new TokenDetails("id") { Expires = Now.AddHours(1) };
            var renewCount = 0;
            var client = GetClientWithFakeTransport(
                opts =>
            {
                opts.TokenDetails = tokenDetails;
                opts.UseBinaryProtocol = false;
                opts.AutoConnect = false;
            }, request =>
            {
                if (request.Url.Contains("/keys"))
                {
                    renewCount++;
                    return _returnedDummyTokenDetails.ToJson().ToAblyResponse();
                }

                return AblyResponse.EmptyResponse.ToTask();
            });

            bool disconnected = false;
            client.Connection.On(ConnectionEvent.Disconnected, (_) =>
            {
                disconnected = true;
            });
            client.Connect();

            await client.WaitForState(ConnectionState.Connecting);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = new ErrorInfo("Unauthorised", ErrorCodes.TokenError, HttpStatusCode.Unauthorized) });

            await client.ProcessCommands();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = new ErrorInfo("Unauthorised", ErrorCodes.TokenError, HttpStatusCode.Unauthorized) });

            await client.ProcessCommands();

            await client.WaitForState(ConnectionState.Disconnected);
            renewCount.Should().Be(1);
            disconnected.Should().BeTrue();
            client.Connection.ErrorReason.Should().NotBeNull();
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        [Trait("spec", "TR2")]
        public async Task WhenTransportFails_ShouldTransitionToDisconnectedAndEmitErrorWithRetry()
        {
            // this will keep it in connecting state
            FakeTransportFactory.InitialiseFakeTransport =
                transport => transport.OnConnectChangeStateToConnected = false;

            ClientOptions options = null;
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.AutoConnect = false;
                options = opts;
            });

            client.Connect();

            await client.WaitForState(ConnectionState.Connecting);

            client.Connection.On((args) =>
            {
                args.Current.Should().Be(ConnectionState.Disconnected);
                args.Previous.Should().Be(ConnectionState.Connecting);
                args.Event.Should().Be(ConnectionEvent.Disconnected);
                args.RetryIn.Should().Be(options.DisconnectedRetryTimeout);
                args.Reason.Should().NotBeNull();
                Done();
            });

            // Let the connecting state complete and create transport, otherwise LastCreatedTransport.Id throws exception
            await client.ProcessCommands();

            LastCreatedTransport.Listener.OnTransportEvent(LastCreatedTransport.Id, TransportState.Closing, new Exception());

            WaitOne();
        }

        [Fact(Skip = "Requires a SandBox Spec")]
        [Trait("spec", "RTN14d")]
        [Trait("spec", "RTN14e")]
        [Trait("sandboxneeded", "true")]
        public async Task WhenTransportFails_ShouldGoFromConnectingToDisconnectedUntilConnectionStateTtlIsReachedAndStateIsSuspended()
        {
            Func<DateTimeOffset> nowFunc = () => DateTimeOffset.UtcNow;

            // We want access to the modified closure so we can manipulate time within ConnectionAttemptsInfo
            // ReSharper disable once AccessToModifiedClosure
            DateTimeOffset NowWrapperFn() => nowFunc();

            FakeTransportFactory.InitialiseFakeTransport =
                transport => transport.OnConnectChangeStateToConnected = false;

            // this will keep it in connecting state
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.AutoConnect = false;
                opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10);
                opts.NowFunc = NowWrapperFn;
            });

            client.Connect();
            List<ConnectionStateChange> stateChanges = new List<ConnectionStateChange>();
            client.Connection.On((args) =>
            {
                stateChanges.Add(args);
            });

            do
            {
                LastCreatedTransport.Listener?.OnTransportEvent(LastCreatedTransport.Id, TransportState.Closing, new Exception());
                await WaitForConnectingOrSuspended(client);
                var now = nowFunc();
                nowFunc = () => now.AddSeconds(30);
            }
            while (client.Connection.State != ConnectionState.Suspended);

            client.Connection.State.Should().Be(ConnectionState.Suspended);

            stateChanges.Select(x => x.Current).Distinct()
                .Should().BeEquivalentTo(new[] { ConnectionState.Connecting, ConnectionState.Disconnected, ConnectionState.Suspended, });
            int numberOfAttempts = (int)Math.Floor(Defaults.ConnectionStateTtl.TotalSeconds / 30);
            stateChanges.Count(x => x.Current == ConnectionState.Connecting).Should().Be(numberOfAttempts);
        }

        [Fact]
        [Trait("spec", "RTN14e")]
        public async Task WhenInSuspendedState_ShouldTryAndReconnectAfterSuspendRetryTimeoutIsReached()
        {
            DateTimeOffset Func() => DateTimeOffset.UtcNow;

            FakeTransportFactory.InitialiseFakeTransport =
                transport => transport.OnConnectChangeStateToConnected = false;

            // this will keep it in connecting state
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.AutoConnect = false;
                opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10);
                opts.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(1000);
                opts.NowFunc = Func;
            });

            client.ExecuteCommand(SetSuspendedStateCommand.Create(ErrorInfo.ReasonSuspended));

            var elapsed = await client.WaitForState(ConnectionState.Connecting);
            elapsed.Should().BeCloseTo(client.Options.SuspendedRetryTimeout, TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        [Trait("spec", "RTN14d")]
        public async Task WhenInDisconnectedState_ReconnectUsingIncrementalBackoffTimeout()
        {
            // this will keep it in connecting state when connect is called
            FakeTransportFactory.InitialiseFakeTransport =
                transport => transport.OnConnectChangeStateToConnected = false;

            var client = GetClientWithFakeTransport(opts =>
            {
                opts.DisconnectedRetryTimeout = TimeSpan.FromSeconds(5);
            });

            // wait for transport to be created for first connecting
            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            var disconnectedRetryTimeouts = new List<double>();
            do
            {
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.WaitForState(ConnectionState.Disconnected);
                var elapsed = await client.WaitForState(ConnectionState.Connecting);
                disconnectedRetryTimeouts.Add(elapsed.TotalSeconds);
            }
            while ((disconnectedRetryTimeouts.Sum() + 10) < client.Connection.ConnectionStateTtl.TotalSeconds);
            Output.WriteLine(disconnectedRetryTimeouts.ToJson());

            // Upper bound = min((retryAttempt + 2) / 3, 2) * initialTimeout
            // Lower bound = 0.8 * Upper bound
            disconnectedRetryTimeouts[0].Should().BeInRange(4, 5 + 0.20);
            disconnectedRetryTimeouts[1].Should().BeInRange(5.33, 6.66 + 0.20);
            disconnectedRetryTimeouts[2].Should().BeInRange(6.66, 8.33 + 0.20);
            for (var i = 3; i < disconnectedRetryTimeouts.Count; i++)
            {
                disconnectedRetryTimeouts[i].Should().BeInRange(8, 10 + 0.20);
            }
        }

        private static Task WaitForConnectingOrSuspended(AblyRealtime client)
        {
            return new ConnectionAwaiter(client.Connection, ConnectionState.Connecting, ConnectionState.Suspended).Wait();
        }

        [Fact]
        [Trait("spec", "RTN14f")]
        public async Task WhenInSuspendedStateAfterRetrying_ShouldGoBackToSuspendedState()
        {
            FakeTransportFactory.InitialiseFakeTransport =
                transport => transport.OnConnectChangeStateToConnected = false;

            // this will keep it in connecting state
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.AutoConnect = true;
                opts.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(10);
                opts.SuspendedRetryTimeout = TimeSpan.FromMilliseconds(10);
                opts.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
            });

            client.ExecuteCommand(SetSuspendedStateCommand.Create(ErrorInfo.ReasonSuspended));

            await client.WaitForState(ConnectionState.Connecting);
            await client.WaitForState(ConnectionState.Suspended);
        }
    }
}
