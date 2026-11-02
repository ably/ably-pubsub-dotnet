using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.InfraTests
{
    /// <summary>
    /// Self-tests of the state waits (<see cref="UtsWait.AwaitConnectionState"/>, <see cref="UtsWait.AwaitChannelState"/>):
    /// they latch a state entered and left before the test looks, complete for a state already reached, and
    /// fail fast on FAILED. Driven through a real client and <see cref="MockWebSocket"/>.
    /// </summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "common")]
    public class StateWaitTests : UtsUnitTestBase
    {
        public StateWaitTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task ConnectionStateWaitLatchesAStateEnteredAndLeftBeforeItIsAwaited()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var states = UtsRecord.ConnectionStates(client.Connection);

            var disconnected = UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);
            mock.SimulateDisconnect();

            // RTN15a: DISCONNECTED is left at once for CONNECTING, and the handler connects again.
            await UtsWait.PollUntil(() => states.Contains(ConnectionState.Connected));
            client.Connection.State.Should().Be(ConnectionState.Connected, "DISCONNECTED was only transient");
            disconnected.IsCompleted.Should().BeTrue("the wait latched DISCONNECTED although the connection had left it");
            await disconnected;
        }

        [Fact]
        public async Task ConnectionStateWaitCompletesForAStateAlreadyReached()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var again = UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(1));

            await again;
            again.IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task ConnectionStateWaitFailsFastWhenTheConnectionEntersFailed()
        {
            // thenClose: false, so this helper test does not depend on the close ordering that
            // ConnectionStaysFailedWhenTheServerClosesAfterAFatalError checks.
            var mock = new MockWebSocket(attempt => attempt.RespondWithError(ProtocolMessages.ErrorMessage(40000, "fatal for the test"), thenClose: false));
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var states = UtsRecord.ConnectionStates(client.Connection);
            int line = 0;
            var stopwatch = Stopwatch.StartNew();

            Func<Task> act = () =>
            {
                line = NextLine() + 1;
                var connected = UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromSeconds(10));
                client.Connect();
                return connected;
            };

            var thrown = await act.Should().ThrowAsync<XunitException>();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "FAILED ends the wait without waiting for its timeout");
            thrown.Which.Message.Should().StartWith($"StateWaitTests.cs:{line}: ")
                .And.Contain("FAILED").And.Contain("40000");
            states.Should().Contain(ConnectionState.Failed);
            client.Connection.State.Should().Be(ConnectionState.Failed);
        }

        /// <summary>
        /// Not a helper contract: an SDK finding made by the test above. RTN14g/RTN15i say a connection-level
        /// ERROR moves the connection to FAILED, which only an explicit <c>connect()</c> leaves. When the server
        /// closes the socket after the ERROR (as <c>mock_websocket.md</c> says it does), the SDK used to handle
        /// the close first (DISCONNECTED, with an RTN15a/RTN17j instant retry queued), then the ERROR (FAILED),
        /// and then the queued retry took the connection from FAILED back to CONNECTING. Fixed in
        /// <c>RealtimeWorkflow</c> (<c>HandleTransportEventCommand</c> defers a close once, behind the commands
        /// the earlier messages queued). It was a race, so this checks 20 clients.
        /// </summary>
        [Fact]
        public async Task ConnectionStaysFailedWhenTheServerClosesAfterAFatalError()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithError(ProtocolMessages.ErrorMessage(40000, "fatal for the test")));
            InstallMock(mock);
            for (var i = 0; i < 20; i++)
            {
                var client = Realtime(new ClientOptions { Key = ValidKey });
                var states = UtsRecord.ConnectionStates(client.Connection);
                await UtsWait.PollUntil(() => states.Contains(ConnectionState.Failed));
                await ProcessPendingEvents();

                states.SkipWhile(state => state != ConnectionState.Failed).Should().NotContain(
                    ConnectionState.Connecting,
                    "client {0}: nothing but connect() may leave FAILED (states: {1})",
                    i,
                    string.Join(", ", states));
            }
        }

        [Fact]
        public async Task ChannelStateWaitLatchesAStateEnteredAndLeftBeforeItIsAwaited()
        {
            const string name = "state-wait-latch";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            mock.OnMessage(ProtocolMessages.Attach, ProtocolMessages.AttachedMessage(name));
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(name);

            var attaching = UtsWait.AwaitChannelState(channel, ChannelState.Attaching, TimeSpan.FromSeconds(5));
            var result = await channel.AttachAsync();

            result.IsSuccess.Should().BeTrue();
            channel.State.Should().Be(ChannelState.Attached, "ATTACHING was only transient");

            // The channel will not enter ATTACHING again, so this completes only if the wait latched
            // the transient ATTACHING; otherwise it fails with its timeout. Not checked through
            // IsCompleted: the wait's task completes after a thread-pool hop (its completion source
            // runs continuations asynchronously), which a starved runner can schedule after this
            // continuation.
            await attaching;
        }

        [Fact]
        public async Task ChannelStateWaitCompletesForAStateAlreadyReached()
        {
            const string name = "state-wait-already";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            mock.OnMessage(ProtocolMessages.Attach, ProtocolMessages.AttachedMessage(name));
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            var channel = client.Channels.Get(name);
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();

            await UtsWait.AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task ChannelStateWaitFailsFastWhenTheChannelEntersFailed()
        {
            const string name = "state-wait-failed";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            mock.OnMessage(ProtocolMessages.Attach, ProtocolMessages.ChannelErrorMessage(name, 40160, "denied for the test"));
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(name);
            int line = 0;
            var stopwatch = Stopwatch.StartNew();

            Func<Task> act = () =>
            {
                line = NextLine() + 1;
                var attached = UtsWait.AwaitChannelState(channel, ChannelState.Attached, TimeSpan.FromSeconds(10));
                channel.Attach();
                return attached;
            };

            var thrown = await act.Should().ThrowAsync<XunitException>();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "FAILED ends the wait without waiting for its timeout");
            thrown.Which.Message.Should().StartWith($"StateWaitTests.cs:{line}: ")
                .And.Contain("FAILED").And.Contain("40160");
            client.Connection.State.Should().Be(ConnectionState.Connected, "a channel-scoped ERROR doesn't end the connection");
        }

        [Fact]
        public async Task StateWaitTimeoutReportsTheCallersLineAndTheCurrentState()
        {
            InstallMock(new MockWebSocket());
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            int line = 0;

            Func<Task> act = () =>
            {
                line = NextLine() + 1;
                return UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, TimeSpan.FromMilliseconds(200));
            };

            var thrown = await act.Should().ThrowAsync<XunitException>();
            thrown.Which.Message.Should().StartWith($"StateWaitTests.cs:{line}: ").And.Contain("current: Initialized");
        }

        private static int NextLine([CallerLineNumber] int line = 0) => line;
    }
}
