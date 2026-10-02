using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/auto_connect_test.md in ably/specification.
    ///
    /// Spec points: RTN3
    ///
    /// <para>
    /// Every test here sets <c>AutoConnect</c> explicitly. <c>UtsClients.Options</c> turns it off for the
    /// whole unit tier, so the other realtime specs can drive the connection themselves; the specs that
    /// are <em>about</em> auto-connect are the exception, and each one has to say which side of the option
    /// it is testing rather than inheriting a tier default that would invert the test.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class AutoConnectTests : UtsTestBase
    {
        private const string ConnectionId = "connection-id";
        private const string ConnectionKey = "connection-key";

        public AutoConnectTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTN3/auto-connect-true-0
        [Fact]
        public async Task RTN3_AutoConnectTrue()
        {
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedMessage());
            });

            // The spec's "Create client with default autoConnect (true) - do NOT call connect()". The SDK
            // default is true; the unit-tier default in UtsClients is false, so it is turned back on here.
            var client = RealtimeClient(mockWs, configure: options => options.AutoConnect = true);

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().Be(ConnectionId);
        }

        // UTS: realtime/unit/RTN3/auto-connect-false-1
        [Fact]
        public async Task RTN3_AutoConnectFalse()
        {
            var connectionAttempted = false;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempted = true;
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedMessage());
            });

            var client = RealtimeClient(mockWs, configure: options => options.AutoConnect = false);

            // The spec's WAIT(500). This is the one shape where a real wait is the mechanism rather than a
            // settling hack: the assertion is about an event that must NOT occur, so there is no state to
            // await and no premise to poll for - only a window in which nothing may happen.
            await Task.Delay(500);

            connectionAttempted.Should().BeFalse();
            client.Connection.State.Should().Be(ConnectionState.Initialized);
        }

        // UTS: realtime/unit/RTN3/explicit-connect-after-false-2
        [Fact]
        public async Task RTN3_ExplicitConnectAfterFalse()
        {
            var connectionAttempted = false;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttempted = true;
                conn.RespondWithSuccess();
                conn.SendToClient(ConnectedMessage());
            });

            var client = RealtimeClient(mockWs, configure: options => options.AutoConnect = false);

            client.Connection.State.Should().Be(ConnectionState.Initialized);
            connectionAttempted.Should().BeFalse();

            client.Connect();

            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            connectionAttempted.Should().BeTrue();
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        /// <summary>
        /// The CONNECTED message all three setups share. The spec writes its own connection id and key
        /// rather than the template's, and the id is what the first test asserts on.
        /// </summary>
        private static JObject ConnectedMessage() =>
            ProtocolMessages.ConnectedMessage(
                connectionId: ConnectionId,
                connectionKey: ConnectionKey,
                connectionStateTtl: 120000,
                maxIdleInterval: 15000);
    }
}
