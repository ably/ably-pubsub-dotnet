using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Transport;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Unit
{
    /// <summary>Self-tests of <see cref="MockWebSocket"/> against <c>mock_websocket.md</c>.</summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "unit")]
    public class MockWebSocketTests : UtsUnitTestBase
    {
        public MockWebSocketTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task MockCloseNotifiesTheListenerAsynchronously()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var transport = (MockTransport)client.ConnectionManager.Transport;
            var recorder = new RecordingListener(forwardTo: null);
            transport.Listener = recorder;

            recorder.InsideCall.Value = true;
            transport.Close(suppressClosedEvent: false);
            recorder.InsideCall.Value = false;

            mock.EventsOfType(MockEventType.ClientClose).Should().HaveCount(1, "CLIENT_CLOSE is recorded when close() is called");
            var closed = await UtsWait.WithTimeout(recorder.FirstEvent(TransportState.Closed), TimeSpan.FromSeconds(5), "the Closed transport event");
            closed.Synchronous.Should().BeFalse("onClose must not run inside close() (mock_websocket.md, Test Isolation)");
        }

        [Fact]
        public async Task ConnectedIsDeliveredAfterTheConnectionCompletes()
        {
            var mock = new MockWebSocket();
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            RecordingListener recorder = null;
            TransportState stateWhenAnswered = TransportState.Initialized;
            var eventsWhenAnswered = new List<MockEventType>();
            mock.OnConnectionAttempt = attempt =>
            {
                var transport = (MockTransport)client.ConnectionManager.Transport;
                recorder = new RecordingListener(transport.Listener);
                transport.Listener = recorder;
                recorder.InsideCall.Value = true;
                attempt.RespondWithSuccess(ProtocolMessages.ConnectedMessage());
                recorder.InsideCall.Value = false;
                stateWhenAnswered = transport.State;
                eventsWhenAnswered.AddRange(mock.Events.Select(e => e.Type));
            };

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            stateWhenAnswered.Should().Be(TransportState.Connected, "the connection completes before respond_with_success returns");
            eventsWhenAnswered.Should().Equal(MockEventType.ConnectionAttempt, MockEventType.ConnectionSuccess, MockEventType.MessageToClient);
            var calls = recorder.Calls.ToList();
            calls.Select(c => c.Description).Should().Equal("event:Connected", "data:Connected");
            calls.Should().OnlyContain(c => !c.Synchronous, "both reach the SDK asynchronously, after respond_with_success returned");
        }

        [Fact]
        public async Task RawUntypedFrameReachesTheClientAndTheConnectionSurvives()
        {
            const string name = "raw-frame";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var states = UtsRecord.ConnectionStates(client.Connection);

            mock.SendToClientRaw("{\"action\":99,\"unknownField\":{\"nested\":[1,2,3]},\"id\":\"raw-1\"}");
            await ProcessPendingEvents();

            var sent = mock.EventsOfType(MockEventType.MessageToClient).Last().Json;
            ((int)sent["action"]).Should().Be(99);
            sent["unknownField"]["nested"].Should().HaveCount(3, "unknown fields reach the wire untouched");
            mock.HandlerErrors.Should().BeEmpty("the SDK accepted the unknown action without throwing");
            states.Should().BeEmpty("an unknown action doesn't change the connection state");

            // The connection still works: a known frame after it is processed.
            var channel = client.Channels.Get(name);
            channel.Attach();
            await mock.AwaitClientMessage(ProtocolMessages.Attach);
            mock.SendToClient(ProtocolMessages.AttachedMessage(name));
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Fact]
        public async Task DnsErrorOutcomeDrivesTheClientToDisconnected()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithDnsError());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] });

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Disconnected);

            var failure = mock.EventsOfType(MockEventType.ConnectionFailure).First().Data;
            failure.Should().BeOfType<SocketException>().Which.SocketErrorCode.Should().Be(SocketError.HostNotFound);
            mock.EventsOfType(MockEventType.ConnectionSuccess).Should().BeEmpty();
        }

        [Fact]
        public async Task TimeoutOutcomeLeavesTheClientToItsConnectTimerOnTheFakeClock()
        {
            EnableFakeTimers();
            var mock = new MockWebSocket(attempt => attempt.RespondWithTimeout());
            InstallMock(mock);
            var options = new ClientOptions { Key = ValidKey, FallbackHosts = new string[0], RealtimeRequestTimeout = TimeSpan.FromSeconds(5) };
            var client = Realtime(options);
            await UtsWait.PollUntil(() => mock.EventsOfType(MockEventType.ConnectionFailure).Count == 1);
            await ProcessPendingEvents();

            client.Connection.State.Should().Be(ConnectionState.Connecting, "a timed-out attempt tells the SDK nothing");
            mock.EventsOfType(MockEventType.ConnectionFailure).Single().Data.Should().BeOfType<TimeoutException>();

            var states = UtsRecord.ConnectionStates(client.Connection);

            await AdvanceTime(options.RealtimeRequestTimeout);

            // The connect timer fired on the fake clock: DISCONNECTED (then the SDK's instant retry).
            UtsAssert.ContainsInOrder(states, new[] { ConnectionState.Disconnected });
            mock.ConnectionAttempts.Should().HaveCount(2);
        }

        [Fact]
        public async Task AwaitRegisteredBeforeTheAttemptTakesPrecedenceOverTheHandler()
        {
            var handlerCalls = 0;
            var mock = new MockWebSocket(attempt =>
            {
                Interlocked.Increment(ref handlerCalls);
                attempt.RespondWithConnected();
            });
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            var awaited = mock.AwaitConnectionAttempt();
            client.Connect();
            var attempt = await awaited;

            handlerCalls.Should().Be(0, "a waiter registered before the attempt takes it and the handler isn't called");
            attempt.Answered.Should().BeFalse();
            attempt.RespondWithConnected();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // With no waiter registered, the next attempt goes to the handler.
            var changes = UtsRecord.ConnectionStates(client.Connection);
            mock.SimulateDisconnect();
            await UtsWait.PollUntil(() => changes.Contains(ConnectionState.Connected));
            handlerCalls.Should().Be(1);
        }

        [Fact]
        public async Task AttemptTheHandlerLeavesUnansweredIsParkedForTheNextAwait()
        {
            PendingWebSocketConnection seenByHandler = null;
            var mock = new MockWebSocket(attempt => seenByHandler = attempt);
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.PollUntil(() => seenByHandler != null);

            var parked = await mock.AwaitConnectionAttempt();

            parked.Should().BeSameAs(seenByHandler);
            parked.RespondWithConnected();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
        }

        [Fact]
        public async Task AParkedAttemptAnsweredMeanwhileIsSkippedByTheNextAwait()
        {
            // The handler leaves the attempt unanswered when it returns (so it is parked), then answers it
            // later, as a SCHEDULE_AFTER(...) respond_with_success does.
            var seenByHandler = new CaptureList<PendingWebSocketConnection>();
            var mock = new MockWebSocket(seenByHandler.Add);
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.PollUntil(() => seenByHandler.Count == 1);
            var first = seenByHandler.Single();
            first.RespondWithConnected();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var next = mock.AwaitConnectionAttempt();

            next.IsCompleted.Should().BeFalse("the parked attempt was answered meanwhile, so the await waits for the next one");
            mock.SimulateDisconnect();
            var second = await next;
            second.Should().NotBeSameAs(first);
            second.Should().BeSameAs(mock.ConnectionAttempts[1]);
            second.Answered.Should().BeFalse();
            second.RespondWithConnected();
            await UtsWait.PollUntil(() => client.Connection.State == ConnectionState.Connected && mock.Connections.Count == 2);
        }

        [Fact]
        public async Task AParkedAttemptTheClientAbandonedIsSkippedByTheNextAwait()
        {
            EnableFakeTimers();
            var mock = new MockWebSocket();
            InstallMock(mock);
            var options = new ClientOptions { Key = ValidKey, FallbackHosts = new string[0], RealtimeRequestTimeout = TimeSpan.FromSeconds(5) };
            var client = Realtime(options);
            await UtsWait.PollUntil(() => mock.ConnectionAttempts.Count == 1);
            var first = mock.ConnectionAttempts[0];

            // Nobody answers: the connect timer fires, the client disposes the transport and retries.
            await AdvanceTime(options.RealtimeRequestTimeout);
            await UtsWait.PollUntil(() => mock.ConnectionAttempts.Count == 2);
            first.Abandoned.Should().BeTrue("the client gave up on the first attempt");
            first.Answered.Should().BeFalse();

            var live = await mock.AwaitConnectionAttempt();

            live.Should().NotBeSameAs(first, "an abandoned parked attempt is skipped");
            live.Should().BeSameAs(mock.ConnectionAttempts[1]);
            live.RespondWithConnected();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
        }

        [Fact]
        public async Task HandlersAndAwaitsSeeTheSameClientMessages()
        {
            const string name = "mixing";
            var handled = new CaptureList<ProtocolMessage>();
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected(), onMessageFromClient: handled.Add);
            mock.OnMessage(ProtocolMessages.Attach, ProtocolMessages.AttachedMessage(name));
            mock.OnMessage(ProtocolMessages.Detach, ProtocolMessages.DetachedMessage(name));
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(name);

            (await channel.AttachAsync()).IsSuccess.Should().BeTrue("the added on_message handler answered the ATTACH");
            var awaitedAttach = await mock.AwaitClientMessage(ProtocolMessages.Attach);

            awaitedAttach.Should().BeSameAs(handled.Single(m => (int)m.Action == ProtocolMessages.Attach), "an await doesn't consume what a handler saw");

            var next = mock.AwaitNextMessageFromClient();
            next.IsCompleted.Should().BeFalse("await_next_message_from_client only sees messages sent after the call");
            channel.Detach();
            ((int)(await next).Action).Should().Be(ProtocolMessages.Detach);
            await UtsWait.AwaitChannelState(channel, ChannelState.Detached);
        }

        [Fact]
        public async Task EveryMockEventTypeIsEmittedInOrder()
        {
            EnableFakeTimers();
            const string name = "every-event";
            var attempts = 0;
            var mock = new MockWebSocket(attempt =>
            {
                if (++attempts == 2)
                {
                    attempt.RespondWithRefused();
                }
                else
                {
                    attempt.RespondWithConnected();
                }
            });
            mock.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());
            InstallMock(mock);
            var options = new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] };
            var client = Realtime(options);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Channels.Get(name).Attach();
            await mock.AwaitClientMessage(ProtocolMessages.Attach);
            mock.SendPingFrame();
            mock.SimulateDisconnect();
            var changes = UtsRecord.ConnectionStateChanges(client.Connection);
            await UtsWait.PollUntil(() => attempts == 2 && client.Connection.State == ConnectionState.Disconnected);
            await ProcessPendingEvents();
            await AdvanceTime(changes.Last(c => c.Current == ConnectionState.Disconnected).RetryIn.Value);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var clientClose = mock.AwaitClientClose();
            client.Close();
            await clientClose;
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);

            var types = mock.Events.Select(e => e.Type).ToList();
            UtsAssert.ContainsInOrder(
                types,
                new[]
                {
                    MockEventType.ConnectionAttempt,
                    MockEventType.ConnectionSuccess,
                    MockEventType.MessageToClient,
                    MockEventType.MessageFromClient,
                    MockEventType.PingFrame,
                    MockEventType.ServerDisconnect,
                    MockEventType.ConnectionAttempt,
                    MockEventType.ConnectionFailure,
                    MockEventType.ConnectionAttempt,
                    MockEventType.ConnectionSuccess,
                    MockEventType.MessageFromClient,
                    MockEventType.ClientClose,
                });
            types.Distinct().Should().BeEquivalentTo(
                Enum.GetValues(typeof(MockEventType)).Cast<MockEventType>(),
                "every declared event type is emitted");
            mock.Events.Select(e => e.Timestamp).Should().BeInAscendingOrder("timestamps follow the timeline");
        }

        [Fact]
        public async Task ProcessPendingEventsRunsAQueuedDeliveryWithNoRealDelay()
        {
            var clock = EnableFakeTimers();
            const string name = "pending-events";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(name);
            channel.Attach();
            await mock.AwaitClientMessage(ProtocolMessages.Attach);
            var fakeNow = clock.Now;

            mock.SendToClient(ProtocolMessages.AttachedMessage(name));
            var stopwatch = Stopwatch.StartNew();
            await ProcessPendingEvents();
            stopwatch.Stop();

            channel.State.Should().Be(ChannelState.Attached, "the posted ATTACHED was handed to the SDK and processed");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "the drain yields; it doesn't sleep");
            clock.Now.Should().Be(fakeNow, "processing pending events doesn't move fake time");
        }

        /// <summary>
        /// An <see cref="ITransportListener"/> that records each call, and whether it ran inside the test's
        /// call (<see cref="InsideCall"/> is per thread, so a call made on another thread reads false).
        /// </summary>
        private sealed class RecordingListener : ITransportListener
        {
            private readonly ITransportListener _forwardTo;
            private readonly CaptureList<Call> _calls = new CaptureList<Call>();
            private readonly List<KeyValuePair<TransportState, TaskCompletionSource<Call>>> _waiters =
                new List<KeyValuePair<TransportState, TaskCompletionSource<Call>>>();

            internal RecordingListener(ITransportListener forwardTo)
            {
                _forwardTo = forwardTo;
            }

            internal ThreadLocal<bool> InsideCall { get; } = new ThreadLocal<bool>();

            internal IReadOnlyList<Call> Calls => _calls.Snapshot();

            public void OnTransportDataReceived(RealtimeTransportData data)
            {
                var action = (ProtocolMessage.MessageAction)(int)JObject.Parse(data.Text)["action"];
                _calls.Add(new Call("data:" + action, InsideCall.Value));
                _forwardTo?.OnTransportDataReceived(data);
            }

            public void OnTransportEvent(Guid transportId, TransportState state, Exception exception = null)
            {
                var call = new Call("event:" + state, InsideCall.Value);
                _calls.Add(call);
                lock (_waiters)
                {
                    foreach (var waiter in _waiters.Where(w => w.Key == state))
                    {
                        waiter.Value.TrySetResult(call);
                    }
                }

                _forwardTo?.OnTransportEvent(transportId, state, exception);
            }

            internal Task<Call> FirstEvent(TransportState state)
            {
                var waiter = new TaskCompletionSource<Call>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_waiters)
                {
                    var seen = _calls.FirstOrDefault(c => c.Description == "event:" + state);
                    if (seen != null)
                    {
                        return Task.FromResult(seen);
                    }

                    _waiters.Add(new KeyValuePair<TransportState, TaskCompletionSource<Call>>(state, waiter));
                }

                return waiter.Task;
            }

            internal sealed class Call
            {
                internal Call(string description, bool synchronous)
                {
                    Description = description;
                    Synchronous = synchronous;
                }

                internal string Description { get; }

                internal bool Synchronous { get; }
            }
        }
    }
}
