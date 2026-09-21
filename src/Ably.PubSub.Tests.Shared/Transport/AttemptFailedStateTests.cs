using IO.Ably.Realtime;
using IO.Ably.Transport;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Transport
{
    public class AttemptFailedStateTests
    {
        [Fact]
        public void PropertiesCorrespondToErrorInfoConstructor()
        {
            const ConnectionState cs = ConnectionState.Closed;

            var o = new AttemptFailedState(cs, ErrorInfo.ReasonDisconnected);

            o.Error.Should().Be(ErrorInfo.ReasonDisconnected);
            o.Exception.Should().BeNull();
            o.State.Should().Be(cs);
        }

        [Fact]
        public void PropertiesCorrespondToExceptionConstructor()
        {
            const ConnectionState cs = ConnectionState.Closed;
            var e = new AblyException("Something wicked this way comes");

            var o = new AttemptFailedState(cs, e);

            o.Error.Should().BeNull();
            o.Exception.Should().Be(e);
            o.State.Should().Be(cs);
        }
    }
}
