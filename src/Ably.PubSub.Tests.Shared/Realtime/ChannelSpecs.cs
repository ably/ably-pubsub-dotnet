using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using IO.Ably.AcceptanceTests;
using IO.Ably.MessageEncoders;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;

using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    public class ChannelSpecs : AblyRealtimeSpecs
    {
        private readonly Action<ClientOptions> _switchBinaryOff = opts => opts.UseBinaryProtocol = false;

        public class GeneralSpecs : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RTL9")]
            [Trait("spec", "RTL9a")]
            public async Task ChannelPresenceShouldReturnAPresenceObject()
            {
                var (_, channel) = await GetClientAndChannel();
                channel.Presence.Should().BeOfType<Presence>();
            }

            public GeneralSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL2")]
        public class EventEmitterSpecs : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RTL2")]
            public void ValidateChannelEventAndChannelStateValues()
            {
                // ChannelState and ChannelEvent should have values that are aligned
                // to allow for casting between the two (with the exception of the Update event)
                // The assigned values are arbitrary, but should be unique per event/state pair
                ((int)ChannelEvent.Initialized).Should().Be(0);
                ((int)ChannelEvent.Attaching).Should().Be(1);
                ((int)ChannelEvent.Attached).Should().Be(2);
                ((int)ChannelEvent.Detaching).Should().Be(3);
                ((int)ChannelEvent.Detached).Should().Be(4);
                ((int)ChannelEvent.Suspended).Should().Be(5);
                ((int)ChannelEvent.Failed).Should().Be(6);
                ((int)ChannelEvent.Update).Should().Be(7);

                // each ChannelState should have an equivalent ChannelEvent
                ((int)ChannelState.Failed).Should().Be((int)ChannelEvent.Failed);
                ((int)ChannelState.Detached).Should().Be((int)ChannelEvent.Detached);
                ((int)ChannelState.Detaching).Should().Be((int)ChannelEvent.Detaching);
                ((int)ChannelState.Initialized).Should().Be((int)ChannelEvent.Initialized);
                ((int)ChannelState.Attached).Should().Be((int)ChannelEvent.Attached);
                ((int)ChannelState.Attaching).Should().Be((int)ChannelEvent.Attaching);
                ((int)ChannelState.Suspended).Should().Be((int)ChannelEvent.Suspended);
            }

            [Theory]
            [InlineData(ChannelEvent.Attaching)]
            [InlineData(ChannelEvent.Attached)]
            [InlineData(ChannelEvent.Detaching)]
            [InlineData(ChannelEvent.Detached)]
            [InlineData(ChannelEvent.Failed)]
            [InlineData(ChannelEvent.Suspended)]
            [Trait("spec", "RTL2a")]
            [Trait("spec", "RTL2b")]
            [Trait("spec", "RTL2d")]
            [Trait("spec", "TH1")]
            [Trait("spec", "TH2")]
            [Trait("spec", "TH5")]
            public async Task ShouldEmitTheFollowingStates(ChannelEvent channelEvent)
            {
                var chanName = "test".AddRandomSuffix();
                var client = await GetConnectedClient();
                var channel = client.Channels.Get(chanName);

                ChannelEvent sourceEvent = ChannelEvent.Update;
                ChannelState previousState = ChannelState.Failed;
                ChannelState newState = ChannelState.Initialized;
                channel.On(channelStateChange =>
                {
                    // RTL2d first argument should be an instance of ChannelStateChange
                    channelStateChange.Should().NotBeNull();
                    channelStateChange.Error.Should().BeNull();

                    // should be the state corresponding to the
                    // passed in ChannelEvent
                    // (which should be anything but Initialized)
                    newState = channelStateChange.Current;

                    // should always be Initialized
                    previousState = channelStateChange.Previous;

                    // TH5
                    sourceEvent = channelStateChange.Event;
                    Done();
                });

                ((RealtimeChannel)channel).SetChannelState((ChannelState)channelEvent);

                WaitOne();

                channel.State.Should().Be((ChannelState)channelEvent);
                newState.Should().Be((ChannelState)channelEvent);
                previousState.Should().Be(ChannelState.Initialized);
                sourceEvent.Should().Be(channelEvent);
            }

            [Theory]
            [InlineData(ProtocolMessage.MessageAction.Attached)]
            [InlineData(ProtocolMessage.MessageAction.Detached)]
            [Trait("spec", "RTL2d")]
            public async Task ShouldSetTheErrorReasonIfPresent(ProtocolMessage.MessageAction action)
            {
                var chanName = "test".AddRandomSuffix();
                var client = await GetConnectedClient();
                var channel = client.Channels.Get(chanName);

                ChannelState previousState = ChannelState.Failed;
                ChannelState newState = ChannelState.Initialized;
                channel.On(channelStateChange =>
                {
                    // RTL2d first argument should be an instance of ChannelStateChange
                    channelStateChange.Should().NotBeNull();
                    channelStateChange.Error.Should().NotBeNull();
                    channelStateChange.Error.Message.Should().Be(action.ToString());
                    channelStateChange.Error.Code.Should().Be((int)action);

                    // should be the state corresponding to the
                    // passed in ChannelEvent which should be
                    // anything but Initialized
                    newState = channelStateChange.Current;

                    // should always be Initialized
                    previousState = channelStateChange.Previous;
                    Done();
                });

                // any state change triggered by a ProtocolMessage that contains an error member
                // should populate the reason with that error in the corresponding state change event
                client.FakeProtocolMessageReceived(new ProtocolMessage(action, chanName)
                {
                    Error = new ErrorInfo(action.ToString(), (int)action)
                });

                WaitOne();

                channel.State.ToString().Should().Be(action.ToString());
                newState.ToString().Should().Be(action.ToString());
                previousState.Should().Be(ChannelState.Initialized);
            }

            [Fact]
            [Trait("spec", "RTL2c")]
            public async Task ShouldEmmitErrorWithTheErrorThatHasOccuredOnTheChannel()
            {
                var error = new ErrorInfo();
                ErrorInfo expectedError = null;
                var channel = await GetChannel();
                channel.On((args) =>
                {
                    expectedError = args.Error;
                    Done();
                });

                ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached, error);

                WaitOne();

                expectedError.Should().BeSameAs(error);
                channel.ErrorReason.Should().BeSameAs(error);
            }

            [Fact]
            [Trait("spec", "RTL2f")]
            [Trait("spec", "RTL2g")]
            [Trait("spec", "RTL12")]
            [Trait("spec", "TH2")]
            [Trait("spec", "TH3")]
            [Trait("spec", "TH4")]
            public async Task WhenAttachedProtocolMessageWithResumedFlagReceived_EmittedChannelStateChangeShouldIndicateResumed()
            {
                var client = await GetConnectedClient();
                var channel = client.Channels.Get("test");
                var tc = new TimeoutCallback<ChannelStateChange>(2000);

                channel.Once(ChannelEvent.Attached, tc.Wrap(csc =>
                {
                    // RTL2f
                    csc.Current.Should().Be(ChannelState.Attached);
                    csc.Previous.Should().Be(ChannelState.Initialized);
                    csc.Resumed.Should().BeFalse();
                }));

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached)
                {
                    Channel = "test",
                });

                var result = await tc.Task;
                result.Should().BeTrue("State change event should have been handled");
                channel.State.Should().Be(ChannelState.Attached);

                // reset
                tc = new TimeoutCallback<ChannelStateChange>(2000);

                // RTL2g / RTL12
                channel.Once(ChannelEvent.Update, tc.Wrap(csc =>
                {
                    // RTL2f, TH2, TH4
                    csc.Current.Should().Be(ChannelState.Attached);
                    csc.Previous.Should().Be(ChannelState.Attached);
                    csc.Resumed.Should().BeFalse();
                }));

                // Send another Attached message without the resume flag.
                // This should cause and Update event to be emitted.
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached, "test"));

                result = await tc.Task;
                result.Should().BeTrue("Update event should have been handled");
                channel.State.Should().Be(ChannelState.Attached);

                channel.Once(stateChange =>
                    throw new Exception("This should not be reached because resumed flag was set"));

                // send another Attached protocol message with the resumed flag set.
                // This should not trigger any channel event (per RTL12).
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached)
                {
                    Channel = "test",
                    Flags = 4, // resumed
                });

                await Task.Delay(2000);

                tc = new TimeoutCallback<ChannelStateChange>(500);

                // set detached so that the next Attached message with resume will pass
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Detached, "test"));

                channel.Once(ChannelEvent.Attached, tc.Wrap(csc =>
                {
                    // RTL2f, TH4
                    csc.Resumed.Should().BeTrue();

                    // TH3
                    csc.Error.Message.Should().Be("test error");
                }));

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached, "test")
                {
                    Flags = (int)ProtocolMessage.Flag.Resumed, // resumed
                    Error = new ErrorInfo("test error"),
                });

                result = await tc.Task;
                result.Should().BeTrue();
            }

            [Theory]
            [InlineData(ChannelState.Initialized)]
            [InlineData(ChannelState.Attaching)]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Detaching)]
            [InlineData(ChannelState.Detached)]
            [InlineData(ChannelState.Failed)]
            [InlineData(ChannelState.Suspended)]
            [Trait("spec", "RTL2g")]
            public async Task ShouldNeverEmitAChannelEventForAStateEqualToThePreviousState(ChannelState state)
            {
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                bool stateDidChange = false;

                // set initial state
                channel.SetChannelState(state);

                channel.Once(stateChange =>
                {
                    stateDidChange = true;
                    throw new Exception("state change event should not be emitted");
                });

                // attempt to set the same state again
                channel.SetChannelState(state);

                stateDidChange.Should().BeFalse();
            }

            public EventEmitterSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL3")]
        public class ConnectionStateChangeEffectSpecs : ChannelSpecs
        {
            [Theory]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Attaching)]
            [Trait("spec", "RTL3a")]
            public async Task WhenConnectionFails_AttachingOrAttachedChannelsShouldTransitionToFailedWithSameError(
                ChannelState state)
            {
                var (client, channel) = await GetClientAndChannel();

                var error = new ErrorInfo();
                ((RealtimeChannel)channel).SetChannelState(state);
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
                { Error = error });

                await client.WaitForState(ConnectionState.Failed);

                channel.State.Should().Be(ChannelState.Failed);
                channel.ErrorReason.Should().BeSameAs(error);
            }

            [Theory]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Attaching)]
            [Trait("spec", "RTL3b")]
            public async Task WhenConnectionIsClosed_AttachingOrAttachedChannelsShouldTransitionToDetached(
                ChannelState state)
            {
                var (client, channel) = await GetClientAndChannel();

                ((RealtimeChannel)channel).SetChannelState(state);

                client.Close();

                await client.WaitForState(ConnectionState.Closing);

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Closed));

                await client.WaitForState(ConnectionState.Closed);

                client.Connection.State.Should().Be(ConnectionState.Closed);
                channel.State.Should().Be(ChannelState.Detached);
            }

            [Fact]
            [Trait("spec", "RTL2d")]
            [Trait("spec", "RTL3d")]
            public async Task WhenChannelIsSuspended_WhenConnectionBecomesConnectedAttemptAttach()
            {
                var client = await GetConnectedClient();
                var channel = client.Channels.Get("test".AddRandomSuffix());

                client.Workflow.QueueCommand(SetSuspendedStateCommand.Create(null));
                await client.WaitForState(ConnectionState.Suspended);

                ((RealtimeChannel)channel).SetChannelState(ChannelState.Suspended);

                client.Workflow.QueueCommand(SetConnectedStateCommand.Create(ConnectedProtocolMessage, false));

                await client.WaitForState(ConnectionState.Connected);

                channel.State.Should().Be(ChannelState.Attaching);

                var tsc = new TaskCompletionAwaiter(15000);
                channel.Once(ChannelEvent.Suspended, s =>
                {
                    /* RTL2d */
                    s.Error.Should().NotBeNull();
                    s.Error.Message.Should().StartWith("Channel didn't attach within");
                    s.Error.Code.Should().Be(ErrorCodes.ChannelOperationFailedNoServerResponse);
                    tsc.SetCompleted();
                });

                var completed = await tsc.Task;
                completed.Should().BeTrue("channel should have become Suspended again");
            }

            [Theory]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Attaching)]
            [Trait("spec", "RTL3c")]
            public async Task WhenConnectionIsSuspended_AttachingOrAttachedChannelsShouldTransitionToSuspended(
                ChannelState state)
            {
                var (client, channel) = await GetClientAndChannel();

                ((RealtimeChannel)channel).SetChannelState(state);

                client.Close();

                client.Workflow.QueueCommand(SetSuspendedStateCommand.Create(null));
                await client.WaitForState(ConnectionState.Suspended);

                // Assert
                channel.State.Should().Be(ChannelState.Suspended);
            }

            [Theory]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Attaching)]
            [InlineData(ChannelState.Failed)]
            [InlineData(ChannelState.Suspended)]
            [InlineData(ChannelState.Detached)]
            [InlineData(ChannelState.Detaching)]
            [InlineData(ChannelState.Initialized)]
            [Trait("spec", "RTL3e")]
            public async Task WhenConnectionIsDisconnected_ChannelStateShouldNotChange(ChannelState state)
            {
                var (client, channel) = await GetClientAndChannel();

                ((RealtimeChannel)channel).SetChannelState(state);

                client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null));

                await client.WaitForState(ConnectionState.Disconnected);

                channel.State.Should().Be(state);
            }

            public ConnectionStateChangeEffectSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL4")]
        public class ChannelAttachSpecs : ChannelSpecs
        {
            [Theory]
            [InlineData(ChannelState.Attached)]
            [Trait("spec", "RTL4a")]
            public async Task WhenAttached_ShouldDoNothing(ChannelState state)
            {
                bool stateChanged = false;

                var channel = await GetTestChannel();
                await SetState(channel, state);
                ChannelState newState = ChannelState.Initialized;
                channel.On((args) =>
                {
                    newState = args.Current;
                    stateChanged = true;
                });

                channel.Attach();

                await Task.Delay(100);

                stateChanged.Should().BeFalse("This should not happen. State changed to: " + newState);
            }

            [Fact]
            public async Task WhenAttaching_ShouldAddMultipleAwaitingHandlers()
            {
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);

                int counter = 0;

                channel.Attach((b, info) => Interlocked.Increment(ref counter));
                channel.Attach((b, info) => Interlocked.Increment(ref counter));

                await ReceiveAttachedMessage(client);

                await Task.Delay(100);
                counter.Should().Be(2);
            }

            [Fact]
            [Trait("spec", "RTL4b")]
            public async Task WhenConnectionIsClosedClosingSuspendedOrFailed_ShouldCallCallbackWithError()
            {
                var client = await GetConnectedClient();

                // Closed
                client.Workflow.SetState(new ConnectionClosedState(client.ConnectionManager, Logger));
                var closedAttach = await client.Channels.Get("closed").AttachAsync();
                AssertAttachResultIsFailure(closedAttach);

                // Closing
                client.Workflow.SetState(new ConnectionClosingState(client.ConnectionManager, false, Logger));
                var closingAttach = await client.Channels.Get("closing").AttachAsync();
                AssertAttachResultIsFailure(closingAttach);

                // Suspended
                client.Workflow.SetState(new ConnectionSuspendedState(client.ConnectionManager, Logger));
                var suspendedAttach = await client.Channels.Get("suspended").AttachAsync();
                AssertAttachResultIsFailure(suspendedAttach);

                // Failed
                client.Workflow.SetState(new ConnectionFailedState(
                    client.ConnectionManager,
                    ErrorInfo.ReasonFailed,
                    Logger));

                var failedAttach = await client.Channels.Get("failed").AttachAsync();
                AssertAttachResultIsFailure(failedAttach);

                void AssertAttachResultIsFailure(Result r)
                {
                    r.IsFailure.Should().BeTrue();
                    r.Error.Code.Should().Be(ErrorCodes.ChannelOperationFailed);
                    r.Error.Message.Should().Contain("Cannot attach when connection is in");
                    var defaultErrorInfo = client.State.Connection.CurrentStateObject.DefaultErrorInfo;
                    r.Error.Cause.Message.Should().Be(defaultErrorInfo.Message);
                    r.Error.Cause.Code.Should().Be(defaultErrorInfo.Code);
                }
            }

            [Theory]
            [Trait("issue", "409")]
            [InlineData(ConnectionState.Closed)]
            [InlineData(ConnectionState.Closing)]
            [InlineData(ConnectionState.Suspended)]
            [InlineData(ConnectionState.Failed)]
            public async Task WhenConnectionIsClosedClosingSuspendedOrFailed_ShouldThrowError(ConnectionState state)
            {
                var client = await GetConnectedClient();

                var waiter = new TaskCompletionAwaiter();
                switch (state)
                {
                    case ConnectionState.Closed:
                        client.Workflow.SetState(new ConnectionClosedState(client.ConnectionManager, Logger));
                        break;
                    case ConnectionState.Closing:
                        client.Workflow.SetState(new ConnectionClosingState(client.ConnectionManager, false, Logger));
                        break;
                    case ConnectionState.Suspended:
                        client.Workflow.SetState(new ConnectionSuspendedState(client.ConnectionManager, Logger));
                        break;
                    case ConnectionState.Failed:
                        client.Workflow.SetState(new ConnectionFailedState(
                            client.ConnectionManager,
                            ErrorInfo.ReasonFailed,
                            Logger));
                        break;
                }

                client.Channels.Get("suspended").Attach((success, error) =>
                {
                    success.Should().BeFalse();
                    error.Should().NotBeNull();
                    waiter.SetCompleted();
                });

                await waiter.Task;
            }

            [Fact]
            [Trait("spec", "RTL4c")]
            public async Task ShouldSetStateToAttachingSendAnAttachMessageAndWaitForAttachedMessage()
            {
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);
                channel.Attach();
                channel.State.Should().Be(ChannelState.Attaching);
                await client.ProcessCommands();
                var lastMessageSend = LastCreatedTransport.LastMessageSend;
                lastMessageSend.Action.Should().Be(ProtocolMessage.MessageAction.Attach);
                lastMessageSend.Channel.Should().Be(channel.Name);

                await ReceiveAttachedMessage(client);

                await channel.WaitForAttachedState();
            }

            [Theory]
            [InlineData(ChannelState.Initialized)]
            [InlineData(ChannelState.Detaching)]
            [InlineData(ChannelState.Detached)]
            [InlineData(ChannelState.Failed)]
            [Trait("spec", "RTL4f")]
            public async Task ShouldBecomeSuspendedIfAttachMessageNotReceivedWithinDefaultTimeout(
                ChannelState previousState)
            {
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);
                await SetState(channel, previousState);

                client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                channel.Attach();

                await Task.Delay(120);
                for (int i = 0; i < 10; i++)
                {
                    if (channel.State != previousState)
                    {
                        await Task.Delay(50);
                    }
                    else
                    {
                        break;
                    }
                }

                for (var i = 0; i < 5; i++)
                {
                    if (channel.State == ChannelState.Attaching)
                    {
                        await Task.Delay(50);
                    }
                    else
                    {
                        break;
                    }
                }

                channel.State.Should().Be(ChannelState.Suspended);
                channel.ErrorReason.Should().NotBeNull();
            }

            [Fact]
            [Trait("spec", "RTL4d")]
            public async Task WithACallback_ShouldCallCallbackOnceAttached()
            {
                var called = false;
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);
                channel.Attach((span, info) =>
                {
                    called = true;
                    info.Should().BeNull();
                    Done();
                });

                await ReceiveAttachedMessage(client);

                WaitOne();

                called.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RTL4d")]
            public async Task WithACallback_ShouldCallCallbackWithErrorIfAttachFails()
            {
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);
                client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                bool called = false;
                channel.Attach((span, info) =>
                {
                    called = true;
                    info.Should().NotBeNull();
                    Done();
                });

                WaitOne();

                called.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RTL4d")]
            public async Task WhenCallingAsyncMethod_ShouldSucceedWhenChannelReachesAttached()
            {
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);
                var attachTask = channel.AttachAsync();
                await Task.WhenAll(attachTask, ReceiveAttachedMessage(client));

                attachTask.Result.IsSuccess.Should().BeTrue();
                attachTask.Result.Error.Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RTL4d")]
            public async Task WhenCallingAsyncMethod_ShouldFailWithErrorWhenAttachFails()
            {
                var client = await GetConnectedClient();
                var channel = await GetTestChannel(client);

                client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                var attachTask = channel.AttachAsync();

                await Task.WhenAll(Task.Delay(120), attachTask);

                attachTask.Result.IsFailure.Should().BeTrue();
                attachTask.Result.Error.Should().NotBeNull();
            }

            private new static async Task SetState(
                IRealtimeChannel channel,
                ChannelState state,
                ErrorInfo error = null,
                ProtocolMessage message = null)
            {
                ((RealtimeChannel)channel).SetChannelState(state, error, message);
                await Task.Delay(10);
            }

            private Task ReceiveAttachedMessage(AblyRealtime client)
            {
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Attached)
                {
                    Channel = TestChannelName
                });

                return Task.CompletedTask;
            }

            public ChannelAttachSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL5")]
        public class ChannelDetachSpecs : ChannelSpecs
        {
            [Theory]
            [InlineData(ChannelState.Initialized)]
            [InlineData(ChannelState.Detached)]
            [InlineData(ChannelState.Detaching)]
            [Trait("spec", "RTL5a")]
            public async Task WhenInitializedDetachedOrDetaching_ShouldDoNothing(ChannelState state)
            {
                var channel = await GetTestChannel();

                SetChannelState(channel, state);
                bool changed = false;

                channel.On((args) => { changed = true; });

                channel.Detach();

                changed.Should().BeFalse();
            }

            [Fact]
            [Trait("spec", "RTL5b")]
            public async Task WhenStateIsFailed_DetachShouldReturnAnError()
            {
                var channel = await GetTestChannel();
                SetChannelState(channel, ChannelState.Failed, new ErrorInfo());

                var result = await channel.DetachAsync();
                result.IsFailure.Should().BeTrue();
                result.Error.Should().NotBeNull();
            }

            [Fact]
            [Trait("spec", "RTL5d")]
            public async Task ShouldSendDetachMessageAndOnceDetachedReceivedShouldMigrateToDetached()
            {
                var (client, channel) = await GetClientAndChannel();

                SetChannelState(channel, ChannelState.Attached);

                channel.Detach();

                await client.ProcessCommands();

                LastCreatedTransport.LastMessageSend.Action.Should().Be(ProtocolMessage.MessageAction.Detach);
                channel.State.Should().Be(ChannelState.Detaching);
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Detached)
                {
                    Channel = channel.Name
                });
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Detached);
            }

            [Fact]
            [Trait("spec", "RTL5f")]
            public async Task ShouldReturnToPreviousStateIfDetachedMessageWasNotReceivedWithinDefaultTimeout()
            {
                var (client, channel) = await GetClientAndChannel();

                SetChannelState(channel, ChannelState.Attached);
                client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                bool detachSuccess = true;
                ErrorInfo detachError = null;

                // use a TaskCompletionSource to let us know when the Detach event has been handled
                TaskCompletionSource<bool> tsc = new TaskCompletionSource<bool>(false);
                channel.Detach((success, error) =>
                {
                    detachSuccess = success;
                    detachError = error;
                    tsc.SetResult(true);
                });

                // timeout the tsc in case the detached event never happens
                async Task TimeoutFn()
                {
                    await Task.Delay(1000);
                    tsc.TrySetCanceled();
                }

#pragma warning disable 4014
                TimeoutFn();
#pragma warning restore 4014
                await tsc.Task;

                channel.State.Should().Be(ChannelState.Attached);
                channel.ErrorReason.Should().NotBeNull();
                detachSuccess.Should().BeFalse();
                detachError.Should().NotBeNull();
            }

            [Fact]
            [Trait("spec", "RTL5e")]
            public async Task WithACallback_ShouldCallCallbackOnceDetach()
            {
                var (client, channel) = await GetClientAndChannel();

                SetChannelState(channel, ChannelState.Attached);

                bool called = false;
                channel.Detach((span, info) =>
                {
                    called = true;
                    Assert.Null(info);
                    Done();
                });

                await ReceiveDetachedMessage(client);

                WaitOne();

                called.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RTL5e")]
            public async Task WithACallback_ShouldCallCallbackWithErrorIfDetachFails()
            {
                var (client, channel) = await GetClientAndChannel();

                SetChannelState(channel, ChannelState.Attached);

                client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                bool called = false;
                channel.Detach((span, info) =>
                {
                    called = true;
                    info.Should().NotBeNull();
                    Done();
                });

                WaitOne();

                called.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RTL5e")]
            public async Task WhenCallingAsyncMethod_ShouldSucceedWhenChannelReachesDetached()
            {
                var (client, channel) = await GetClientAndChannel();

                SetChannelState(channel, ChannelState.Attached);

                var detachTask = channel.DetachAsync();
                await Task.WhenAll(detachTask, ReceiveDetachedMessage(client));

                detachTask.Result.IsSuccess.Should().BeTrue();
                detachTask.Result.Error.Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RTL5e")]
            public async Task WhenCallingAsyncMethod_ShouldFailWithErrorWhenDetachFails()
            {
                var (client, channel) = await GetClientAndChannel();

                SetChannelState(channel, ChannelState.Attached);

                client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(100);
                var detachTask = channel.DetachAsync();

                await Task.WhenAll(Task.Delay(120), detachTask);

                detachTask.Result.IsFailure.Should().BeTrue();
                detachTask.Result.Error.Should().NotBeNull();
            }

            private Task ReceiveDetachedMessage(AblyRealtime client)
            {
                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Detached)
                {
                    Channel = TestChannelName,
                });
                return Task.CompletedTask;
            }

            private void SetChannelState(
                IRealtimeChannel channel,
                ChannelState state,
                ErrorInfo error = null,
                ProtocolMessage message = null)
            {
                ((RealtimeChannel)channel).SetChannelState(state, error, message);
            }

            public ChannelDetachSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL6")]
        public class PublishSpecs : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RTL6a")]
            [Trait("spec", "RTL6i")]
            [Trait("spec", "RTL6ia")]
            public async Task WithNameAndData_ShouldSendASingleProtocolMessageWithASingleEncodedMessageInside()
            {
                var channel = await GetTestChannel();
                var bytes = new byte[] { 1, 2, 3, 4, 5 };

                SetState(channel, ChannelState.Attached);

                await channel.PublishAsync("byte", bytes);

                await new ConditionalAwaiter(() => LastCreatedTransport.LastMessageSend.Messages.Length > 0);
                var sentMessage = LastCreatedTransport.LastMessageSend.Messages.First();
                LastCreatedTransport.SentMessages.Should().HaveCount(1);
                sentMessage.Encoding.Should().Be("base64");
            }

            [Fact]
            [Trait("spec", "RTL6i")]
            [Trait("spec", "RTL6i2")]
            public async Task WithListOfMessages_ShouldPublishASingleProtocolMessageToTransport()
            {
                var (client, channel) = await GetClientAndChannel();
                SetState(channel, ChannelState.Attached);

                var list = new List<Message>
                {
                    new Message("name", "test"),
                    new Message("test", "best"),
                };

                channel.Publish(list);

                await client.ProcessCommands();

                LastCreatedTransport.SentMessages.Should().HaveCount(1);
                LastCreatedTransport.LastMessageSend.Messages.Should().HaveCount(2);
            }

            [Fact]
            [Trait("spec", "RTL6i3")]
            public async Task WithMessageWithOutData_ShouldSendOnlyData()
            {
                var (client, channel) = await GetClientAndChannel();
                SetState(channel, ChannelState.Attached);

                channel.Publish(null, "data");

                await client.ProcessCommands();
                LastCreatedTransport.SentMessages.First().Text.Should().Contain("\"messages\":[{\"data\":\"data\"}]");
            }

            [Fact]
            [Trait("spec", "RTL6i3")]
            public async Task WithMessageWithOutName_ShouldSendOnlyData()
            {
                var (client, channel) = await GetClientAndChannel();
                SetState(channel, ChannelState.Attached);

                channel.Publish("name", null);
                await client.ProcessCommands();
                LastCreatedTransport.SentMessages.First().Text.Should().Contain("\"messages\":[{\"name\":\"name\"}]");
            }

            [Trait("spec", "RTL6c")]
            public class ConnectionStateConditions : PublishSpecs
            {
                [Fact]
                [Trait("spec", "RTL6c1")]
                public async Task WhenConnectionIsConnected_ShouldSendMessagesDirectly()
                {
                    var (client, channel) = await GetClientAndChannel();

                    SetState(channel, ChannelState.Attached);

                    channel.Publish("test", "best");

                    await client.ProcessCommands();

                    var lastMessageSend = LastCreatedTransport.LastMessageSend;
                    lastMessageSend.Channel.Should().Be("test");
                    lastMessageSend.Messages.First().Name.Should().Be("test");
                    lastMessageSend.Messages.First().Data.Should().Be("best");
                }

                [Fact]
                [Trait("spec", "RTL6c2")]
                public async Task WhenConnectionIsConnecting_MessageShouldBeQueuedUntilConnectionMovesToConnected()
                {
                    var client = GetClientWithFakeTransport();
                    client.Connect();

                    await client.ProcessCommands();
                    await client.WaitForState(ConnectionState.Connecting);

                    var channel = client.Channels.Get("connecting");
                    SetState(channel, ChannelState.Attached);
                    channel.Publish("test", "connecting");

                    await client.ProcessCommands();

                    LastCreatedTransport.LastMessageSend.Should().BeNull();
                    client.State.PendingMessages.Should().HaveCount(1);
                    client.State.PendingMessages.First().Message.Messages.First().Data.Should().Be("connecting");

                    // Now connect the client
                    client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected));

                    await client.WaitForState(ConnectionState.Connected);
                    await client.ProcessCommands();

                    // Messages should be sent
                    LastCreatedTransport.LastMessageSend.Should().NotBeNull();
                    client.State.PendingMessages.Should().BeEmpty();
                }

                [Fact]
                [Trait("spec", "RTL6c2")]
                public async Task WhenConnectionIsDisconnecting_MessageShouldBeQueuedUntilConnectionMovesToConnected()
                {
                    var client = GetClientWithFakeTransport();
                    client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null));

                    await client.WaitForState(ConnectionState.Disconnected);

                    var channel = client.Channels.Get("connecting");
                    SetState(channel, ChannelState.Attached);
                    channel.Publish("test", "connecting");

                    await client.ProcessCommands();
                    LastCreatedTransport.LastMessageSend.Should().BeNull();
                    client.State.PendingMessages.Should().HaveCount(1);
                    client.State.PendingMessages.First().Message.Messages.First().Data.Should().Be("connecting");

                    // Now connect
                    client.Connect();
                    client.FakeProtocolMessageReceived(ConnectedProtocolMessage);

                    await client.WaitForState(ConnectionState.Connected);
                    await client.ProcessCommands();

                    // Pending messages are sent
                    LastCreatedTransport.LastMessageSend.Should().NotBeNull();
                    client.State.PendingMessages.Should().BeEmpty();
                }

                public ConnectionStateConditions(ITestOutputHelper output)
                    : base(output)
                {
                }
            }

            public class ClientIdSpecs : PublishSpecs
            {
                [Fact]
                [Trait("spec", "RTL6g1")]
                [Trait("spec", "RTL6g1a")]
                public async Task WithClientIdInOptions_DoesNotSetClientIdOnPublishedMessages()
                {
                    var client = await GetConnectedClient(opts =>
                    {
                        opts.ClientId = "123";
                        opts.Token = "test";
                    });
                    var channel = client.Channels.Get("test");
                    await channel.PublishAsync("test", "test");
                    SetState(channel, ChannelState.Attached);

                    LastCreatedTransport.LastMessageSend.Messages.First().ClientId.Should().BeNullOrEmpty();
                }

                [Fact]
                [Trait("spec", "RTL6h")]
                public async Task CanEasilyAddClientIdWhenPublishingAMessage()
                {
                    var channel = await GetTestChannel();
                    SetState(channel, ChannelState.Attached);
                    await channel.PublishAsync("test", "best", "123");

                    LastCreatedTransport.LastMessageSend.Messages.First().ClientId.Should().Be("123");
                }

                [Fact]
                [Trait("spec", "RTL4k")]
                public async Task SendsChannelParamsWithAttachMessage()
                {
                    var client = await GetConnectedClient();
                    var channelParams = new ChannelParams { { "test", "blah" }, { "test2", "value" } };
                    var channelOptions = new ChannelOptions(channelParams: channelParams);
                    var channel = await GetTestChannel(client, channelOptions);
                    channel.Attach();

                    channel.WaitForState(ChannelState.Attaching);
                    await client.ProcessCommands();

                    var protocolMessage = LastCreatedTransport.LastMessageSend;
                    protocolMessage.Action.Should().Be(ProtocolMessage.MessageAction.Attach);
                    protocolMessage.Params.Should().NotBeNull();
                    protocolMessage.Params.Should().BeEquivalentTo(channelParams);
                }

                [Fact]
                [Trait("spec", "RTL4l")]
                public async Task SendsChannelModesWithAttachMessage()
                {
                    var client = await GetConnectedClient();
                    var modes = new ChannelModes(ChannelMode.Presence, ChannelMode.Publish);
                    var channelOptions = new ChannelOptions(modes: modes);
                    var channel = await GetTestChannel(client, channelOptions);
                    channel.Attach();

                    channel.WaitForState(ChannelState.Attaching);
                    await client.ProcessCommands();

                    var protocolMessage = LastCreatedTransport.LastMessageSend;
                    protocolMessage.HasFlag(ProtocolMessage.Flag.Presence).Should().BeTrue();
                    protocolMessage.HasFlag(ProtocolMessage.Flag.Publish).Should().BeTrue();
                }

                public ClientIdSpecs(ITestOutputHelper output)
                    : base(output)
                {
                }
            }

            public PublishSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class PresenceSpecs : ChannelSpecs
        {
            [Theory(Skip = "Intermittently fails")]
            [InlineData(ChannelState.Detached)]
            [InlineData(ChannelState.Failed)]
            [InlineData(ChannelState.Suspended)]
            [Trait("spec", "RTL11")]
            public async Task WhenDetachedOrFailed_AllQueuedPresenceMessagesShouldBeDeletedAndFailCallbackInvoked(
                ChannelState state)
            {
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                var expectedError = new ErrorInfo();

                channel.Attach();

                bool didSucceed = false;
                ErrorInfo err = null;
                channel.Presence.Enter(
                    null,
                    (b, info) =>
                    {
                        didSucceed = b;
                        err = info;
                    });

                channel.Presence.PendingPresenceQueue.Should().HaveCount(1);

                channel.SetChannelState(state, expectedError);
                channel.Presence.PendingPresenceQueue.Should().HaveCount(0);

                didSucceed.Should().BeFalse();
                err.Should().BeSameAs(expectedError);
            }

            public PresenceSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class SubscribeSpecs : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RTL7a")]
            public async Task WithNoArguments_AddsAListenerForAllMessages()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);
                SetState(channel, ChannelState.Attached);
                var messages = new ConcurrentBag<Message>();
                int count = 0;
                channel.Subscribe(message =>
                {
                    messages.Add(message);
                    count++;
                    if (count == 2)
                    {
                        Done();
                    }
                });

                client.FakeMessageReceived(new Message("test", "best"), TestChannelName);
                client.FakeMessageReceived(new Message(string.Empty, "best"), TestChannelName);
                client.FakeMessageReceived(new Message("blah", "best"), TestChannelName);

                WaitOne();
                await Task.Delay(250);
                messages.Should().HaveCount(3);
            }

            [Fact]
            [Trait("spec", "RTL7b")]
            public async Task WithEventArguments_ShouldOnlyNotifyWhenNameMatchesMessageName()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);
                SetState(channel, ChannelState.Attached);
                var messages = new List<Message>();
                channel.Subscribe("test", message =>
                {
                    messages.Add(message);
                    Done();
                });

                client.FakeMessageReceived(new Message(string.Empty, "best"), "test");
                client.FakeMessageReceived(new Message("blah", "best"), "test");
                client.FakeMessageReceived(new Message("test", "best"), "test");

                WaitOne();

                messages.Should().HaveCount(1);
            }

            [Fact]
            [Trait("spec", "RTL7c")]
            public async Task ShouldImplicitlyAttachAChannel()
            {
                var channel = await GetTestChannel();
                channel.State.Should().Be(ChannelState.Initialized);
                channel.Subscribe(message =>
                {
                    // do nothing
                });
                channel.State.Should().Be(ChannelState.Attaching);
            }

            [Fact]
            [Trait("spec", "RTL7d")]
            public async Task WithAMessageThatFailDecryption_ShouldDeliverMessageAndNotPutAnErrorOnTheChannel()
            {
                var otherChannelOptions = new ChannelOptions(true);
                var client = await GetConnectedClient(_switchBinaryOff);
                var encryptedChannel = client.Channels.Get("encrypted", new ChannelOptions(true));
                SetState(encryptedChannel, ChannelState.Attached);
                var awaiter = new TaskCompletionAwaiter();
                var msgReceived = false;
                var errorEmitted = false;

                encryptedChannel.Subscribe(msg =>
                {
                    msgReceived = true;
                    awaiter.Done();
                });

                encryptedChannel.Error += (sender, args) =>
                {
                    errorEmitted = true;
                };

                var message = new Message("name", "encrypted with otherChannelOptions");
                MessageHandler.EncodePayloads(otherChannelOptions.ToDecodingContext(), new[] { message });

                client.FakeMessageReceived(message, encryptedChannel.Name);

                await awaiter;

                msgReceived.Should().BeTrue();
                errorEmitted.Should().BeFalse();
            }

            [Fact]
            [Trait("spec", "RTL7e")]
            public async Task
                WithMessageThatCantBeDecoded_ShouldDeliverMessageWithResidualEncodingAndLogError()
            {
                var otherChannelOptions = new ChannelOptions(true);
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);
                SetState(channel, ChannelState.Attached);

                Message receivedMessage = null;
                channel.Subscribe(msg => { receivedMessage = msg; });

                var message = new Message("name", "encrypted with otherChannelOptions") { Encoding = "json" };
                MessageHandler.EncodePayloads(otherChannelOptions.ToDecodingContext(), new[] { message });

                var testSink = new TestLoggerSink();
                using (DefaultLogger.SetTempDestination(testSink))
                {
                    client.FakeMessageReceived(message, channel.Name);
                    await client.ProcessCommands();
                }

                receivedMessage.Encoding.Should().Be(message.Encoding);
                testSink.Messages.Should().Contain(x => x.Contains("Error decrypting payload"));
            }

            [Fact]
            public async Task WithMultipleMessages_ShouldSetIdOnEachMessage()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);

                SetState(channel, ChannelState.Attached);

                List<Message> receivedMessages = new List<Message>();
                channel.Subscribe(msg => { receivedMessages.Add(msg); });

                var protocolMessage = SetupTestProtocolMessage();
                client.FakeProtocolMessageReceived(protocolMessage);

                await client.ProcessCommands();

                receivedMessages.Should().HaveCount(3);
                receivedMessages.Select(x => x.Id).Should().BeEquivalentTo(
                    $"{protocolMessage.Id}:0",
                    $"{protocolMessage.Id}:1",
                    $"{protocolMessage.Id}:2");
            }

            [Fact]
            public async Task WithMultipleMessagesHaveIds_ShouldPreserveTheirIds()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);

                SetState(channel, ChannelState.Attached);

                List<Message> receivedMessages = new List<Message>();
                channel.Subscribe(msg => { receivedMessages.Add(msg); });

                var protocolMessage = SetupTestProtocolMessage(messages: new[]
                {
                    new Message("message1", "data") { Id = "1" },
                    new Message("message2", "data") { Id = "2" },
                    new Message("message3", "data") { Id = "3" },
                });

                client.FakeProtocolMessageReceived(protocolMessage);

                await client.ProcessCommands();

                receivedMessages.Should().HaveCount(3);
                receivedMessages.Select(x => x.Id)
                    .Should()
                    .BeEquivalentTo("1", "2", "3");
            }

            [Theory]
            [InlineData("protocolMessageConnId", null, "protocolMessageConnId")]
            [InlineData("protocolMessageConnId", "messageConId", "messageConId")]
            public async Task WithMessageWithConnectionIdEqualTo_ShouldBeEqualToExpected(
                string protocolMessageConId,
                string messageConId,
                string expectedConnId)
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);

                SetState(channel, ChannelState.Attached);

                Message receivedMessage = null;
                channel.Subscribe(msg => { receivedMessage = msg; });

                var protocolMessage = SetupTestProtocolMessage(protocolMessageConId, messages: new[]
                {
                    new Message("message1", "data") { Id = "1", ConnectionId = messageConId },
                });

                client.FakeProtocolMessageReceived(protocolMessage);

                await client.ProcessCommands();
                receivedMessage.ConnectionId.Should().Be(expectedConnId);
            }

            [Fact]
            public async Task WithProtocolMessage_ShouldSetMessageTimestampWhenNotThere()
            {
                SetNowFunc(() => DateTimeOffset.UtcNow);
                var timeStamp = Now;
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);

                SetState(channel, ChannelState.Attached);

                List<Message> receivedMessages = new List<Message>();
                channel.Subscribe(msg => { receivedMessages.Add(msg); });

                var protocolMessage = SetupTestProtocolMessage(
                    timestamp: timeStamp,
                    messages: new[]
                    {
                        new Message("message1", "data"),
                        new Message("message1", "data") { Timestamp = timeStamp.AddMinutes(1) },
                    });

                client.FakeProtocolMessageReceived(protocolMessage);

                await client.ProcessCommands();
                await Task.Delay(50);

                receivedMessages.Should().HaveCount(2);
                receivedMessages.First().Timestamp.Should().Be(timeStamp);
                receivedMessages.Last().Timestamp.Should().Be(timeStamp.AddMinutes(1));
            }

            [Fact]
            [Trait("spec", "RTL16")]
            public async Task SetChannelOptions_ShouldUpdateStoreOptionsOnTheChannel()
            {
                var (_, channel) = await GetClientAndChannel(_switchBinaryOff);
                var newOptions = new ChannelOptions(true);
                channel.Options.Should().NotBeSameAs(newOptions);

                channel.SetOptions(newOptions);

                channel.Options.Should().BeSameAs(newOptions);
            }

            [Fact]
            [Trait("spec", "RTL16a")]
            public async Task SetChannelOptionsWithoutModesOrParams_ShouldIndicateSuccessImmediately()
            {
                var (_, channel) = await GetClientAndChannel(_switchBinaryOff);
                var newOptions = new ChannelOptions(true);
                channel.Options.Should().NotBeSameAs(newOptions);
                var states = new List<ChannelState>();
                channel.On(x => states.Add(x.Current));

                await channel.SetOptionsAsync(newOptions);

                channel.Options.Should().BeSameAs(newOptions);
                states.Should().BeEmpty(); // No state change happened.
            }

            [Fact]
            [Trait("spec", "RTL16a")]
            public async Task SetOptions_WithSameModesAndParams_ShouldIndicateSuccessImmediately()
            {
                var client = await GetConnectedClient(_switchBinaryOff);
                var options = new ChannelOptions(channelParams: new ChannelParams { { "test", "test" } }, modes: new ChannelModes(ChannelMode.Presence));
                var newOptions = new ChannelOptions(true, channelParams: new ChannelParams { { "test", "test" } }, modes: new ChannelModes(ChannelMode.Presence));
                var channel = client.Channels.Get("test", options);
                ((RealtimeChannel)channel).SetChannelState(ChannelState.Attached);

                channel.Options.Should().NotBeSameAs(newOptions);
                var states = new List<ChannelState>();
                channel.On(x => states.Add(x.Current));

                await channel.SetOptionsAsync(newOptions);

                channel.Options.Should().BeSameAs(newOptions);
                states.Should().BeEmpty(); // No state change happened.
            }

            [Fact]
            [Trait("spec", "RTL17")]
            public async Task WhenMessageReceivedWhileChannelIsAttaching_ShouldIgnoreMessage()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);

                SetState(channel, ChannelState.Attached);
                List<Message> received = new List<Message>();
                channel.Subscribe(received.Add);

                await ReceiveMessage("1");
                received.Should().Contain(x => x.Id == "1");

                SetState(channel, ChannelState.Attaching);
                await ReceiveMessage("2");
                received.Should().NotContain(x => x.Id == "2");

                SetState(channel, ChannelState.Attached);
                await ReceiveMessage("3");
                received.Should().Contain(x => x.Id == "3");

                Task ReceiveMessage(string id)
                {
                    var protocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message)
                    {
                        Channel = channel.Name,
                        Messages = new[] { new Message { Id = id, Data = "message " }, },
                    };
                    return client.ProcessMessage(protocolMessage);
                }
            }

            private ProtocolMessage SetupTestProtocolMessage(
                string connectionId = null,
                DateTimeOffset? timestamp = null,
                Message[] messages = null)
            {
                var protocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test")
                {
                    ConnectionId = connectionId,
                    Timestamp = timestamp,
                    Id = "protocolMessageId",
                    Messages = messages ?? new[]
                    {
                        new Message("message1", "data"),
                        new Message("message2", "data"),
                        new Message("message3", "data"),
                    }
                };
                return protocolMessage;
            }

            public SubscribeSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL8")]
        public class UnsubscribeSpecs : ChannelSpecs
        {
            private readonly Action<Message> _handler = message => throw new AssertionFailedException("This handler should no longer be called");

            [Fact]
            [Trait("spec", "RTL8a")]
            public async Task ShouldRemoveHandlerFromSubscribers()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);
                SetState(channel, ChannelState.Attached);

                channel.Subscribe(_handler);
                channel.Unsubscribe(_handler);
                client.FakeMessageReceived(new Message("test", "best"), channel.Name);

                // Handler should not throw
            }

            [Fact]
            [Trait("spec", "RTL8b")]
            public async Task WithEventName_ShouldUnsubscribeHandlerFromTheSpecifiedEvent()
            {
                var (client, channel) = await GetClientAndChannel(_switchBinaryOff);
                SetState(channel, ChannelState.Attached);
                channel.Subscribe("test", _handler);
                channel.Unsubscribe("test", _handler);
                client.FakeMessageReceived(new Message("test", "best"), channel.Name);

                // Handler should not throw
            }

            public UnsubscribeSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTL10")]
        public class HistorySpecs : ChannelSpecs
        {
            [Fact]
            [Trait("spec", "RTL10a")]
            public async Task ShouldCallRestClientToGetHistory()
            {
                var client = await GetConnectedClient();
                var channel = client.Channels.Get("history");

                await channel.HistoryAsync();

                LastRequest.Url.Should().Be($"/channels/{channel.Name}/messages");
            }

            [Fact]
            [Trait("spec", "RTL10b")]
            public async Task WithUntilAttach_ShouldPassAttachedSerialToHistoryQuery()
            {
                var client = await GetConnectedClient();

                var channel = client.Channels.Get("history");
                client.ProcessMessage(new ProtocolMessage(ProtocolMessage.MessageAction.Attached)
                {
                    Channel = "history",
                    ChannelSerial = "101"
                });
                await client.ProcessCommands();

#pragma warning disable 618
                await channel.HistoryAsync(true);
#pragma warning restore 618

                LastRequest.QueryParameters.Should()
                    .ContainKey("fromSerial")
                    .WhoseValue.Should().Be("101");
            }

            [Fact]
            public async Task WithUntilAttachButChannelNotAttached_ShouldThrowException()
            {
                var client = await GetConnectedClient();

                var channel = client.Channels.Get("history");

#pragma warning disable 618
                _ = await Assert.ThrowsAsync<AblyException>(() => channel.HistoryAsync(true));
#pragma warning restore 618
            }

            public HistorySpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        protected void SetState(
            IRealtimeChannel channel,
            ChannelState state,
            ErrorInfo error = null,
            ProtocolMessage message = null)
        {
            ((RealtimeChannel)channel).SetChannelState(state, error, message);
        }

        public ChannelSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
