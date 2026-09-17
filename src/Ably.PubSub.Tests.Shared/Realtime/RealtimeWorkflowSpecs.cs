using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
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
                var client = GetRealtimeClient(opts => opts.RealtimeHost = "non-default.ably.io"); // Force no fallback

                await client.WaitForState(ConnectionState.Connecting);

                // Arrange
                ErrorInfo targetError = new ErrorInfo("test", 123);

                // Act
                client.Workflow.ProcessCommand(ProcessMessageCommand.Create(new ProtocolMessage(ProtocolMessage.MessageAction.Error) { Error = targetError }));

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
            [Fact]
            public async Task WithInboundErrorMessageWhenItCanUseFallBack_ShouldClearsConnectionKey()
            {
                // Arrange
                var client = GetRealtimeClient(options =>
                {
                    options.RealtimeRequestTimeout = TimeSpan.FromSeconds(60);
                    options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(60);
                });

                await client.WaitForState(ConnectionState.Connecting);

                var messageWithError = new ProtocolMessage(ProtocolMessage.MessageAction.Error)
                {
                    Error = new ErrorInfo("test", 123, System.Net.HttpStatusCode.InternalServerError),
                };

                // Act
                await client.ProcessMessage(messageWithError);

                client.State.Connection.Key.Should().BeEmpty();
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

            public AckProtocolTests(ITestOutputHelper output)
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
