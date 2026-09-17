using System;
using FluentAssertions;
using IO.Ably.Utils;
using Xunit;

namespace IO.Ably.Tests.Utils
{
    public class ErrorPolicyTests
    {
        [Fact]
        public void HandleUnexpectedLogsCorrectly()
        {
            try
            {
                throw new ApplicationException("Troll in the Dungeon");
            }
            catch (Exception e)
            {
                const string expectedMessage = "Caught unexpected 'ApplicationException': 'Troll in the Dungeon'";

                var testLogger = new TestLogger(expectedMessage);
                ErrorPolicy.HandleUnexpected(e, testLogger);

                testLogger.MessageSeen.Should().BeTrue();
                testLogger.SeenCount.Should().Be(1);
                testLogger.FullMessage.Should().Be(expectedMessage);
            }
        }
    }
}
