using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Trait("spec", "RTN13")]
    public class ConnectionPingSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN13a")]
        public async Task OnHeartBeatMessageReceived_ShouldReturnElapsedTime()
        {
            SetNowFunc(() => DateTimeOffset.UtcNow);
            var client = await GetConnectedClient();

            FakeTransportFactory.LastCreatedTransport.SetSendAction(async message =>
            {
                NowAdd(TimeSpan.FromMilliseconds(100));
                if (message.Original.Action == ProtocolMessage.MessageAction.Heartbeat)
                {
                    await Task.Delay(1);
                    client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Heartbeat)
                    {
                        Id = message.Original.Id,
                    });
                }
            });

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeTrue();

            result.Value.Value.Should().BeGreaterThan(TimeSpan.FromMilliseconds(0));

            // reset
            SetNowFunc(() => DateTimeOffset.UtcNow);
        }

        [Fact]
        [Trait("spec", "RTN13b")]
        public async Task WithClosedOrFailedConnectionStates_ShouldReturnError()
        {
            var client = GetClientWithFakeTransport();

            client.Workflow.QueueCommand(SetClosedStateCommand.Create(new ErrorInfo()));
            await client.WaitForState(ConnectionState.Closed);

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(PingRequest.DefaultError);

            client.Workflow.QueueCommand(SetFailedStateCommand.Create(new ErrorInfo()));
            await client.WaitForState(ConnectionState.Failed);

            var resultFailed = await client.Connection.PingAsync();

            resultFailed.IsSuccess.Should().BeFalse();
            resultFailed.Error.Should().Be(PingRequest.DefaultError);
        }

        [Fact]
        [Trait("spec", "RTN13c")]
        public async Task WhenDefaultTimeoutExpiresWithoutReceivingHeartbeatMessage_ShouldFailWithTimeoutError()
        {
            var client = await GetConnectedClient(opts => opts.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000));

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeFalse();
            result.Error.StatusCode.Should().Be(HttpStatusCode.RequestTimeout);
        }

        public ConnectionPingSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
