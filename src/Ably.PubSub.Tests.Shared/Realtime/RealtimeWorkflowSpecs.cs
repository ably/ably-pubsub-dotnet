using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Tests.Realtime;
using IO.Ably.Transport;
using IO.Ably.Types;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.NETFramework.Realtime
{
    public class RealtimeWorkflowSpecs : AblyRealtimeSpecs
    {
        public class GeneralSpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN8b")]
            public void ConnectedState_UpdatesConnectionInformation()
            {
             // Act
             var connectedProtocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
             {
                 ConnectionId = "1",
                 ConnectionDetails = new ConnectionDetails
                 {
                     ClientId = "client1",
                     ConnectionKey = "validKey"
                 },
             };
             var client = GetRealtimeClient(options => options.AutoConnect = false);
             client.Workflow.ProcessCommand(SetConnectedStateCommand.Create(connectedProtocolMessage, false));

             // Assert
             var connection = client.Connection;
             connection.Id.Should().Be("1");
             connection.Key.Should().Be("validKey");
             client.Auth.ClientId.Should().Be("client1");
            }

            [Fact]
            public async Task SetFailedState_ShouldClearConnectionKeyAndId()
            {
             var client = GetDisconnectedClient();

             client.State.Connection.Key = "Test";
             client.State.Connection.Id = "Test";

             client.ExecuteCommand(SetFailedStateCommand.Create(null));

             await client.ProcessCommands();

             client.State.Connection.Key.Should().BeEmpty();
             client.State.Connection.Id.Should().BeEmpty();
            }

            public GeneralSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class ConnectingStateSpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN14g")]
            public async Task WithInboundErrorMessage_WhenNotTokenErrorAndChannelsEmpty_GoesToFailed()
            {
                // A custom host means no fallbacks. The transport is faked and held short of
                // Connected, and realtimeRequestTimeout put out of reach, so neither a real DNS
                // failure nor the CONNECTING timeout can reach Disconnected before the injected
                // error does.
                FakeTransportFactory.InitialiseFakeTransport =
                    transport => transport.OnConnectChangeStateToConnected = false;

                var client = GetClientWithFakeTransport(opts =>
                {
                    opts.RealtimeHost = "non-default.ably.io";
                    opts.RealtimeRequestTimeout = TimeSpan.FromMinutes(1);
                });

                await client.WaitForState(ConnectionState.Connecting);

                // Arrange
                ErrorInfo targetError = new ErrorInfo("test", 123);

                // Queued and drained rather than fired and forgotten: an un-awaited ProcessCommand
                // can have its continuation delayed past the wait below under a parallel suite.
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = targetError }));
                await client.ProcessCommands();

                // Assert
                await client.WaitForState(ConnectionState.Failed);
            }

            public ConnectingStateSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class ConnectingCommandSpecs : AblyRealtimeSpecs
        {
            // UTS: realtime/unit/RTN14h/resume-after-ttl-0
            [Fact]
            [Trait("spec", "RTN14h")]
            [Trait("spec", "RTN15b1")]
            public async Task AfterSuspendedAndAFailedAttempt_EveryReconnectionShouldCarryResume()
            {
                // RTN14h: "Reconnection attempts in this state should continue to attempt to
                // resume, regardless of how long it has been since the client was last connected."
                var client = await GetConnectedClient();
                var key = client.State.Connection.Key;
                var id = client.State.Connection.Id;
                key.Should().NotBeNullOrEmpty();

                client.ExecuteCommand(SetSuspendedStateCommand.Create(ErrorInfo.ReasonSuspended));
                await client.ProcessCommands();

                // RTN8d, RTN9d - retained, because SUSPENDED is not one of the terminal states.
                client.State.Connection.Key.Should().Be(key);
                client.State.Connection.Id.Should().Be(id);

                client.ExecuteCommand(SetConnectingStateCommand.Create());
                await client.ProcessCommands();

                // A second and third attempt, because the clause is "reconnection attempts" plural -
                // one attempt carrying a resume does not show that every later one does.
                for (var i = 0; i < 2; i++)
                {
                    client.ExecuteCommand(HandleConnectingErrorCommand.Create(
                        new ErrorInfo("boom", 50000, System.Net.HttpStatusCode.InternalServerError)));
                    await client.ProcessCommands();
                    client.ExecuteCommand(SetConnectingStateCommand.Create());
                    await client.ProcessCommands();
                }

                var reconnects = CreatedTransports.Skip(1).ToList();
                reconnects.Should().HaveCountGreaterOrEqualTo(3);
                foreach (var transport in reconnects)
                {
                    transport.Parameters.GetParams()
                        .Should().Contain(new KeyValuePair<string, string>("resume", key)); // RTN15b1
                }
            }

            [Fact]
            [Trait("spec", "RTN14h")]
            [Trait("spec", "RTN15b1")]
            public async Task AfterAFailedAttempt_TheNextReconnectionShouldStillCarryResume()
            {
                // RTN14h's "continue to attempt to resume" applies from the second attempt onwards,
                // not just after a suspend.
                //
                // Deliberately never passes through SUSPENDED, and asserts as much: once AttemptsInfo
                // has recorded a suspend, ShouldSuspend returns true and the DISCONNECTED handler
                // diverts straight back to SUSPENDED without reaching the path under test.
                var client = await GetConnectedClient(opts =>
                    opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

                var key = client.State.Connection.Key;
                key.Should().NotBeNullOrEmpty();

                client.ExecuteCommand(HandleConnectingErrorCommand.Create(
                    new ErrorInfo("boom", 50000, System.Net.HttpStatusCode.InternalServerError)));
                await client.ProcessCommands();

                client.Connection.State.Should().NotBe(ConnectionState.Suspended);
                client.State.Connection.Key.Should().Be(key);

                // A 500 earns the RTN15h3 instant retry, so the next transport is already built by
                // the time the commands settle, and must carry the resume.
                LastCreatedTransport.Parameters.GetParams()
                    .Should().Contain(new KeyValuePair<string, string>("resume", key));
            }

            [Fact]
            [Trait("spec", "RTN14h")]
            public async Task WithInboundErrorMessageWhenItCanUseFallBack_ShouldKeepConnectionKey()
            {
                // RTN14h - a retryable inbound ERROR must not cost the key, because the reconnection
                // attempt has to continue to attempt to resume. The client is driven through
                // CONNECTED first so there is a real key at stake.
                var client = await GetConnectedClient(options =>
                {
                    options.RealtimeRequestTimeout = TimeSpan.FromSeconds(60);
                    options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(60);
                });

                var key = client.State.Connection.Key;
                key.Should().NotBeNullOrEmpty();

                // Back to CONNECTING, where a retryable inbound ERROR is a failed attempt rather
                // than RTN15j's fatal connection error.
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.ProcessCommands();
                client.ExecuteCommand(SetConnectingStateCommand.Create());
                await client.ProcessCommands();

                client.State.Connection.Key.Should().Be(key);

                var messageWithError = new ProtocolMessage(ProtocolMessage.MessageAction.Error)
                {
                    Error = new ErrorInfo("test", 123, System.Net.HttpStatusCode.InternalServerError),
                };

                await client.ProcessMessage(messageWithError);

                client.State.Connection.Key.Should().Be(key);
            }

            [Fact]
            public async Task WithInboundDisconnectedMessage_ShouldTransitionToDisconnectedState()
            {
                // Arrange
                var client = GetRealtimeClient();
                client.Connect();

                await client.WaitForState(ConnectionState.Connecting);
                var disconnectedMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected) { Error = ErrorInfo.ReasonDisconnected };

                // Act
                client.ExecuteCommand(ProcessMessageCommand.Create(disconnectedMessage));

                await client.WaitForState(ConnectionState.Disconnected, TimeSpan.FromSeconds(5));
            }

            [Fact]
            public async Task WhenDisconnectedWithFallback_ShouldRetryConnectionImmediately()
            {
                var client = GetClientWithFakeTransport();

                await client.WaitForState(ConnectionState.Connecting);
                var states = new List<ConnectionState>();
                client.Connection.On(changes => states.Add(changes.Current));

                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonClosed, true));
                await client.ProcessCommands();

                // Assert
                states.Should().HaveCount(2);
                states.Should().BeEquivalentTo(new[] { ConnectionState.Disconnected, ConnectionState.Connecting });
            }

            [Fact]
            [Trait("spec", "RTN17j")]
            [Trait("spec", "RTN14d")]
            public async Task InstantRetries_ShouldBeBoundedByTheNumberOfDomains()
            {
                // RTN17j sanctions reconnecting immediately to work through the fallback domains, but
                // the traversal is bounded - every failure carries an exception and so qualifies
                // again, which would mean RTB1 is never reached.
                var client = GetClientWithFakeTransport(opts => opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));
                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();

                var domainCount = 1 + client.State.Connection.FallbackHosts.Count;
                domainCount.Should().BeGreaterThan(1);

                var connectingCount = 0;
                client.Connection.On(x =>
                {
                    if (x.Current == ConnectionState.Connecting)
                    {
                        connectingCount++;
                    }
                });

                // Each failure carries an exception, so each one qualifies for an instant retry.
                for (var i = 0; i < domainCount + 3; i++)
                {
                    client.ExecuteCommand(SetDisconnectedStateCommand.Create(
                        ErrorInfo.ReasonDisconnected, exception: new Exception("transport gone")));
                    await client.ProcessCommands();
                }

                // One per domain. The long retry timeout means nothing else can have produced a
                // CONNECTING, so anything past the bound went to the timer instead.
                connectingCount.Should().Be(domainCount);
                client.State.AttemptsInfo.InstantRetryCount.Should().Be(domainCount);
                client.Connection.State.Should().Be(ConnectionState.Disconnected);
            }

            [Fact]
            public async Task ShouldCreateTransport()
            {
                // Arrange
                var client = GetClientWithFakeTransport(opts => opts.AutoConnect = false);

                LastCreatedTransport.Should().BeNull();

                client.ExecuteCommand(SetConnectingStateCommand.Create());
                await client.ProcessCommands();

                // Assert
                LastCreatedTransport.Should().NotBeNull();
            }

            public ConnectingCommandSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class SuspendedCommandSpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN7c")]
            [Trait("sandboxTest", "needed")]
            public async Task OnAttached_ClearsAckQueue()
            {
                var client = GetDisconnectedClient();

                client.State.WaitingForAck.Add(new MessageAndCallback(new ProtocolMessage(), null));

                client.ExecuteCommand(SetSuspendedStateCommand.Create(null));

                await client.ProcessCommands(); // Wait for the command to be executed

                client.State.WaitingForAck.Should().BeEmpty();
            }

            public SuspendedCommandSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class ClosingCommandSpecs : AblyRealtimeSpecs
        {
            [Theory]
            [InlineData(TransportState.Closed)]
            [InlineData(TransportState.Closing)]
            [InlineData(TransportState.Connecting)]
            [InlineData(TransportState.Initialized)]
            public async Task WhenTransportIsNotConnected_ShouldGoStraightToClosed(TransportState transportState)
            {
                var client = await GetConnectedClient();

                // Arrange
                client.ConnectionManager.Transport = new FakeTransport { State = transportState };

                // Act
                client.ExecuteCommand(SetClosingStateCommand.Create());

                await client.ProcessCommands();

                // Assert
                client.State.Connection.State.Should().Be(ConnectionState.Closed);
            }

            [Fact]
            [Trait("spec", "RTN12a")]

            // When the closing state is initialised a Close message is sent
            public async Task WhenTransportIsNotConnected_ShouldSendCloseMessage()
            {
                var client = await GetConnectedClient();

                // Act
                client.ExecuteCommand(SetClosingStateCommand.Create());

                await client.ProcessCommands();

                // Assert
                LastCreatedTransport.LastMessageSend.Should().NotBeNull();
                LastCreatedTransport.LastMessageSend.Action.Should().Be(ProtocolMessage.MessageAction.Close);
            }

            public ClosingCommandSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class SetFailedStateCommandSpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN7c")]
            [Trait("sandboxTest", "needed")]
            public async Task ShouldDestroyTransport()
            {
                var client = await GetConnectedClient();

                client.ConnectionManager.Transport.Should().NotBeNull();

                client.ExecuteCommand(SetFailedStateCommand.Create(ErrorInfo.ReasonFailed));

                await client.ProcessCommands();

                client.ConnectionManager.Transport.Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RTN7c")]
            [Trait("sandboxTest", "needed")]
            public async Task ShouldClearsAckQueue()
            {
                var client = GetDisconnectedClient();

                client.State.WaitingForAck.Add(new MessageAndCallback(new ProtocolMessage(), null));

                client.ExecuteCommand(SetClosedStateCommand.Create());

                await client.ProcessCommands(); // Wait for the command to be executed

                client.State.WaitingForAck.Should().BeEmpty();
            }

            public SetFailedStateCommandSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class ClosedCommandSpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN7c")]
            [Trait("sandboxTest", "needed")]
            public async Task OnAttached_ShouldDestroyTranspоrt()
            {
                var client = await GetConnectedClient();

                client.ConnectionManager.Transport.Should().NotBeNull();

                client.ExecuteCommand(SetClosedStateCommand.Create());

                await client.ProcessCommands();

                client.ConnectionManager.Transport.Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RTN7c")]
            [Trait("sandboxTest", "needed")]
            public async Task OnAttached_ClearsAckQueue()
            {
                var client = GetDisconnectedClient();

                client.State.WaitingForAck.Add(new MessageAndCallback(new ProtocolMessage(), null));

                client.ExecuteCommand(SetClosedStateCommand.Create());

                await client.ProcessCommands(); // Wait for the command to be executed

                client.State.WaitingForAck.Should().BeEmpty();
            }

            public ClosedCommandSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class AckProtocolTests : RealtimeWorkflowSpecs
        {
            [Theory]
            [InlineData(ProtocolMessage.MessageAction.Message)]
            [InlineData(ProtocolMessage.MessageAction.Presence)]
            [Trait("spec", "RTN7a")]
            [Trait("spec", "RTN7b")]
            [Trait("sandboxTest", "needed")]
            public async Task WhenSendingPresenceOrDataMessage_IncrementsMsgSerial(
                ProtocolMessage.MessageAction messageAction)
            {
                // Arrange
                var client = await GetConnectedClient();

                var targetMessage1 = new ProtocolMessage(messageAction, "Test");
                var targetMessage2 = new ProtocolMessage(messageAction, "Test");
                var targetMessage3 = new ProtocolMessage(messageAction, "Test");

                client.ExecuteCommand(SendMessageCommand.Create(targetMessage1));
                client.ExecuteCommand(SendMessageCommand.Create(targetMessage2));
                client.ExecuteCommand(SendMessageCommand.Create(targetMessage3));

                await client.ProcessCommands();

                // Assert
                targetMessage1.MsgSerial.Should().Be(0);
                targetMessage2.MsgSerial.Should().Be(1);
                targetMessage3.MsgSerial.Should().Be(2);
            }

            // TODO: Move the test to the workflow tests for send message
            [Theory]
            [InlineData(ProtocolMessage.MessageAction.Ack)]
            [InlineData(ProtocolMessage.MessageAction.Attach)]
            [InlineData(ProtocolMessage.MessageAction.Attached)]
            [InlineData(ProtocolMessage.MessageAction.Close)]
            [InlineData(ProtocolMessage.MessageAction.Closed)]
            [InlineData(ProtocolMessage.MessageAction.Connect)]
            [InlineData(ProtocolMessage.MessageAction.Connected)]
            [InlineData(ProtocolMessage.MessageAction.Detach)]
            [InlineData(ProtocolMessage.MessageAction.Detached)]
            [InlineData(ProtocolMessage.MessageAction.Disconnect)]
            [InlineData(ProtocolMessage.MessageAction.Disconnected)]
            [InlineData(ProtocolMessage.MessageAction.Error)]
            [InlineData(ProtocolMessage.MessageAction.Heartbeat)]
            [InlineData(ProtocolMessage.MessageAction.Nack)]
            [InlineData(ProtocolMessage.MessageAction.Sync)]
            [Trait("spec", "RTN7a")]
            public async Task WhenSendingNotAPresenceOrDataMessage_MsgSerialNotIncremented(
                ProtocolMessage.MessageAction messageAction)
            {
                // Arrange
                var client = await GetConnectedClient();

                var targetMessage1 = new ProtocolMessage(messageAction, "Test");
                var targetMessage2 = new ProtocolMessage(messageAction, "Test");
                var targetMessage3 = new ProtocolMessage(messageAction, "Test");

                client.ExecuteCommand(SendMessageCommand.Create(targetMessage1));
                client.ExecuteCommand(SendMessageCommand.Create(targetMessage2));
                client.ExecuteCommand(SendMessageCommand.Create(targetMessage3));

                await client.ProcessCommands();

                // Assert
                targetMessage1.MsgSerial.Should().Be(0);
                targetMessage2.MsgSerial.Should().Be(0);
                targetMessage3.MsgSerial.Should().Be(0);
            }

            [Theory]
            [InlineData(ProtocolMessage.MessageAction.Ack)]
            [InlineData(ProtocolMessage.MessageAction.Nack)]
            public async Task WhenReceivingAckOrNackMessage_ShouldHandleAction(ProtocolMessage.MessageAction action)
            {
                var client = GetDisconnectedClient();

                // Act
                bool result = await client.Workflow.HandleAckMessage(new ProtocolMessage(action));

                // Assert
                result.Should().BeTrue();
            }

            [Theory]
            [InlineData(ProtocolMessage.MessageAction.Attach)]
            [InlineData(ProtocolMessage.MessageAction.Attached)]
            [InlineData(ProtocolMessage.MessageAction.Close)]
            [InlineData(ProtocolMessage.MessageAction.Closed)]
            [InlineData(ProtocolMessage.MessageAction.Connect)]
            [InlineData(ProtocolMessage.MessageAction.Connected)]
            [InlineData(ProtocolMessage.MessageAction.Detach)]
            [InlineData(ProtocolMessage.MessageAction.Detached)]
            [InlineData(ProtocolMessage.MessageAction.Disconnect)]
            [InlineData(ProtocolMessage.MessageAction.Disconnected)]
            [InlineData(ProtocolMessage.MessageAction.Error)]
            [InlineData(ProtocolMessage.MessageAction.Heartbeat)]
            [InlineData(ProtocolMessage.MessageAction.Message)]
            [InlineData(ProtocolMessage.MessageAction.Presence)]
            [InlineData(ProtocolMessage.MessageAction.Sync)]
            public async Task WhenReceivingNonAckOrNackMessage_ShouldNotHandleAction(
                ProtocolMessage.MessageAction action)
            {
                var client = GetDisconnectedClient();

                // Act
                bool result = await client.Workflow.HandleAckMessage(new ProtocolMessage(action));

                // Assert
                result.Should().BeFalse();
            }

            [Fact]
            public async Task OnAckReceivedForAMessage_AckCallbackCalled()
            {
                // Arrange
                var client = await GetConnectedClient();

                var callbacks = new List<ValueTuple<bool, ErrorInfo>>();
                var message = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "Test");

                void Callback(bool ack, ErrorInfo err)
                {
                    callbacks.Add((ack, err));
                }

                // Act
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = 0, Count = 1 }));
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = 1, Count = 1 }));

                await client.ProcessCommands();

                // Assert
                callbacks.Count.Should().Be(2);
                Assert.True(callbacks.TrueForAll(c => c.Item1)); // Ack
                Assert.True(callbacks.TrueForAll(c => c.Item2 == null)); // No error
            }

            [Fact]
            public async Task WhenSendingMessage_AckCallbackCalled_ForMultipleMessages()
            {
                // Arrange
                var client = await GetConnectedClient();

                var callbacks = new List<ValueTuple<bool, ErrorInfo>>();

                var message1 = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "Test");
                var message2 = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "Test");
                var message3 = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "Test");

                var awaiter = new TaskCompletionAwaiter();

                Action<bool, ErrorInfo> GetCallback(int forCount) =>
                    (ack, err) =>
                    {
                        if (callbacks.Count == forCount)
                        {
                            callbacks.Add((ack, err));
                        }

                        if (callbacks.Count == 3)
                        {
                            awaiter.SetCompleted();
                        }
                    };

                var ackMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = 0, Count = 3 };

                // Act
                client.Workflow.QueueAck(message1, GetCallback(0));
                client.Workflow.QueueAck(message2, GetCallback(1));
                client.Workflow.QueueAck(message3, GetCallback(2));
                client.ExecuteCommand(ProcessMessageCommand.Create(ackMessage));

                await client.ProcessCommands();

                await awaiter.Task;

                // Assert
                callbacks.Count.Should().Be(3);
                Assert.True(callbacks.TrueForAll(c => c.Item1)); // Ack
                Assert.True(callbacks.TrueForAll(c => c.Item2 == null)); // No error
            }

            [Fact]
            public async Task WithNackMessageReceived_CallbackIsCalledWithError()
            {
                // Arrange
                var client = await GetConnectedClient();

                var callbacks = new List<ValueTuple<bool, ErrorInfo>>();
                var message = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "Test");

                void Callback(bool ack, ErrorInfo err)
                {
                    callbacks.Add((ack, err));
                }

                // Act
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 0, Count = 1 }));
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 1, Count = 1 }));

                await client.ProcessCommands();

                // Assert
                callbacks.Count.Should().Be(2);
                Assert.True(callbacks.TrueForAll(c => c.Item1 == false)); // Nack
                Assert.True(callbacks.TrueForAll(c => c.Item2 != null)); // Error
            }

            [Fact]
            public async Task WhenNackReceivedForMultipleMessage_AllCallbacksAreCalledAndErrorMessagePassed()
            {
                // Arrange
                var client = await GetConnectedClient();
                var callbacks = new List<ValueTuple<bool, ErrorInfo>>();

                var message = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "Test");

                void Callback(bool ack, ErrorInfo err)
                {
                    callbacks.Add((ack, err));
                }

                // Act
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));
                client.ExecuteCommand(SendMessageCommand.Create(message, Callback));

                var error = new ErrorInfo("reason", 123);

                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Nack) { MsgSerial = 0, Count = 3, Error = error }));

                await client.ProcessCommands();

                // Assert
                callbacks.Count.Should().Be(3);
                Assert.True(callbacks.TrueForAll(c => !c.Item1)); // Nack
                Assert.True(callbacks.TrueForAll(c => ReferenceEquals(c.Item2, error))); // Error
            }

            [Fact]
            [Trait("spec", "RTN7b")]
            [Trait("spec", "RTN19a")]
            public async Task WhenAnAckedMessageFailsToSend_ShouldHoldItInOneQueueOnly()
            {
                // A message can be awaiting an ACK or queued for a later connection, never both.
                // RTN19a resends WaitingForAck on reconnect, so a message in both queues is sent
                // twice and the second msgSerial assignment renumbers the copy WaitingForAck holds,
                // leaving a hole in the sequence Ably is tracking.
                //
                // No caller produces this shape today - AckRequired implies CanSend and so
                // CONNECTED, where CanQueue is false - so the invariant is asserted directly.
                var client = GetClientWithFakeTransport();
                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();

                // CONNECTING can queue, so forcing an ack-requiring message there reaches both
                // branches. A throwing write is what SendToTransport turns into a failure.
                LastCreatedTransport.SetSendAction(_ => throw new Exception("socket gone"));

                var message = new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test");
                client.ExecuteCommand(SendMessageCommand.Create(message, (_, __) => { }, force: true));

                await client.ProcessCommands();

                client.State.WaitingForAck.Should().HaveCount(1);
                client.State.PendingMessages.Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "RTN7b")]
            [Trait("spec", "RTN19a")]
            public async Task WhenANonAckedMessageFailsToSend_ShouldStillQueueItForTheNextConnection()
            {
                // The other half: a message not awaiting an ACK is tracked nowhere else, so dropping
                // it here would lose it silently. CLOSE is what the two force:true callers send.
                var client = GetClientWithFakeTransport();
                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();

                LastCreatedTransport.SetSendAction(_ => throw new Exception("socket gone"));

                var message = new ProtocolMessage(ProtocolMessage.MessageAction.Close);
                client.ExecuteCommand(SendMessageCommand.Create(message, (_, __) => { }, force: true));

                await client.ProcessCommands();

                client.State.WaitingForAck.Should().BeEmpty();
                client.State.PendingMessages.Should().HaveCount(1);
            }

            // UTS: realtime/unit/RTN7e/error-represents-reason-4
            [Theory]
            [InlineData(ConnectionState.Failed)]
            [InlineData(ConnectionState.Suspended)]
            [InlineData(ConnectionState.Closed)]
            [Trait("spec", "RTN7e")]
            public async Task WhenTheConnectionFailsAMessage_ShouldReportTheReasonForTheStateChange(
                ConnectionState state)
            {
                // RTN7e: the callback "should be called with an error representing the reason for
                // the state change" - this transition's reason, not a module level constant.
                var client = await GetConnectedClient();
                var reason = new ErrorInfo("the actual reason", 12345);

                var errors = new List<ErrorInfo>();
                client.ExecuteCommand(SendMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test"),
                    (_, error) => errors.Add(error)));
                await client.ProcessCommands();

                RealtimeCommand command = state switch
                {
                    ConnectionState.Failed => SetFailedStateCommand.Create(reason),
                    ConnectionState.Suspended => SetSuspendedStateCommand.Create(reason),
                    _ => SetClosedStateCommand.Create(reason),
                };

                client.ExecuteCommand(command);
                await client.ProcessCommands();

                errors.Should().ContainSingle();
                errors[0].Message.Should().Be("the actual reason");
                errors[0].Code.Should().Be(12345);

                // The point of the clause: the error handed to the publisher is the reason the
                // connection itself is reporting, not a separately minted one.
                client.Connection.ErrorReason.Should().NotBeNull();
                client.Connection.ErrorReason.Code.Should().Be(errors[0].Code);
            }

            [Fact]
            [Trait("spec", "RTN7e")]
            public async Task WhenTheConnectionFailsAMessage_ShouldHaveAlreadyEnteredTheNewState()
            {
                // An application inspecting the connection from inside its publish callback sees the
                // state it is being told about, not the previous one. ably-js orders
                // enactStateChange before failQueuedMessages for the same reason.
                var client = await GetConnectedClient();

                var seen = new List<ConnectionState>();
                client.ExecuteCommand(SendMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test"),
                    (_, __) => seen.Add(client.Connection.State)));
                await client.ProcessCommands();

                client.ExecuteCommand(SetSuspendedStateCommand.Create(new ErrorInfo("gone", 1)));
                await client.ProcessCommands();

                seen.Should().Equal(new[] { ConnectionState.Suspended });
            }

            [Theory]
            [InlineData(ConnectionState.Suspended)]
            [Trait("spec", "RTN7e")]
            public async Task WhenTheTransitionThrows_ShouldStillFailTheMessage(ConnectionState state)
            {
                // The connection enters the state before the throw, so RTN7e applies whether or not
                // the transition threw - hence the finally. Reachable from application code:
                // SuspendedRetryTimeout is public and unvalidated, and a negative value makes
                // System.Threading.Timer throw.
                //
                // SUSPENDED only, because ConnectionSuspendedState.StartTimer is the one state whose
                // timer takes a caller-supplied delay.
                var client = await GetConnectedClient(opts =>
                    opts.SuspendedRetryTimeout = TimeSpan.FromSeconds(-5));

                var errors = new List<ErrorInfo>();
                client.ExecuteCommand(SendMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test"),
                    (_, error) => errors.Add(error)));
                await client.ProcessCommands();
                client.State.WaitingForAck.Should().HaveCount(1);

                RealtimeCommand command = state switch
                {
                    ConnectionState.Failed => SetFailedStateCommand.Create(new ErrorInfo("gone", 1)),
                    ConnectionState.Suspended => SetSuspendedStateCommand.Create(new ErrorInfo("gone", 1)),
                    _ => SetClosedStateCommand.Create(new ErrorInfo("gone", 1)),
                };

                client.ExecuteCommand(command);
                await client.ProcessCommands();

                client.Connection.State.Should().Be(state);
                errors.Should().ContainSingle();
                client.State.WaitingForAck.Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "RTN7e")]
            public async Task WhenAlreadyInTheState_ShouldNotReportThePreviousReason()
            {
                // SetState early-returns when already in the target state, before
                // Connection.ErrorReason is updated - so the reason is read off the state object
                // being entered, which cannot go stale.
                var client = await GetConnectedClient();

                client.ExecuteCommand(SetSuspendedStateCommand.Create(new ErrorInfo("first reason", 1)));
                await client.ProcessCommands();

                var errors = new List<ErrorInfo>();
                client.ExecuteCommand(SendMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test"),
                    (_, error) => errors.Add(error),
                    force: true));
                await client.ProcessCommands();

                client.ExecuteCommand(SetSuspendedStateCommand.Create(new ErrorInfo("second reason", 2)));
                await client.ProcessCommands();

                errors.Should().ContainSingle();
                errors[0].Message.Should().Be("second reason");
            }

            public AckProtocolTests(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTN11d")]
        public class ReinitialiseOnConnectSpecs : AblyRealtimeSpecs
        {
            [Theory]
            [InlineData(ConnectionState.Closed)]
            [InlineData(ConnectionState.Failed)]
            public async Task WhenConnectingFromClosedOrFailed_ShouldReinitialiseEverythingRTN11dNames(ConnectionState from)
            {
                var client = await GetClientWithHistory();
                var channel = (RealtimeChannel)client.Channels.Get("test");

                await MoveTo(client, from);

                // Preconditions: there is something to clear on all four counts.
                channel.State.Should().NotBe(ChannelState.Initialized);
                channel.ErrorReason.Should().NotBeNull();
                client.Connection.ErrorReason.Should().NotBeNull();
                client.State.Connection.MessageSerial.Should().Be(3);

                client.Connect();
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Initialized);
                channel.ErrorReason.Should().BeNull();
                client.Connection.ErrorReason.Should().BeNull();
                client.State.Connection.MessageSerial.Should().Be(0);
            }

            [Fact]
            public async Task WhenConnectingFromClosed_ShouldResetTheSerialAtConnectTime()
            {
                // RTN11d asks for the reset at connect(), not at the next CONNECTED. Not framed
                // around Connection.CreateRecoveryKey(), which returns empty for the whole of that window.
                var client = await GetClientWithHistory();
                await MoveTo(client, ConnectionState.Closed);

                client.Connect();
                await client.ProcessCommands();

                client.State.Connection.MessageSerial.Should().Be(0);
            }

            [Fact]
            public async Task WhenConnectingFromDisconnected_ShouldLeaveTheSerialAlone()
            {
                // RTN11d applies only to CLOSED and FAILED. From DISCONNECTED the connection may
                // still be resumable, and RTN19a2 needs the sequence intact to resend with it.
                var client = await GetClientWithHistory();
                await MoveTo(client, ConnectionState.Disconnected);

                client.Connect();
                await client.ProcessCommands();

                client.State.Connection.MessageSerial.Should().Be(3);
            }

            [Fact]
            [Trait("spec", "RTN11b")]
            [Trait("spec", "RTL3b")]
            public async Task WhenTheConnectionCloses_ShouldDetachChannelsBeforeReinitialisingThem()
            {
                // RTN11b: "the client should ensure that all channels first transition to DETACHED,
                // following RTL3b, and then reinitialize channels per RTN11d". The DETACHED branch
                // is the only caller of Presence.ChannelDetachedOrFailed, so skipping it would carry
                // presence members from the abandoned connection into the next one.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                var states = new List<ChannelState>();
                channel.On(args => states.Add(args.Current));

                client.Close();
                await client.WaitForState(ConnectionState.Closing);
                await client.ProcessCommands();

                states.Should().Contain(ChannelState.Detached);

                client.Connect();
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Initialized);
            }

            [Fact]
            [Trait("spec", "RTN11b")]
            [Trait("spec", "RTL4f")]
            public async Task WhenTheConnectionCloses_ShouldNotLetAStaleAwaiterSuspendAReinitialisedChannel()
            {
                // The awaiters are failed on CLOSING, so an attach still in flight cannot keep its
                // timer and later apply RTL4f's SUSPENDED to a channel RTN11d has already reset.
                var client = await GetConnectedClient(opts =>
                    opts.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(200));

                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.Attach();
                await client.ProcessCommands();
                channel.State.Should().Be(ChannelState.Attaching);

                client.Close();
                await client.WaitForState(ConnectionState.Closing);
                await client.ProcessCommands();

                channel.AttachedAwaiter.Waiting.Should().BeFalse();

                client.Connect();
                await client.ProcessCommands();
                channel.State.Should().Be(ChannelState.Initialized);

                // Long enough that the original attach timer would have fired.
                await Task.Delay(500);

                channel.State.Should().NotBe(ChannelState.Suspended);
            }

            [Theory]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Suspended)]
            [InlineData(ChannelState.Detaching)]
            [Trait("spec", "RTN11b")]
            [Trait("spec", "RTP5a")]
            public async Task WhenTheConnectionCloses_ShouldClearPresenceForEveryPendingChannel(
                ChannelState from)
            {
                // RTP5a clears the presence maps on entering DETACHED, so any channel left in
                // another state would carry its members from the abandoned connection into the next
                // one - hence all four pending states, not just the two RTL3b names.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                channel.Presence.MembersMap.Put(
                    new PresenceMessage(PresenceAction.Enter, "ghost") { ConnectionId = "old" });
                channel.Presence.InternalMembersMap.Put(
                    new PresenceMessage(PresenceAction.Enter, "ghost") { ConnectionId = "old" });

                channel.SetChannelState(from);
                await client.ProcessCommands();
                channel.Presence.MembersMap.Values.Should().NotBeEmpty();

                client.Close();
                await client.WaitForState(ConnectionState.Closing);
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Detached);
                channel.Presence.MembersMap.Values.Should().BeEmpty();
                channel.Presence.InternalMembersMap.Values.Should().BeEmpty();
            }

            [Theory]
            [InlineData(ChannelState.Attached)]
            [InlineData(ChannelState.Suspended)]
            [InlineData(ChannelState.Detaching)]
            [Trait("spec", "RTP5a")]
            public async Task WhenTheConnectionClosesFromSuspended_ShouldStillClearPresence(ChannelState from)
            {
                // ConnectionSuspendedState.Close() queues SetClosedStateCommand directly, so CLOSING
                // never happens and the CLOSED branch has to cover the same RTP5a teardown.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                channel.Presence.MembersMap.Put(
                    new PresenceMessage(PresenceAction.Enter, "ghost") { ConnectionId = "old" });
                channel.Presence.InternalMembersMap.Put(
                    new PresenceMessage(PresenceAction.Enter, "ghost") { ConnectionId = "old" });

                client.ExecuteCommand(SetSuspendedStateCommand.Create(ErrorInfo.ReasonSuspended));
                await client.WaitForState(ConnectionState.Suspended);
                channel.SetChannelState(from);
                await client.ProcessCommands();

                client.Close();
                await client.WaitForState(ConnectionState.Closed);
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Detached);
                channel.Presence.MembersMap.Values.Should().BeEmpty();
                channel.Presence.InternalMembersMap.Values.Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "RTL24")]
            public async Task AfterACleanClose_ShouldNotStampAFabricatedChannelError()
            {
                // RTL24 lists RTN11d, RTL3a, RTL4g and RTL14 as the sources of channel errorReason. A
                // clean close is none of them, so the ErrorInfo minted for RTL11's presence callbacks
                // must not reach the channel. ably-js passes change.reason, which is null here.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                client.Close();
                await client.WaitForState(ConnectionState.Closed);
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Detached);
                channel.ErrorReason.Should().BeNull();
            }

            [Fact]
            [Trait("spec", "RTL5e")]
            public async Task WhenAPendingDetachCompletesOnClose_ShouldReportSuccess()
            {
                // The detach the caller asked for does complete - CLOSING takes the channel to
                // DETACHED - so the awaiter must report success, not failure. ably-js resolves
                // detach() on that transition too.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                var detach = channel.DetachAsync();
                await client.ProcessCommands();
                channel.State.Should().Be(ChannelState.Detaching);

                client.Close();
                await client.WaitForState(ConnectionState.Closing);
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Detached);
                (await detach).IsSuccess.Should().BeTrue();
            }

            // UTS: realtime/unit/RTL5l/detach-attached-when-disconnected-1
            [Fact]
            [Trait("spec", "RTL5l")]
            public async Task WhenDetachingWhileDisconnected_ShouldDetachImmediately()
            {
                // RTL5l is "anything other than CONNECTED", which includes DISCONNECTED - where an
                // enumerated check would park the DETACH in the RTL6c2 queue and never call back.
                var client = await GetConnectedClient(opts =>
                    opts.DisconnectedRetryTimeout = TimeSpan.FromMinutes(10));

                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.WaitForState(ConnectionState.Disconnected);
                await client.ProcessCommands();

                var detach = await channel.DetachAsync();

                detach.IsSuccess.Should().BeTrue();
                channel.State.Should().Be(ChannelState.Detached);
                client.State.PendingMessages.Should().BeEmpty();

                // "No DETACH message was sent (transport is unavailable)" - checked on the wire,
                // not only on the outbound queue.
                LastCreatedTransport.SentMessages
                    .Select(x => x.Original)
                    .Where(x => x != null)
                    .Should().NotContain(x => x.Action == ProtocolMessage.MessageAction.Detach);
            }

            [Theory]
            [InlineData(ConnectionState.Closing)]
            [InlineData(ConnectionState.Closed)]
            [InlineData(ConnectionState.Failed)]
            [Trait("spec", "RTN8d")]
            [Trait("spec", "RTN9d")]
            public async Task WhenATerminalStateIsEmitted_TheKeyAndIdShouldAlreadyBeNull(
                ConnectionState state)
            {
                // RTN8d and RTN9d: both are null in CLOSED, CLOSING and FAILED. SetState is what
                // emits - inline, when no SynchronizationContext is installed - so the clear has to
                // precede it.
                //
                // Read from inside the listener deliberately: asserting after the commands settle
                // passes either way. Same shape as the RTL3d1 ordering requirement one clause over.
                var client = await GetConnectedClient();
                client.State.Connection.Key.Should().NotBeEmpty();

                string keyAtEmit = null;
                string idAtEmit = null;
                client.Connection.On(state.ToConnectionEvent(), _ =>
                {
                    keyAtEmit = client.Connection.Key;
                    idAtEmit = client.Connection.Id;
                });

                client.ExecuteCommand(state switch
                {
                    ConnectionState.Closing => SetClosingStateCommand.Create(),
                    ConnectionState.Closed => SetClosedStateCommand.Create(),
                    _ => SetFailedStateCommand.Create(new ErrorInfo("gone", 1)),
                });
                await client.ProcessCommands();

                keyAtEmit.Should().BeNullOrEmpty();
                idAtEmit.Should().BeNullOrEmpty();
            }

            [Theory]
            [InlineData(ConnectionState.Failed)]
            [InlineData(ConnectionState.Closed)]
            [InlineData(ConnectionState.Suspended)]
            [Trait("spec", "RTN7e")]
            [Trait("spec", "RTN8d")]
            [Trait("spec", "RTN9d")]
            [Trait("spec", "RTN14h")]
            public async Task WhenTheTransitionThrows_ShouldStillCompleteTheTeardown(
                ConnectionState state)
            {
                // The teardown must complete even when SetState rethrows: otherwise a live transport
                // survives with its listener attached, behind an RTN23a monitor gated on Connected,
                // and RTN7e's failure of the ack queue is skipped.
                //
                // All three states are driven because the key and id differ between them: RTN8d and
                // RTN9d name only CLOSED, CLOSING and FAILED, while SUSPENDED retains them for
                // RTN14h's next resume.
                var client = await GetConnectedClient();
                client.State.WaitingForAck.Add(new MessageAndCallback(new ProtocolMessage(), null));

                // Connection.NotifyUpdate invokes internal handlers unguarded, so throwing from one
                // lands after the connection has entered the state and before the teardown. A plain
                // Exception, not an AblyException, so the workflow's catch does not divert to FAILED
                // and hide the state under test.
                client.Connection.InternalStateChanged += (_, change) =>
                {
                    if (change.Current == state)
                    {
                        throw new Exception("thrown from a state change handler");
                    }
                };

                client.ExecuteCommand(state switch
                {
                    ConnectionState.Failed => SetFailedStateCommand.Create(new ErrorInfo("gone", 1)),
                    ConnectionState.Closed => SetClosedStateCommand.Create(),
                    _ => SetSuspendedStateCommand.Create(new ErrorInfo("gone", 1)),
                });
                await client.ProcessCommands();

                client.Connection.State.Should().Be(state);
                client.State.WaitingForAck.Should().BeEmpty();
                client.ConnectionManager.Transport.Should().BeNull();

                if (state == ConnectionState.Suspended)
                {
                    client.State.Connection.Key.Should().NotBeEmpty();
                    client.State.Connection.Id.Should().NotBeEmpty();
                }
                else
                {
                    client.State.Connection.Key.Should().BeEmpty();
                    client.State.Connection.Id.Should().BeEmpty();
                }
            }

            [Fact]
            [Trait("spec", "RTN21")]
            [Trait("spec", "RTN15b")]
            [Trait("spec", "RTN8b")]
            public async Task WhenAConnectedCarriesNoConnectionDetails_ShouldKeepTheKeyAndTakeTheId()
            {
                // The two fields are guarded differently on purpose. connectionId is top-level and
                // always meaningful per RTN8b - ConnectionIdSpecs pins that a CONNECTED carrying only
                // an id sets it. The key lives inside connectionDetails, which is what RTN21 scopes
                // its override to, so a message carrying none must leave the key alone or a live
                // connection is left with nothing to resume with under RTN15b.
                var client = await GetConnectedClient();
                client.State.Connection.Key.Should().Be("connectionKey");

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "a-different-id",
                });

                await client.ProcessCommands();

                client.State.Connection.Key.Should().Be("connectionKey");
                client.State.Connection.Id.Should().Be("a-different-id");
            }

            private async Task<PubSubRealtimeClient> GetClientWithHistory()
            {
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);

                for (var i = 0; i < 3; i++)
                {
                    client.ExecuteCommand(SendMessageCommand.Create(
                        new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test")));
                }

                await client.ProcessCommands();
                client.State.Connection.MessageSerial.Should().Be(3);
                return client;
            }

            private static async Task MoveTo(PubSubRealtimeClient client, ConnectionState state)
            {
                var error = new ErrorInfo("something went wrong", 50000);

                switch (state)
                {
                    case ConnectionState.Closed:
                        // Closed carries no error of its own, so put one on the channel and the
                        // connection first - RTN11d has to clear both.
                        client.ExecuteCommand(SetFailedStateCommand.Create(error));
                        await client.ProcessCommands();
                        client.ExecuteCommand(SetClosedStateCommand.Create());
                        break;
                    case ConnectionState.Failed:
                        client.ExecuteCommand(SetFailedStateCommand.Create(error));
                        break;
                    default:
                        client.ExecuteCommand(SetDisconnectedStateCommand.Create(error));
                        break;
                }

                await client.ProcessCommands();
            }

            public ReinitialiseOnConnectSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTN19a2")]
        public class ConnectionContinuitySpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN15c6")]
            public async Task OnASuccessfulResume_ShouldKeepTheSerialSequence()
            {
                var client = await GetClientWithOneUnackedMessage();

                await Reconnect(client, connectionId: "1");

                // The same connection is still expecting the serial this message was given.
                client.State.Connection.MessageSerial.Should().Be(3);
                SentSerials(client).Should().Equal(2L);
            }

            [Fact]
            [Trait("spec", "RTN15c6")]
            public async Task OnAResumeCarryingAnError_ShouldStillKeepTheSerialSequence()
            {
                // RTN15c6 - continuity is judged on the connectionId alone. A resume the server
                // honoured can still carry a non-fatal error, which must be surfaced without
                // restarting the sequence. The old code keyed continuity on the error's presence and
                // so got precisely this case wrong, renumbering a connection that had continued.
                var client = await GetClientWithOneUnackedMessage();

                // Through DISCONNECTED first, as a real reconnect arrives. Reconnecting straight from
                // CONNECTED trips UpdateState's same-state early return, which drops the error before
                // it reaches Connection.ErrorReason.
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.WaitForState(ConnectionState.Disconnected);
                await client.ProcessCommands();

                await Reconnect(
                    client,
                    connectionId: "1",
                    error: new ErrorInfo("resumed, with something to report", 80018));

                // Same id, so the counter and the in-flight serial both stand.
                client.State.Connection.MessageSerial.Should().Be(3);
                SentSerials(client).Should().Equal(2L);

                // And the error still reaches the application, per RTN25.
                client.Connection.ErrorReason.Should().NotBeNull();
                client.Connection.ErrorReason.Code.Should().Be(80018);
                client.Connection.State.Should().Be(ConnectionState.Connected);
            }

            [Fact]
            [Trait("spec", "RTN24")]
            public async Task OnAnUpdate_ShouldKeepTheSerialSequence()
            {
                var client = await GetClientWithOneUnackedMessage();

                await Reconnect(client, connectionId: "1", isUpdate: true);

                client.State.Connection.MessageSerial.Should().Be(3);
            }

            [Fact]
            [Trait("spec", "RTN24")]
            [Trait("spec", "RTN19a")]
            public async Task OnAnUpdate_ShouldNotResendTheMessagesStillInFlight()
            {
                // RTN19a is scoped to a transport that has been disconnected. An RTN24 update - a
                // reauth, typically - disconnects nothing, so the transport still holding these
                // messages is the one that will ACK them. Resending would put a duplicate of each on
                // the wire for Ably to discard by msgSerial.
                var client = await GetClientWithOneUnackedMessage();

                await Reconnect(client, connectionId: "1", isUpdate: true);

                SentSerials(client).Should().BeEmpty();

                // Left in flight under its original serial, which is the one the ACK will carry.
                client.State.WaitingForAck.Should().HaveCount(1);
                client.State.WaitingForAck.Single().Message.MsgSerial.Should().Be(2);
            }

            [Fact]
            [Trait("spec", "RTN15c7")]
            public async Task OnAFreshConnectionWithoutAnError_ShouldRestartTheSerialSequence()
            {
                // A resume the server refuses is answered with a new connectionId and, often, no
                // error at all - which is precisely when the sequence must restart.
                var client = await GetClientWithOneUnackedMessage();

                await Reconnect(client, connectionId: "different");

                SentSerials(client).Should().Equal(0L);
                client.State.Connection.MessageSerial.Should().Be(1);
            }

            // UTS: realtime/unit/RTN15c7/failed-resume-new-id-0
            [Fact]
            [Trait("spec", "RTN15c7")]
            public async Task OnAFailedResume_ShouldRestartTheSerialSequence()
            {
                var client = await GetClientWithOneUnackedMessage();
                var originalId = client.State.Connection.Id;

                // Through DISCONNECTED first, as a refused resume actually arrives. Reconnecting
                // straight from CONNECTED trips UpdateState's same-state early return, which drops
                // the error before it reaches Connection.ErrorReason.
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.WaitForState(ConnectionState.Disconnected);
                await client.ProcessCommands();

                await Reconnect(
                    client,
                    connectionId: "different",
                    error: new ErrorInfo("resume failed", 80008),
                    connectionKey: "connectionKey-2");

                SentSerials(client).Should().Equal(0L);
                client.State.Connection.MessageSerial.Should().Be(1);

                // The rest of what a refused resume looks like: a new id and key, the reason on the
                // connection, and CONNECTED all the same.
                client.State.Connection.Id.Should().Be("different").And.NotBe(originalId);
                client.State.Connection.Key.Should().Be("connectionKey-2");
                client.Connection.ErrorReason.Should().NotBeNull();
                client.Connection.ErrorReason.Code.Should().Be(80008);
                client.Connection.State.Should().Be(ConnectionState.Connected);
            }

            [Fact]
            public async Task WhenRestartingTheSequence_ShouldNotLeaveStaleEntriesAwaitingAck()
            {
                // Requeued messages are re-registered for their ACK as they are sent, so the original
                // entries must go: they hold serials from the abandoned sequence that a later ACK
                // would also match, running the same callback twice.
                var client = await GetClientWithOneUnackedMessage();

                await Reconnect(client, connectionId: "different");

                client.State.WaitingForAck.Should().HaveCount(1);
                client.State.WaitingForAck.Single().Message.MsgSerial.Should().Be(0);
            }

            [Fact]
            [Trait("spec", "RTN19a1")]
            public async Task OnAFailedResume_ShouldRenumberTheInFlightMessageAheadOfTheQueuedOne()
            {
                // The in-flight message was published before the one queued while disconnected, so on
                // a fresh sequence it must take the lower serial. RTN19a1 puts the renumbered
                // messages on the RTL6c2 queue, which already holds the later one - appending would
                // reverse publish order on the wire, and every other test here leaves that queue
                // empty, so nothing pins it.
                var client = await GetClientWithOneUnackedMessage();

                // A publish while disconnected, on its own channel so the two are told apart on the
                // wire rather than only by serial - a reversal would still read as 0 then 1.
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.WaitForState(ConnectionState.Disconnected);
                client.ExecuteCommand(SendMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Message, "queued-while-disconnected")));
                await client.ProcessCommands();

                client.State.WaitingForAck.Should().HaveCount(1, "the in-flight message is still awaiting its ACK");
                client.State.PendingMessages.Should().HaveCount(1, "and the new publish is queued behind it");

                await Reconnect(client, connectionId: "different");

                SentFrames(client).Should().Equal(
                    ("test", 0L),
                    ("queued-while-disconnected", 1L));
            }

            // UTS: realtime/unit/RTN16f/recover-initializes-msgserial-0
            [Fact]
            [Trait("spec", "RTN16f")]
            public async Task OnASuccessfulRecover_ShouldKeepTheRecoveredSerial()
            {
                // RTN16f initialises the counter from the recovery key. A recover deliberately adopts
                // another connection's sequence, so a successful one must not restart it, even though
                // the connectionId is one we have never seen.
                var client = GetClientWithFakeTransport(options => options.Recover =
                    "{\"connectionKey\":\"uniqueKey\",\"msgSerial\":45,\"channelSerials\":{}}");
                // WaitForState returns as soon as the state is set, which happens before
                // ConnectionManager.CreateTransport runs - and that is what applies the recovered
                // serial. Drain the queue so the precondition is established rather than raced.
                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();
                client.State.Connection.MessageSerial.Should().Be(45);

                await Reconnect(client, connectionId: "recovered");

                client.State.Connection.MessageSerial.Should().Be(45);

                // And it is the serial actually put on the wire, which is what RTN16f is for.
                client.ExecuteCommand(SendMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test")));
                await client.ProcessCommands();
                SentSerials(client).Should().Equal(45L);
            }

            [Fact]
            [Trait("spec", "RTN16f")]
            [Trait("spec", "RTN15c7")]
            public async Task OnAFailedRecover_ShouldRestartTheSerialSequence()
            {
                // RTN16f: "If the recover fails, the counter should be reset to 0 per RTN15c7."
                var client = GetClientWithFakeTransport(options => options.Recover =
                    "{\"connectionKey\":\"uniqueKey\",\"msgSerial\":45,\"channelSerials\":{}}");
                // WaitForState returns as soon as the state is set, which happens before
                // ConnectionManager.CreateTransport runs - and that is what applies the recovered
                // serial. Drain the queue so the precondition is established rather than raced.
                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();
                client.State.Connection.MessageSerial.Should().Be(45);

                await Reconnect(client, connectionId: "different", error: new ErrorInfo("unable to recover", 80008));

                client.State.Connection.MessageSerial.Should().Be(0);
            }

            private async Task<PubSubRealtimeClient> GetClientWithOneUnackedMessage()
            {
                var client = await GetConnectedClient();

                for (var i = 0; i < 3; i++)
                {
                    client.ExecuteCommand(SendMessageCommand.Create(
                        new ProtocolMessage(ProtocolMessage.MessageAction.Message, "test")));
                }

                // Acknowledge the first two, so the sequence is deliberately not zero based and a
                // restart is distinguishable from a resume.
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Ack) { MsgSerial = 0, Count = 2 }));
                await client.ProcessCommands();

                client.State.Connection.MessageSerial.Should().Be(3);
                client.State.WaitingForAck.Should().HaveCount(1);
                client.State.WaitingForAck.Single().Message.MsgSerial.Should().Be(2);

                LastCreatedTransport.SentMessages.Clear();
                return client;
            }

            private static async Task Reconnect(
                PubSubRealtimeClient client, string connectionId, bool isUpdate = false, ErrorInfo error = null, string connectionKey = "connectionKey")
            {
                await client.Workflow.ProcessCommand(SetConnectedStateCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                    {
                        ConnectionId = connectionId,
                        ConnectionDetails = new ConnectionDetails { ConnectionKey = connectionKey },
                        Error = error,
                    },
                    isUpdate));
                await client.ProcessCommands();
            }

            private IEnumerable<(string Channel, long Serial)> SentFrames(PubSubRealtimeClient client) =>
                LastCreatedTransport.SentMessages
                    .Select(x => x.Original)
                    .Where(x => x != null && x.Action == ProtocolMessage.MessageAction.Message)
                    .Select(x => (x.Channel, x.MsgSerial))
                    .ToList();

            private IEnumerable<long> SentSerials(PubSubRealtimeClient client) =>
                LastCreatedTransport.SentMessages
                    .Select(x => x.Original)
                    .Where(x => x != null && x.Action == ProtocolMessage.MessageAction.Message)
                    .Select(x => x.MsgSerial)
                    .ToList();

            public ConnectionContinuitySpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTN24")]
        public class ConnectedUpdateSpecs : AblyRealtimeSpecs
        {
            [Fact]
            [Trait("spec", "RTN15c7")]
            [Trait("spec", "RTL3d")]
            public async Task AfterAFailedResume_ShouldReattachAnAttachedChannel()
            {
                // RTN15c7 - a resume the server refused, answered with a connectionId we were not
                // holding. RTL3d reattaches regardless.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.ProcessCommands();
                client.ExecuteCommand(SetConnectingStateCommand.Create());
                await client.ProcessCommands();

                // RTN14h - the reconnection attempt still carries the resume.
                client.State.Connection.Key.Should().NotBeEmpty();
                LastCreatedTransport.SentMessages.Clear();

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "brand-new",
                    ConnectionDetails = new ConnectionDetails { ConnectionKey = "newKey" },
                });
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Attaching);
                LastCreatedTransport.SentMessages
                    .Select(x => x.Original)
                    .Where(x => x != null)
                    .Select(x => x.Action)
                    .Should().Contain(ProtocolMessage.MessageAction.Attach);
            }

            [Fact]
            [Trait("spec", "RTL3d")]
            public async Task OnAnUpdate_ShouldNotReattachAttachedChannels()
            {
                // RTL3d applies on entering CONNECTED. An RTN24 update arrives on the connection we
                // already hold, so reattaching would be a spurious round trip per channel.
                var client = await GetConnectedClient();
                var channel = (RealtimeChannel)client.Channels.Get("test");
                channel.SetChannelState(ChannelState.Attached);
                await client.ProcessCommands();

                LastCreatedTransport.SentMessages.Clear();
                var states = new List<ChannelState>();
                channel.On(x => states.Add(x.Current));

                // Same connectionId, delivered as an update - the shape RTC8a reauth produces.
                await client.Workflow.ProcessCommand(SetConnectedStateCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                    {
                        ConnectionId = "1",
                        ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                    },
                    isUpdate: true));
                await client.ProcessCommands();

                channel.State.Should().Be(ChannelState.Attached);
                states.Should().NotContain(ChannelState.Attaching);
                LastCreatedTransport.SentMessages
                    .Select(x => x.Original?.Action)
                    .Should().NotContain(ProtocolMessage.MessageAction.Attach);
            }

            public ConnectedUpdateSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RTN23a")]
        public class IdleTimeoutSpecs : AblyRealtimeSpecs
        {
            private static readonly TimeSpan PromisedMaxIdleInterval = TimeSpan.FromSeconds(15);
            private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

            // 15s promised by the server plus the 10s realtimeRequestTimeout.
            private static readonly TimeSpan AllowedIdleTime = PromisedMaxIdleInterval + RequestTimeout;

            private readonly Now _now = new Now();

            [Fact]
            [Trait("spec", "TO3l11")]
            public async Task ShouldUseTheConfiguredRealtimeRequestTimeout()
            {
                // TO3l11 makes realtimeRequestTimeout a client option, and RTN23a derives its
                // threshold from it, so a caller changing it has to move the idle timeout with it.
                var client = await GetConnectedClient(PromisedMaxIdleInterval, TimeSpan.FromSeconds(30));

                // Past the default 25s window but inside the 45s this client asked for.
                _now.Reset(_now.Value.Add(TimeSpan.FromSeconds(40)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value))).Should().BeEmpty();

                _now.Reset(_now.Value.Add(TimeSpan.FromSeconds(10)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().ContainSingle().Which.Should().BeOfType<SetDisconnectedStateCommand>();
            }

            [Fact]
            public async Task WhenIdleForLongerThanAllowed_ShouldDisconnect()
            {
                var client = await GetConnectedClient(PromisedMaxIdleInterval);

                _now.Reset(_now.Value.Add(AllowedIdleTime.Add(TimeSpan.FromSeconds(1))));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                var disconnect = commands.Should().ContainSingle()
                    .Which.Should().BeOfType<SetDisconnectedStateCommand>().Subject;
                disconnect.Error.Code.Should().Be(ErrorCodes.Disconnected);
                disconnect.Error.StatusCode.Should().Be(HttpStatusCode.RequestTimeout);

                // RTN15a - the idle disconnect asks to reconnect at once rather than waiting out
                // the disconnected retry timeout.
                disconnect.RetryInstantly.Should().BeTrue();
            }

            // UTS: realtime/unit/RTN23a/idle-timeout-reconnect-1
            [Fact]
            [Trait("spec", "RTN23a")]
            [Trait("spec", "RTN15a")]
            public async Task WhenTheIdleTimeoutFires_ShouldDisconnectAndReconnect()
            {
                // The UTS case asserts the whole cycle, not just the decision to disconnect:
                // connecting, connected, disconnected, connecting, connected, with a second
                // connection attempt and a new connectionId. The test above pins what the monitor
                // decides; this one pins what the client does with it.
                var client = await GetConnectedClient(PromisedMaxIdleInterval);

                var states = new List<ConnectionState>();
                client.Connection.On(args => states.Add(args.Current));

                CreatedTransports.Should().HaveCount(1);

                _now.Reset(_now.Value.Add(AllowedIdleTime.Add(TimeSpan.FromSeconds(1))));
                foreach (var command in await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                {
                    client.ExecuteCommand(command);
                }

                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();

                // RTN15a's immediate reconnect built a second transport.
                CreatedTransports.Should().HaveCount(2);

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "connection-id-2",
                    ConnectionDetails = new ConnectionDetails
                    {
                        ConnectionKey = "key-2",
                        MaxIdleInterval = PromisedMaxIdleInterval,
                    },
                });
                await client.WaitForState(ConnectionState.Connected);
                await client.ProcessCommands();

                states.Should().ContainInOrder(
                    ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected);
                client.Connection.Id.Should().Be("connection-id-2");
            }

            [Fact]
            public async Task WhenIdleForExactlyTheAllowedTime_ShouldNotDisconnect()
            {
                var client = await GetConnectedClient(PromisedMaxIdleInterval);

                _now.Reset(_now.Value.Add(AllowedIdleTime));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                commands.Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "CD2h")]
            public async Task WhenMaxIdleIntervalIsZero_ShouldNeverDisconnect()
            {
                // A zero maxIdleInterval means Ably allows arbitrarily long inactivity.
                var client = await GetConnectedClient(TimeSpan.Zero);

                _now.Reset(_now.Value.Add(TimeSpan.FromHours(1)));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                commands.Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "CD2h")]
            public async Task WhenMaxIdleIntervalIsAbsent_ShouldNeverDisconnect()
            {
                // Ably declining to send the field is Ably declining to make the promise.
                var client = await GetConnectedClient(null);

                _now.Reset(_now.Value.Add(TimeSpan.FromHours(1)));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                commands.Should().BeEmpty();
            }

            [Fact]
            public async Task WhenNotConnected_ShouldNotDisconnect()
            {
                // ConfirmedAliveAt can still be carrying a timestamp from a previous transport, so
                // acting on it outside Connected risks tearing down a healthy new one.
                var client = await GetConnectedClient(PromisedMaxIdleInterval);

                await client.Workflow.ProcessCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                _now.Reset(_now.Value.Add(TimeSpan.FromHours(1)));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                commands.Should().BeEmpty();
            }

            // UTS: realtime/unit/RTN23a/any-message-resets-timer-3
            [Fact]
            public async Task AnyReceivedMessage_NotOnlyHeartbeat_ShouldResetTheIdleTimer()
            {
                var client = await GetConnectedClient(PromisedMaxIdleInterval);

                // Most of the way through the allowed window, then a message that is deliberately
                // not a Heartbeat - RTN23a counts any received message as a sign of activity.
                _now.Reset(_now.Value.Add(TimeSpan.FromSeconds(20)));
                client.ExecuteCommand(ProcessMessageCommand.Create(
                    new ProtocolMessage(ProtocolMessage.MessageAction.Attached) { Channel = "test" }));
                await client.ProcessCommands();

                // Another 20s on from that message is still inside the 25s window.
                _now.Reset(_now.Value.Add(TimeSpan.FromSeconds(20)));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                commands.Should().BeEmpty();

                // Still a single attempt at this point, per the case.
                CreatedTransports.Should().HaveCount(1);

                // And once the window really does elapse with no activity, the cycle runs.
                _now.Reset(_now.Value.Add(AllowedIdleTime.Add(TimeSpan.FromSeconds(1))));
                foreach (var command in await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                {
                    client.ExecuteCommand(command);
                }

                await client.WaitForState(ConnectionState.Connecting);
                await client.ProcessCommands();
                CreatedTransports.Should().HaveCount(2);
            }

            [Fact]
            [Trait("spec", "RTN23a")]
            [Trait("spec", "CD2h")]
            public async Task WhenANewTransportOmitsMaxIdleInterval_ShouldStopMeasuring()
            {
                // RTN23a measures against the interval "sent in the connectionDetails of the most
                // recent CONNECTED message received on that transport", so a CONNECTED starting a new
                // transport must not inherit the previous threshold. An omitted field is Ably
                // declining to promise anything, which CD2h treats as arbitrarily long inactivity.
                var client = await GetConnectedClient(PromisedMaxIdleInterval);

                // A real reconnect, so the CONNECTED lands on a new transport rather than becoming
                // an RTN24 update on the one we already hold.
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.ProcessCommands();
                client.ExecuteCommand(SetConnectingStateCommand.Create());
                await client.ProcessCommands();

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "2",
                    ConnectionDetails = new ConnectionDetails { ConnectionKey = "anotherKey" },
                });

                await client.WaitForState(ConnectionState.Connected);
                await client.ProcessCommands();

                client.State.Connection.MaxIdleInterval.Should().BeNull();

                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "RTN23a")]
            [Trait("spec", "RTN24")]
            public async Task WhenAnUpdateOmitsMaxIdleInterval_ShouldKeepMeasuring()
            {
                // An RTN24 update arrives on the transport we already hold, which is still bound by
                // whatever it promised, so an omitted field is not a withdrawal. ably-js keeps the
                // previous value here with its if (maxPromisedIdle) guard, nulling the field per
                // transport instead. Nulling it here would disarm RTN23a on a live transport.
                var client = await GetConnectedClient(PromisedMaxIdleInterval);
                var transportBefore = LastCreatedTransport;

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "1",
                    ConnectionDetails = new ConnectionDetails { ConnectionKey = "connectionKey" },
                });

                await client.ProcessCommands();

                // Same transport - nothing reconnected.
                LastCreatedTransport.Should().BeSameAs(transportBefore);
                client.State.Connection.MaxIdleInterval.Should().Be(PromisedMaxIdleInterval);

                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().ContainSingle().Which.Should().BeOfType<SetDisconnectedStateCommand>();
            }

            [Fact]
            [Trait("spec", "RTN23a")]
            public async Task WhenTheThresholdCannotBeRepresented_ShouldNotThrow()
            {
                // The wire value is an unbounded integer of milliseconds, so the sum can overflow. A
                // throw would be logged and dropped by the command loop, and every later tick would
                // throw too - killing idle detection silently for the life of the connection.
                var client = await GetConnectedClient(TimeSpan.MaxValue - TimeSpan.FromSeconds(1));

                _now.Reset(_now.Value.Add(TimeSpan.FromDays(1)));

                var commands = await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value));

                commands.Should().BeEmpty();
                client.Connection.State.Should().Be(ConnectionState.Connected);
            }

            [Theory]
            [InlineData("Heartbeats", false)]
            [InlineData("heartbeats", false)]
            [InlineData("heartbeats", "no")]
            [InlineData("heartbeats", 0)]
            [InlineData("heartbeats", null)]

            // A differently-cased key with a true value still leaves nothing on the wire asking for
            // protocol heartbeats: Merge drops our correctly-cased entry, and only that spelling is
            // the param Ably reads.
            [InlineData("Heartbeats", true)]
            [InlineData("HEARTBEATS", "true")]
            [Trait("spec", "RTN23b")]
            public async Task WhenTheCallerDoesNotAskForProtocolHeartbeats_ShouldStandDown(
                string key, object value)
            {
                // Two halves, both needed. Merge drops our own heartbeats param on a
                // case-insensitive key match, so "Heartbeats" reaches the wire in place of ours. And
                // RTN23b guarantees protocol heartbeats only for the literal "true": "if it is false
                // or unspecified, the server is permitted to use any transport-level mechanism" -
                // which this library cannot observe.
                var client = GetClientWithFakeTransport(opts =>
                {
                    opts.NowFunc = _now.ValueFn;
                    opts.RealtimeRequestTimeout = RequestTimeout;
                    opts.HeartbeatMonitorDelay = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
                    opts.TransportParams = new Dictionary<string, object> { { key, value } };
                });

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "1",
                    ConnectionDetails = new ConnectionDetails
                    {
                        ConnectionKey = "connectionKey",
                        MaxIdleInterval = PromisedMaxIdleInterval,
                    },
                });

                await client.WaitForState(ConnectionState.Connected);

                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().BeEmpty();
            }

            [Fact]
            [Trait("spec", "RTN23b")]
            public async Task WhenTheCallerChangesTransportParams_ShouldFollowTheNextTransport()
            {
                // The decision is per transport, not per client and not per tick. A caller can
                // mutate ClientOptions.TransportParams at any time, but the transport already open
                // went out with whatever it went out with - so a change must not retune the monitor
                // for the current transport, only for the next one built from it.
                var client = GetClientWithFakeTransport(opts =>
                {
                    opts.NowFunc = _now.ValueFn;
                    opts.RealtimeRequestTimeout = RequestTimeout;
                    opts.HeartbeatMonitorDelay = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
                    opts.TransportParams = new Dictionary<string, object> { { "heartbeats", "false" } };
                });

                client.FakeProtocolMessageReceived(ConnectedWithMaxIdleInterval());
                await client.WaitForState(ConnectionState.Connected);

                LastCreatedTransport.Parameters.GetParams()
                    .Should().Contain(new KeyValuePair<string, string>("heartbeats", "false"));

                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().BeEmpty("this transport did not ask for protocol heartbeats");

                client.Options.TransportParams["heartbeats"] = "true";

                // Still stood down: the change cannot reach a transport that has already been built.
                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().BeEmpty("the open transport still went out with heartbeats=false");

                // A new transport, which does carry the change.
                client.ExecuteCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
                await client.WaitForState(ConnectionState.Disconnected);
                client.ExecuteCommand(SetConnectingStateCommand.Create());
                await client.ProcessCommands();

                LastCreatedTransport.Parameters.GetParams()
                    .Should().Contain(new KeyValuePair<string, string>("heartbeats", "true"));

                client.FakeProtocolMessageReceived(ConnectedWithMaxIdleInterval());
                await client.WaitForState(ConnectionState.Connected);

                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().ContainSingle("the current transport does ask for protocol heartbeats");
            }

            private static ProtocolMessage ConnectedWithMaxIdleInterval() =>
                new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "1",
                    ConnectionDetails = new ConnectionDetails
                    {
                        ConnectionKey = "connectionKey",
                        MaxIdleInterval = PromisedMaxIdleInterval,
                    },
                };

            [Theory]
            [InlineData("heartbeats", true)]
            [InlineData("heartbeats", "true")]
            [InlineData("heartbeats", "TRUE")]
            [Trait("spec", "RTN23b")]
            public async Task WhenTheCallerAsksForProtocolHeartbeats_ShouldStayArmed(string key, object value)
            {
                // Standing down is the guard's default outcome for any caller value, so the armed
                // branch is the one an edit could lose silently. This pins it.
                var client = GetClientWithFakeTransport(opts =>
                {
                    opts.NowFunc = _now.ValueFn;
                    opts.RealtimeRequestTimeout = RequestTimeout;
                    opts.HeartbeatMonitorDelay = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
                    opts.TransportParams = new Dictionary<string, object> { { key, value } };
                });

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "1",
                    ConnectionDetails = new ConnectionDetails
                    {
                        ConnectionKey = "connectionKey",
                        MaxIdleInterval = PromisedMaxIdleInterval,
                    },
                });

                await client.WaitForState(ConnectionState.Connected);

                _now.Reset(_now.Value.Add(AllowedIdleTime).Add(TimeSpan.FromSeconds(1)));
                (await client.Workflow.ProcessCommand(HeartbeatMonitorCommand.Create(_now.Value)))
                    .Should().ContainSingle().Which.Should().BeOfType<SetDisconnectedStateCommand>();
            }

            [Fact]
            [Trait("spec", "RTN21")]
            [Trait("spec", "RTN15b")]
            public async Task WhenAConnectedCarriesNoConnectionDetails_ShouldKeepTheConnectionKey()
            {
                // RTN21 scopes the override to "the attributes within ConnectionDetails", so a
                // CONNECTED carrying none overrides nothing and the key has to survive.
                var client = await GetConnectedClient(PromisedMaxIdleInterval);
                client.State.Connection.Key.Should().Be("connectionKey");

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "1",
                });

                await client.ProcessCommands();

                client.State.Connection.Key.Should().Be("connectionKey");
            }

            private async Task<PubSubRealtimeClient> GetConnectedClient(
                TimeSpan? maxIdleInterval, TimeSpan? requestTimeout = null)
            {
                var client = GetClientWithFakeTransport(opts =>
                {
                    opts.NowFunc = _now.ValueFn;
                    opts.RealtimeRequestTimeout = requestTimeout ?? RequestTimeout;

                    // These tests drive the monitor tick explicitly and assert on what that tick
                    // decides, so the workflow's own once-a-second loop is pushed out of reach; on a
                    // loaded run it would fire first and the explicit tick would find the disconnect
                    // already requested.
                    opts.HeartbeatMonitorDelay = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
                });

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
                {
                    ConnectionId = "1",
                    ConnectionDetails = new ConnectionDetails
                    {
                        ConnectionKey = "connectionKey",
                        MaxIdleInterval = maxIdleInterval,
                    },
                });

                await client.WaitForState(ConnectionState.Connected);
                return client;
            }

            public IdleTimeoutSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public RealtimeWorkflowSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
