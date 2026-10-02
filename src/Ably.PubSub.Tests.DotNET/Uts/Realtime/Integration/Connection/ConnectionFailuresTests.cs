using System;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Integration.Connection
{
    /// <summary>
    /// Derived from uts/realtime/integration/connection/connection_failures_test.md in
    /// ably/specification.
    ///
    /// Spec points: RTN14a, RTN14g
    ///
    /// Integration tier: nothing sits in front of the client, so both tests here depend on the real
    /// sandbox rejecting the credentials itself. That is the point of the file - the unit tier already
    /// covers the client-side state machine with a mocked transport, and what is left to show
    /// end-to-end is that the server's ERROR lands on Connection#errorReason and sends the connection
    /// to FAILED.
    ///
    /// Neither test needs a state recorder: FAILED is terminal, so unlike DISCONNECTED it is still the
    /// current state - with its error still on ErrorReason - after the wait returns.
    ///
    /// The spec's CLOSE_CLIENT(client) is automatic: SandboxRealtimeClient registers the client with
    /// UtsIntegrationTestBase, which disposes it after every test including when the test throws.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class ConnectionFailuresTests : UtsRealtimeIntegrationTestBase
    {
        /// <summary>The spec's "AWAIT_STATE client.connection.state == FAILED WITH timeout: 15s".</summary>
        private static readonly TimeSpan FailureTimeout = TimeSpan.FromSeconds(15);

        public ConnectionFailuresTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: realtime/integration/RTN14a/invalid-key-failed-0
        [Fact]
        public async Task RTN14a_InvalidKeyFailed()
        {
            // NOTE: the key is deliberately the spec's literal, not a sandbox key. It is a valid key
            // shape - ApiKey's format check passes it - so the rejection comes from the server rather
            // than from client-side validation, which is what makes the test an end-to-end one.
            var client = await SandboxRealtimeClient(
                key: "invalid.key:secret",
                configure: options =>
                {
                    options.AutoConnect = false;

                    // The spec's useBinaryProtocol: false. A no-op here - msgpack is compiled out
                    // - but set so the setup reads as the spec writes it.
                    options.UseBinaryProtocol = false;
                });

            client.Connect();

            await AwaitConnectionState(client.Connection, ConnectionState.Failed, FailureTimeout);

            client.Connection.State.Should().Be(ConnectionState.Failed);

            var error = client.Connection.ErrorReason;
            error.Should().NotBeNull();

            error.Code.Should().BeOneOf(
                new[] { 40005, 40101 },
                "RTN14a requires the server's credential error to be set on Connection#errorReason");

            error.StatusCode.Should().NotBeNull();
            ((int)error.StatusCode.Value).Should().BeOneOf(
                new[] { 401, 404 },
                "the spec admits either an unauthorized or a not-found status for an invalid key");
        }

        // UTS: realtime/integration/RTN14g/revoked-key-failed-0
        [Fact]
        public async Task RTN14g_RevokedKeyFailed()
        {
            // NOTE: the spec's own note - nothing is actually revoked. The key is syntactically valid
            // but names an app that does not exist, so the server's rejection is not a token error
            // (outside 40140-40149) and RTN14g rather than RTN14b is the rule under test.
            var client = await SandboxRealtimeClient(
                key: "nonexistent.keyname:keysecret",
                configure: options =>
                {
                    options.AutoConnect = false;
                    options.UseBinaryProtocol = false;
                });

            client.Connect();

            await AwaitConnectionState(client.Connection, ConnectionState.Failed, FailureTimeout);

            client.Connection.State.Should().Be(ConnectionState.Failed);

            var error = client.Connection.ErrorReason;
            error.Should().NotBeNull();

            // The spec's "code < 40140 OR code >= 40150", which is the complement of the token-error
            // block that RTN14b owns.
            error.Code.Should().NotBeInRange(
                40140,
                40149,
                "RTN14g covers an ERROR with an empty channel for reasons other than a token error");
        }
    }
}
