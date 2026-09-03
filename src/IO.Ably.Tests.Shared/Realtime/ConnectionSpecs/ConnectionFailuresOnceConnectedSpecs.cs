using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
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

        // UTS: realtime/unit/RTN15b/successful-resume-0
        [Fact]
        [Trait("spec", "RTN15b")]
        [Trait("spec", "RTN15c6")]
        public async Task WhenTheTransportDropsAndTheResumeSucceeds_ShouldKeepTheConnectionId()
        {
            // RTN15b - the reconnect carries the connectionKey in the resume query param. RTN15c6 -
            // the server signals a successful resume by answering with the same connectionId, and
            // may hand back a refreshed connectionKey with it.
            var client = await SetupConnectedClient();

            var connectionId = client.Connection.Id;
            var connectionKey = client.Connection.Key;
            connectionId.Should().NotBeNullOrEmpty();
            connectionKey.Should().NotBeNullOrEmpty();

            // An unexpected transport drop, so the next attempt is a resume rather than a fresh
            // connection.
            LastCreatedTransport.Listener.OnTransportEvent(LastCreatedTransport.Id, TransportState.Closed);
            await client.WaitForState(ConnectionState.Connecting);
            await client.ProcessCommands();

            LastCreatedTransport.Parameters.GetParams()
                .Should().ContainKey("resume")
                .WhoseValue.Should().Be(connectionKey);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionId = connectionId,
                ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey-updated" },
            });
            await client.WaitForState(ConnectionState.Connected);
            await client.ProcessCommands();

            client.Connection.Id.Should().Be(connectionId);
            client.Connection.Key.Should().Be("connectionKey-updated");
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

            var originalId = client.Connection.Id;
            var connectionKey = client.Connection.Key;

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo("Something else went wrong", 50000),
            });

            await client.ProcessCommands();

            // Immediately, not in ten minutes.
            client.State.AttemptsInfo.InstantRetryCount.Should().Be(1);
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // RTN15h3 asks for a reconnect *with a resume attempt*, so follow it through: the new
            // attempt carries the key, and a CONNECTED bearing the same id keeps the connection.
            LastCreatedTransport.Parameters.GetParams()
                .Should().ContainKey("resume")
                .WhoseValue.Should().Be(connectionKey);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionId = originalId,
                ConnectionDetails = new ConnectionDetails { ConnectionKey = connectionKey },
            });
            await client.WaitForState(ConnectionState.Connected);
            await client.ProcessCommands();

            client.Connection.Id.Should().Be(originalId);
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

        [Fact]
        [Trait("spec", "RSA4c")]
        public async Task WithASynchronouslyBlockingAuthCallback_ShouldStillBoundTheAttempt()
        {
            // RSA4c - TimeoutAfter extends an already-created Task, so the callback has to be invoked
            // through Task.Run to be bounded at all. A callback whose body runs synchronously - the
            // most ordinary C# shape - would otherwise block before there is anything to bound and
            // hold the workflow's single reader thread for as long as it takes.
            var released = new ManualResetEventSlim(false);
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.Key = ValidKey;
                opts.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200);
                opts.AuthCallback = _ =>
                {
                    released.Wait(TimeSpan.FromSeconds(30));
                    return Task.FromResult<object>(new TokenDetails("blocked"));
                };
            });

            try
            {
                var sw = Stopwatch.StartNew();
                var error = await Assert.ThrowsAsync<AblyException>(() => client.Auth.AuthorizeAsync());
                sw.Stop();

                // The specific failure, not merely any failure - a bare IsFaulted check would also
                // have been satisfied by an unrelated fast fault.
                error.ErrorInfo.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                error.ErrorInfo.Cause.Should().NotBeNull();
                error.ErrorInfo.Cause.Code.Should().Be(ErrorCodes.ClientCallbackError);

                // Comfortably inside the 30s the callback blocks for, and comfortably outside the
                // 200ms bound so a loaded run does not flake.
                sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            }
            finally
            {
                released.Set();
            }
        }

        [Fact]
        [Trait("spec", "RSA4c1")]
        public async Task WhenTheAuthCallbackFails_ShouldSetTheCauseNotOnlyTheInnerException()
        {
            // RSA4c1 wants an ErrorInfo "with code 80019, statusCode 401, and cause set to the
            // underlying cause". The four argument overload assigns InnerException instead, which is
            // not the spec's field and is not serialised as one.
            var client = GetClientWithFakeTransport(opts =>
            {
                opts.Key = ValidKey;
                opts.AuthCallback = _ => throw new AblyException(new ErrorInfo("the underlying cause", 40100));
            });

            var error = await Assert.ThrowsAsync<AblyException>(() => client.Auth.AuthorizeAsync());

            error.ErrorInfo.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
            error.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            error.ErrorInfo.Cause.Should().NotBeNull();
            error.ErrorInfo.Cause.Message.Should().Be("the underlying cause");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [Trait("spec", "TO3l11")]
        public void WithANonPositiveRealtimeRequestTimeout_ShouldReject(int seconds)
        {
            var options = new ClientOptions(ValidKey);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => options.RealtimeRequestTimeout = TimeSpan.FromSeconds(seconds));
        }

        [Fact]
        [Trait("spec", "TO3l11")]
        public void WithARealtimeRequestTimeoutTooLargeForATimer_ShouldReject()
        {
            // TimeSpan.MaxValue is the idiomatic "never time out", and it reaches Task.Delay through
            // TimeoutAfter, which rejects anything over uint.MaxValue - 1 ms - surfacing as a code-0
            // error out of Authorize(). Rejected up front instead.
            var options = new ClientOptions(ValidKey);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => options.RealtimeRequestTimeout = TimeSpan.MaxValue);

            // uint.MaxValue - 1 is Task.Delay's limit on .NET 6+ only: it is above what net46, Mono
            // and Xamarin accept, and above what CountdownTimer's cast to int can carry into
            // System.Threading.Timer on any framework.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1));

            // Just inside the limit is still allowed.
            options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(int.MaxValue);
            options.RealtimeRequestTimeout.TotalMilliseconds.Should().Be(int.MaxValue);
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
