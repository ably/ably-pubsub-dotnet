using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using IO.Ably.Realtime;
using IO.Ably.Transport;
using IO.Ably.Types;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Trait("spec", "RTN15")]
    public class ConnectionFailuresOnceConnectedSpecs : AblyRealtimeSpecs
    {
        private const int FailedRenewalErrorCode = 1234;

        private readonly TokenDetails _returnedDummyTokenDetails = new TokenDetails("123") { Expires = TestHelpers.Now().AddDays(1), ClientId = "123" };
        private readonly TokenDetails _validToken;
        private readonly ErrorInfo _tokenErrorInfo;

        private bool _renewTokenCalled;

        public ConnectionFailuresOnceConnectedSpecs(ITestOutputHelper output)
            : base(output)
        {
            SetNowFunc(() => DateTimeOffset.UtcNow);
            _validToken = new TokenDetails("id") { Expires = Now.AddHours(1) };
            _renewTokenCalled = false;
            _tokenErrorInfo = new ErrorInfo { Code = ErrorCodes.TokenError, StatusCode = HttpStatusCode.Unauthorized };
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RTN15h")]
        public async Task WithDisconnectMessageWithTokenError_ShouldRenewTokenAndReconnect()
        {
            var client = await SetupConnectedClient();

            List<ConnectionState> states = new List<ConnectionState>();
            var errors = new ConcurrentBag<ErrorInfo>();
            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    errors.Add(args.Reason);
                }

                states.Add(args.Current);
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected) { Error = _tokenErrorInfo });

            await client.ProcessCommands();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected));

            await client.WaitForState(ConnectionState.Connected);

            _renewTokenCalled.Should().BeTrue();

            Assert.Equal(new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected }, states);

            errors.Should().HaveCount(1);
            errors.First().Should().Be(_tokenErrorInfo);

            var currentToken = client.RestClient.AblyAuth.CurrentToken;
            currentToken.Token.Should().Be(_returnedDummyTokenDetails.Token);
            currentToken.ClientId.Should().Be(_returnedDummyTokenDetails.ClientId);
            currentToken.Expires.Should().BeCloseTo(_returnedDummyTokenDetails.Expires, TimeSpan.Zero);
        }

        [Fact(Skip = "Intermittently fails")]
        [Trait("spec", "RTN15a")]
        public async Task WithDisconnectMessageWithTokenError_ShouldResumeConnection()
        {
            var client = await SetupConnectedClient();

            var states = new List<ConnectionState>();
            var errors = new List<ErrorInfo>();
            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    errors.Add(args.Reason);
                }

                states.Add(args.Current);
                if (args.Current == ConnectionState.Connecting)
                {
                    client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected));
                }
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected) { Error = _tokenErrorInfo });

            await client.ProcessCommands();

            states.Should().NotBeEmpty();
            errors.Should().NotBeEmpty();

            var urlParams = LastCreatedTransport.Parameters.GetParams();
            urlParams.Should().ContainKey("resume");
            urlParams.Should().ContainKey("connection_serial");
        }

        [Fact]
        [Trait("spec", "RTN15h2")]
        public async Task WithTokenErrorWhenTokenRenewalFails_ShouldGoToDisconnectedAndEmitError()
        {
            var client = await SetupConnectedClient(ConnectedClientErrors.FailRenewal);

            List<ConnectionState> states = new List<ConnectionState>();
            var errors = new List<ErrorInfo>();
            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    errors.Add(args.Reason);
                }

                states.Add(args.Current);
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = _tokenErrorInfo,
            });

            await client.ProcessCommands();

            Assert.Equal(
                new[]
            {
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Disconnected,
            }, states);

            errors.Should().NotBeEmpty();
            errors.Should().HaveCount(2);
            errors[0].Code.Should().Be(ErrorCodes.TokenError);
            errors[1].Code.Should().Be(FailedRenewalErrorCode);
        }

        [Fact]
        [Trait("spec", "RTN15h")]
        public async Task WhenConnectionFailsWithTokenErrorButTokenIsNotRenewable_ShouldTransitionDirectlyToFailedWithError()
        {
            var client = await SetupConnectedClient(ConnectedClientErrors.RenewalNotSupported);

            var errors = new List<ErrorInfo>();
            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    errors.Add(args.Reason);
                }
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = _tokenErrorInfo
            });

            await client.WaitForState(ConnectionState.Failed);

            errors.Should().NotBeEmpty();
        }

        [Fact]
        [Trait("spec", "RTN15a")]
        [Trait("spec", "RTN15b")]
        [Trait("spec", "RTN15b1")]
        [Trait("spec", "RTN15b2")]
        public async Task WhenTransportCloses_ShouldResumeConnection()
        {
            var client = await SetupConnectedClient();

            var states = new List<ConnectionState>();
            var errors = new List<ErrorInfo>();
            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    errors.Add(args.Reason);
                }

                states.Add(args.Current);
                if (args.Current == ConnectionState.Connecting)
                {
                    client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected));
                }
            });

            var firstTransport = LastCreatedTransport;
            var connectionKey = client.Connection.Key;
            LastCreatedTransport.Listener.OnTransportEvent(LastCreatedTransport.Id, TransportState.Closed);

            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            states.Should().NotBeEmpty();
            errors.Should().NotBeEmpty();

            var urlParams = LastCreatedTransport.Parameters.GetParams();
            urlParams.Should().ContainKey("resume")
                .WhoseValue.Should().Be(connectionKey);
            LastCreatedTransport.Should().NotBeSameAs(firstTransport);
        }

        [Fact]
        [Trait("spec", "RTN15a")]
        public async Task AckMessagesAreSentWhenConnectionIsDroppedAndNotResumed()
        {
            var client = await SetupConnectedClient();

            List<bool> callbackResults = new List<bool>();
            void Callback(bool b, ErrorInfo info) => callbackResults.Add(b);

            client.ConnectionManager.Send(new ProtocolMessage(ProtocolMessage.MessageAction.Message), Callback);
            client.ConnectionManager.Send(new ProtocolMessage(ProtocolMessage.MessageAction.Message), Callback);

            await client.ProcessCommands();

            client.State.WaitingForAck.Should().HaveCount(2);

            await CloseAndWaitToReconnect(client);
            client.State.WaitingForAck.Should().HaveCount(2);
            LastCreatedTransport.SentMessages.Should().HaveCount(2);
        }

        [Fact]
        [Trait("spec", "RTN15a")]
        public async Task AckMessagesAreResentWhenConnectionIsDroppedAndResumed()
        {
            var client = await SetupConnectedClient();

            string initialConnectionId = client.Connection.Id;
            client.ConnectionManager.Send(new ProtocolMessage(ProtocolMessage.MessageAction.Message));
            client.ConnectionManager.Send(new ProtocolMessage(ProtocolMessage.MessageAction.Message));

            await CloseAndWaitToReconnect(client, new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionId = initialConnectionId // if the connection ids match then the connection has been resumed
            });

            LastCreatedTransport.SentMessages.Should().HaveCount(2);
            client.State.WaitingForAck.Should().HaveCount(2);
        }

        [Fact]
        [Trait("spec", "RTN15h2")]
        [Trait("spec", "RTN15h3")]
        public async Task WithTokenError_ShouldNotAlsoGrantTheNonTokenImmediateReconnect()
        {
            // RTN15h3's immediate reconnect is for "an error other than a token error". RTN15h2 owns
            // token errors and reconnects of its own accord, so granting a retry here as well gives
            // two overlapping attempts - and the second reaches FAILED where RTN15h2 requires
            // DISCONNECTED.
            var client = await SetupConnectedClient(ConnectedClientErrors.FailRenewal);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = _tokenErrorInfo,
            });

            await client.ProcessCommands();

            client.State.AttemptsInfo.InstantRetryCount.Should().Be(0);
            client.Connection.State.Should().Be(ConnectionState.Disconnected);
        }

        // UTS: realtime/unit/RTN15h3/non-token-error-resume-0
        [Fact]
        [Trait("spec", "RTN15h3")]
        public async Task WithNonTokenDisconnected_ShouldReconnectImmediately()
        {
            var client = await GetConnectedClient(opts =>
                opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo("Something else went wrong", 50000),
            });

            await client.ProcessCommands();

            // Immediately, not in ten minutes.
            client.State.AttemptsInfo.InstantRetryCount.Should().Be(1);
            client.Connection.State.Should().Be(ConnectionState.Connecting);
        }

        [Fact]
        [Trait("spec", "RTN15h3")]
        [Trait("spec", "RTN17j")]
        public async Task WhenAConnectionSucceeds_ShouldClearTheImmediateRetryBudget()
        {
            // The budget that bounds RTN17j's traversal is per failure run, cleared by
            // UpdateAttemptState's Connected case. Without that a client which spent its retries once
            // would never get an immediate reconnect again, leaving RTN15h3 unimplemented from the
            // second disconnect onwards.
            var client = await GetConnectedClient(opts =>
                opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo("Something else went wrong", 50000),
            });
            await client.ProcessCommands();

            client.State.AttemptsInfo.InstantRetryCount.Should().Be(1);

            client.FakeProtocolMessageReceived(ConnectedProtocolMessage);
            await client.WaitForState(ConnectionState.Connected);
            await client.ProcessCommands();

            client.State.AttemptsInfo.InstantRetryCount.Should().Be(0);
        }

        [Flags]
        private enum ConnectedClientErrors
        {
            None = 1,
            RenewalNotSupported = 2,
            FailRenewal = 4,
        }

        private Task<AblyRealtime> SetupConnectedClient(ConnectedClientErrors errors = ConnectedClientErrors.None)
        {
            return GetConnectedClient(
                opts =>
                {
                    if (errors.HasFlag(ConnectedClientErrors.RenewalNotSupported))
                    {
                        opts.Key = string.Empty; // clear the key to make the token non renewable
                    }

                    opts.TokenDetails = _validToken;
                    opts.UseBinaryProtocol = false;
                }, request =>
                {
                    if (request.Url.Contains("/keys"))
                    {
                        if (errors.HasFlag(ConnectedClientErrors.FailRenewal))
                        {
                            throw new AblyException(new ErrorInfo("Failed to renew token", FailedRenewalErrorCode));
                        }

                        _renewTokenCalled = true;
                        return _returnedDummyTokenDetails.ToJson().ToAblyResponse();
                    }

                    return AblyResponse.EmptyResponse.ToTask();
                });
        }

        private async Task CloseAndWaitToReconnect(AblyRealtime client, ProtocolMessage connectedMessage = null)
        {
            connectedMessage = connectedMessage ?? new ProtocolMessage(ProtocolMessage.MessageAction.Connected);
            LastCreatedTransport.Listener.OnTransportEvent(LastCreatedTransport.Id, TransportState.Closed);
            await client.WaitForState(ConnectionState.Connecting);
            client.FakeProtocolMessageReceived(connectedMessage);
            await client.WaitForState(ConnectionState.Connected);
            await client.ProcessCommands();
        }
    }
}
