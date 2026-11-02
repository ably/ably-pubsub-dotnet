using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Realtime;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Infrastructure
{
    public class ConnectionAwaiterTests : AblyRealtimeSpecs
    {
        public ConnectionAwaiterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task Wait_WhenAlreadyInAwaitedState_ShouldReturnImmediatelyAndLeaveNoListener()
        {
            var client = await GetConnectedClient();
            var listenersBefore = client.Connection.GetState().Count;

            var elapsed = await new ConnectionAwaiter(client.Connection, ConnectionState.Connected).Wait(TimeSpan.FromSeconds(5));

            elapsed.Should().Be(TimeSpan.Zero);
            client.Connection.GetState().Count.Should().Be(listenersBefore);
        }

        [Fact]
        public async Task Wait_WhenAwaitedStateIsReached_ShouldCompleteAndRemoveListener()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);
            var listenersBefore = client.Connection.GetState().Count;

            var wait = new ConnectionAwaiter(client.Connection, ConnectionState.Connected).Wait(TimeSpan.FromSeconds(10));
            client.Connect();
            client.FakeProtocolMessageReceived(ConnectedProtocolMessage);
            await wait;

            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.Connection.GetState().Count.Should().Be(listenersBefore);
        }

        [Fact]
        public async Task Wait_WhenTimeoutExceeded_ShouldThrowTimeoutExceptionAndRemoveListener()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);
            var listenersBefore = client.Connection.GetState().Count;

            Func<Task> wait = () => new ConnectionAwaiter(client.Connection, ConnectionState.Connected).Wait(TimeSpan.FromMilliseconds(200));

            var exception = await wait.Should().ThrowAsync<TimeoutException>();
            exception.Which.Message.Should().Contain("Expected 'Connected' but current state was");
            client.Connection.GetState().Count.Should().Be(listenersBefore);
        }

        [Fact]
        public async Task Wait_WhenStateIsReachedWhileWaitStarts_ShouldNeverMissTheStateChange()
        {
            // Queue the CONNECTED message from another thread at (almost) the same moment Wait starts so that
            // the state change can land anywhere in Wait, including between its state check and its subscription.
            // Each iteration disposes its client: GetClientWithFakeTransport doesn't register clients for teardown,
            // so otherwise every iteration would leave a live client and realtime workflow behind.
            for (var i = 0; i < 200; i++)
            {
                var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);
                try
                {
                    client.Connect();

                    var message = Task.Run(() => client.FakeProtocolMessageReceived(ConnectedProtocolMessage));
                    await new ConnectionAwaiter(client.Connection, ConnectionState.Connected).Wait(TimeSpan.FromSeconds(10));
                    await message;

                    client.Connection.State.Should().Be(ConnectionState.Connected, $"iteration {i}");
                }
                finally
                {
                    client.Dispose();
                }
            }
        }
    }
}
