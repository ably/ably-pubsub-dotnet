using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Trait("spec", "RTE1")]
    [Trait("spec", "RTN4")]
    public class EventEmitterSpecs : AblyRealtimeSpecs
    {
        [Fact]
        [Trait("spec", "RTN4a")]
        public void EmittedEventTypesShouldBe()
        {
            var states = Enum.GetNames(typeof(ConnectionEvent));
            states.Should().BeEquivalentTo(
                "Initialized",
                "Connecting",
                "Connected",
                "Disconnected",
                "Suspended",
                "Closing",
                "Closed",
                "Failed",
                "Update");
        }

        [Fact]
        [Trait("spec", "RTN4b")]
        [Trait("spec", "RTN4d")]
        [Trait("spec", "RTN4e")]
        [Trait("spec", "TA1")]
        public async Task ANewConnectionShouldRaiseConnectingAndConnectedEvents()
        {
            var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);
            var states = new List<ConnectionStateChange>();
            client.Connection.On(args =>
            {
                // (TA1) Whenever the connection state changes,
                // a ConnectionStateChange object is emitted on the Connection object
                args.Should().BeOfType<ConnectionStateChange>();
                states.Add(args);
            });

            client.Connect();

            // SendConnected Message
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected));

            await client.WaitForState(ConnectionState.Connected);

            states.Select(x => x.Current).Should().BeEquivalentTo(new[] { ConnectionState.Connecting, ConnectionState.Connected });

            states[1].Previous.Should().Be(ConnectionState.Connecting);
            states[1].Current.Should().Be(ConnectionState.Connected);
            states[1].Event.Should().Be(ConnectionEvent.Connected);
        }

        [Fact]
        [Trait("spec", "RTN4c")]
        [Trait("spec", "RTN4d")]
        [Trait("spec", "RTN4e")]
        [Trait("spec", "TA1")]
        public async Task WhenClosingAConnection_ItShouldRaiseClosingAndClosedEvents()
        {
            var client = await GetConnectedClient();
            var states = new List<ConnectionStateChange>();
            client.Connection.On((args) =>
            {
                // (TA1) Whenever the connection state changes,
                // a ConnectionStateChange object is emitted on the Connection object
                args.Should().BeOfType<ConnectionStateChange>();
                states.Add(args);
            });

            LastCreatedTransport.SetSendAction(message =>
            {
                if (message.Original.Action == ProtocolMessage.MessageAction.Close)
                {
                    LastCreatedTransport.Close(false);
                }
            });

            client.Close();

            await client.WaitForState(ConnectionState.Closed);

            states.Count.Should().Be(2);
            (from s in states select s.Current).Should().BeEquivalentTo(new[] { ConnectionState.Closing, ConnectionState.Closed });
            states[1].Previous.Should().Be(ConnectionState.Closing);
            states[1].Current.Should().Be(ConnectionState.Closed);
            states[1].Event.Should().Be(ConnectionEvent.Closed);
            client.Connection.State.Should().Be(ConnectionState.Closed);
        }

        [Fact]
        [Trait("spec", "RTN4f")]
        [Trait("spec", "TA1")]
        [Trait("spec", "TA3")]
        [Trait("spec", "TA5")]
        [Trait("sandboxTest", "needed")]
        public async Task WithAConnectionError_ShouldRaiseChangeStateEventWithError()
        {
            var client = GetClientWithFakeTransport();

            ConnectionStateChange stateChange = null;
            var awaiter = new TaskCompletionAwaiter();
            client.Connection.On(ConnectionEvent.Failed, state =>
            {
                stateChange = state;
                awaiter.Tick();
            });

            var expectedError = new ErrorInfo("fake error");
            client.FakeProtocolMessageReceived(
                new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = expectedError });

            await awaiter.Task;

            stateChange.HasError.Should().BeTrue();
            stateChange.Reason.Should().Be(expectedError);

            // RTN14g, expect FAILED
            stateChange.Event.Should().Be(ConnectionEvent.Failed);
        }

        [Fact]
        [Trait("spec", "RTE3")]
        public void WithEventEmitter_WhenOn_ListenerRegistersForRepeatedEvents()
        {
            var em = new TestEventEmitter(DefaultLogger.LoggerInstance);
            string message = string.Empty;
            int counter = 0;
            int handledCounter1 = 0;
            int handledCounter2 = 0;
            bool handled1 = false;
            bool handled2 = false;

            // no event/state argument, catch all
            void Handler1(TestEventEmitterArgs args)
            {
                counter++;
                handledCounter1++;
                message = args.Message;
                handled1 = true;
            }

            void Handler2(TestEventEmitterArgs args)
            {
                counter++;
                handledCounter2++;
                message = args.Message;
                handled2 = true;
            }

            void Reset()
            {
                handled1 = false;
                handled2 = false;
            }

            // add Handler1 as a catch all
            em.On(Handler1);
            em.DoDummyEmit(1, "on");
            handled1.Should().BeTrue();
            message.Should().Be("on");

            // currently one listener
            counter.Should().Be(1);
            handledCounter1.Should().Be(1);
            handledCounter2.Should().Be(0);
            Reset();

            // add another Handler1 as a catch all
            em.On(Handler1);
            em.DoDummyEmit(1, "on");
            handled1.Should().BeTrue();
            counter.Should().Be(3);
            handledCounter1.Should().Be(3);

            // only catch 1 events
            em.On(1, Handler2);
            em.DoDummyEmit(2, "on");

            // handled2 should be false here
            handled1.Should().BeTrue();
            handled2.Should().BeFalse();

            // now there should be 3 listeners, the first is 2 are catch all
            handledCounter1.Should().Be(5);
            handledCounter2.Should().Be(0);
            counter.Should().Be(5);
            Reset();

            em.DoDummyEmit(1, "on");
            handled1.Should().BeTrue();
            handled2.Should().BeTrue();
            handledCounter1.Should().Be(7);
            handledCounter2.Should().Be(1);

            // still 2 listeners and we sent a 1 event which both should handle
            counter.Should().Be(8);
            Reset();
        }

        [Fact]
        [Trait("spec", "RTE4")]
        public void WithEventEmitter_WhenOnce_ListenerRegistersForOneEvent()
        {
            var em = new TestEventEmitter(DefaultLogger.LoggerInstance);
            bool t = false;
            bool tt = false;
            string message = string.Empty;
            int counter = 0;

            void Reset()
            {
                t = false;
                tt = false;
            }

            void Handler1(TestEventEmitterArgs args)
            {
                counter++;
                message = args.Message;
                t = true;
            }

            void Handler2(TestEventEmitterArgs args)
            {
                counter++;
                message = args.Message;
                tt = true;
            }

            // no event/state argument, catch all
            em.Once(Handler1);
            em.Once(Handler1);
            em.DoDummyEmit(1, "once");
            t.Should().BeTrue();
            message.Should().Be("once");
            counter.Should().Be(2);
            Reset();

            // only catch 1 events
            em.Once(1, Handler2);
            em.DoDummyEmit(2, "on");

            // no events should be handled, t & tt should be false here
            t.Should().BeFalse();
            tt.Should().BeFalse();

            // there are 2 listeners and the first is catch all
            // but the first should have already handled an event and de-registered
            // so the count remains the same
            counter.Should().Be(2);
            Reset();
            em.DoDummyEmit(1, "on");

            // t should not have been set, tt should complete for the first time
            t.Should().BeFalse();
            tt.Should().BeTrue();

            // still 2 listeners and we sent a 1 event which second should handle
            counter.Should().Be(3);
            Reset();
            em.DoDummyEmit(1, "on");

            // t & tt should both be false
            t.Should().BeFalse();
            tt.Should().BeFalse();

            // handlers should be de-registered so the count should remain at 2
            counter.Should().Be(3);
        }

        [Fact]
        [Trait("spec", "RTE5")]
        public void WithEventEmitter_WhenOff_DeRegistersForEvents()
        {
            var em = new TestEventEmitter(DefaultLogger.LoggerInstance);
            string message = string.Empty;
            int counter = 0;

            void Reset()
            {
                counter = 0;
            }

            void Listener1(TestEventEmitterArgs args)
            {
                counter++;
                message = args.Message;
            }

            void Listener2(TestEventEmitterArgs args)
            {
                counter++;
                message = args.Message;
            }

            // if called with no arguments, it removes all registrations, for all events and listeners
            em.On(1, Listener1);
            em.On(2, Listener1);
            em.On(3, Listener1);
            em.DoDummyEmit(1, "off");
            message.Should().Be("off");
            counter.Should().Be(1);
            Reset();

            em.Off();
            em.DoDummyEmit(1, "off");
            em.DoDummyEmit(2, "off");
            em.DoDummyEmit(3, "off");
            counter.Should().Be(0);
            Reset();

            em.On(1, Listener1);
            em.On(2, Listener2);
            em.On(1, Listener2);
            em.DoDummyEmit(1, "off");
            message.Should().Be("off");
            counter.Should().Be(2);
            Reset();

            em.Off();
            em.DoDummyEmit(1, "off");
            em.DoDummyEmit(2, "off");
            counter.Should().Be(0);
            Reset();

            // if called only with a listener, it removes all registrations matching the given listener,
            // regardless of whether they are associated with an event or not
            em.On(1, Listener1);
            em.On(1, Listener2);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(2);
            Reset();

            em.Off(Listener2);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(1);
            Reset();

            em.Off(Listener1);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(0);

            // If called with a specific event and a listener,
            // it removes all registrations that match both the given listener and the given event
            em.On(1, Listener1);
            em.On(1, Listener2);
            em.On(2, Listener1);
            Reset();

            em.DoDummyEmit(1, "off");
            counter.Should().Be(2);
            Reset();

            em.DoDummyEmit(2, "off");
            counter.Should().Be(1);
            Reset();

            // no handler for this event, so this should have no effect
            em.Off(3, Listener1);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(2);
            Reset();

            // no handler for this listener, so this should have no effect
            em.Off(2, Listener2);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(2);
            Reset();

            // remove handler 1 of 2 remaining
            em.Off(1, Listener1);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(1);
            Reset();

            // remove the final handler
            em.Off(1, Listener2);
            em.DoDummyEmit(1, "off");
            counter.Should().Be(0);
        }

        /*
         It should test the order of listener invocation is the same as the order in which the listeners are added;
         it should test conditions such as what happens when emitting an event if listeners are added or removed within the listener callback itself.
         (that includes listeners removing themselves, removing other listeners, removing all listeners, adding a new listener, etc).
         */
        [Fact]
        [Trait("spec", "RTE6")]
        [Trait("spec", "RTE6a")]
        public void WithEventEmitter_WhenEmitsEvent_CallsListenersWithEventNameAndArguments()
        {
            var callList = new List<int>();

            var em = new TestEventEmitter(DefaultLogger.LoggerInstance);

            TestEventEmitterArgs listener1Args = null;
            void Listener1(TestEventEmitterArgs args)
            {
                callList.Add(1);
                em.Off(1, Listener2);
                em.On(1, Listener4);
                listener1Args = args;
            }

            void Listener2(TestEventEmitterArgs args)
            {
                callList.Add(2);
                em.Off(1, Listener3);
                throw new Exception("should not be hit");
            }

            void Listener3(TestEventEmitterArgs args)
            {
                callList.Add(3);
            }

            void Listener4(TestEventEmitterArgs args)
            {
                callList.Add(4);
            }

            void Listener5(TestEventEmitterArgs args)
            {
                callList.Add(5);
                em.Off(1, Listener5);
            }

            em.On(1, Listener1);
            em.On(1, Listener2);
            em.On(1, Listener3);
            em.On(1, Listener5);

            // Listener1 is called first, it subscribes Listener4
            // Listener2 is removed by Listener1, but should still be called
            // Listener3 is called
            // Listener4 is should not be called as it was added by Listener1
            // Listener5 is called
            em.DoDummyEmit(1, "emit1");
            callList.Count.Should().Be(4);
            callList[0].Should().Be(1);
            callList[1].Should().Be(2);
            callList[2].Should().Be(3);
            callList[3].Should().Be(5);
            listener1Args.Message.Should().Be("emit1");

            callList = new List<int>();

            // Listener1 is called first, it subscribes Listener4. Listener4 now has 2 subscriptions, but only 1 should fire here
            // Listener2 was already removed by Listener, so it is not called
            // Listener3 was removed by Listener2, it should not be called
            // Listener4 is called once
            // Listener5 is not called as it removed itself previously
            em.DoDummyEmit(1, "emit2");
            callList.Count.Should().Be(2);
            callList[0].Should().Be(1);
            callList[1].Should().Be(4);
            listener1Args.Message.Should().Be("emit2");

            callList = new List<int>();

            void Listener6(TestEventEmitterArgs args)
            {
                callList.Add(6);
                em.Off();
            }

            em.On(1, Listener6);
            em.DoDummyEmit(1, "emit3");
            callList.Count.Should().Be(4);
            callList[0].Should().Be(1);
            callList[1].Should().Be(4);
            callList[2].Should().Be(4);
            callList[3].Should().Be(6);
            listener1Args.Message.Should().Be("emit3");

            callList = new List<int>();

            // Listener6 removed all listeners
            em.DoDummyEmit(1, "emit4");
            callList.Count.Should().Be(0);
            listener1Args.Message.Should().Be("emit3");
        }

        [Fact]
        [Trait("spec", "RTE6")]
        public void WithEventEmitter_WhenExceptionRaised_ExceptionIsCaughtAndLogged()
        {
            const string exceptionMessage = "Listener1 exception";
            bool handled1 = false;
            bool handled2 = false;

            // test logger, logs to memory and set the MessageSeen property if
            // the message passed to the constructor is logged;
            var logger = new TestLogger(messageToTest: exceptionMessage);
            var em = new TestEventEmitter(logger);

            List<int> callOrder = new List<int>();
            void Listener1(TestEventEmitterArgs args)
            {
                handled1 = true;
                callOrder.Add(1);
                throw new Exception(exceptionMessage);
            }

            void Listener2(TestEventEmitterArgs args)
            {
                handled2 = true;
                callOrder.Add(2);
            }

            em.On(1, Listener1);
            em.On(1, Listener2);
            em.DoDummyEmit(1, string.Empty);
            handled1.Should().BeTrue();
            handled2.Should().BeTrue();
            logger.MessageSeen.Should().BeTrue();
            callOrder.Count.Should().Be(2);
            callOrder[0].Should().Be(1);
            callOrder[1].Should().Be(2);

            handled1 = handled2 = false;
            logger.Reset();
            callOrder = new List<int>();

            em.Once(1, Listener1);
            em.Once(1, Listener2);
            em.DoDummyEmit(1, string.Empty);
            handled1.Should().BeTrue();
            handled2.Should().BeTrue();
            callOrder.Count.Should().Be(4);

            // On is handled
            callOrder[0].Should().Be(1);
            callOrder[1].Should().Be(2);

            // then Once
            callOrder[2].Should().Be(1);
            callOrder[3].Should().Be(2);
            logger.MessageSeen.Should().BeTrue();
        }

        public EventEmitterSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private class TestEventEmitterArgs : EventArgs
        {
            public string Message { get; set; }
        }

        private class TestEventEmitter : EventEmitter<int, TestEventEmitterArgs>
        {
            public TestEventEmitter(ILogger logger)
                : base(logger)
            {
            }

            protected override Action<Action> NotifyClient => action => action();

            public void DoDummyEmit(int state, string message)
            {
                Emit(state, new TestEventEmitterArgs { Message = message });
            }
        }
    }
}
