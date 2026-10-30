using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests
{
    /// <summary>
    /// Self-tests of <see cref="UtsTestBase"/>'s teardown, run by calling <see cref="UtsTestBase.DisposeAsync"/>
    /// on a helper instance (a <see cref="Probe"/>) exactly as xUnit does after a test: clients are disposed
    /// and their timers stopped even when the body threw; CLOSE is only awaited from a state that can reach
    /// CLOSED; and a cleanup error, a mock handler error or an unexpected HTTP request fails the test.
    /// </summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "common")]
    public class TeardownTests
    {
        private readonly ITestOutputHelper _output;

        public TeardownTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TeardownDisposesClientsWhenTheBodyThrows()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            probe.InstallMock(mock);
            PubSubRealtimeClient client = null;

            Func<Task> body = async () =>
            {
                client = probe.Realtime(new ClientOptions { Key = UtsUnitTestBase.ValidKey });
                await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
                throw new InvalidOperationException("the test body failed");
            };

            await body.Should().ThrowAsync<InvalidOperationException>();
            client.Disposed.Should().BeFalse();

            await probe.DisposeAsync();

            client.Disposed.Should().BeTrue("teardown disposes every client the factory built, whatever the body did");
            await UtsWait.PollUntil(
                () => mock.EventsOfType(MockEventType.ClientClose).Count == 1,
                description: "the disposed client to close its WebSocket");
        }

        [Fact]
        public async Task TeardownStopsTheClientsTimers()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            probe.EnableFakeTimers();
            var attempts = 0;
            var mock = new MockWebSocket(attempt =>
            {
                if (++attempts == 1)
                {
                    attempt.RespondWithConnected();
                }
                else
                {
                    attempt.RespondWithRefused();
                }
            });
            probe.InstallMock(mock);
            var options = new ClientOptions { Key = UtsUnitTestBase.ValidKey, FallbackHosts = new string[0] };
            var client = probe.Realtime(options);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var states = UtsRecord.ConnectionStates(client.Connection);
            mock.SimulateDisconnect();

            // The drop retries instantly (attempt 2, refused); the refusal's DISCONNECTED then waits on the
            // fake-clock retry timer, so no third attempt can start until time is advanced.
            await UtsWait.PollUntil(
                () => mock.EventsOfType(MockEventType.ConnectionFailure).Count == 1
                    && states.Count(s => s == ConnectionState.Disconnected) == 2
                    && states.Last() == ConnectionState.Disconnected,
                description: "attempt 2's CONNECTION_FAILURE and the DISCONNECTED after it");
            await probe.ProcessPendingEvents();
            UtsAssert.ContainsInOrder(
                states,
                new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Disconnected });
            mock.ConnectionAttempts.Count.Should().Be(2, "nothing but the fake clock can start a third attempt");
            probe.Clock.PendingCount.Should().BeGreaterThan(0, "the DISCONNECTED retry timer is waiting");

            await probe.DisposeAsync();
            UtsTestBase.IsWorkflowStopped(client).Should().BeTrue("teardown waits until the disposed client's workflow has stopped");
            await probe.AdvanceTime(TimeSpan.FromTicks(options.DisconnectedRetryTimeout.Ticks * 4));

            mock.ConnectionAttempts.Count.Should().Be(2, "a disposed client's retry timer never fires a new attempt");
        }

        [Fact]
        public async Task TeardownDoesNotWaitForClosedFromInitializedOrFailed()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: true);
            var mock = new MockWebSocket(attempt => attempt.RespondWithError(ProtocolMessages.ErrorMessage(40000, "fatal for the test"), thenClose: false));
            probe.InstallMock(mock);
            var initialized = probe.Realtime(new ClientOptions { Key = UtsUnitTestBase.ValidKey, AutoConnect = false });
            var failed = probe.Realtime(new ClientOptions { Key = UtsUnitTestBase.ValidKey });
            await UtsWait.PollUntil(() => failed.Connection.State == ConnectionState.Failed);
            initialized.Connection.State.Should().Be(ConnectionState.Initialized);

            var stopwatch = Stopwatch.StartNew();
            await probe.DisposeAsync();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "it doesn't wait out the 5 s CloseTimeout for a CLOSED that can't come");
            initialized.Disposed.Should().BeTrue();
            failed.Disposed.Should().BeTrue();
            initialized.Connection.State.Should().Be(ConnectionState.Initialized, "no close() was attempted");
            mock.MessagesFromClientOfAction(ProtocolMessages.Close).Should().BeEmpty();
        }

        [Fact]
        public async Task TeardownClosesAConnectedClientAndWaitsForClosedWhenTheTierAsks()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: true);
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            mock.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            probe.InstallMock(mock);
            var client = probe.Realtime(new ClientOptions { Key = UtsUnitTestBase.ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var states = UtsRecord.ConnectionStates(client.Connection);

            await probe.DisposeAsync();

            mock.MessagesFromClientOfAction(ProtocolMessages.Close).Should().HaveCount(1);
            states.Should().Contain(ConnectionState.Closed, "teardown waited for CLOSED before disposing");
            client.Disposed.Should().BeTrue();
        }

        [Fact]
        public async Task TeardownRunsEveryCleanupLastInFirstOutAndFailsIfOneThrew()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            var ran = new List<string>();
            probe.AddCleanup(() =>
            {
                ran.Add("first registered");
                return Task.CompletedTask;
            });
            probe.AddCleanup(() => throw new InvalidOperationException("cleanup broke"));
            probe.AddCleanup(() =>
            {
                ran.Add("last registered");
                return Task.CompletedTask;
            });

            Func<Task> act = () => probe.DisposeAsync();

            var thrown = await act.Should().ThrowAsync<UtsTeardownException>();
            thrown.Which.Errors.Should().ContainSingle().Which.Message.Should().Be("cleanup broke");
            thrown.Which.Message.Should().Contain("cleanup broke");
            ran.Should().Equal("last registered", "first registered");
        }

        [Fact]
        public async Task TeardownFailsWhenAVerificationFails()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            probe.AddVerification(() => throw new InvalidOperationException("verification failed"));

            Func<Task> act = () => probe.DisposeAsync();

            (await act.Should().ThrowAsync<UtsTeardownException>()).Which.Message.Should().Contain("verification failed");
        }

        [Fact]
        public async Task TeardownFailsWhenAMockWebSocketHandlerThrew()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            var mock = new MockWebSocket(attempt => throw new InvalidOperationException("websocket handler broke"));
            probe.InstallMock(mock);
            var client = probe.Realtime(new ClientOptions { Key = UtsUnitTestBase.ValidKey, FallbackHosts = new string[0] });
            await UtsWait.PollUntil(() => mock.HandlerErrors.Count > 0);

            Func<Task> act = () => probe.DisposeAsync();

            (await act.Should().ThrowAsync<UtsTeardownException>()).Which.Message.Should().Contain("websocket handler broke");
            client.Disposed.Should().BeTrue("clients are torn down before the verification fails");
        }

        [Fact]
        public async Task TeardownFailsWhenAMockHttpClientHandlerThrew()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            var mock = new MockHttpClient(onRequest: request => throw new InvalidOperationException("http handler broke"));
            probe.InstallMock(mock);
            var rest = probe.Rest(new ClientOptions { Key = UtsUnitTestBase.ValidKey, FallbackHosts = new string[0] });
            Func<Task> call = () => rest.TimeAsync();
            await call.Should().ThrowAsync<Exception>();

            Func<Task> act = () => probe.DisposeAsync();

            (await act.Should().ThrowAsync<UtsTeardownException>()).Which.Message.Should().Contain("http handler broke");
        }

        [Fact]
        public async Task TeardownFailsWhenTheStrictHttpDefaultAnsweredAnUnexpectedRequest()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            var rest = probe.Rest(new ClientOptions { Key = UtsUnitTestBase.ValidKey, FallbackHosts = new string[0] });
            Func<Task> call = () => rest.TimeAsync();
            await call.Should().ThrowAsync<AblyException>();
            probe.UnexpectedRequests.Should().ContainSingle().Which.Path.Should().Be("/time");

            Func<Task> act = () => probe.DisposeAsync();

            (await act.Should().ThrowAsync<UtsTeardownException>()).Which.Message.Should().Contain("unexpected HTTP request").And.Contain("/time");
        }

        [Fact]
        public async Task TeardownPassesWhenNothingWentWrong()
        {
            var probe = new Probe(_output, closeClientsOnTeardown: false);
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            probe.InstallMock(mock);
            var client = probe.Realtime(new ClientOptions { Key = UtsUnitTestBase.ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            Func<Task> act = () => probe.DisposeAsync();

            await act.Should().NotThrowAsync();
            client.Disposed.Should().BeTrue();
        }

        /// <summary>A unit base instance the tests drive by hand, as xUnit would drive a test class.</summary>
        private sealed class Probe : UtsUnitTestBase
        {
            private readonly bool _closeClientsOnTeardown;

            internal Probe(ITestOutputHelper output, bool closeClientsOnTeardown)
                : base(output)
            {
                _closeClientsOnTeardown = closeClientsOnTeardown;
            }

            protected override bool CloseClientsOnTeardown => _closeClientsOnTeardown;

            protected override TimeSpan CloseTimeout => TimeSpan.FromSeconds(5);

            internal void AddCleanup(Func<Task> cleanup) => OnTeardown(cleanup);

            internal void AddVerification(Action verification) => OnVerify(verification);
        }
    }
}
