using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Encryption;
using IO.Ably.Realtime;
using IO.Ably.Tests.Extensions;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Collection("Channel SandBox")]
    [Trait("type", "integration")]
    public class ChannelSandboxSpecs : SandboxSpecs
    {
        [Theory]
        [ProtocolData]
        public async Task TestGetChannel_ReturnsValidChannel(Protocol protocol)
        {
            // Arrange
            var client = await GetRealtimeClient(protocol);

            // Act
            IRealtimeChannel target = client.Channels.Get("test");

            // Assert
            target.Name.Should().BeEquivalentTo("test");
            target.State.Should().Be(ChannelState.Initialized);
        }

        [Theory]
        [ProtocolData]
        public async Task TestAttachChannel_AttachesSuccessfully(Protocol protocol)
        {
            // Arrange
            var client = await GetRealtimeClient(protocol);
            Semaphore signal = new Semaphore(0, 2);
            var stateChanges = new List<ChannelStateChange>();
            IRealtimeChannel target = client.Channels.Get("test".AddRandomSuffix());
            target.StateChanged += (s, stateChange) =>
            {
                stateChanges.Add(stateChange);
                signal.Release();
            };

            // Act
            target.Attach();

            // Assert
            signal.WaitOne(10000);
            stateChanges.Count.Should().Be(1);
            stateChanges[0].Current.Should().Be(ChannelState.Attaching);
            stateChanges[0].Error.Should().BeNull();
            target.State.Should().Be(ChannelState.Attaching);

            signal.WaitOne(10000);
            stateChanges.Count.Should().Be(2);
            stateChanges[1].Current.Should().Be(ChannelState.Attached);
            stateChanges[1].Error.Should().BeNull();
            target.State.Should().Be(ChannelState.Attached);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL1")]
        public async Task SendingAMessageAttachesTheChannel_BeforeReceivingTheMessages(Protocol protocol)
        {
            // Arrange
            var client = await GetRealtimeClient(protocol);
            IRealtimeChannel target = client.Channels.Get("test");
            var messagesReceived = new List<Message>();
            target.Subscribe(message =>
            {
                messagesReceived.Add(message);
                ResetEvent.Set();
            });

            // Act
            target.Publish("test", "test data");
            target.State.Should().Be(ChannelState.Attaching);
            ResetEvent.WaitOne(6000);

            // Assert
            target.State.Should().Be(ChannelState.Attached);
            messagesReceived.Count.Should().Be(1);
            messagesReceived[0].Name.Should().BeEquivalentTo("test");
            messagesReceived[0].Data.Should().BeEquivalentTo("test data");
        }

        // TODO: RTL1 Spec about presence and sync messages
        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL4e")]
        public async Task WhenAttachingAChannelWithInsufficientPermissions_ShouldSetItToFailedWithError(
            Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.Key = settings.KeyWithChannelLimitations;
            });

            var channel = client.Channels.Get("nono_" + protocol);
            var result = await channel.AttachAsync();

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(ErrorCodes.OperationNotPermittedWithCapability);
            result.Error.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL4j")]
        public async Task WhenChannelIsAlreadyAttached_AndReAttachIsForcedByChangingChannelOptions_ShouldPassAttachResumeFlagInAttachMessage(Protocol protocol)
        {
            var sentMessages = new List<ProtocolMessage>();
            var client = await GetRealtimeClient(protocol, (options, _) =>
            {
                var optionsTransportFactory = new TestTransportFactory
                {
                    OnMessageSent = sentMessages.Add,
                };
                options.TransportFactory = optionsTransportFactory;
            });

            var channel = client.Channels.Get("Test");
            await channel.AttachAsync();

            var result = await channel.SetOptionsAsync(new ChannelOptions().WithModes(ChannelMode.Publish));

            result.IsSuccess.Should().BeTrue();
            var attachMessages = sentMessages.Where(x => x.Action == ProtocolMessage.MessageAction.Attach).ToList();
            attachMessages.Should().HaveCount(2);
            attachMessages.First().Flags.Should().BeNull();
            attachMessages.Last().HasFlag(ProtocolMessage.Flag.AttachResume).Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL4j2")]
        public async Task TestAttachResume_And_RewindParam(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            var client1 = await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false);
            var client2 = await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false);

            var channel = client.Channels.Get("Test");
            await channel.PublishAsync("test", "test");

            var channelWithAttachResume = client1.Channels.Get("Test", new ChannelOptions().WithRewind(1)) as RealtimeChannel;
            channelWithAttachResume.AttachResume = true;
            var channel1Messages = new List<Message>();
            channelWithAttachResume.Subscribe(channel1Messages.Add);
            await channelWithAttachResume.WaitForAttachedState();
            var channelWithoutAttachResume = client2.Channels.Get("Test", new ChannelOptions().WithRewind(1));

            var channel2Messages = new List<Message>();
            channelWithoutAttachResume.Subscribe(channel2Messages.Add);
            await channelWithoutAttachResume.WaitForAttachedState();

            await Task.Delay(2000);

            channel2Messages.Should().HaveCount(1);
            channel1Messages.Should().BeEmpty();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL4k1")]
        public async Task ChannelParamsIncludedInTheAttachedMessage_ShouldBeExposedAsReadonlyOnChannel(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            var options = new ChannelOptions(
                channelParams: new ChannelParams { { "delta", "vcdiff" }, { "martin", "no chance" } });
            var channel = client.Channels.Get("Test", options);

            await channel.AttachAsync();

            channel.Params["delta"].Should().Be("vcdiff");
            channel.Params.Should().HaveCount(1);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL4m")]
        public async Task ChannelModesIncludedInTheAttachedMessage_ShouldBeExposedAsReadonlyOnChannel(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            var options = new ChannelOptions(
                modes: new ChannelModes(ChannelMode.Presence, ChannelMode.Subscribe));
            var channel = client.Channels.Get("Test", options);

            await channel.AttachAsync();

            channel.Modes.Should().HaveCount(2);
            channel.Modes.Should().BeEquivalentTo(new[] { ChannelMode.Presence, ChannelMode.Subscribe });
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL16a")]
        public async Task SetOptions_WithDifferentModesOrParams_ShouldReAttachChannel(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            var channelParams = new ChannelParams { { "delta", "vcdiff" } };
            var options = new ChannelOptions(channelParams: channelParams);
            var channel = client.Channels.Get("test", options);
            await channel.AttachAsync();
            channel.Params.Should().ContainKey("delta");
            var states = new List<(ChannelState, ProtocolMessage)>();
            channel.On(x =>
            {
                Output.WriteLine(x.ToString());
                states.Add((x.Current, x.ProtocolMessage));
            });
            var newOptions = new ChannelOptions(channelParams: channelParams, modes: new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe));
            await channel.SetOptionsAsync(newOptions);

            await client.ProcessCommands();

            states.Should().HaveCount(2);
            Output.WriteLine(states.Select(x => x.Item1.ToString()).JoinStrings());
            states.First(x => x.Item1 == ChannelState.Attached)
                .Item2.HasFlag(ProtocolMessage.Flag.Publish).Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTC1a")]
        public async Task TestAttachChannel_Sending3Messages_EchoesItBack(Protocol protocol)
        {
            // Arrange
            var client = await GetRealtimeClient(protocol);
            await client.WaitForState(ConnectionState.Connected);

            var tsc = new TaskCompletionAwaiter(10000, 3);
            IRealtimeChannel target = client.Channels.Get("test" + protocol);
            target.Attach();
            await target.WaitForAttachedState();

            ConcurrentQueue<Message> messagesReceived = new ConcurrentQueue<Message>();

            target.Subscribe(message =>
            {
                messagesReceived.Enqueue(message);
                tsc.Tick();
            });

            // Act
            await target.PublishAsync("test1", "test 12");
            await target.PublishAsync("test2", "test 123");
            await target.PublishAsync("test3", "test 321");

            bool result = await tsc.Task;
            result.Should().BeTrue();

            // Assert
            messagesReceived.Should().HaveCount(3);
            var messages = messagesReceived.ToList();
            messages[0].Name.Should().BeEquivalentTo("test1");
            messages[0].Data.Should().BeEquivalentTo("test 12");
            messages[1].Name.Should().BeEquivalentTo("test2");
            messages[1].Data.Should().BeEquivalentTo("test 123");
            messages[2].Name.Should().BeEquivalentTo("test3");
            messages[2].Data.Should().BeEquivalentTo("test 321");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL7f")]
        [Trait("spec", "RTC1a")]
        public async Task TestAttachChannel_SendingMessage_DoesNot_EchoesItBack(Protocol protocol)
        {
            const string channelName = "echo_off_test";

            // this should be logged in MsWebSocketTransport.CreateSocket
            var testLogger = new TestLogger("Connecting to web socket on url:");

            // Arrange
            var client = await GetRealtimeClient(protocol, (o, _) =>
            {
                o.EchoMessages = false;
                o.Logger = testLogger;
                o.LogLevel = LogLevel.Debug;
            });
            await client.WaitForState();
            client.Options.EchoMessages.Should().Be(false);
            testLogger.MessageSeen.Should().Be(true);
            testLogger.FullMessage.Contains("echo=false").Should().Be(true);

            var channel = client.Channels.Get(channelName);

            channel.Attach();

            List<Message> messagesReceived = new List<Message>();
            channel.Subscribe(message =>
            {
                messagesReceived.Add(message);
            });

            // Act
            await channel.PublishAsync(channelName, "test data");

            // Assert
            messagesReceived.Should().BeEmpty();
        }

        /*
         * An optional callback can be provided to the #publish method that is called when the message
         * is successfully delivered or upon failure with the appropriate ErrorInfo error.
         * A test should exist to publish lots of messages on a few connections
         * to ensure all message success callbacks are called for all messages published.
         */
        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6b")]
        public async Task With3ClientsAnd60MessagesAndCallbacks_ShouldExecuteAllCallbacks(Protocol protocol)
        {
            var channelName = "test".AddRandomSuffix();

            List<bool> successes1 = new List<bool>();
            List<bool> successes2 = new List<bool>();
            List<bool> successes3 = new List<bool>();

            bool retry = true;
            int tries = 3;
            while (retry)
            {
                var client1 = await GetRealtimeClient(protocol);
                var client2 = await GetRealtimeClient(protocol);
                var client3 = await GetRealtimeClient(protocol);

                client1.Channels.Get(channelName).Attach();
                client2.Channels.Get(channelName).Attach();
                client3.Channels.Get(channelName).Attach();

                var messages = new List<Message>();
                for (int i = 0; i < 20; i++)
                {
                    messages.Add(new Message("name" + i, "data" + i));
                }

                var awaiter = new TaskCountAwaiter(60);
                foreach (var message in messages)
                {
                    client1.Channels.Get(channelName).Publish(new[] { message }, (b, info) =>
                    {
                        successes1.Add(b);
                        awaiter.Tick();
                    });
                    client2.Channels.Get(channelName).Publish(new[] { message }, (b, info) =>
                    {
                        successes2.Add(b);
                        awaiter.Tick();
                    });
                    client3.Channels.Get(channelName).Publish(new[] { message }, (b, info) =>
                    {
                        successes3.Add(b);
                        awaiter.Tick();
                    });
                }

                await awaiter.Task;
                if ((successes1.Count == 20 && successes2.Count == 20 && successes3.Count == 20) || tries <= 0)
                {
                    retry = false;
                }

                tries--;
            }

            successes1.Should().HaveCount(20, "Should have 20 successful callback executed");
            successes2.Should().HaveCount(20, "Should have 20 successful callback executed");
            successes3.Should().HaveCount(20, "Should have 20 successful callback executed");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6c1")]
        public async Task TransientPublishing_WhenConnected_ShouldPublishWithoutAttemptingAttach(Protocol protocol)
        {
            var channelName = "RTL6c1".AddRandomSuffix();
            var pubClient = await GetRealtimeClient(protocol);
            var subClient = await GetRealtimeClient(protocol);
            await pubClient.WaitForState(ConnectionState.Connected);
            await subClient.WaitForState(ConnectionState.Connected);

            var subCh = subClient.Channels.Get(channelName);
            subCh.Attach();
            var tsc = new TaskCompletionAwaiter();
            Message msg = null;
            subCh.Subscribe(m =>
                {
                    msg = m;
                    tsc.SetCompleted();
                });
            await subCh.WaitForAttachedState();

            var pubCh = pubClient.Channels.Get(channelName);
            await pubCh.PublishAsync("foo", "bar");

            pubCh.State.Should().Be(ChannelState.Initialized);

            var result = await tsc.Task;
            result.Should().BeTrue();
            msg.Should().NotBeNull();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6c1")]
        public async Task TransientPublishing_WhenConnecting_ShouldPublishWithoutAttemptingAttach(Protocol protocol)
        {
            var channelName = "RTL6c1".AddRandomSuffix();
            var subClient = await GetRealtimeClient(protocol);
            await subClient.WaitForState(ConnectionState.Connected);

            var subCh = subClient.Channels.Get(channelName);
            subCh.Attach();

            var tsc = new TaskCompletionAwaiter();
            Message msg = null;
            subCh.Subscribe(m =>
                {
                    msg = m;
                    tsc.SetCompleted();
                });

            await subCh.WaitForAttachedState();

            var pubClient = await GetRealtimeClient(protocol);
            var pubCh = pubClient.Channels.Get(channelName);
            await pubClient.WaitForState(ConnectionState.Connecting);

            await pubCh.PublishAsync("foo", "bar");

            pubCh.State.Should().Be(ChannelState.Initialized);

            var result = await tsc.Task;
            result.Should().BeTrue();
            msg.Should().NotBeNull();

            pubClient.Close();
            subClient.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6c2")]
        public async Task TransientPublishing_WhenNotConnected_ShouldPublishWithoutAttemptingAttach(Protocol protocol)
        {
            var channelName = "RTL6c2".AddRandomSuffix();
            var pubClient = await GetRealtimeClient(
                                protocol,
                                (options, settings) => { options.AutoConnect = false; });

            var subClient = await GetRealtimeClient(protocol);
            await subClient.WaitForState(ConnectionState.Connected);

            var subCh = subClient.Channels.Get(channelName);
            subCh.Attach();

            var tsc = new TaskCompletionAwaiter();
            Message msg = null;
            subCh.Subscribe(m =>
                {
                    msg = m;
                    tsc.SetCompleted();
                });
            await subCh.WaitForAttachedState();

            var pubCh = pubClient.Channels.Get(channelName);
            pubCh.Publish("foo", "bar");

            pubCh.State.Should().Be(ChannelState.Initialized);

            pubClient.Connect();

            var result = await tsc.Task;
            result.Should().BeTrue();
            msg.Should().NotBeNull();

            pubClient.Close();
            subClient.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6c4")]
        public async Task TransientPublishing_WhenConnectionFailed_ShouldResultInError(Protocol protocol)
        {
            var channelName = "RTL6c4".AddRandomSuffix();

            var pubClient = await GetRealtimeClient(
                                protocol,
                                (options, settings) =>
                                    {
                                        options.Key = "not:valid.key";
                                    });

            var pubChannel = pubClient.Channels.Get(channelName);
            await pubClient.WaitForState(ConnectionState.Failed);
            pubClient.Connection.State.Should().Be(ConnectionState.Failed);

            bool expectedError = false;
            try
            {
                pubChannel.Publish("foo", "bar");
            }
            catch (AblyException)
            {
                expectedError = true;
            }

            expectedError.Should().BeTrue();

            pubClient.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6c4")]
        public async Task TransientPublishing_WhenChannelFailed_ShouldResultInError(Protocol protocol)
        {
            var channelName = "RTL6c4".AddRandomSuffix();

            var pubClient = await GetRealtimeClient(protocol);
            await pubClient.WaitForState(ConnectionState.Connected);

            var pubChannel = pubClient.Channels.Get(channelName) as RealtimeChannel;
            await pubChannel.AttachAsync();

            pubChannel.SetChannelState(ChannelState.Failed);
            pubChannel.State.Should().Be(ChannelState.Failed);

            bool expectedError = false;
            try
            {
                pubChannel.Publish("foo", "bar");
            }
            catch
            {
                expectedError = true;
            }

            expectedError.Should().BeTrue();

            pubClient.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6c5")]
        public async Task PublishShouldNotImplicitlyAttachAChannel(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            var channel = client.Channels.Get("RTL6c5".AddRandomSuffix());

            var awaiter = new TaskCompletionAwaiter(5000);
            channel.Once(ChannelEvent.Attached, change =>
            {
                awaiter.SetCompleted();
            });

            await client.WaitForState(ConnectionState.Connected);
            channel.Publish(null, "foo");

            var result = await awaiter.Task;
            result.Should().BeFalse("channel should not have become attached");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6e")]
        [Trait("spec", "RTL6e1")]
        public async Task WithBasicAuthAndAMessageWithClientId_ShouldReturnTheMessageWithThatClientID(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            client.Connect();
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            bool messageReceived = false;
            channel.Subscribe(message =>
            {
                message.ClientId.Should().Be("123");
                messageReceived = true;
                ResetEvent.Set();
            });

            await channel.PublishAsync(new Message("test", "withClientId") { ClientId = "123" });

            ResetEvent.WaitOne(4000).Should().BeTrue("Operation timed out");

            messageReceived.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6g1b")]
        public async Task WithAClientIdInOptions_ShouldReceiveMessageWithCorrectClientID(Protocol protocol)
        {
            var clientId = (new Random().Next(1, 100000) % 1000).ToString();
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = clientId);

            client.Connect();
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            int messagesReceived = 0;
            string receivedClientId = string.Empty;
            channel.Subscribe(message =>
            {
                receivedClientId = message.ClientId;
                Interlocked.Increment(ref messagesReceived);
            });

            await channel.PublishAsync(new Message("test", "withClientId"));

            await client.ProcessCommands();

            messagesReceived.Should().BeGreaterThan(0);
            receivedClientId.Should().Be(clientId);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6g1b")]
        public async Task WithAnImplicitClientIdFromToken_ShouldReceiveMessageWithCorrectClientID(Protocol protocol)
        {
            var rest = await GetRestClient(protocol);
            var token = await rest.Auth.RequestTokenAsync(new TokenParams { ClientId = "1000" });
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.TokenDetails = token);

            client.Connect();
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            bool messageReceived = false;
            channel.Subscribe(message =>
            {
                message.ClientId.Should().Be("1000");
                messageReceived = true;
            });

            await channel.PublishAsync(new Message("test", "withClientId"));
            await client.ProcessCommands();
            messageReceived.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6g2")]
        public async Task WithAClientIdInOptionsAndMatchingClientIdInMessage_ShouldSendAndReceiveMessageWithCorrectClientID(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = "999");

            client.Connect();
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            bool messageReceived = false;
            channel.Subscribe(message =>
            {
                message.ClientId.Should().Be("999");
                messageReceived = true;
                ResetEvent.Set();
            });

            await channel.PublishAsync(new Message("test", "data") { ClientId = "999" });
            ResetEvent.WaitOne(4000).Should().BeTrue("Timed out");

            messageReceived.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6g2")]
        public async Task WithAClientIdInOptionsAndDifferentClientIdInMessage_ShouldNotSendMessageAndResultInAnError(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = "999");

            client.Connect();
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            bool messageReceived = false;
            channel.Subscribe(message =>
            {
                message.ClientId.Should().Be("999");
                messageReceived = true;
            });

            var result = await channel.PublishAsync(new Message("test", "data") { ClientId = "1000" });
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            messageReceived.Should().BeFalse();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6g4")]
        public async Task
            WhenPublishingMessageWithCompatibleClientIdBeforeClientIdHasBeenConfigured_ShouldPublishTheMessageSuccessfully(
            Protocol protocol)
        {
            const string clientId = "client1";
            var rest = await GetRestClient(protocol);
            var realtimeClient = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;
                opts.AuthCallback = async @params => await rest.Auth.RequestTokenAsync(new TokenParams { ClientId = clientId });
            });

            var channelName = "test".AddRandomSuffix();
            var channel = realtimeClient.Channels.Get(channelName);
            bool messageReceived = false;
            channel.Subscribe(message =>
            {
                messageReceived = true;
                message.ClientId.Should().Be(clientId);
            });

            await channel.PublishAsync(new Message("test", "best") { ClientId = "client1" });

            // wait up to ten seconds
            for (var i = 0; i < 100; i++)
            {
                if (!messageReceived)
                {
                    await Task.Delay(100);
                }
                else
                {
                    break;
                }
            }

            messageReceived.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6g4")]
        [Trait("spec", "RTL6g3")]
        [Trait("spec", "RTL6h")] // Look at PublishAsync
        public async Task
            WhenPublishingMessageWithInCompatibleClientIdBeforeClientIdHasBeenConfigured_ShouldPublishTheMessageAndReturnErrorFromTheServerAllowingFurtherMessagesToBePublished(
            Protocol protocol)
        {
            const string clientId = "client1";
            var rest = await GetRestClient(protocol);
            var realtimeClient = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;
                opts.AuthCallback = async @params => await rest.Auth.RequestTokenAsync(new TokenParams { ClientId = clientId });
            });
            var channelName = "test".AddRandomSuffix();
            var channel = realtimeClient.Channels.Get(channelName);
            int messageReceived = 0;
            channel.Subscribe(message =>
            {
                Interlocked.Add(ref messageReceived, 1);
                message.ClientId.Should().Be(clientId);
            });

            var result = await channel.PublishAsync("test", "best", "client2");
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();

            messageReceived.Should().Be(0);

            // Send a followup message
            var followupMessage = await channel.PublishAsync("followup", "message");
            followupMessage.IsSuccess.Should().BeTrue();
            await realtimeClient.ProcessCommands();

            messageReceived.Should().Be(1);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6f")]
        public async Task ConnectionIdShouldMatchThatOfThePublisher(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = "999");
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            Message testMessage = null;
            channel.Subscribe(message =>
            {
                testMessage = message;
                ResetEvent.Set();
            });

            await channel.PublishAsync(new Message("test", "best"));

            ResetEvent.WaitOne();
            var connectionId = client.Connection.Id;
            testMessage.Should().NotBeNull();
            testMessage.ConnectionId.Should().Be(connectionId);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL6f")]
        public async Task PublishedMessagesShouldContainMessageIdsWhenReceived(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = "999");
            var channelName = "test".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            List<Message> testMessages = new List<Message>();
            channel.Subscribe(message =>
            {
                testMessages.Add(message);
                if (testMessages.Count == 5)
                {
                    ResetEvent.Set();
                }
            });
            var messages = new[]
            {
                new Message("test", "best"),
                new Message("test", "best"),
                new Message("test", "best"),
                new Message("test", "best"),
                new Message("test", "best")
            };

            await channel.PublishAsync(messages);

            ResetEvent.WaitOne();
            testMessages.Select(x => x.Id).Should().NotContainNulls();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL7c")]
        public async Task WhenSubscribingToAChannelWithInsufficientPermissions_ShouldSetItToFailedWithError(
            Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.Key = settings.KeyWithChannelLimitations;
            });

            var channel = client.Channels.Get("nono");
            channel.Subscribe(message =>
            {
                // do nothing
            });

            var result = await new ChannelAwaiter(channel, ChannelState.Failed).WaitAsync();
            await Task.Delay(100);
            result.IsSuccess.Should().BeTrue();

            var error = channel.ErrorReason;
            error.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        public static IEnumerable<object[]> FixtureData
        {
            get
            {
                if (Defaults.MsgPackEnabled)
#pragma warning disable 162
                {
                    yield return new object[] { Defaults.Protocol, GetAes128FixtureData() };
                    yield return new object[] { Defaults.Protocol, GetAes256FixtureData() };
                }
#pragma warning restore 162

                yield return new object[] { Protocol.Json, GetAes128FixtureData() };
                yield return new object[] { Protocol.Json, GetAes256FixtureData() };
            }
        }

        [Theory]
        [MemberData(nameof(FixtureData))]
        [Trait("spec", "RTL7d")]
        public async Task ShouldPublishAndReceiveFixtureData(Protocol protocol, JObject fixtureData)
        {
            var items = (JArray)fixtureData["items"];
            ManualResetEvent resetEvent = new ManualResetEvent(false);
            var client = await GetRealtimeClient(protocol);

            var channel = client.Channels.Get("persisted:test".AddRandomSuffix(), GetOptions(fixtureData));
            var count = 0;
            Message lastMessage = null;
            channel.Subscribe(message =>
            {
                lastMessage = message;
                resetEvent.Set();
            });

            foreach (var item in items)
            {
                var encoded = item["encoded"];
                var encoding = (string)encoded["encoding"];
                var decodedData = DecodeData((string)encoded["data"], encoding);
                await channel.PublishAsync((string)encoded["name"], decodedData);
                var result = resetEvent.WaitOne(10000);
                result.Should().BeTrue("Operation timed out");
                if (lastMessage.Data is byte[])
                {
                    (lastMessage.Data as byte[]).Should().BeEquivalentTo(decodedData as byte[], "Item number {0} data does not match decoded data", count);
                }
                else if (encoding == "json")
                {
                    JToken.DeepEquals((JToken)lastMessage.Data, (JToken)decodedData).Should().BeTrue("Item number {0} data does not match decoded data", count);
                }
                else
                {
                    lastMessage.Data.Should().Be(decodedData, "Item number {0} data does not match decoded data", count);
                }

                count++;
                resetEvent.Reset();
            }

            client.Close();
        }

        [Theory]
        [InlineData(ChannelState.Detached)]
        [InlineData(ChannelState.Failed)]
        [InlineData(ChannelState.Suspended)]
        [Trait("spec", "RTL11")]
        public async Task WhenChannelEntersDetachedFailedSuspendedState_ShouldDeleteQueuedPresenceMessageAndCallbackShouldIndicateError(ChannelState state)
        {
            var client = await GetRealtimeClient(Defaults.Protocol, (options, settings) =>
                {
                    // A bogus AuthUrl will cause connection to become disconnected
                    options.AuthUrl = new Uri("http://235424c24.fake:49999");

                    // speed up the AuthUrl failure
                    options.HttpMaxRetryCount = 1;
                    options.HttpMaxRetryDuration = TimeSpan.FromMilliseconds(100);
                });

            var channel = client.Channels.Get("test".AddRandomSuffix());

            var tsc = new TaskCompletionAwaiter(15000);
            int pendingCount = 0;
            client.Connection.Once(ConnectionEvent.Disconnected, change =>
            {
                // place a message on the queue
                channel.Presence.Enter();
                pendingCount = channel.Presence.PendingPresenceQueue.Count;

                // setting the state should cause the queued message to be removed
                // and the callback to indicate an error
                (channel as RealtimeChannel).SetChannelState(state);
                tsc.SetCompleted();
            });

            var result = await tsc.Task;

            pendingCount.Should().Be(1);
            channel.Presence.PendingPresenceQueue.Count.Should().Be(0);

            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(ChannelState.Detached)]
        [InlineData(ChannelState.Failed)]
        [InlineData(ChannelState.Suspended)]
        [Trait("spec", "RTL11a")]
        public async Task WhenChannelEntersDetachedFailedSuspendedState_MessagesAwaitingAckOrNackShouldNotBeAffected(ChannelState state)
        {
            var client = await GetRealtimeClient(Defaults.Protocol);
            var channel = client.Channels.Get("test".AddRandomSuffix());
            var tsc = new TaskCompletionAwaiter(5000);

            channel.Once(ChannelEvent.Attached, x =>
            {
                channel.Publish("wibble", "wobble", (success, info) =>
                {
                    // message publish should succeed
                    tsc.Set(success);
                });

                client.Connection.Once(ConnectionEvent.Disconnected, change =>
                {
                    (channel as RealtimeChannel).SetChannelState(state);
                });

                client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected)
                {
                    Error = new ErrorInfo("test", ErrorCodes.TokenError)
                });
            });

            channel.Attach();

            var result = await tsc.Task;
            result.Should().BeTrue();
        }

        [Fact]
        [Trait("bug", "102")]
        public async Task WhenAttachingToAChannelFromMultipleThreads_ItShouldNotThrowAnError()
        {
            var client1 = await GetRealtimeClient(Protocol.Json);
            var channel = client1.Channels.Get("test".AddRandomSuffix());
            var task = Task.Run(() => channel.Attach());
            await task.ConfigureAwait(false);
            var task2 = Task.Run(() => channel.Attach());
            await task2.ConfigureAwait(false);

            await Task.WhenAll(task, task2);

            var result = await new ChannelAwaiter(channel, ChannelState.Attached).WaitAsync();
            result.IsSuccess.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL10d")]
        public async Task WithOneClientPublishingAnotherShouldBeAbleToRetrieveMessages(Protocol protocol)
        {
            var client1 = await GetRealtimeClient(protocol);

            var channelName = "persisted:history".AddRandomSuffix();
            var channel = client1.Channels.Get(channelName);
            await channel.AttachAsync();
            var messages = Enumerable.Range(1, 10).Select(x => new Message("name:" + x, "value:" + x));
            await channel.PublishAsync(messages);

            await Task.Delay(2000);

            var client2 = await GetRealtimeClient(protocol);
            var historyChannel = client2.Channels.Get(channelName);
            var history = await historyChannel.HistoryAsync(new PaginatedRequestParams { Direction = QueryDirection.Forwards });

            history.Should().BeOfType<PaginatedResult<Message>>();
            history.Items.Should().HaveCount(10);
            for (int i = 0; i < 10; i++)
            {
                var message = history.Items.ElementAt(i);
                message.Name.Should().Be("name:" + (i + 1));
                message.Data.ToString().Should().Be("value:" + (i + 1));
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL13a")]
        public async Task ServerInitiatedDetach_WhenChannelAttached_ShouldReattachImmediately(Protocol protocol)
        {
            var channelName = "RTL13a".AddRandomSuffix();
            var client = await GetRealtimeClient(protocol);
            var channel = client.Channels.Get(channelName);
            await channel.AttachAsync();

            channel.State.Should().Be(ChannelState.Attached);

            var msg = new ProtocolMessage(ProtocolMessage.MessageAction.Detached, channelName);

            ChannelStateChange stateChange = null;
            await WaitFor(done =>
            {
                channel.Once(ChannelEvent.Attaching, change =>
                {
                    stateChange = change;
                    done();
                });

                client.GetTestTransport().FakeReceivedMessage(msg);
            });

            stateChange.Error.Should().BeEquivalentTo(msg.Error);
            channel.ErrorReason.Should().BeNull();
            await client.ProcessCommands();
            client.GetTestTransport().ProtocolMessagesSent
                .Count(x => x.Action == ProtocolMessage.MessageAction.Attach).Should().Be(2);

            client.Close();
        }

        [Theory(Skip = "Intermittently fails")]
        [ProtocolData]
        [Trait("spec", "RTL13a")]
        public async Task ServerInitiatedDetach_WhenChannelSuspended_ShouldReattachImmediately(Protocol protocol)
        {
            var channelName = "RTL13a".AddRandomSuffix();
            var client = await GetRealtimeClient(protocol);
            client.Connect();
            await client.WaitForState();
            var channel = client.Channels.Get(channelName) as RealtimeChannel;
            channel.SetChannelState(ChannelState.Suspended);
            await channel.WaitForState(ChannelState.Suspended);

            channel.State.Should().Be(ChannelState.Suspended);

            var msg = new ProtocolMessage(ProtocolMessage.MessageAction.Detached, channelName);

            ChannelStateChange stateChange = null;
            await WaitFor(done =>
            {
                channel.Once(ChannelEvent.Attaching, change =>
                {
                    stateChange = change;
                    done();
                });

                client.GetTestTransport().FakeReceivedMessage(msg);
            });

            stateChange.Error.Should().BeEquivalentTo(msg.Error);
            channel.ErrorReason.Should().BeNull();

            client.GetTestTransport().ProtocolMessagesSent
                .Count(x => x.Action == ProtocolMessage.MessageAction.Attach).Should().Be(1);

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL13b")]
        public async Task WhenChannelSuspended_ShouldRetryUsingIncrementalBackoffAfterRetryWhenFirstDetachReceived(Protocol protocol)
        {
            // reduce timeouts to speed up test
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.RealtimeRequestTimeout = TimeSpan.FromSeconds(2);
                options.ChannelRetryTimeout = TimeSpan.FromSeconds(5);
            });

            await client.WaitForState();

            var channelName = "RTL13a".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);

            // block attach messages being sent, keeping channel in attaching state
            client.GetTestTransport().BlockSendActions.Add(ProtocolMessage.MessageAction.Attach);

            channel.Attach();

            // Send first detach message, it will keep retrying until attached state is reached.
            var detachedMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Detached, channelName)
            {
                Error = new ErrorInfo("fake error")
            };
            client.GetTestTransport().FakeReceivedMessage(detachedMessage);

            var stopWatch = new Stopwatch();
            var channelRetryTimeouts = new List<double>();
            do
            {
                await channel.WaitForState(ChannelState.Suspended);
                stopWatch.Start();

                await channel.WaitForState(ChannelState.Attaching, TimeSpan.FromSeconds(10));
                channelRetryTimeouts.Add(stopWatch.Elapsed.TotalSeconds);
                stopWatch.Reset();
            }
            while (channelRetryTimeouts.Count < 8);

            Output.WriteLine(channelRetryTimeouts.ToJson());

            // Upper bound = min((retryAttempt + 2) / 3, 2) * initialTimeout
            // Lower bound = 0.8 * Upper bound
            // Adding 20ms delay to accomodate start and stop
            channelRetryTimeouts[0].Should().BeInRange(4, 5 + 0.20);
            channelRetryTimeouts[1].Should().BeInRange(5.33, 6.66 + 0.20);
            channelRetryTimeouts[2].Should().BeInRange(6.66, 8.33 + 0.20);
            for (var i = 3; i < channelRetryTimeouts.Count; i++)
            {
                channelRetryTimeouts[i].Should().BeInRange(8, 10 + 0.20);
            }

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL13b")]
        public async Task WhenChannelSuspended_ShouldRetryUsingIncrementalBackoffForConsistentDetachReceived(Protocol protocol)
        {
            // reduce timeouts to speed up test
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.ChannelRetryTimeout = TimeSpan.FromSeconds(5);
            });

            await client.WaitForState();

            var channelName = "RTL13a".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);

            // block attach messages being sent, keeping channel in attaching state
            client.GetTestTransport().BlockSendActions.Add(ProtocolMessage.MessageAction.Attach);

            channel.Attach();

            var detachedMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Detached, channelName)
            {
                Error = new ErrorInfo("fake error")
            };

            var stopWatch = new Stopwatch();
            var channelRetryTimeouts = new List<double>();
            do
            {
                client.GetTestTransport().FakeReceivedMessage(detachedMessage);

                await channel.WaitForState(ChannelState.Suspended);
                stopWatch.Start();

                await channel.WaitForState(ChannelState.Attaching, TimeSpan.FromSeconds(10));
                channelRetryTimeouts.Add(stopWatch.Elapsed.TotalSeconds);
                stopWatch.Reset();
            }
            while (channelRetryTimeouts.Count < 8);

            Output.WriteLine(channelRetryTimeouts.ToJson());

            // Upper bound = min((retryAttempt + 2) / 3, 2) * initialTimeout
            // Lower bound = 0.8 * Upper bound
            // Adding 20ms delay to accomodate start and stop
            channelRetryTimeouts[0].Should().BeInRange(4, 5 + 0.20);
            channelRetryTimeouts[1].Should().BeInRange(5.33, 6.66 + 0.20);
            channelRetryTimeouts[2].Should().BeInRange(6.66, 8.33 + 0.20);
            for (var i = 3; i < channelRetryTimeouts.Count; i++)
            {
                channelRetryTimeouts[i].Should().BeInRange(8, 10 + 0.20);
            }

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL13b")]
        public async Task ServerInitiatedDetach_WhenChannelAttached_ShouldAttemptReattachImmediately_WhenReattachFailsBecomeSuspended(Protocol protocol)
        {
            // reduce timeouts to speed up test
            var requestTimeout = TimeSpan.FromSeconds(2);
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.RealtimeRequestTimeout = requestTimeout;
                options.ChannelRetryTimeout = requestTimeout;
            });

            var channelName = "RTL13a".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);
            channel.Attach();

            await channel.WaitForAttachedState();
            channel.State.Should().Be(ChannelState.Attached);

            // block attach messages being sent causing the attach to timeout
            client.GetTestTransport().BlockSendActions.Add(ProtocolMessage.MessageAction.Attach);

            var detachedMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Detached, channelName)
            {
                Error = new ErrorInfo("fake error")
            };

            ChannelStateChange stateChange = null;
            ChannelStateChange stateChange2 = null;
            var start = DateTimeOffset.MinValue;
            var end = DateTimeOffset.MaxValue;

            await WaitFor(30000, done =>
            {
                // after detached message channel should become ATTACHING
                channel.Once(ChannelEvent.Attaching, change =>
                {
                    stateChange = change;

                    // after the ATTACH fails it should become SUSPENDED
                    channel.Once(ChannelEvent.Suspended, change2 =>
                    {
                        stateChange2 = change2;
                        start = DateTimeOffset.UtcNow;

                        // it should keep trying to attach
                        channel.Once(ChannelEvent.Attaching, change3 =>
                        {
                            end = DateTimeOffset.UtcNow;
                            done();
                        });
                    });
                });

                // inject detached message
                client.GetTestTransport().FakeReceivedMessage(detachedMessage);
            });

            // the first error should be from the detached message
            stateChange.Error.Should().BeEquivalentTo(detachedMessage.Error);

            // the second should be a timeout error
            stateChange2.Error.Message.Should().StartWith("Channel didn't attach within");

            // retry should happen after ChannelRetryTimeout has elapsed (TL3l7)
            (end - start).Should().BeCloseTo(requestTimeout, TimeSpan.FromMilliseconds(500));

            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTL13b")]
        [Trait("spec", "RTL13c")]
        public async Task ServerInitiatedDetach_WhenChannelAttaching_ShouldAttemptReattachImmediately_WhenReattachFailsBecomeSuspended(Protocol protocol)
        {
            // reduce timeouts to speed up test
            var requestTimeout = TimeSpan.FromSeconds(2);
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.RealtimeRequestTimeout = requestTimeout;
                options.ChannelRetryTimeout = requestTimeout;
            });
            await client.WaitForState(ConnectionState.Connected);
            var channelName = "RTL13a".AddRandomSuffix();
            var channel = client.Channels.Get(channelName);

            // block attach messages being sent causing the attach to timeout
            client.GetTestTransport().BlockSendActions.Add(ProtocolMessage.MessageAction.Attach);

            var detachedMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Detached, channelName)
            {
                Error = new ErrorInfo("fake error")
            };

            ChannelStateChange stateChange = null;
            ChannelStateChange stateChange2 = null;
            var start = DateTimeOffset.MinValue;
            var end = DateTimeOffset.MaxValue;

            await WaitFor(30000, done =>
            {
                // after detached message channel should become ATTACHING
                channel.Once(ChannelEvent.Attaching, change =>
                {
                    stateChange = change;

                    // after the ATTACH fails it should become SUSPENDED
                    channel.Once(ChannelEvent.Suspended, change2 =>
                    {
                        stateChange2 = change2;
                        start = DateTimeOffset.UtcNow;

                        // it should keep trying to attach
                        channel.Once(ChannelEvent.Attaching, change3 =>
                        {
                            end = DateTimeOffset.UtcNow;
                            done();
                        });
                    });

                    // inject detached message
                    client.GetTestTransport().FakeReceivedMessage(detachedMessage);
                });

                channel.Attach();
            });

            client.Close();

            // wait for double the requestTimeout
            var tsc = new TaskCompletionAwaiter(requestTimeout.Add(requestTimeout).Seconds * 1000);
            await client.WaitForState(ConnectionState.Closed);

            // RTL13c If the connection is no longer CONNECTED,
            // then the automatic attempts to re-attach the channel should stop
            channel.Once(ChannelEvent.Attaching, change3 =>
            {
                tsc.SetCompleted(); // should not be called
            });

            var didRetry = await tsc.Task;
            didRetry.Should().BeFalse();

            // the first error should be null
            stateChange.Error.Should().BeNull();

            // the second should be a timeout error
            stateChange2.Error.Message.Should().Be(detachedMessage.Error.Message);

            // retry should happen after SuspendedRetryTimeout has elapsed
            (end - start).Should().BeCloseTo(requestTimeout, TimeSpan.FromMilliseconds(2000));
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "117")]
        public async Task AttachAwaitShouldTimeoutIfStateChanges(Protocol protocol)
        {
            var client1 = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;
                opts.RealtimeRequestTimeout = new TimeSpan(0, 0, 30);
            });

            var channelName = "test".AddRandomSuffix();
            var channel = client1.Channels.Get(channelName);

            TaskCompletionSource<bool> tsc = new TaskCompletionSource<bool>();
            client1.Connection.On(ConnectionEvent.Connected, async state =>
            {
                tsc.SetResult(true);
                client1.GetTestTransport().Close(false);
                var result = await channel.AttachAsync();
                result.Error.Should().NotBeNull();
                result.Error.Message.Should().Contain("Timeout exceeded");
            });
            client1.Connect();
            var didConnect = await tsc.Task;
            didConnect.Should().Be(true, "this indicates that the connection event was handled.");
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "104")]
        public async Task AttachWithMultipleConcurrentClientsShouldWork(Protocol protocol)
        {
            var clients = new List<IRealtimeClient>
            {
                await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false),
                await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false),
                await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false),
                await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false),
                await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false)
            };

            var channelName = "test".AddRandomSuffix();
            foreach (var client in clients)
            {
                client.Connect();
                await client.WaitForState();
                client.Channels.Get(channelName).Attach();
                await client.Channels.Get(channelName).WaitForState();
            }

            await Task.Delay(TimeSpan.FromSeconds(15));
            foreach (var client in clients)
            {
                client.Channels.Get(channelName).State.Should().Be(ChannelState.Attached);
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "116")]
        public async Task FailureOfHistoryApiCallMeansChannelsNoLongerAttach(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false);
            client.Connection.On(ConnectionEvent.Connected, async args =>
            {
                await client.Channels.Get("test")
                    .HistoryAsync(new PaginatedRequestParams { Start = DateHelper.CreateDate(1969, 1, 1) });
            });

            var result = await client.Channels.Get("name").AttachAsync();

            result.IsSuccess.Should().BeTrue();
            result.Error.Should().BeNull();
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "205")]
        public async Task ShouldReturnToPreviousStateIfAttachMessageNotReceivedWithinDefaultTimeout2(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            client.Options.RealtimeRequestTimeout = TimeSpan.FromMilliseconds(2000);
            bool? didAttach = null;
            var channel = (RealtimeChannel)client.Channels.Get("test-issue#205".AddRandomSuffix());
            channel.Attach((b, info) =>
            {
                didAttach = b;
                if (info != null)
                {
                    throw new Exception($"Attach returned an error: {info.Message}");
                }
            });
            await Task.Delay(1000);
            didAttach.Should().BeTrue();
            channel.InternalStateChanged += (sender, change) => throw new AblyException(change.Error);
            channel.State.Should().Be(ChannelState.Attached);
            await Task.Delay(3000);
            channel.State.Should().Be(ChannelState.Attached);
        }

        [Theory]
        [ProtocolData]
        public async Task WhenAttachAsyncCalledAfterSubscribe_ShouldWaitUntilChannelIsAttached(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            var channel = client.Channels.Get("test".AddRandomSuffix());
            channel.Subscribe(message => { });
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);
        }

        [Theory]
        [ProtocolData]
        [Trait("issue ", "382")]

        public async Task WhenSubscribeHandlerBlocks_ShouldBehaveOk(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);
            var channel = client.Channels.Get("test".AddRandomSuffix());
            int processedMessaged = 0;
            List<int> order = new List<int>();
            var taskAwaiter = new TaskCompletionAwaiter(20000);
            channel.Subscribe(m =>
            {
                if (processedMessaged <= 0)
                {
                    Task.Delay(15000).WaitAndUnwrapException();
                }

                order.Add(int.Parse(m.Data.ToString()));

                processedMessaged++;
                if (processedMessaged == 3)
                {
                    taskAwaiter.SetCompleted();
                }
            });

            await channel.AttachAsync();
            channel.Publish("test", "1");
            channel.Publish("test", "2");
            channel.Publish("test", "3");
            await taskAwaiter.Task;

            order.Should().HaveCount(3);
            order.Should().BeInAscendingOrder();
        }

        [Theory]
        [ProtocolData]
        [Trait("issue ", "980")]

        public async Task WhenReleasingChannelsBeforeTheyAreAttached_TheyShouldBeRemovedImmediately(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (opts, _) => opts.AutoConnect = false);

            var channels = Enumerable.Range(1, 10).Select(_ => client.Channels.Get("test".AddRandomSuffix())).ToList();

            client.Channels.Should().HaveCount(10);
            client.Channels.ReleaseAll();
            client.Channels.Should().BeEmpty();
            foreach (var channel in channels)
            {
                client.Channels.Exists(channel.Name).Should().BeFalse();
            }
        }

        [Theory]
        [ProtocolData]
        [Trait("issue ", "980")]

        public async Task WhenReleasingAttachedChannels_TheyShouldBeRemovedFromTheList(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol);

            var channels = Enumerable.Range(1, 100).Select(_ => client.Channels.Get("test".AddRandomSuffix())).ToList();

            client.Connect();
            foreach (var channel in channels)
            {
                await channel.AttachAsync();
            }

            client.Channels.Should().HaveCount(100);

            client.Channels.ReleaseAll();

            var taskAwaiter = new ConditionalAwaiter(() => client.Channels.Any() == false, () => $"Time elapsed: Channels {client.Channels.Count()}");
            await taskAwaiter;

            client.Channels.Should().BeEmpty();
            foreach (var channel in channels)
            {
                client.Channels.Exists(channel.Name).Should().BeFalse();
            }
        }

        private static JObject GetAes128FixtureData()
        {
            return JObject.Parse(ResourceHelper.GetResource("crypto-data-128.json"));
        }

        private static JObject GetAes256FixtureData()
        {
            return JObject.Parse(ResourceHelper.GetResource("crypto-data-256.json"));
        }

        private static ChannelOptions GetOptions(JObject data)
        {
            var key = (string)data["key"];
            var iv = (string)data["iv"];
            var cipherParams = new CipherParams("aes", key, CipherMode.CBC, iv);
            return new ChannelOptions(cipherParams);
        }

        private static object DecodeData(string data, string encoding)
        {
            if (encoding == "json")
            {
                return JsonHelper.Deserialize(data);
            }

            if (encoding == "base64")
            {
                return data.FromBase64();
            }

            return data;
        }

        public ChannelSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }
    }
}
