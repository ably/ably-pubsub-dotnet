using System;
using System.Runtime.CompilerServices;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.InfraTests
{
    /// <summary>Self-tests of <see cref="UtsAssert"/>: CONTAINS_IN_ORDER semantics and caller-attributed failures.</summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "common")]
    public class UtsAssertTests
    {
        private static readonly ConnectionState[] Expected =
        {
            ConnectionState.Disconnected,
            ConnectionState.Connecting,
            ConnectionState.Connected,
        };

        [Fact]
        public void ContainsInOrderPassesWhenOtherItemsAreInterleaved()
        {
            var observed = new[]
            {
                ConnectionState.Connecting,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connected,
            };

            Action act = () => UtsAssert.ContainsInOrder(observed, Expected);

            act.Should().NotThrow();
        }

        [Fact]
        public void ContainsInOrderPassesWhenAnExpectedItemRepeats()
        {
            var observed = new[]
            {
                ConnectionState.Disconnected,
                ConnectionState.Disconnected,
                ConnectionState.Connecting,
                ConnectionState.Connecting,
                ConnectionState.Connected,
                ConnectionState.Connected,
            };

            Action act = () => UtsAssert.ContainsInOrder(observed, Expected);

            act.Should().NotThrow();
        }

        [Fact]
        public void ContainsInOrderFailsWhenTheOrderIsBroken()
        {
            var observed = new[]
            {
                ConnectionState.Connecting,
                ConnectionState.Disconnected,
                ConnectionState.Connected,
            };

            Action act = () => UtsAssert.ContainsInOrder(observed, Expected);

            act.Should().Throw<XunitException>().WithMessage("*to contain in order*only the first 1 matched*");
        }

        [Fact]
        public void ContainsInOrderFailsWhenAnExpectedItemIsMissing()
        {
            var observed = new[] { ConnectionState.Disconnected, ConnectionState.Connected };

            Action act = () => UtsAssert.ContainsInOrder(observed, Expected);

            act.Should().Throw<XunitException>();
        }

        [Fact]
        public void ContainsInOrderFailureReportsTheCallersLine()
        {
            var observed = new[] { ConnectionState.Connected };
            int line = 0;

            Action act = () =>
            {
                line = NextLine() + 1;
                UtsAssert.ContainsInOrder(observed, Expected);
            };

            act.Should().Throw<XunitException>().Which.Message.Should().StartWith($"UtsAssertTests.cs:{line}: ");
        }

        [Fact]
        public void FailReportsTheCallersLineAndMessage()
        {
            int line = 0;

            Action act = () =>
            {
                line = NextLine() + 1;
                UtsAssert.Fail("the spec says so");
            };

            act.Should().Throw<XunitException>().Which.Message.Should().Be($"UtsAssertTests.cs:{line}: the spec says so");
        }

        [Fact]
        public void FailureKeepsTheCause()
        {
            var cause = new InvalidOperationException("root cause");

            var failure = UtsAssert.Failure("helper failed", "/some/dir/CallerTests.cs", 42, cause);

            failure.Should().BeOfType<UtsAssert.UtsHelperException>();
            ((UtsAssert.UtsHelperException)failure).Cause.Should().BeSameAs(cause);
            failure.Message.Should().StartWith("CallerTests.cs:42: helper failed").And.Contain("root cause");
        }

        /// <summary>The line it is called from.</summary>
        private static int NextLine([CallerLineNumber] int line = 0) => line;
    }
}
