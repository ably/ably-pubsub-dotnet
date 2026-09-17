using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Shared.Realtime;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Tests.Shared.Utils;
using IO.Ably.Transport;
using IO.Ably.Transport.States.Connection;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Collection("SandBox Connection")]
    [Trait("type", "integration")]
    public class ConnectionSandBoxSpecs : SandboxSpecs
    {
        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN6")]
        public async Task WithAutoConnectTrue_ShouldConnectToAblyInTheBackground(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            await WaitForState(client);

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN4b")]
        [Trait("spec", "RTN4d")]
        [Trait("spec", "RTN4e")]
        [Trait("spec", "RTN11a")]
        public async Task ANewConnectionShouldRaiseConnectingAndConnectedEvents(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false);
            var states = new List<ConnectionState>();
            client.Connection.On((args) =>
            {
                args.Should().BeOfType<ConnectionStateChange>();
                states.Add(args.Current);
            });

            client.Connect();

            await WaitForState(client);

            states.Should().BeEquivalentTo(new[] { ConnectionState.Connecting, ConnectionState.Connected });
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        public async Task WhenClosingAConnection_ItShouldRaiseClosingAndClosedEvents(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            // Start collecting events after the connection is open
            await WaitForState(client);

            var states = new List<ConnectionState>();
            client.Connection.On((args) =>
            {
                args.Should().BeOfType<ConnectionStateChange>();
                states.Add(args.Current);
            });

            client.Close();

            await WaitForState(client, ConnectionState.Closed, TimeSpan.FromSeconds(5));

            states.Should().BeEquivalentTo(new[] { ConnectionState.Closing, ConnectionState.Closed });
            client.Connection.State.Should().Be(ConnectionState.Closed);
        }

        [Theory]
        [ProtocolData]
        public async Task ShouldSaveConnectionStateTtlToConnectionObject(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, _) => options.AutoConnect = false);
            client.State.Connection.ConnectionStateTtl = TimeSpan.FromSeconds(60);
            client.State.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromSeconds(60));
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromSeconds(60));

            client.Connect();
            await WaitForState(client);

            var connectMessage = client.GetTestTransport().ProtocolMessagesReceived.First(msg => msg.Action == ProtocolMessage.MessageAction.Connected);
            connectMessage.ConnectionDetails.ConnectionStateTtl.Should().Be(TimeSpan.FromMinutes(2));
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromMinutes(2));
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN8b")]
        [Trait("spec", "RTN9b")]
        public async Task WithMultipleClients_ShouldHaveUniqueConnectionKeysAndIdsProvidedByAbly(Protocol protocol)
        {
            var clients = new[]
            {
                await GetRealtimeClient(protocol),
                await GetRealtimeClient(protocol),
                await GetRealtimeClient(protocol)
            };

            // Wait for the clients to connect
            await Task.WhenAll(clients.Select(x => x.WaitForState(ConnectionState.Connected)));

            var distinctConnectionKeys = clients.Select(x => x.Connection.Key).Distinct();
            var distinctConnectionIds = clients.Select(x => x.Connection.Id).Distinct();
            distinctConnectionKeys.Should().HaveCount(3);
            distinctConnectionIds.Should().HaveCount(3);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN11b")]
        public async Task WithClosingConnection_WhenConnectCalled_ShouldMakeNewConnectionAndTransport(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.DisconnectedRetryTimeout = TimeSpan.MaxValue;
            });

            await client.WaitForState();

            // capture initial values
            var initialConnection = client.Connection;
            var initialConnectionId = initialConnection.Id;
            var initialTransport = client.ConnectionManager.Transport;

            // The close timeout is 1000ms, so 3000ms is enough time to wait
            var awaiter = new TaskCompletionAwaiter(3000);
            client.Connection.On(ConnectionEvent.Closed, (state) =>
            {
                awaiter.SetCompleted();
            });

            client.Close();
            await client.WaitForState(ConnectionState.Closing);

            client.Connect();
            await client.WaitForState(ConnectionState.Connected);

            client.Connection.Id.Should().NotBeNullOrEmpty();
            client.Connection.Id.Should().NotBe(initialConnectionId);
            client.ConnectionManager.Transport.Should().NotBe(initialTransport);

            // because a new transport is created the CLOSED message for the
            // old connection never arrives.
            var didClose = await awaiter.Task;
            didClose.Should().BeFalse();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN11c")]
        public async Task WithDisconnectedConnection_WhenConnectCalled_ImmediatelyReconnect(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            await client.WaitForState();
            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected));
            await client.WaitForState(ConnectionState.Disconnected);
            var s = new Stopwatch();
            s.Start();
            client.Connect();
            await client.WaitForState(ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);
            s.Stop();

            // show the reconnect happened before the retry timeout could have fired
            s.Elapsed.Should().BeLessThan(client.Options.DisconnectedRetryTimeout);
        }

        [Theory(Skip = "Intermittently fails")]
        [ProtocolData]
        [Trait("spec", "RTN11c")]
        public async Task WithSuspendedConnection_WhenConnectCalled_ImmediatelyReconnect(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            client.Workflow.SetState(new ConnectionSuspendedState(client.ConnectionManager, new ErrorInfo("force suspended"), client.Logger));
            await client.WaitForState(ConnectionState.Suspended);
            var s = new Stopwatch();
            s.Start();
            client.Connect();
            await client.WaitForState(ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);
            s.Stop();

            // show the reconnect happened before the retry timeout should fired
            s.Elapsed.Should().BeLessThan(client.Options.DisconnectedRetryTimeout);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN11d")]
        public async Task WithFailedConnection_WhenConnectCalled_TransitionsChannelsToInitialized(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            await client.WaitForState(ConnectionState.Connected);

            var chan1 = client.Channels.Get("RTN11d".AddRandomSuffix());
            await chan1.AttachAsync();

            // show that the channel is not in the initialized state already
            chan1.State.Should().NotBe(ChannelState.Initialized);

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected));
            await client.WaitForState(ConnectionState.Disconnected);
            client.Workflow.QueueCommand(SetFailedStateCommand.Create(new ErrorInfo("force failed")));
            await client.WaitForState(ConnectionState.Failed);

            // show there is a no-null error present on the connection
            client.Connection.ErrorReason.Message.Should().Be("force failed");
            client.Connect();
            await client.WaitForState(ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // transitions all the channels to INITIALIZED
            chan1.State.Should().Be(ChannelState.Initialized);

            // sets their errorReason to null
            chan1.ErrorReason.Should().BeNull();

            // and sets the connection's errorReason to null
            client.Connection.ErrorReason.Should().BeNull();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN11d")]
        public async Task WithClosedConnection_WhenConnectCalled_TransitionsChannelsToInitialized(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            await client.WaitForState(ConnectionState.Connected);

            var chan1 = client.Channels.Get("RTN11d".AddRandomSuffix());
            await chan1.AttachAsync();

            // show that the channel is not in the initialized state already
            chan1.State.Should().NotBe(ChannelState.Initialized);

            client.Close();
            await client.WaitForState(ConnectionState.Closed);

            // show there is a no-null error present on the connection
            client.Connect();
            await client.WaitForState(ConnectionState.Connecting);
            client.Connection.State.Should().Be(ConnectionState.Connecting);

            // transitions all the channels to INITIALIZED
            chan1.State.Should().Be(ChannelState.Initialized);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN12d")]
        public async Task WithDisconnectedOrSuspendedConnection_WhenCloseCalled_AbortRetryAndCloseImmediately(Protocol protocol)
        {
            async Task AssertsClosesAndDoesNotReconnect(AblyRealtime realtime, ConnectionState state)
            {
                await realtime.WaitForState(state);

                var reconnectAwaiter = new TaskCompletionAwaiter(5000);
                realtime.Connection.On(args =>
                {
                    if (realtime.Connection.State == ConnectionState.Connecting
                        || realtime.Connection.State == ConnectionState.Connected)
                    {
                        reconnectAwaiter.SetCompleted();
                    }
                });

                realtime.Close();
                await realtime.WaitForState(ConnectionState.Closed);
                realtime.Connection.State.Should().Be(ConnectionState.Closed);

                var didReconnect = await reconnectAwaiter.Task;
                didReconnect.Should().BeFalse($"should not attempt a reconnect for state {state}");
            }

            // setup a new client and put into a DISCONNECTED state
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.DisconnectedRetryTimeout = TimeSpan.FromSeconds(2);
            });

            await client.WaitForState(ConnectionState.Connected);
            client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(new ErrorInfo("force disconnect")));

            await AssertsClosesAndDoesNotReconnect(client, ConnectionState.Disconnected);

            // reinitialize the client and put into a SUSPENDED state
            client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.SuspendedRetryTimeout = TimeSpan.FromSeconds(2);
            });

            await client.WaitForState();
            client.Workflow.QueueCommand(SetSuspendedStateCommand.Create(new ErrorInfo("force suspend")));
            await AssertsClosesAndDoesNotReconnect(client, ConnectionState.Suspended);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN13a")]
        public async Task WithConnectedClient_PingShouldReturnServiceTime(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            await WaitForState(client);

            var result = await client.Connection.PingAsync();

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN14a")]
        public async Task WithInvalidApiKey_ShouldSetToFailedStateAndAddErrorMessageToEmittedState(Protocol protocol)
        {
            var invalidKey = "invalid-key".AddRandomSuffix();
            ApiKey.IsValidFormat(invalidKey).Should().BeFalse();

            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;

                // a string not in the valid key format aaaa.bbbb:cccc
                opts.Key = invalidKey;
            });

            ErrorInfo error = null;
            client.Connection.On((args) =>
            {
                error = args.Reason;
            });

            client.Connect();

            await WaitForState(client, ConnectionState.Failed);

            error.Should().NotBeNull();
            client.Connection.ErrorReason.Should().BeSameAs(error);

            // this assertion shows that we are picking up a client side validation error
            // if this key is passed to the server we would get an error with a 40005 code
            client.Connection.ErrorReason.Code.Should().Be(ErrorCodes.InvalidCredentials);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN14b")]
        public async Task WithExpiredRenewableToken_ShouldAutomaticallyRenewTokenAndNoErrorShouldBeEmitted(Protocol protocol)
        {
            var restClient = await GetRestClient(protocol);
            var invalidToken = await restClient.Auth.RequestTokenAsync();

            // Use an old token which will result in 40143 Unrecognised token
            invalidToken.Token = invalidToken.Token.Split('.')[0] +
                                 ".DOcRVPgv1Wf1-YGgJFjyk2PNOGl_DFL7aCDzEPju8TYHorfxHHVoNoDGz5fKRW0UxePiVjD1EVEW0ZiknIK8u3S5p1FBq5Rtw_I7OX7fW8U4sGxJjAfMS_fTcXFdvouTQ";

            var realtimeClient = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.TokenDetails = invalidToken;
                options.AutoConnect = false;
            });

            ErrorInfo error = null;
            realtimeClient.Connection.On(ConnectionEvent.Connected, (args) =>
            {
                error = args.Reason;
                ResetEvent.Set();
            });

            realtimeClient.Connect();

            ResetEvent.WaitOne(10000);

            realtimeClient.RestClient.AblyAuth.CurrentToken.Expires.Should()
                .BeAfter(TestHelpers.Now(), "The token should be valid and expire in the future.");
            error.Should().BeNull("No error should be raised!");
        }

        [Theory(Skip = "Intermittently fails")]
        [ProtocolData]
        [Trait("spec", "RTN14c")]
        public async Task ShouldDisconnectIfConnectionIsNotEstablishedWithInDefaultTimeout(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.AutoConnect = false;
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(10);
            });

            ErrorInfo disconnectedStateError = null;
            client.Connection.On((change) =>
            {
                if (change.Current == ConnectionState.Disconnected && disconnectedStateError is null)
                {
                    disconnectedStateError = change.Reason;
                }
            });

            client.Connect();

            await new ConditionalAwaiter(() => disconnectedStateError != null);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15d")]
        public async Task ResumeRequest_ShouldReceivePendingMessagesOnceConnectionResumed(Protocol protocol)
        {
            var client1 = await GetRealtimeClient(protocol);
            await client1.WaitForState(ConnectionState.Connected);

            var client2TransportFactory = new TestTransportFactory();
            var client2 = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.TransportFactory = client2TransportFactory;
            });
            await client2.WaitForState(ConnectionState.Connected);
            var client2InitialConnectionId = client2.Connection.Id;

            var channelName = "RTN15d".AddRandomSuffix();
            var channelsCount = 5;
            for (var i = 0; i < channelsCount; i++)
            {
                await client1.Channels.Get($"{channelName}_{i}").AttachAsync();
                await client2.Channels.Get($"{channelName}_{i}").AttachAsync();
            }

            var client2MessageAwaiter = new TaskCompletionAwaiter(15000, channelsCount);
            var channel2Messages = new List<Message>();
            foreach (var realtimeChannel in client2.Channels)
            {
                realtimeChannel.Subscribe("data", message =>
                {
                    channel2Messages.Add(message);
                    client2MessageAwaiter.Tick();
                });
            }

            var client2ConnectedAwaiter = new TaskCompletionAwaiter(15000, channelsCount);
            client2TransportFactory.BeforeMessageSent += message =>
            {
                if (message.Action == ProtocolMessage.MessageAction.Connect)
                {
                    client2ConnectedAwaiter.Task.Wait();
                }
            };
            client2.ConnectionManager.Transport.Close(false);
            await client2.WaitForState(ConnectionState.Disconnected);

            // Publish messages on client1 channels
            foreach (var client1Channel in client1.Channels)
            {
                await client1Channel.PublishAsync("data", "hello");
            }

            client2ConnectedAwaiter.SetCompleted();
            await client2.WaitForState(ConnectionState.Connected);
            client2.Connection.ErrorReason.Should().BeNull();
            client2.Connection.Id.Should().Be(client2InitialConnectionId); // connection is successfully resumed/recovered

            var client2MessagesReceived = await client2MessageAwaiter.Task;
            client2MessagesReceived.Should().BeTrue();

            channel2Messages.Should().HaveCount(channelsCount);
            foreach (var channel2Message in channel2Messages)
            {
                channel2Message.Data.Should().Be("hello");
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN16d")]
        public async Task RecoverRequest_ShouldInitializeRecoveryContextAndReceiveSameConnectionIdOnRecoverSuccess(Protocol protocol)
        {
            var client1 = await GetRealtimeClient(protocol);
            await client1.WaitForState(ConnectionState.Connected);
            var client1ConnectionId = client1.Connection.Id;
            for (var i = 0; i < 5; i++)
            {
                var channel = client1.Channels.Get("RTN16d".AddRandomSuffix());
                await channel.AttachAsync();
            }

            var recoveryKey = client1.Connection.CreateRecoveryKey();
            var recoveryKeyContext = RecoveryKeyContext.Decode(recoveryKey);

            recoveryKeyContext.ConnectionKey.Should().Be(client1.Connection.Key);
            recoveryKeyContext.ChannelSerials.Count.Should().Be(client1.Channels.Count());
            recoveryKeyContext.MsgSerial.Should().Be(client1.Connection.MessageSerial);

            client1.ExecuteCommand(SetDisconnectedStateCommand.Create(null));

            var client2 = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.Recover = recoveryKey;
            });

            await client2.WaitForState(ConnectionState.Connected);
            client2.Connection.Id.Should().Be(client1ConnectionId);
            client2.Connection.MessageSerial.Should().Be(recoveryKeyContext.MsgSerial);
            client2.Connection.Key.Should().NotBe(recoveryKeyContext.ConnectionKey);
            foreach (var realtimeChannel in client1.Channels)
            {
                realtimeChannel.Properties.ChannelSerial.Should().Be(recoveryKeyContext.ChannelSerials[realtimeChannel.Name]);
            }

            client2.Options.Recover.Should().BeNull();

            client1.Close();
            client2.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15e")]
        public async Task ResumeRequest_ShouldUpdateConnectionKeyWhenConnectionIsResumed(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            await WaitForState(client, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            var initialConnectionKey = client.Connection.Key;
            var initialConnectionId = client.Connection.Id;
            client.ConnectionManager.Transport.Close(false);
            await WaitForState(client, ConnectionState.Disconnected);
            await WaitForState(client, ConnectionState.Connected, TimeSpan.FromSeconds(10));
            client.Connection.Id.Should().Be(initialConnectionId);
            client.Connection.Key.Should().NotBe(initialConnectionKey);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15c7")]
        public async Task ResumeRequest_ConnectedProtocolMessageWithResumeFailedShouldEmitErrorOnConnection(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            var channel = (RealtimeChannel)client.Channels.Get("RTN15c7".AddRandomSuffix());
            await client.WaitForState(ConnectionState.Connected);
            channel.Attach();
            await channel.WaitForAttachedState();
            channel.State.Should().Be(ChannelState.Attached);

            var oldConnectionId = client.Connection.Id;
            var oldKey = client.Connection.Key;

            client.State.Connection.Id = string.Empty;
            client.State.Connection.Key = "xxxxx!xxxxxxx-xxxxxxxx-xxxxxxxx"; // invalid connection key for next resume request
            client.GetTestTransport().Close(false);

            ConnectionStateChange stateChange = null;
            await WaitFor(done =>
            {
                client.Connection.On(ConnectionEvent.Connected, change =>
                {
                    stateChange = change;
                    done();
                });
            });

            stateChange.Should().NotBeNull();
            stateChange.HasError.Should().BeTrue();
            stateChange.Reason.Code.Should().Be(ErrorCodes.InvalidFormatForConnectionId);
            stateChange.Reason.Should().Be(client.Connection.ErrorReason);

            var protocolMessage = client.GetTestTransport().ProtocolMessagesReceived.FirstOrDefault(x => x.Action == ProtocolMessage.MessageAction.Connected);

            protocolMessage.Should().NotBeNull();
            Debug.Assert(protocolMessage != null, "Expected a non-null 'TestTransportWrapper', got null");
            protocolMessage.ConnectionId.Should().NotBe(oldConnectionId);

            client.Connection.Id.Should().NotBe(oldConnectionId);
            client.Connection.Key.Should().NotBe(oldKey);
            client.Connection.MessageSerial.Should().Be(0);

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15c6")]
        [Trait("spec", "RTN15c7")]
        public async Task ResumeRequest_ConnectedProtocolMessage_AttachAllChannelsAndProcessPendingMessages(
            Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            var channelName = "RTN15c6.RTN15c7.".AddRandomSuffix();
            const int channelCount = 5;
            await client.WaitForState(ConnectionState.Connected);
            var connectionId = client.Connection.Id;

            var channels = new List<RealtimeChannel>();
            for (var i = 0; i < channelCount; i++)
            {
                var channel = client.Channels.Get($"{channelName}_{i}");
                await channel.AttachAsync();
                channels.Add(channel as RealtimeChannel);
            }

            TaskCompletionAwaiter connectedAwaiter = null;

            // Should add messages to connection-wide pending queue
            async Task PublishMessagesWhileNotConnected()
            {
                // Make sure new transport is created to apply BeforeProtocolMessageProcessed method
                await client.WaitForState(ConnectionState.Connecting);
                connectedAwaiter = new TaskCompletionAwaiter(15000);
                client.BeforeProtocolMessageProcessed(message =>
                {
                    if (message.Action == ProtocolMessage.MessageAction.Connected)
                    {
                        connectedAwaiter.Task.Wait(); // Keep in connecting state
                    }
                });

                foreach (var channel in channels)
                {
                    channel.Publish("eventName", "foo");
                }

                await client.ProcessCommands();
                client.State.PendingMessages.Should().HaveCount(channelCount);
            }

            async Task CheckForAttachedChannelsAfterResume()
            {
                var attachedChannels = new List<RealtimeChannel>();
                await WaitForMultiple(channelCount, partialDone =>
                {
                    foreach (var channel in channels)
                    {
                        channel.Once(ChannelEvent.Attaching, _ =>
                        {
                            channel.Once(ChannelEvent.Attached, _ =>
                            {
                                attachedChannels.Add(channel);
                                partialDone();
                            });
                        });
                    }

                    connectedAwaiter.SetCompleted();
                });
                attachedChannels.Should().HaveCount(channelCount);
            }

            async Task CheckForProcessedPendingMessages()
            {
                client.State.PendingMessages.Should().HaveCount(0);
                foreach (var channel in channels)
                {
                    var history = await channel.HistoryAsync();
                    history.Items.Count.Should().BeGreaterOrEqualTo(1);
                    history.Items[0].Data.Should().Be("foo");
                }
            }

            // resume successful - RTN15c6
            client.GetTestTransport().Close(false);
            await PublishMessagesWhileNotConnected();
            await CheckForAttachedChannelsAfterResume();
            await CheckForProcessedPendingMessages();
            client.GetTestTransport().ProtocolMessagesReceived.Count(x => x.Action == ProtocolMessage.MessageAction.Connected).Should().Be(1); // For every new transport, list is reset
            var connectedProtocolMessage = client.GetTestTransport().ProtocolMessagesReceived.First(x => x.Action == ProtocolMessage.MessageAction.Connected);
            connectedProtocolMessage.ConnectionId.Should().Be(connectionId);
            connectedProtocolMessage.Error.Should().BeNull();
            client.Connection.ErrorReason.Should().BeNull();

            // resume unsuccessful - RTN15c7
            client.State.Connection.Id = string.Empty;
            client.State.Connection.Key = "xxxxx!xxxxxxx-xxxxxxxx-xxxxxxxx"; // invalid connection key for next resume request
            client.GetTestTransport().Close(false);
            await PublishMessagesWhileNotConnected();
            await CheckForAttachedChannelsAfterResume();
            await CheckForProcessedPendingMessages();
            client.GetTestTransport().ProtocolMessagesReceived.Count(x => x.Action == ProtocolMessage.MessageAction.Connected).Should().Be(1); // For every new transport, list is reset
            connectedProtocolMessage = client.GetTestTransport().ProtocolMessagesReceived.First(x => x.Action == ProtocolMessage.MessageAction.Connected);
            connectedProtocolMessage.ConnectionId.Should().NotBe(connectionId);
            connectedProtocolMessage.Error.Should().NotBeNull();
            client.Connection.ErrorReason.ToString().Should().Be(connectedProtocolMessage.Error.ToString());
            client.Connection.ErrorReason.Code.Should().Be(ErrorCodes.InvalidFormatForConnectionId);

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15c4")]
        public async Task ResumeRequest_WithFatalErrorInConnection_ClientAndChannelsShouldBecomeFailed(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(2);
            });
            await client.WaitForState(ConnectionState.Connected);

            var channels = new List<RealtimeChannel>();
            for (var i = 0; i < 4; i++)
            {
                var channel = (RealtimeChannel)client.Channels.Get("RTN15c4".AddRandomSuffix());
                channel.Attach();
                await channel.WaitForAttachedState();
                channels.Add(channel);
            }

            client.GetTestTransport().Close(false);
            await client.WaitForState(ConnectionState.Disconnected);

            var errInfo = new ErrorInfo("faked error", 0);
            var connectedAwaiter = new TaskCompletionAwaiter(15000);
            await client.WaitForState(ConnectionState.Connecting);
            client.BeforeProtocolMessageProcessed(message =>
            {
                if (message.Action == ProtocolMessage.MessageAction.Connected)
                {
                    message.Action = ProtocolMessage.MessageAction.Error;
                    message.Error = errInfo;
                    connectedAwaiter.Task.Wait();
                }
            });

            ConnectionStateChange failedStateChange = null;
            await WaitFor(done =>
            {
                client.Connection.Once(ConnectionEvent.Failed, change =>
                {
                    failedStateChange = change;
                    done();
                });
                connectedAwaiter.SetCompleted();
            });

            failedStateChange.Previous.Should().Be(ConnectionState.Connecting);
            failedStateChange.Current.Should().Be(ConnectionState.Failed);
            failedStateChange.Reason.Code.Should().Be(errInfo.Code);
            failedStateChange.Reason.Message.Should().Be(errInfo.Message);

            client.Connection.ErrorReason.Code.Should().Be(errInfo.Code);
            client.Connection.ErrorReason.Message.Should().Be(errInfo.Message);
            client.Connection.State.Should().Be(ConnectionState.Failed);

            foreach (var channel in channels)
            {
                await channel.WaitForState(ChannelState.Failed);
                channel.State.Should().Be(ChannelState.Failed);
                channel.ErrorReason.Code.Should().Be(errInfo.Code);
                channel.ErrorReason.Message.Should().Be(errInfo.Message);
            }

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15c5")]
        public async Task ResumeRequest_WithTokenAuthError_TransportWillBeClosed(Protocol protocol)
        {
            var authClient = await GetRestClient(protocol);
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.AuthCallback = async @params =>
                {
                    var tokenDetails = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromSeconds(5) });
                    tokenDetails.Expires = DateTimeOffset.UtcNow.AddMinutes(1); // Cheat to make sure the client uses the token
                    return tokenDetails;
                };
                options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(1);
            });
            await client.WaitForState(ConnectionState.Connected);
            var channel = client.Channels.Get("RTN15c5".AddRandomSuffix());
            channel.Attach();
            await channel.WaitForAttachedState();
            channel.Once(ChannelEvent.Detached, change => throw new Exception("channel should not detach"));

            await client.WaitForState(ConnectionState.Disconnected);
            await client.WaitForState(ConnectionState.Connected); // Connected with a resume request
            var prevConnectionId = client.Connection.Id;
            var prevTransport = client.GetTestTransport();

            ConnectionStateChange stateChange = null;
            await WaitFor(done =>
            {
                client.Connection.Once(ConnectionEvent.Disconnected, change =>
                {
                    stateChange = change;
                    done();
                });
            });
            stateChange.Should().NotBeNull();
            stateChange.Current.Should().Be(ConnectionState.Disconnected); // Disconnected due to token expired
            stateChange.HasError.Should().BeTrue();
            stateChange.Reason.Code.Should().Be(ErrorCodes.TokenExpired);

            await client.WaitForState(ConnectionState.Connecting);
            await client.WaitForState(ConnectionState.Connected);
            prevTransport.State.Should().Be(TransportState.Closed); // transport should have been closed and the client should have a new transport instanced
            var newTransport = client.GetTestTransport();
            newTransport.Should().NotBe(prevTransport);
            client.Connection.Id.Should().Be(prevConnectionId); // connection should be resumed, connectionId should be unchanged

            client.Close();
        }

        [Theory]
        [ProtocolData]
        public async Task WithAuthUrlShouldGetTokenFromUrl(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var settings = await AblySandboxFixture.GetSettings();
            var authUrl = "http://echo.ably.io/?type=text&body=" + token.Token;

            var authUrlClient = new AblyRealtime(new ClientOptions
            {
                AuthUrl = new Uri(authUrl),
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol
            });

            await WaitForState(authUrlClient, waitSpan: TimeSpan.FromSeconds(5));
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15h1")]
        public async Task WhenDisconnectedMessageContainsTokenError_IfTokenIsNotRenewable_ShouldBecomeFailedAndEmitError(Protocol protocol)
        {
            var authClient = await GetRestClient(protocol);
            var tokenDetails = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromSeconds(2) });

            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.TokenDetails = tokenDetails;
                options.AutoConnect = false;
            });

            client.Connect();
            await client.WaitForState(ConnectionState.Connected);

            // null the key so the token is not renewable
            client.Options.Key = null;

            client.Connection.Once(ConnectionEvent.Disconnected, state => throw new Exception("should not become DISCONNECTED"));
            client.Connection.Once(ConnectionEvent.Connected, state => throw new Exception("should not become CONNECTED"));

            await client.WaitForState(ConnectionState.Failed);

            client.Connection.ErrorReason.Should().NotBeNull();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15h2")]
        public async Task WhenDisconnectedMessageContainsTokenError_IfTokenIsRenewable_ShouldNotEmitError(Protocol protocol)
        {
            var authClient = await GetRestClient(protocol);
            var tokenDetails = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromSeconds(2) });

            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.TokenDetails = tokenDetails;
                options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(1);
            });

            await client.WaitForState(ConnectionState.Connected);

            var awaiter = new TaskCompletionAwaiter();
            var stateChanges = new List<ConnectionStateChange>();
            client.Connection.On(state =>
            {
                stateChanges.Add(state);
                if (state.Current == ConnectionState.Connected)
                {
                    client.Connection.State.Should().Be(ConnectionState.Connected);
                    client.Connection.ErrorReason.Should().BeNull();
                    awaiter.SetCompleted();
                }
            });

            await awaiter.Task;
            stateChanges.Should().HaveCount(3);
            stateChanges[0].HasError.Should().BeTrue();
            stateChanges[0].Reason.Code.Should().Be(ErrorCodes.TokenExpired);
            stateChanges[1].HasError.Should().BeFalse();
            stateChanges[2].HasError.Should().BeFalse();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15h2")]
        public async Task WhenDisconnectedMessageContainsTokenError_IfTokenRenewFails_ShouldBecomeDisconnectedAndEmitError(Protocol protocol)
        {
            var awaiter = new TaskCompletionAwaiter();
            var authClient = await GetRestClient(protocol);

            var tokenDetails = await authClient.AblyAuth.RequestTokenAsync(new TokenParams
            {
                ClientId = "123",
                Ttl = TimeSpan.FromSeconds(5),
            });

            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.TokenDetails = tokenDetails;

                // has means to renew that should fail
                options.AuthCallback = tokenParams => throw new Exception("fail auth callback");
            });

            await client.WaitForState(ConnectionState.Connected);

            var stateChanges = new List<ConnectionStateChange>();
            client.Connection.Once(ConnectionEvent.Disconnected, state =>
            {
                stateChanges.Add(state);
                client.Connection.Once(ConnectionEvent.Connecting, state2 =>
                {
                    stateChanges.Add(state2);
                    client.Connection.Once(ConnectionEvent.Disconnected, state3 =>
                    {
                        client.Connection.State.Should().Be(ConnectionState.Disconnected);
                        client.Connection.ErrorReason.Should().NotBeNull();
                        stateChanges.Add(state3);
                        awaiter.SetCompleted();
                    });
                });
            });

            client.Connection.Once(ConnectionEvent.Failed, state => throw new Exception("should not become FAILED"));

            await awaiter.Task;
            stateChanges.Select(x => x.Current).Should().BeEquivalentTo(new[]
                                                                            {
                                                                                ConnectionState.Disconnected,
                                                                                ConnectionState.Connecting,
                                                                                ConnectionState.Disconnected
                                                                            });

            stateChanges[0].HasError.Should().BeTrue();
            stateChanges[0].Reason.Code.Should().Be(ErrorCodes.TokenExpired);
            stateChanges[1].HasError.Should().BeFalse();
            stateChanges[2].HasError.Should().BeTrue();
            stateChanges[2].Reason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN15g")]
        [Trait("spec", "RTN15g1")]
        // "RTN15g2" It can't implement that spec item because RTN23a is not even implemented
        [Trait("spec", "RTN15g3")]
        public async Task WhenDisconnectedPastTTL_ShouldNotResume_ShouldClearConnectionStateAndAttemptNewConnection(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(1000);
                options.DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(5000);
            });

            await client.WaitForState(ConnectionState.Connected);

            client.State.Connection.ConnectionStateTtl = TimeSpan.FromSeconds(1);
            string initialConnectionId = client.Connection.Id;
            TimeSpan connectionStateTtl = client.Connection.ConnectionStateTtl;

            var aliveAt1 = client.Connection.ConfirmedAliveAt;
            var aliveAt2 = aliveAt1;

            // RTN15g3 ATTACHED, ATTACHING, or SUSPENDED must be automatically reattached
            var channels = new List<RealtimeChannel>
            {
                client.Channels.Get("attached".AddRandomSuffix()) as RealtimeChannel,
                client.Channels.Get("attaching".AddRandomSuffix()) as RealtimeChannel,
                client.Channels.Get("suspended".AddRandomSuffix()) as RealtimeChannel,
            };
            channels[2].State = ChannelState.Suspended;

            channels[0].Attach();
            await channels[0].WaitForState();

            channels[0].State.Should().Be(ChannelState.Attached);
            channels[1].State.Should().Be(ChannelState.Initialized); // set attaching later
            channels[2].State.Should().Be(ChannelState.Suspended);

            DateTime disconnectedAt = DateTime.MinValue;
            DateTime reconnectedAt = DateTime.MinValue;
            string newConnectionId = string.Empty;

            await WaitFor(60000, done =>
            {
                client.Connection.Once(ConnectionEvent.Disconnected, change2 =>
                {
                    disconnectedAt = DateTime.UtcNow;
                    channels[1].Attach();
                    client.Connection.Once(ConnectionEvent.Connecting, change3 =>
                    {
                        reconnectedAt = DateTime.UtcNow;
                        client.Connection.Once(ConnectionEvent.Connected, change4 =>
                        {
                            newConnectionId = client.Connection.Id;
                            aliveAt2 = client.Connection.ConfirmedAliveAt;
                            done();
                        });
                    });
                });

                client.GetTestTransport().Close(); // close event is suppressed by default
                client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(ErrorInfo.ReasonDisconnected));
            });

            var reconnectedInTime = reconnectedAt - disconnectedAt;

            var (lowerBound, _) = ReconnectionStrategyTest.Bounds(1, 5000);
            reconnectedInTime.TotalMilliseconds.Should().BeGreaterThan(lowerBound);

            initialConnectionId.Should().NotBeNullOrEmpty();
            initialConnectionId.Should().NotBe(newConnectionId);
            connectionStateTtl.Should().Be(TimeSpan.FromSeconds(1));
            aliveAt1.Value.Should().BeBefore(aliveAt2.Value);

            await channels[0].WaitForAttachedState();
            await channels[1].WaitForAttachedState();
            await channels[2].WaitForAttachedState();
        }

        [Theory(Skip = "Intermittently fails")]
        [ProtocolData]
        [Trait("spec", "RTN15i")]
        public async Task WithConnectedClient_WhenErrorProtocolMessageReceived_ShouldBecomeFailed(Protocol protocol)
        {
            /*
            (RTN15i)
             If an ERROR ProtocolMessage is received, this indicates a fatal error in the connection.
             The server will close the transport immediately after.
             The client should transition to the FAILED state triggering all attached channels to transition to the FAILED state as well.
             Additionally the Connection#errorReason should be set with the error received from Ably
             */

            var client = await GetRealtimeClient(protocol, (options, settings) => { });
            var channel = client.Channels.Get("RTN15i".AddRandomSuffix());
            channel.Attach();
            await channel.WaitForAttachedState();

            var states = new List<ConnectionState>();
            var errors = new List<ErrorInfo>();

            client.Connection.On((args) =>
            {
                if (args.HasError)
                {
                    errors.Add(args.Reason);
                }

                states.Add(args.Current);
            });

            var dummyError = new ErrorInfo
            {
                Code = ErrorCodes.KeyError,
                StatusCode = HttpStatusCode.Unauthorized,
                Message = "fake error"
            };

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Error)
            {
                Error = dummyError
            });

            states.Should().Equal(ConnectionState.Failed);

            errors.Should().HaveCount(1);
            errors[0].Should().Be(dummyError);
            channel.State.Should().Be(ChannelState.Failed);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN16l")]
        [Trait("spec", "RTN15c7")]
        public async Task WithDummyRecoverData_ShouldConnectAndSetAReasonOnTheConnection(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.Recover = "{\"connectionKey\":\"c17a8!WeXvJum2pbuVYZtF-1b63c17a8\",\"msgSerial\":5,\"channelSerials\":{\"channel1\":\"98\",\"channel2\":\"32\",\"channel3\":\"09\"}}";
                opts.AutoConnect = false;
            });

            ErrorInfo err = null;
            client.Connection.On((args) =>
            {
                err = args.Reason;
                if (args.Current == ConnectionState.Connected)
                {
                    ResetEvent.Set();
                }
            });
            client.Connect();

            var result = ResetEvent.WaitOne(10000);
            result.Should().BeTrue("Timeout");
            err.Should().NotBeNull();
            err.Code.Should().Be(ErrorCodes.InvalidFormatForConnectionId);
            client.Connection.MessageSerial.Should().Be(0);
            client.Connection.ErrorReason.Code.Should().Be(ErrorCodes.InvalidFormatForConnectionId);
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "65")]
        public async Task WithShortLivedToken_ShouldRenewTokenMoreThanOnce(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;
            });

            var stateChanges = new List<ConnectionState>();

            client.Connection.On((args) =>
            {
                stateChanges.Add(args.Current);
            });

            await client.Auth.AuthorizeAsync(new TokenParams { Ttl = TimeSpan.FromSeconds(5) });

            var channel = client.Channels.Get("shortToken_test" + protocol);
            await channel.AttachAsync();

            int count = 0;
            while (true)
            {
                Interlocked.Increment(ref count);
                channel.Publish("test", "test");
                await Task.Delay(2000);
                if (count == 10)
                {
                    break;
                }
            }

            stateChanges.Count(x => x == ConnectionState.Connected).Should().BeGreaterThan(2);

            await client.WaitForState();
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "177")]
        public async Task WhenWebSocketClientIsNull_SendShouldSetDisconnectedState(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            await WaitForState(client); // wait for connection
            client.Connection.State.Should().Be(ConnectionState.Connected);

            var transportWrapper = client.ConnectionManager.Transport as TestTransportWrapper;
            transportWrapper.Should().NotBeNull();

            Debug.Assert(transportWrapper != null, nameof(transportWrapper) + " != null");
            var wsTransport = transportWrapper.WrappedTransport as MsWebSocketTransport;
            wsTransport.Should().NotBeNull();

            Debug.Assert(wsTransport != null, nameof(wsTransport) + " != null");
            wsTransport.ReleaseClientWebSocket();

            var tca = new TaskCompletionAwaiter();
            client.Connection.On(s =>
            {
                if (s.Current == ConnectionState.Disconnected)
                {
                    tca.SetCompleted();
                }
            });

            await client.Channels.Get("test").PublishAsync("event", "data");
            await tca.Task;

            // should auto connect again
            await WaitForState(client);
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "402")]
        public async Task WhenInternetConnectionIsLost_WithoutOSNotification_ShouldBehaveProperly(Protocol protocol)
        {
            var transportFactory = new TestTransportFactory(transport => { transport.ThrowOnConnect = true; });

            var client = await GetRealtimeClient(
                protocol,
                (options, settings) =>
                {
                    options.AutoConnect = false;
                    options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(1);
                    options.TransportFactory = transportFactory;
                });

            client.State.Connection.ConnectionStateTtl = TimeSpan.FromSeconds(5);

            var oldExecuteRequest = client.RestClient.ExecuteHttpRequest;

            client.RestClient.ExecuteHttpRequest = request =>
            {
                // Throw 500
                if (request.Url.Contains("internet"))
                {
                    return Task.FromResult(new AblyResponse { StatusCode = HttpStatusCode.BadGateway });
                }

                return oldExecuteRequest(request);
            };

            client.Connect();

            // should auto connect again
            await client.WaitForState(ConnectionState.Suspended, TimeSpan.FromSeconds(10));
        }

        [Fact]
        [Trait("issue", "437")]
        public async Task WhenTimeoutMessageReceived_ShouldReconnectSuccessfully()
        {
            var client = await GetRealtimeClient(Protocol.Json, (options, _) =>
            {
                options.FallbackHosts = new string[] { };
            });

            await client.WaitForState(ConnectionState.Connected);
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
            {
                Error = new ErrorInfo { StatusCode = HttpStatusCode.GatewayTimeout },
            });

            var host = client.RestClient.HttpClient.PreferredHost;
            await client.ProcessCommands();

            await client.WaitForState(ConnectionState.Connected);
            stopwatch.Stop();
            stopwatch.Elapsed.Should().BeLessThan(Defaults.DisconnectedRetryTimeout, "If the internet check doesn't work it will wait for the full 15 seconds before it retries");

            var newHost = client.RestClient.HttpClient.PreferredHost;
            host.Should().Be(newHost);
        }

        [Fact]
        [Trait("issue", "437")]
        public async Task CanConnectToAbly_ShouldReturnTrue()
        {
            var restClient = await GetRestClient(Protocol.Json);

            var result = await restClient.CanConnectToAbly();
            result.Should().BeTrue();
        }

        public ConnectionSandBoxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }
    }
}
