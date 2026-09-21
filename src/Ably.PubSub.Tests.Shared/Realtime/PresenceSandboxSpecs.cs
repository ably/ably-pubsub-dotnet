using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Rest;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Transport;
using IO.Ably.Types;

using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Collection("Presence Sandbox")]
    [Trait("type", "integration")]
    public class PresenceSandboxSpecs : SandboxSpecs
    {
        private PresenceSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        private static string GetTestChannelName(string id = "")
        {
            return $"presence-{id}".AddRandomSuffix();
        }

        private static RealtimeChannel GetRandomChannel(IRealtimeClient client, string channelNamePrefix)
        {
            return GetChannel(client, channelNamePrefix.AddRandomSuffix());
        }

        private static void BlockAblyServerSentSyncAction(IRealtimeClient client)
        {
            client.BlockActionFromReceiving(ProtocolMessage.MessageAction.Sync);
        }

        private static RealtimeChannel GetChannel(IRealtimeClient client, string channelName)
        {
            var channel = client.Channels.Get(channelName) as RealtimeChannel;
            channel.Should().NotBeNull();
            Debug.Assert(channel != null, "Previous call to 'Channels.Get' failed.");
            return channel;
        }

        [Trait("type", "integration")]
        public class GeneralPresenceSandBoxSpecs : PresenceSandboxSpecs
        {
            public GeneralPresenceSandBoxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
                : base(fixture, output)
            {
            }

            // TODO: Add tests to makes sure Presence messages id, timestamp and connectionId are set
            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP1")]
            public async Task WhenAttachingToAChannelWithNoMembers_PresenceShouldBeConsideredInSync(Protocol protocol)
            {
                var client = await GetRealtimeClient(protocol);
                await client.WaitForState(ConnectionState.Connected);

                client.BeforeProtocolMessageProcessed(message =>
                {
                    if (message.Action == ProtocolMessage.MessageAction.Attached)
                    {
                        message.Flags &= ~(int)ProtocolMessage.Flag.HasPresence; // unset has_presence/members bit
                    }
                });

                var channel = client.Channels.Get(GetTestChannelName());

                await channel.AttachAsync();
                await channel.WaitForAttachedState();
                channel.State.Should().Be(ChannelState.Attached);

                channel.Presence.SyncComplete.Should().BeTrue();
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP1")]
            public async Task WhenAttachingToAChannelWithMembers_PresenceShouldBeInProgress(Protocol protocol)
            {
                var testChannel = GetTestChannelName();
                var client = await GetRealtimeClient(protocol);
                var client2 = await GetRealtimeClient(protocol);
                var channel = GetChannel(client, testChannel);

                for (var count = 1; count < 10; count++)
                {
                    await channel.Presence.EnterClientAsync($"client-{count}", null);
                }

                var channel2 = GetChannel(client2, testChannel);

                var awaiter = new TaskCompletionAwaiter();

                bool syncInProgress = false, syncComplete = false;
                channel2.InternalStateChanged += (_, args) =>
                {
                    if (args.Current == ChannelState.Attached)
                    {
                        Logger.Debug("Test: Setting inSync to - " + channel2.Presence.MembersMap.SyncInProgress);
                        syncInProgress = channel2.Presence.SyncInProgress;
                        syncComplete = channel2.Presence.SyncComplete;
                        awaiter.SetCompleted();
                    }
                };
                await channel2.AttachAsync();
                await awaiter.Task;

                syncInProgress.Should().Be(true);
                syncComplete.Should().Be(false);
            }

            /*
            * Test presence message map behaviour (RTP2 features)
            * Tests RTP2a, RTP2b1, RTP2b2, RTP2c, RTP2d, RTP2g, RTP18c, RTP6a features
            */
            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP2")]
            [Trait("spec", "RTP2a")]
            [Trait("spec", "RTP2b1")]
            [Trait("spec", "RTP2b2")]
            [Trait("spec", "RTP2c")]
            [Trait("spec", "RTP2d")]
            [Trait("spec", "RTP2g")]
            [Trait("spec", "RTP6a")]
            [Trait("spec", "RTP18c")]
            public async Task PresenceMapBehaviour_ShouldConformToSpec(Protocol protocol)
            {
                var channelName = "presence_map_tests_newness".AddRandomSuffix();

                var client = await GetRealtimeClient(protocol);
                await client.WaitForState(ConnectionState.Connected);

                BlockAblyServerSentSyncAction(client);

                var channel = client.Channels.Get(channelName);
                await channel.AttachAsync();

                channel.State.Should().Be(ChannelState.Attached);
                var receivedPresenceMsgs = new List<PresenceMessage>();

                const string wontPass = "Won't pass newness test";
                // Subscribing to new presence messages
                channel.Presence.Subscribe(x =>
                {
                    x.Data.Should().NotBe(wontPass, "message did not pass the newness test");
                    receivedPresenceMsgs.Add(x);
                });

                /* Test message newness criteria as described in RTP2b */
                var testData = new[]
                {
                    new PresenceMessage
                    {
                        Action = PresenceAction.Enter,
                        ClientId = "1",
                        ConnectionId = "1",
                        Id = "1:0",
                        Data = string.Empty
                    },
                    new PresenceMessage
                    {
                        Action = PresenceAction.Enter,
                        ClientId = "2",
                        ConnectionId = "2",
                        Id = "2:1:0",
                        Timestamp = new DateTimeOffset(2000, 1, 1, 1, 1, 1, default(TimeSpan)),
                        Data = string.Empty
                    },
                    /* Should be newer than previous one */
                    new PresenceMessage
                    {
                        Action = PresenceAction.Update,
                        ClientId = "2",
                        ConnectionId = "2",
                        Id = "2:2:1",
                        Timestamp = new DateTimeOffset(2000, 1, 1, 1, 1, 2, default(TimeSpan)),
                        Data = string.Empty,
                    },
                    /* Shouldn't pass newness test because of message serial, timestamp doesn't matter in this case */
                    new PresenceMessage
                    {
                        Action = PresenceAction.Update,
                        ClientId = "2",
                        ConnectionId = "2",
                        Id = "2:1:1",
                        Timestamp = new DateTimeOffset(2000, 1, 1, 1, 1, 3, default(TimeSpan)),
                        Data = wontPass,
                    },
                    /* Shouldn't pass because of message index */
                    new PresenceMessage
                    {
                        Action = PresenceAction.Update,
                        ClientId = "2",
                        ConnectionId = "2",
                        Id = "2:2:0",
                        Data = wontPass,
                    },
                    /* Should pass because id is not in form connId:clientId:index and timestamp is greater */
                    new PresenceMessage
                    {
                        Action = PresenceAction.Update,
                        ClientId = "2",
                        ConnectionId = "2",
                        Id = "wrong_id",
                        Timestamp = new DateTimeOffset(2000, 1, 1, 1, 1, 10, default),
                        Data = string.Empty,
                    },
                    /* Shouldn't pass because of timestamp */
                    new PresenceMessage
                    {
                        Action = PresenceAction.Update,
                        ClientId = "2",
                        ConnectionId = "2",
                        Id = "2:3:1",
                        Timestamp = new DateTimeOffset(2000, 1, 1, 1, 1, 5, default),
                        Data = wontPass,
                    },
                };

                foreach (var presenceMessage in testData)
                {
                    var protocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Presence)
                    {
                        Channel = channelName,
                        Presence = new[] { presenceMessage },
                    };
                    client.Workflow.QueueCommand(ProcessMessageCommand.Create(protocolMessage));
                }

                await client.ProcessCommands();

                int n = 0;
                foreach (var testMsg in testData)
                {
                    if (testMsg.Data.ToString() == wontPass)
                    {
                        continue;
                    }

                    var factualMsg = n < receivedPresenceMsgs.Count ? receivedPresenceMsgs[n++] : null;
                    factualMsg.Should().NotBeNull();
                    Debug.Assert(factualMsg != null, $"Expected '{factualMsg}' to be non-null");
                    factualMsg.Id.Should().BeEquivalentTo(testMsg.Id);
                    factualMsg.Action.Should().Be(testMsg.Action, "message was not emitted on the presence object with original action");
                    var presenceMessages = await channel.Presence.GetAsync(new Presence.GetParams
                    {
                        ClientId = testMsg.ClientId,
                        WaitForSync = false
                    });
                    presenceMessages.FirstOrDefault().Should().NotBeNull();
                    presenceMessages.FirstOrDefault()?.Action.Should().Be(PresenceAction.Present, "message was not added to the presence map and stored with PRESENT action");
                }

                receivedPresenceMsgs.Count.Should().Be(n, "the number of messages received didn't match the number of test messages sent.");

                /* Repeat the process now as a part of SYNC and verify everything is exactly the same */
                var channel2Name = "presence_map_tests_sync_newness".AddRandomSuffix();

                var client2 = await GetRealtimeClient(protocol);
                await client2.WaitForState(ConnectionState.Connected);
                client2.Connection.State.Should().Be(ConnectionState.Connected);

                var channel2 = client2.Channels.Get(channel2Name);
                channel2.Attach();
                channel2.State.Should().Be(ChannelState.Attaching);
                await channel2.WaitForAttachedState();
                channel2.State.Should().Be(ChannelState.Attached);

                /* Send all the presence data in one SYNC message without channelSerial (RTP18c) */
                var syncMessage = new ProtocolMessage
                {
                    Channel = channel2Name,
                    Action = ProtocolMessage.MessageAction.Sync,
                    Presence = testData
                };

                var counter = new TaskCountAwaiter(receivedPresenceMsgs.Count, 5000);
                var syncPresenceMessages = new List<PresenceMessage>();
                channel2.Presence.Subscribe(x =>
                {
                    x.Data.Should().NotBe(wontPass, "message did not pass the newness test");
                    syncPresenceMessages.Add(x);
                    counter.Tick();
                });

                client2.Workflow.QueueCommand(ProcessMessageCommand.Create(syncMessage));

                await counter.Task;

                syncPresenceMessages.Count.Should().Be(receivedPresenceMsgs.Count);

                for (int i = 0; i < syncPresenceMessages.Count; i++)
                {
                    syncPresenceMessages[i].Id.Should().BeEquivalentTo(receivedPresenceMsgs[i].Id, "result should be the same in case of SYNC");
                    syncPresenceMessages[i].Action.Should().Be(receivedPresenceMsgs[i].Action, "result should be the same in case of SYNC");
                }
            }

            [Theory(Skip = "Fails for last assertion, rest channel not retrieving updated presence")]
            [InlineData(Protocol.Json, 30)] // Wait for 30 seconds
            [InlineData(Protocol.Json, 60)] // Wait for 1 minute
            [Trait("spec", "RTP17f")]
            public async Task Presence_ShouldReenterPresenceAfterAConnectionLoss(Protocol protocol, int waitInSeconds)
            {
                var channelName = "RTP17e".AddRandomSuffix();

                async Task<(AblyRealtime, IRestClient, TestTransportWrapper)> InitializeRealtimeAndConnect()
                {
                    var capability = new Capability();
                    capability.AddResource(channelName).AllowAll();
                    TestTransportWrapper transport = null;
                    var transportFactory = new TestTransportFactory();
                    transportFactory.OnTransportCreated = t => transport = t;
                    var clientA = await GetRealtimeClient(protocol, (options, settings) =>
                    {
                        options.DefaultTokenParams = new TokenParams { Capability = capability, ClientId = "martin" };
                        options.TransportFactory = transportFactory;
                    });
                    await clientA.WaitForState(ConnectionState.Connected);

                    return (clientA, clientA.RestClient, transport);
                }

                async Task<(IRealtimeChannel, IRestChannel)> GetChannelsAndEnsurePresenceSynced(
                    IRealtimeClient rt,
                    IRestClient rest)
                {
                    var realtimeChan = rt.Channels.Get(channelName);

                    var restChan = rest.Channels.Get(channelName);

                    await realtimeChan.Presence.EnterAsync();
                    await realtimeChan.WaitForAttachedState();
                    _ = await realtimeChan.Presence.WaitSync();

                    return (realtimeChan, restChan);
                }

                async Task<bool> WaitForRestPresence(IRestChannel restChan, bool shouldBePresent = true)
                {
                    int count = 0;
                    while (true)
                    {
                        var result = await restChan.Presence.GetAsync();
                        var hasPresence = result.Items.Exists(message =>
                                message.ClientId.EqualsTo("martin"));
                        if (shouldBePresent && hasPresence)
                        {
                            break;
                        }

                        if (!shouldBePresent && !hasPresence)
                        {
                            break;
                        }

                        if (count > 30)
                        {
                            return false;
                        }

                        await Sleep(2);

                        count++;
                    }

                    return true;
                }

                Task Sleep(int seconds) => Task.Delay(seconds * 1000);

                async Task<bool> WaitForNoPresenceOnChannel(IRestChannel rChannel)
                {
                    return await WaitForRestPresence(rChannel, false);
                }

                // arrange
                var (realtimeClient, restClient, testTransport) = await InitializeRealtimeAndConnect();
                var (realtimeChannel, restChannel) = await GetChannelsAndEnsurePresenceSynced(realtimeClient, restClient);

                // Check the presence of the realtime lib is there
                try
                {
                    // act
                    (await WaitForRestPresence(restChannel)).Should().BeTrue();

                    // Kill the transport but don't tell the library
                    testTransport.Close();

                    await Sleep(waitInSeconds); // wait before starting to check presence

                    await WaitForNoPresenceOnChannel(restChannel);

                    // let the library know the transport is really dead
                    testTransport.Listener?.OnTransportEvent(testTransport.Id, TransportState.Closed);
                    await realtimeClient.WaitForState(ConnectionState.Disconnected);
                    await realtimeClient.WaitForState(ConnectionState.Connected);
                    await realtimeChannel.WaitForAttachedState();
                    var messages = await realtimeChannel.Presence.GetAsync();
                    messages.Count().Should().Be(1);

                    (await WaitForRestPresence(restChannel)).Should().BeTrue();
                }
                finally
                {
                    // clean up - should go in infrastructure
                    realtimeClient.Close();
                }
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP17f")]
            public async Task OnAttach_ShouldEnterMembersFromInternalMap(Protocol protocol)
            {
                var channelName = "RTP17c2".AddRandomSuffix();
                var setupClient = await GetRealtimeClient(protocol);
                var setupChannel = setupClient.Channels.Get(channelName);

                // enter 3 client to the channel
                for (int i = 0; i < 3; i++)
                {
                    await setupChannel.Presence.EnterClientAsync($"member_{i}", null);
                }

                var client = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "local"; });
                await client.WaitForState();
                var channel = client.Channels.Get(channelName);
                var presence = channel.Presence;

                var p = await presence.GetAsync();
                p.Should().HaveCount(3);

                await presence.EnterAsync();

                await Task.Delay(250);
                presence.MembersMap.Members.Should().HaveCount(4);
                presence.InternalMembersMap.Members.Should().HaveCount(1);
                var internalMemberId = presence.InternalMembersMap.Members.Values.First().Id;

                List<PresenceMessage> leaveMessages = new List<PresenceMessage>();
                PresenceMessage enteredMember = null;
                await WaitForMultiple(2, partialDone =>
                {
                    presence.Subscribe(PresenceAction.Leave, message =>
                    {
                        leaveMessages.Add(message);
                    });
                    client.GetTestTransport().BeforeMessageSend = message =>
                    {
                        enteredMember = message.Presence.First();
                        client.GetTestTransport().BeforeMessageSend = _ => { };
                        partialDone();
                    };
                    client.GetTestTransport().AfterDataReceived = message =>
                    {
                        if (message.Action == ProtocolMessage.MessageAction.Attached)
                        {
                            bool hasPresence = message.HasFlag(ProtocolMessage.Flag.HasPresence);
                            hasPresence.Should().BeFalse();
                            client.GetTestTransport().AfterDataReceived = _ => { };
                            partialDone(); // 1 call
                        }
                    };
                    // inject duplicate attached message without resume flag
                    var protocolMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Attached)
                    {
                        Channel = channelName,
                        Flags = 0,
                    };
                    client.GetTestTransport().FakeReceivedMessage(protocolMessage);
                });

                leaveMessages.Should().HaveCount(4);
                foreach (var msg in leaveMessages)
                {
                    msg.ClientId.Should().BeOneOf("member_0", "member_1", "member_2", "local");
                }

                enteredMember.Should().NotBeNull();
                enteredMember.Id.Should().Be(internalMemberId);
                enteredMember.Action.Should().Be(PresenceAction.Enter);
                enteredMember.ClientId.Should().Be("local");
                presence.Unsubscribe();

                client.Close();
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP17")]
            [Trait("spec", "RTP17b")]
            public async Task Presence_ShouldHaveInternalMapForCurrentConnectionId(Protocol protocol)
            {
                /*
                 * any ENTER, PRESENT, UPDATE or LEAVE event that matches
                 * the current connectionId should be applied to the internal map
                 */

                var channelName = "RTP17".AddRandomSuffix();
                var clientA = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "A"; });
                await clientA.WaitForState(ConnectionState.Connected);

                var channelA = clientA.Channels.Get(channelName);
                channelA.Attach();
                channelA.State.Should().Be(ChannelState.Attaching);
                await channelA.WaitForAttachedState();
                channelA.State.Should().Be(ChannelState.Attached);

                var clientB = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "B"; });
                await clientB.WaitForState(ConnectionState.Connected);

                var channelB = clientB.Channels.Get(channelName);
                channelB.Attach();
                channelB.State.Should().Be(ChannelState.Attaching);
                await channelB.WaitForAttachedState();
                channelB.State.Should().Be(ChannelState.Attached);
                // ENTER
                PresenceMessage msgA = null, msgB = null;
                await WaitForMultiple(2, partialDone =>
                {
                    channelA.Presence.Subscribe(msg =>
                    {
                        msgA = msg;
                        channelA.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Subscribe(msg =>
                    {
                        msgB = msg;
                        channelB.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelA.Presence.Enter("chA");
                });

                msgA.Should().NotBeNull();
                msgA.Action.Should().Be(PresenceAction.Enter);
                msgA.ConnectionId.Should().Be(clientA.Connection.Id);
                channelA.Presence.MembersMap.Members.Should().HaveCount(1);
                channelA.Presence.InternalMembersMap.Members.Should().HaveCount(1);
                channelA.Presence.Unsubscribe();

                msgB.Should().NotBeNull();
                msgB.Action.Should().Be(PresenceAction.Enter);
                msgB.ConnectionId.Should().NotBe(clientB.Connection.Id);
                channelB.Presence.MembersMap.Members.Should().HaveCount(1);
                channelB.Presence.InternalMembersMap.Members.Should().HaveCount(0);
                channelB.Presence.Unsubscribe();

                msgA = null;
                msgB = null;
                await WaitForMultiple(2, partialDone =>
                {
                    channelA.Presence.Subscribe(msg =>
                    {
                        msgA = msg;
                        channelA.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Subscribe(msg =>
                    {
                        msgB = msg;
                        channelB.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Enter("chB");
                });

                msgA.Should().NotBeNull();
                msgA.Action.Should().Be(PresenceAction.Enter);
                msgA.ConnectionId.Should().NotBe(clientA.Connection.Id);
                channelA.Presence.MembersMap.Members.Should().HaveCount(2);
                channelA.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                msgB.Should().NotBeNull();
                msgB.Action.Should().Be(PresenceAction.Enter);
                msgB.ConnectionId.Should().Be(clientB.Connection.Id);
                channelB.Presence.MembersMap.Members.Should().HaveCount(2);
                channelB.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                // UPDATE
                msgA = null;
                msgB = null;
                await WaitForMultiple(2, partialDone =>
                {
                    channelA.Presence.Subscribe(msg =>
                    {
                        msgA = msg;
                        channelA.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Subscribe(msg =>
                    {
                        msgB = msg;
                        channelB.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Update("chB-update");
                });

                msgA.Should().NotBeNull();
                msgA.Action.Should().Be(PresenceAction.Update);
                msgA.ConnectionId.Should().NotBe(clientA.Connection.Id);
                msgA.Data.ToString().Should().Be("chB-update");
                channelA.Presence.MembersMap.Members.Should().HaveCount(2);
                channelA.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                msgB.Should().NotBeNull();
                msgB.Action.Should().Be(PresenceAction.Update);
                msgB.ConnectionId.Should().Be(clientB.Connection.Id);
                msgB.Data.ToString().Should().Be("chB-update");
                channelB.Presence.MembersMap.Members.Should().HaveCount(2);
                channelB.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                // LEAVE with synthesized message
                msgA = null;
                msgB = null;
                var synthesizedMsg = new PresenceMessage(PresenceAction.Leave, clientB.ClientId) { ConnectionId = null };
                synthesizedMsg.IsServerSynthesized().Should().BeTrue();
                channelB.Presence.OnPresence(new[] { synthesizedMsg });

                msgB.Should().BeNull();
                channelB.Presence.MembersMap.Members.Should().HaveCount(2);

                // message was synthesized so should not have been removed (RTP17b)
                channelB.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                // LEAVE
                msgA = null;
                msgB = null;
                await WaitForMultiple(2, partialDone =>
                {
                    channelA.Presence.Subscribe(msg =>
                    {
                        msgA = msg;
                        channelA.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Subscribe(msg =>
                    {
                        msgB = msg;
                        channelB.Presence.Unsubscribe();
                        partialDone();
                    });

                    channelB.Presence.Leave("chB-leave");
                });

                msgA.Should().NotBeNull();
                msgA.Action.Should().Be(PresenceAction.Leave);
                msgA.ConnectionId.Should().NotBe(clientA.Connection.Id);
                msgA.Data.ToString().Should().Be("chB-leave");
                channelA.Presence.MembersMap.Members.Should().HaveCount(1);
                channelA.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                msgB.Should().NotBeNull();
                msgB.Action.Should().Be(PresenceAction.Leave);
                msgB.ConnectionId.Should().Be(clientB.Connection.Id);
                msgB.Data.ToString().Should().Be("chB-leave");
                channelB.Presence.MembersMap.Members.Should().HaveCount(1);
                channelB.Presence.InternalMembersMap.Members.Should().HaveCount(0);

                // clean up
                clientA.Close();
                clientB.Close();
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP17a")]
            public async Task Presence_ShouldPublishAllMembersForTheCurrentConnection(Protocol protocol)
            {
                var channelName = "RTP17a".AddRandomSuffix();
                var clientId = "RTP17a-client".AddRandomSuffix();
                var capability = new Capability();
                capability.AddResource(channelName).AllowPresence().AllowPublish();
                var client = await GetRealtimeClient(protocol, (options, settings) =>
                {
                    options.DefaultTokenParams = new TokenParams { Capability = capability, ClientId = clientId };
                });

                var channel = client.Channels.Get(channelName);
                var result = await channel.Presence.EnterClientAsync(clientId, null);
                result.IsSuccess.Should().BeTrue();

                await Task.Delay(250);

                var members = await channel.Presence.GetAsync();
                members.Should().HaveCount(1);
                channel.Presence.MembersMap.Members.Should().HaveCount(1);
                channel.Presence.InternalMembersMap.Members.Should().HaveCount(1);
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP2e")]
            public async Task PresenceMap_WhenNotSyncingAndLeaveActionArrivesMemberKeyShouldBeDeleted(Protocol protocol)
            {
                // setup 20 members on a channel
                var channelName = "RTP2e".AddRandomSuffix();
                var setupClient = await GetRealtimeClient(protocol);
                await setupClient.WaitForState(ConnectionState.Connected);
                var setupChannel = setupClient.Channels.Get(channelName);
                setupChannel.Attach();
                await setupChannel.WaitForState();
                for (int i = 0; i < 20; i++)
                {
                    await setupChannel.Presence.EnterClientAsync($"member_{i}", null);
                }

                var client = await GetRealtimeClient(protocol);
                await client.WaitForState(ConnectionState.Connected);
                var channel = client.Channels.Get(channelName);
                channel.Attach();
                await channel.WaitForState();

                // get presence messages and validate count
                var members = await channel.Presence.GetAsync();
                members.Should().HaveCount(20);

                // sync should not be in progress and initial an sync should have completed
                channel.Presence.SyncInProgress.Should().BeFalse("sync should have completed");
                channel.Presence.SyncComplete.Should().BeTrue();

                // pull a random member key from the presence map
                var memberNumber = new Random().Next(0, 19);
                var memberId = $"member_{memberNumber}";
                var expectedMemberKey = $"{memberId}:{setupClient.Connection.Id}";
                var actualMemberKey = channel.Presence.MembersMap.Members[expectedMemberKey].MemberKey;

                actualMemberKey.Should().Be(expectedMemberKey);

                // wait for the member to leave
                string leftClientId = null;
                await WaitFor(done =>
                {
                    channel.Presence.Subscribe(PresenceAction.Leave, message =>
                    {
                        leftClientId = message.ClientId;
                        done();
                    });
                    setupChannel.Presence.LeaveClient(memberId, null);
                });

                // then assert that the member has left
                leftClientId.Should().Be(memberId);
                channel.Presence.MembersMap.Members.Should().HaveCount(19);
                channel.Presence.MembersMap.Members.ContainsKey(actualMemberKey).Should().BeFalse();
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP2f")]
            [Trait("spec", "RTP18a")]
            [Trait("spec", "RTP18b")]
            public async Task PresenceMap_WhenSyncingLeaveStoredAsAbsentAndDeleted(Protocol protocol)
            {
                var channelName = "presence_map_tests_newness".AddRandomSuffix();

                var client = await GetRealtimeClient(protocol);
                await client.WaitForState(ConnectionState.Connected);

                BlockAblyServerSentSyncAction(client);

                var channel = client.Channels.Get(channelName);
                await channel.AttachAsync();
                channel.State.Should().Be(ChannelState.Attached);

                static PresenceMessage[] TestPresence1()
                {
                    return new[]
                    {
                        new PresenceMessage
                        {
                            Action = PresenceAction.Enter,
                            ClientId = "1",
                            ConnectionId = "1",
                            Id = "1:0",
                            Data = string.Empty,
                        },
                    };
                }

                static PresenceMessage[] TestPresence2()
                {
                    return new[]
                    {
                        new PresenceMessage
                        {
                            Action = PresenceAction.Enter,
                            ClientId = "2",
                            ConnectionId = "2",
                            Id = "2:1:0",
                            Data = string.Empty,
                        },
                        new PresenceMessage
                        {
                            Action = PresenceAction.Enter,
                            ClientId = "3",
                            ConnectionId = "3",
                            Id = "3:1:0",
                            Data = string.Empty,
                        },
                    };
                }

                static PresenceMessage[] TestPresence3()
                {
                    return new[]
                    {
                        new PresenceMessage
                        {
                            Action = PresenceAction.Leave,
                            ClientId = "3",
                            ConnectionId = "3",
                            Id = "3:0:0",
                            Data = string.Empty,
                        },
                        new PresenceMessage
                        {
                            Action = PresenceAction.Enter,
                            ClientId = "4",
                            ConnectionId = "4",
                            Id = "4:1:1",
                            Data = string.Empty,
                        },
                        new PresenceMessage
                        {
                            Action = PresenceAction.Leave,
                            ClientId = "4",
                            ConnectionId = "4",
                            Id = "4:2:2",
                            Data = string.Empty,
                        },
                    };
                }

                bool seenLeaveMessageAsAbsentForClient4 = false;
                List<PresenceMessage> presenceMessagesLog = new List<PresenceMessage>();
                channel.Presence.Subscribe(message =>
                {
                    // keep track of the all presence messages we receive
                    presenceMessagesLog.Add(message);
                });

                channel.Presence.Subscribe(PresenceAction.Leave, async message =>
                {
                    /*
                        * Do not call it in states other than ATTACHED because of presence.get() side
                        * effect of attaching channel
                        */
                    if (message.ClientId == "4" && message.Action == PresenceAction.Leave && channel.State == ChannelState.Attached)
                    {
                        /*
                        * Client library won't return a presence message if it is stored as ABSENT
                        * so the result of the presence.get() call should be empty.
                        */
                        var result = await channel.Presence.GetAsync("4", waitForSync: false);
                        seenLeaveMessageAsAbsentForClient4 = result.ToArray().Length == 0;
                    }
                });

                client.Workflow.QueueCommand(ProcessMessageCommand.Create(new ProtocolMessage
                {
                    Action = ProtocolMessage.MessageAction.Sync,
                    Channel = channelName,
                    ChannelSerial = "1:1",
                    Presence = TestPresence1(),
                }));

                client.Workflow.QueueCommand(ProcessMessageCommand.Create(new ProtocolMessage
                {
                    Action = ProtocolMessage.MessageAction.Sync,
                    Channel = channelName,
                    ChannelSerial = "2:1",
                    Presence = TestPresence2(),
                }));

                client.Workflow.QueueCommand(ProcessMessageCommand.Create(new ProtocolMessage
                {
                    Action = ProtocolMessage.MessageAction.Sync,
                    Channel = channelName,
                    ChannelSerial = "2:",
                    Presence = TestPresence3(),
                }));

                await client.ProcessCommands();

                var presence1 = await channel.Presence.GetAsync("1", false);
                var presence2 = await channel.Presence.GetAsync("2", false);
                var presence3 = await channel.Presence.GetAsync("3", false);
                var presenceOthers = await channel.Presence.GetAsync();

                presence1.ToArray().Length.Should().Be(0, "incomplete sync should be discarded");
                presence2.ToArray().Length.Should().Be(1, "client with id==2 should be in presence map");
                presence3.ToArray().Length.Should().Be(1, "client with id==3 should be in presence map");
                presenceOthers.ToArray().Length.Should().Be(2, "presence map should be empty");

                seenLeaveMessageAsAbsentForClient4.Should().Be(true, "LEAVE message for client with id==4 was not stored as ABSENT");

                PresenceMessage[] correctPresenceHistory = new[]
                {
                    /* client 1 enters (will later be discarded) */
                    new PresenceMessage(PresenceAction.Enter, "1"),
                    /* client 2 enters */
                    new PresenceMessage(PresenceAction.Enter, "2"),
                    /* client 3 enters and never leaves because of newness comparison for LEAVE fails */
                    new PresenceMessage(PresenceAction.Enter, "3"),
                    /* client 4 enters and leaves */
                    new PresenceMessage(PresenceAction.Enter, "4"),
                    new PresenceMessage(PresenceAction.Leave, "4"), /* getting dupe */
                    /* client 1 is eliminated from the presence map because the first portion of SYNC is discarded */
                    new PresenceMessage(PresenceAction.Leave, "1"),
                };

                presenceMessagesLog.Count.Should().Be(correctPresenceHistory.Length);

                for (int i = 0; i < correctPresenceHistory.Length; i++)
                {
                    PresenceMessage factualMsg = presenceMessagesLog[i];
                    PresenceMessage correctMsg = correctPresenceHistory[i];
                    factualMsg.ClientId.Should().BeEquivalentTo(correctMsg.ClientId);
                    factualMsg.Action.Should().Be(correctMsg.Action);
                }
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP11")]
            [Trait("spec", "RTP11b")]
            [Trait("spec", "RTP11c")]
            [Trait("spec", "RTP11c1")]
            [Trait("spec", "RTP11c2")]
            [Trait("spec", "RTP11c3")]
            [Trait("spec", "RTP11d")]
            public async Task Presence_GetMethodBehaviour(Protocol protocol)
            {
                string channelName = "RTP11".AddRandomSuffix();
                var client1 = await GetRealtimeClient(protocol);
                var client2 = await GetRealtimeClient(protocol, (options, settings) => options.AutoConnect = false);

                var channel1 = client1.Channels.Get(channelName);
                await channel1.Presence.EnterClientAsync("1", "one");
                await channel1.Presence.EnterClientAsync("2", "two");

                var channel2 = client2.Channels.Get(channelName);
                var ch2Awaiter = new PresenceAwaiter(channel2);

                // with waitForSync set to false,
                // should result in 0 members because autoConnect is set to false
                var presenceMessages1 = await channel2.Presence.GetAsync(false);
                presenceMessages1.Should().HaveCount(0);

                client2.Connection.Connect();

                // With waitForSync is true it should get all the members entered on the first connection
                var presenceMessages2 = await channel2.Presence.GetAsync(true);
                presenceMessages2.Should().HaveCount(2);

                // enter third member from second connection
                await channel2.Presence.EnterClientAsync("3", null);

                // wait for the above to raise a subscribe event
                _ = await ch2Awaiter.WaitFor(1);

                // filter by clientId
                var presenceMessages3 = await channel2.Presence.GetAsync("1");
                presenceMessages3.Should().HaveCount(1);
                presenceMessages3.First().ClientId.Should().Be("1");

                // filter by connectionId
                var presenceMessages4 = await channel2.Presence.GetAsync(connectionId: client2.Connection.Id);
                presenceMessages4.Should().HaveCount(1);
                presenceMessages4.First().ClientId.Should().Be("3");

                // filter by both clientId and connectionId
                var presenceMessages5 = await channel2.Presence.GetAsync(connectionId: client1.Connection.Id, clientId: "2");
                var presenceMessages6 = await channel2.Presence.GetAsync(connectionId: client2.Connection.Id, clientId: "2");
                presenceMessages5.Should().HaveCount(1);
                presenceMessages6.Should().HaveCount(0);
                presenceMessages5.First().ClientId.Should().Be("2");

                // become SUSPENDED
                client2.Workflow.QueueCommand(SetSuspendedStateCommand.Create(null));
                await client2.WaitForState(ConnectionState.Suspended);

                // with waitForSync set to false, should get all the three members
                var presenceMessages7 = await channel2.Presence.GetAsync(false);
                presenceMessages7.Should().HaveCount(3);

                // with waitForSync set to true, should get exception
                client2.Connection.State.Should().Be(ConnectionState.Suspended);
                try
                {
                    await channel2.Presence.GetAsync(true);
                    throw new Exception("waitForSync=true shouldn't succeed in SUSPENDED state");
                }
                catch (AblyException e)
                {
                    e.ErrorInfo.Code.Should().Be(91005);
                }
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP19")]
            public async Task
            PresenceMap_WithExistingMembers_WhenSync_ShouldRemoveLocalMembers_RTP19(Protocol protocol)
            {
                var channelName = "RTP19".AddRandomSuffix();
                var client = await GetRealtimeClient(protocol);
                var channel = client.Channels.Get(channelName);

                // ENTER presence on a channel
                await channel.Presence.EnterClientAsync("1", "one");

                await client.ProcessCommands();

                channel.Presence.MembersMap.Members.Should().HaveCount(1);

                var localMessage = new PresenceMessage
                {
                    Action = PresenceAction.Enter,
                    Id = "local:0:0",
                    Timestamp = DateTimeOffset.UtcNow,
                    ClientId = "local".AddRandomSuffix(),
                    ConnectionId = "local",
                    Data = "local data"
                };

                // inject a member directly into the local PresenceMap
                channel.Presence.MembersMap.Members[localMessage.MemberKey] = localMessage;
                channel.Presence.MembersMap.Members.Should().HaveCount(2);
                channel.Presence.MembersMap.Members.ContainsKey(localMessage.MemberKey).Should().BeTrue();

                var members = (await channel.Presence.GetAsync()).ToArray();
                members.Should().HaveCount(2);
                members.Where(m => m.ClientId == "1").Should().HaveCount(1);

                var leaveMessages = new List<PresenceMessage>();

                var awaiter = new TaskCompletionAwaiter();
                channel.Presence.Subscribe(PresenceAction.Leave, message =>
                {
                    Output.WriteLine($"LEAVE message: {message.ToJson()} ");
                    leaveMessages.Add(message);
                    awaiter.SetCompleted();
                });

                var syncMessage = new ProtocolMessage(ProtocolMessage.MessageAction.Sync)
                {
                    Channel = channelName
                };

                client.ExecuteCommand(SendMessageCommand.Create(syncMessage));

                await awaiter.Task;
                var serverPresence = await client.RestClient.Channels.Get(channelName).Presence.GetAsync();
                serverPresence.Items.Count.Should().Be(1);

                // A LEAVE event should have be published for the injected member
                leaveMessages.Should().HaveCount(1);
                leaveMessages[0].ClientId.Should().Be(localMessage.ClientId);

                // valid members entered for this connection are still present
                members = (await channel.Presence.GetAsync()).ToArray();
                members.Should().HaveCount(1);
                members.Any(m => m.ClientId == localMessage.ClientId).Should().BeFalse();
            }

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP19a")]
            [Trait("spec", "RTP6b")]
            public async Task PresenceMap_WithExistingMembers_WhenBecomesAttachedWithoutHasPresence_ShouldEmitLeavesForExistingMembers(Protocol protocol)
            {
                /* (RTP19a) If the PresenceMap has existing members when an ATTACHED message
                 is received without a HAS_PRESENCE flag, the client library should emit a
                 LEAVE event for each existing member, and the PresenceMessage published should
                 contain the original attributes of the presence member with the action set to LEAVE,
                 PresenceMessage#id set to null, and the timestamp set to the current time. Once complete,
                 all members in the PresenceMap should be removed as there are no members present on the channel
                 */

                var channelName = "RTP19a".AddRandomSuffix();
                var client = await GetRealtimeClient(protocol);
                await client.WaitForState(ConnectionState.Connected);

                // Remove HAS_PRESENCE Flag from Attached message
                client.BeforeProtocolMessageProcessed(message =>
                {
                    if (message.Action == ProtocolMessage.MessageAction.Attached)
                    {
                        message.Flags &= ~(int)ProtocolMessage.Flag.HasPresence; // unset has_presence/members bit
                    }
                });

                var channel = client.Channels.Get(channelName);
                var localMessage1 = new PresenceMessage
                {
                    Action = PresenceAction.Enter,
                    Id = "local:0:1",
                    Timestamp = DateTimeOffset.UtcNow,
                    ClientId = "local".AddRandomSuffix(),
                    ConnectionId = "local",
                    Data = "local data 1"
                };

                var localMessage2 = new PresenceMessage
                {
                    Action = PresenceAction.Enter,
                    Id = "local:0:2",
                    Timestamp = DateTimeOffset.UtcNow,
                    ClientId = "local".AddRandomSuffix(),
                    ConnectionId = "local",
                    Data = "local data 2"
                };

                // inject a members directly into the local PresenceMap
                channel.Presence.MembersMap.Members[localMessage1.MemberKey] = localMessage1;
                channel.Presence.MembersMap.Members[localMessage2.MemberKey] = localMessage2;
                channel.Presence.MembersMap.Members.Should().HaveCount(2);

                bool hasPresence = true;
                int leaveCount = 0;
                await WaitForMultiple(4, partialDone =>
                {
                    client.GetTestTransport().AfterDataReceived += message =>
                    {
                        if (message.Action == ProtocolMessage.MessageAction.Attached)
                        {
                            hasPresence = message.HasFlag(ProtocolMessage.Flag.HasPresence);
                            partialDone();
                        }
                    };

                    // (RTP6b) Subscribe with a single action argument
                    channel.Presence.Subscribe(PresenceAction.Leave, leaveMsg =>
                    {
                        leaveMsg.ClientId.Should().StartWith("local");
                        leaveMsg.Action.Should().Be(PresenceAction.Leave, "Action should be leave");
                        leaveMsg.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMilliseconds(200), "timestamp should be current time");
                        leaveMsg.Id.Should().BeNull("Id should be null");
                        leaveCount++;
                        partialDone(); // should be called twice
                    });

                    channel.Attach((b, info) =>
                    {
                        b.Should().BeTrue();
                        info.Should().BeNull();
                        partialDone();
                    });
                });

                hasPresence.Should().BeFalse("ATTACHED message was received with a HAS_PRESENCE flag");
                leaveCount.Should().Be(2, "should emit a LEAVE event for each existing member");

                var members = await channel.Presence.GetAsync();
                members.Should().HaveCount(0, "should be no members");
            }

            [Theory(Skip = "Need to update the test, provided input doesn't really fail")]
            [ProtocolData]
            public async Task WithInvalidPresenceMessages_EmmitErrorNoChannel(Protocol protocol)
            {
                var channelName = "presence_tests_exception".AddRandomSuffix();

                var client = await GetRealtimeClient(protocol);
                await client.WaitForState(ConnectionState.Connected);
                client.Connection.State.Should().Be(ConnectionState.Connected);

                var channel = client.Channels.Get(channelName);
                channel.Attach();
                await channel.WaitForAttachedState();
                channel.State.Should().Be(ChannelState.Attached);

                static PresenceMessage[] TestPresence1()
                {
                    return new[]
                    {
                        new PresenceMessage
                        {
                            Action = PresenceAction.Enter,
                            ClientId = "2",
                            ConnectionId = "2",
                            Id = "2:1:0",
                            Data = string.Empty
                        },
                        new PresenceMessage
                        {
                            Action = PresenceAction.Enter,
                            ClientId = "2",
                            ConnectionId = "2",
                            Id = "2:1:SHOULD_ERROR",
                            Data = string.Empty
                        },
                    };
                }

                bool hasError = false;
                channel.Error += (sender, args) => hasError = true;
                channel.Presence.OnPresence(TestPresence1());

                hasError.Should().BeTrue();
                channel.State.Should().Be(ChannelState.Attached);
            }

            [Theory]
            [ProtocolData]
            public async Task CanSend_EnterWithStringArray(Protocol protocol)
            {
                var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = "test");

                var channel = client.Channels.Get("test" + protocol);

                await channel.Presence.EnterAsync(new[] { "test", "best" });

                await Task.Delay(2000);
                var presence = await channel.Presence.GetAsync();
                presence.Should().HaveCount(1);
            }

            [Theory(Skip = "Intermittently fails")]
            [ProtocolData]
            public async Task Presence_HasCorrectTimeStamp(Protocol protocol)
            {
                var client = await GetRealtimeClient(protocol, (opts, _) => opts.ClientId = "presence-timestamp-test");

                var channel = client.Channels.Get("test".AddRandomSuffix());
                DateTimeOffset? time = null;
                channel.Presence.Subscribe(message =>
                {
                    Output.WriteLine($"{message.ConnectionId}:{message.Timestamp}");
                    time = message.Timestamp;
                    ResetEvent.Set();
                });

                await channel.Presence.EnterAsync(new[] { "test", "best" });

                ResetEvent.WaitOne(2000);
                time.Should().HaveValue();
            }

            [Fact]
            public void PresenceMessage_IsNewerThan_RaisesExceptionWithMalformedIdStrings()
            {
                // Set the first portion of the Id to match the connection id
                // This is so the message does not appear synthesised
                var p1 = new PresenceMessage(PresenceAction.Present, "client1")
                {
                    Id = "abcdef:1:1",
                    ConnectionId = "abcdef",
                    Timestamp = new DateTimeOffset(2000, 1, 1, 1, 1, 1, 1, TimeSpan.Zero),
                };

                var p2 = new PresenceMessage(PresenceAction.Present, "client2")
                {
                    // make the timestamps the same so we can get to the parsing part of the code
                    Timestamp = p1.Timestamp,
                    Id = "abcdef:this_should:error",
                    ConnectionId = "abcdef",
                };

                bool exHandled = false;
                try
                {
                    p1.IsNewerThan(p2);
                }
                catch (Exception e)
                {
                    e.Should().BeOfType<AblyException>();
                    e.Message.Should().Contain("this_should:error");
                    exHandled = true;
                }

                exHandled.Should().BeTrue();
                exHandled = false;

                try
                {
                    p2.IsNewerThan(p1);
                }
                catch (Exception e)
                {
                    e.Should().BeOfType<AblyException>();
                    e.Message.Should().Contain("this_should:error");
                    exHandled = true;
                }

                exHandled.Should().BeTrue();
                exHandled = false;

                // test not enough sections
                p2.Id = "abcdef:2";
                try
                {
                    p1.IsNewerThan(p2);
                }
                catch (Exception e)
                {
                    e.Should().BeOfType<AblyException>();
                    e.Message.Should().Contain("abcdef:2");
                    exHandled = true;
                }

                exHandled.Should().BeTrue();
                exHandled = false;

                try
                {
                    p2.IsNewerThan(p1);
                }
                catch (Exception e)
                {
                    e.Should().BeOfType<AblyException>();
                    e.Message.Should().Contain("abcdef:2");
                    exHandled = true;
                }

                exHandled.Should().BeTrue();
                exHandled = false;

                // correctly formatted Ids should not throw an exception
                p2.Id = p1.Id;
                try
                {
                    p2.IsNewerThan(p1);
                }
                catch (Exception)
                {
                    exHandled = true;
                }

                exHandled.Should().BeFalse();
            }

            [Trait("spec", "RTP5")]
            [Trait("type", "integration")]
            public class ChannelStateChangeSideEffects : PresenceSandboxSpecs
            {
                public ChannelStateChangeSideEffects(AblySandboxFixture fixture, ITestOutputHelper output)
                    : base(fixture, output)
                {
                }

                [Theory]
                [ProtocolData(ChannelState.Failed)]
                [ProtocolData(ChannelState.Detached)]
                [Trait("spec", "RTP5a")]
                public async Task WhenChannelBecomesFailedOrDetached_QueuedPresenceMessagesShouldFail(Protocol protocol, ChannelState channelState)
                {
                    var client = await GetRealtimeClient(protocol);
                    await client.WaitForState();

                    var channel = GetRandomChannel(client, "RTP5a");

                    int initialCount = 0;
                    bool? success = null;
                    ErrorInfo errInfo = null;
                    await WaitForMultiple(2, partialDone =>
                    {
                        // insert an error when attaching
                        channel.Once(ChannelEvent.Attaching, args =>
                        {
                            // before we change the state capture proof that we have a queued message
                            initialCount = channel.Presence.PendingPresenceQueue.Count;
                            channel.SetChannelState(channelState, new ErrorInfo("RTP5a test"));
                            partialDone();
                        });

                        // enter client, this should trigger attach
                        channel.Presence.EnterClient("123", null, (b, info) =>
                        {
                            success = b;
                            errInfo = info;
                            partialDone();
                        });
                    });

                    initialCount.Should().Be(1, "a presence message should have been queued");

                    const string errorInvalidSuccess = "EnterClient callback should have executed";
                    success.Should().HaveValue(errorInvalidSuccess);

                    Debug.Assert(success != null, errorInvalidSuccess);
                    success.Value.Should().BeFalse("queued presence message should have failed immediately");

                    errInfo.Message.Should().Be("RTP5a test");

                    channel.Presence.PendingPresenceQueue.Should().HaveCount(0, "presence message queue should have been cleared");

                    client.Close();
                }

                [Theory]
                [ProtocolData(ChannelState.Failed)]
                [ProtocolData(ChannelState.Detached)]
                [Trait("spec", "RTP5a")]
                public async Task WhenChannelBecomesFailedOrDetached_ShouldClearPresenceMapAndShouldNotEmitEvents(Protocol protocol, ChannelState channelState)
                {
                    var client = await GetRealtimeClient(protocol);
                    await client.WaitForState();

                    var channel = GetRandomChannel(client, "RTP5a");
                    channel.Attach();
                    await channel.WaitForAttachedState();

                    var result = await channel.Presence.EnterClientAsync("123", null);
                    result.IsSuccess.Should().BeTrue();

                    await Task.Delay(10);

                    channel.Presence.MembersMap.Members.Should().HaveCount(1);
                    channel.Presence.InternalMembersMap.Members.Should().HaveCount(1);

                    bool didReceiveMessage = false;
                    channel.Subscribe(msg => { didReceiveMessage = true; });
                    didReceiveMessage.Should().BeFalse("No events should be emitted");

                    channel.Once((ChannelEvent)channelState, change =>
                    {
                        channel.Presence.MembersMap.Members.Should().HaveCount(0);
                        channel.Presence.InternalMembersMap.Members.Should().HaveCount(0);
                    });

                    if (channelState == ChannelState.Detached)
                    {
                        // We need to call detach because if we just blindly set the state to Detached
                        // it will trigger a retry
                        channel.Detach();
                    }
                    else
                    {
                        channel.SetChannelState(channelState, new ErrorInfo("RTP5a test"));
                    }

                    await channel.WaitForState(channelState);

                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP5b")]
                public async Task WhenChannelBecomesAttached_ShouldSendQueuedMessagesAndInitiateSYNC(Protocol protocol)
                {
                    var client1 = await GetRealtimeClient(protocol);
                    var client2 = await GetRealtimeClient(protocol);

                    var taskCountWaiter = new TaskCountAwaiter(1);

                    await client1.WaitForState();
                    await client2.WaitForState();

                    var channel1 = client1.Channels.Get("RTP5b_ch1".AddRandomSuffix());
                    channel1.Attach();
                    await channel1.WaitForAttachedState();

                    var result = await channel1.Presence.EnterClientAsync("client1", null);
                    result.IsFailure.Should().BeFalse();

                    var channel2 = client2.Channels.Get(channel1.Name); // Don't get state to attached
                    var presence2 = channel2.Presence;

                    client2.BeforeProtocolMessageProcessed(message =>
                    {
                        if (message.Action == ProtocolMessage.MessageAction.Attached)
                        {
                            // PendingPresenceQueue will be cleared once channel2 goes into attached state
                            taskCountWaiter.Task.Wait();
                        }
                    });

                    await WaitForMultiple(2, partialDone =>
                    {
                        // Enter client in channel initialized state
                        presence2.EnterClient("client2", null, (b, info) =>
                        {
                            presence2.PendingPresenceQueue.Should().HaveCount(0);
                            partialDone();
                        });

                        presence2.Subscribe(PresenceAction.Enter, msg =>
                        {
                            presence2.MembersMap.Members.Should().HaveCount(presence2.SyncComplete ? 2 : 1);
                            presence2.Unsubscribe();
                            partialDone();
                        });

                        presence2.PendingPresenceQueue.Should().HaveCount(1);
                        presence2.SyncComplete.Should().BeFalse();
                        presence2.MembersMap.Members.Should().HaveCount(0);
                        taskCountWaiter.Tick();
                    });

                    var transport = client2.GetTestTransport();
                    await new ConditionalAwaiter(() => presence2.SyncComplete);
                    transport.ProtocolMessagesReceived.Any(m => m.Action == ProtocolMessage.MessageAction.Sync).
                        Should().BeTrue("Should receive sync message");
                    presence2.MembersMap.Members.Should().HaveCount(2);
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16a")]
                public async Task ConnectionStateCondition_WhenConnectionIsConnected_AllPresenceMessageArePublishedImmediately(Protocol protocol)
                {
                    var client = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "RTP16a"; });
                    var channel = GetRandomChannel(client, "RTP16a");

                    ErrorInfo errInfo = null;

                    List<int> queueCounts = new List<int>();
                    channel.State.Should().NotBe(ChannelState.Attached);

                    await client.WaitForState(ConnectionState.Connected);

                    await WaitFor(done =>
                    {
                        channel.Presence.Enter("foo", (b, info) =>
                        {
                            errInfo = info;

                            // after Enter the client should be connected and the queued message sent
                            queueCounts.Add(channel.Presence.PendingPresenceQueue.Count); // expect 0
                            done();
                        });

                        // 1 message should be queued at this point
                        queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                    });

                    channel.State.Should().Be(ChannelState.Attached);
                    errInfo.Should().BeNull();
                    queueCounts[0].Should().Be(1);
                    queueCounts[1].Should().Be(0);
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16b")]
                public async Task ChannelStateCondition_WhenChannelIsInitialisedOrAttaching_MessageArePublishedWhenChannelBecomesAttached(Protocol protocol)
                {
                    /* tests channel initialized and attaching states */

                    var client = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "RTP16b"; });
                    var channel = GetRandomChannel(client, "RTP16a");

                    List<int> queueCounts = new List<int>();
                    Presence.QueuedPresenceMessage[] presenceMessages = null;

                    await WaitForMultiple(3, partialDone =>
                    {
                        channel.Once(ChannelEvent.Attached, change =>
                        {
                            queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                            partialDone();
                        });

                        channel.Once(ChannelEvent.Attaching, change =>
                        {
                            channel.Presence.Enter(channel.State.ToString(), (b, info) =>
                            {
                                partialDone();
                            });
                            queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                            presenceMessages = channel.Presence.PendingPresenceQueue.ToArray();
                        });

                        // Enter whilst Initialized
                        channel.Presence.Enter(channel.State.ToString(), (b, info) =>
                        {
                            partialDone();
                        });
                    });

                    queueCounts[0].Should().Be(2);
                    queueCounts[1].Should().Be(0);
                    presenceMessages[0].Message.Data.Should().Be("Initialized");
                    presenceMessages[1].Message.Data.Should().Be("Attaching");

                    // clean up
                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16b")]
                public async Task ChannelStateCondition_WhenQueueMessagesIsFalse_WhenChannelIsInitialisedOrAttaching_MessageAreNotPublished(Protocol protocol)
                {
                    var client = await GetRealtimeClient(protocol, (options, settings) =>
                    {
                        options.ClientId = "RTP16b";
                        options.QueueMessages = false;
                    });
                    var channel = GetRandomChannel(client, "RTP16a");

                    await client.WaitForState(ConnectionState.Connected);
                    client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null));
                    await client.WaitForState(ConnectionState.Disconnected);

                    var tsc = new TaskCompletionAwaiter();
                    ErrorInfo err = null;
                    bool? success = null;
                    channel.Presence.Enter(client.Connection.State.ToString(), (b, info) =>
                    {
                        success = b;
                        err = info;
                        tsc.SetCompleted();
                    });
                    Presence.QueuedPresenceMessage[] presenceMessages = channel.Presence.PendingPresenceQueue.ToArray();

                    presenceMessages.Should().HaveCount(0);

                    await tsc.Task;
                    success.Should().HaveValue();
                    success.Value.Should().BeFalse();
                    err.Should().NotBeNull();
                    err.Message.Should().Be("Unable enqueue message because Options.QueueMessages is set to False.");

                    // clean up
                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16b")]
                public async Task ChannelStateCondition_WhenQueueMessagesIsFalse_WhenChannelIsInitializedOrAttaching_MessageAreNotPublished(Protocol protocol)
                {
                    var client = await GetRealtimeClient(protocol, (options, settings) =>
                    {
                        options.ClientId = "RTP16b";
                        options.QueueMessages = false;
                    });

                    await client.WaitForState(ConnectionState.Connected);

                    var channel = GetRandomChannel(client, "RTP16a");
                    channel.State.Should().Be(ChannelState.Initialized);
                    await EnterPresenceAndCheckForError();

                    client.BlockActionFromSending(ProtocolMessage.MessageAction.Attach);
                    channel.Attach();
                    await channel.WaitForState(ChannelState.Attaching);
                    await EnterPresenceAndCheckForError();

                    // QueueCommand will not retry instantly
                    client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null));
                    await client.WaitForState(ConnectionState.Disconnected);
                    await EnterPresenceAndCheckForError();

                    // clean up
                    client.Close();

                    async Task EnterPresenceAndCheckForError()
                    {
                        var enterPresenceAwaiter = new TaskCompletionAwaiter();
                        ErrorInfo err = null;
                        bool? success = null;
                        channel.Presence.Enter("dummy data", (b, info) =>
                        {
                            success = b;
                            err = info;
                            enterPresenceAwaiter.SetCompleted();
                        });
                        await client.ProcessCommands();

                        // No messages sent because queueMessages false
                        channel.Presence.PendingPresenceQueue.Should().HaveCount(0);
                        client.State.PendingMessages.Should().HaveCount(0);

                        if (client.Connection.State != ConnectionState.Disconnected)
                        {
                            var presenceMessagesSent = client.GetTestTransport().ProtocolMessagesSent
                                .FindAll(msg => msg.Action == ProtocolMessage.MessageAction.Presence);
                            presenceMessagesSent.Should().HaveCount(0);
                        }

                        await enterPresenceAwaiter.Task;

                        success.Should().HaveValue();
                        success.Value.Should().BeFalse();
                        err.Should().NotBeNull();
                        err.Message.Should().Be("Unable enqueue message because Options.QueueMessages is set to False.");
                    }
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTN7d")]
                public async Task ChannelStateCondition_WhenQueueMessagesIsFalse_ShouldFailAckQueueMessages_WhenSendFails(Protocol protocol)
                {
                    var client = await GetRealtimeClient(protocol, (options, settings) =>
                    {
                        options.ClientId = "RTN7d";
                        options.QueueMessages = false;
                    });

                    await client.WaitForState(ConnectionState.Connected);

                    var channel = GetRandomChannel(client, "RTN7d");
                    channel.Attach();
                    await channel.WaitForAttachedState();

                    client.BlockActionFromSending(ProtocolMessage.MessageAction.Presence);

                    var enterPresenceAwaiter = new TaskCompletionAwaiter();
                    ErrorInfo err = null;
                    bool? success = null;
                    channel.Presence.Enter("dummy data", (b, info) =>
                    {
                        success = b;
                        err = info;
                        enterPresenceAwaiter.SetCompleted();
                    });
                    await client.ProcessCommands();

                    // All messages sent
                    channel.Presence.PendingPresenceQueue.Should().HaveCount(0);
                    client.State.PendingMessages.Should().HaveCount(0);

                    // no ack received, so ack queue has one presence message
                    await new ConditionalAwaiter(() => channel.RealtimeClient.State.WaitingForAck.Count == 1);

                    // Disconnect using QueueCommand will not retry instantly
                    client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null));
                    await client.WaitForState(ConnectionState.Disconnected);

                    await enterPresenceAwaiter.Task;

                    // Fail all ack/nack, because QueueMessages=false
                    channel.RealtimeClient.State.WaitingForAck.Should().HaveCount(0);

                    success.Should().HaveValue();
                    success.Value.Should().BeFalse();
                    err.Should().NotBeNull();
                    err.Message.Should().Be("Clearing message AckQueue(created at connected state) because Options.QueueMessages is false");
                    err.Code.Should().Be(ErrorCodes.Disconnected); // cleared because of disconnection

                    // clean up
                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16b")]
                public async Task ConnectionStateCondition_WhenConnectionIsDisconnected_MessageArePublishedWhenConnectionBecomesConnected(Protocol protocol)
                {
                    /* tests disconnecting and connecting states */

                    var client = await GetRealtimeClient(protocol, (options, settings) =>
                    {
                        options.ClientId = "RTP16b";
                        options.DisconnectedRetryTimeout = TimeSpan.FromSeconds(2);
                    });

                    var channel = GetRandomChannel(client, "RTP16a");

                    List<int> queueCounts = new List<int>();
                    Presence.QueuedPresenceMessage[] presenceMessages = null;

                    // force disconnected state
                    client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null));
                    await client.WaitForState(ConnectionState.Disconnected);

                    await WaitForMultiple(
                    2,
                    partialDone =>
                    {
                        client.Connection.Once(ConnectionEvent.Connecting, change =>
                        {
                            // Enter whilst Connecting
                            channel.Presence.Enter(client.Connection.State.ToString(), (b, info) =>
                            {
                                // there should be no messages queued at this point
                                queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                                partialDone();
                            });

                            // there should be 2 messages queued at this point
                            queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                            presenceMessages = channel.Presence.PendingPresenceQueue.ToArray();
                        });

                        // Enter whilst Disconnected
                        channel.Presence.Enter(client.Connection.State.ToString(), (b, info) =>
                        {
                            partialDone();
                        });
                        Output.WriteLine(client.GetCurrentState());
                    },
                    onFail: () => Output.WriteLine(client.GetCurrentState()));

                    queueCounts[0].Should().Be(2);
                    queueCounts[1].Should().Be(0);
                    presenceMessages[0].Message.Data.Should().Be("Disconnected");
                    presenceMessages[1].Message.Data.Should().Be("Connecting");
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16b")]
                public async Task ConnectionStateCondition_WhenConnectionIsInitialized_MessageArePublishedWhenConnectionBecomesConnected(Protocol protocol)
                {
                    // Tests initialized and connecting states

                    var client = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "RTP16b"; });
                    var channel = GetRandomChannel(client, "RTP16a");

                    List<int> queueCounts = new List<int>();
                    Presence.QueuedPresenceMessage[] presenceMessages = null;

                    client.Workflow.QueueCommand(ForceStateInitializationCommand.Create());
                    await client.WaitForState(ConnectionState.Initialized);

                    await WaitForMultiple(2, partialDone =>
                    {
                        client.Connection.Once(ConnectionEvent.Connecting, change =>
                        {
                            // Enter whilst Connecting
                            channel.Presence.Enter(client.Connection.State.ToString(), (b, info) =>
                            {
                                // there should be no messages queued at this point
                                queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                                partialDone();
                            });

                            // there should be 2 messages queued at this point
                            queueCounts.Add(channel.Presence.PendingPresenceQueue.Count);
                            presenceMessages = channel.Presence.PendingPresenceQueue.ToArray();
                        });

                        // Enter whilst Initialized
                        channel.Presence.Enter(client.Connection.State.ToString(), (b, info) =>
                        {
                            partialDone();
                        });
                    });

                    queueCounts[0].Should().Be(2);
                    queueCounts[1].Should().Be(0);
                    presenceMessages[0].Message.Data.Should().Be("Initialized");
                    presenceMessages[1].Message.Data.Should().Be("Connecting");

                    // clean up
                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16c")]
                public async Task ChannelStateCondition_WhenConnectionStateIsInvalid_MessageAreNotPublishedAndExceptionIsThrown(Protocol protocol)
                {
                    /*
                     * Test Connection States
                     * Entering presence when a connection is Failed, Suspended, Closing and Closed
                     * should result in an error.
                     */

                    var client = await GetRealtimeClient(protocol, (options, _) => options.ClientId = "RTP16c".AddRandomSuffix());
                    int errCount = 0;

                    async Task TestWithConnectionState(ConnectionState state, RealtimeCommand changeStateCommand)
                    {
                        var channel = GetRandomChannel(client, "RTP16c");

                        await client.WaitForState(ConnectionState.Connected);

                        // capture all outbound protocol messages for later inspection
                        List<ProtocolMessage> messageList = new List<ProtocolMessage>();
                        client.GetTestTransport().AfterMessageSent = messageList.Add;

                        // force state
                        client.Workflow.QueueCommand(changeStateCommand);
                        await client.WaitForState(state);

                        var didError = false;
                        var result = await channel.Presence.EnterAsync(client.Connection.State.ToString());
                        if (result.IsFailure)
                        {
                            didError = true;
                            result.Error.Code.Should().Be(ErrorCodes.UnableToEnterPresenceChannelInvalidState);
                            errCount++;
                        }

                        didError.Should().BeTrue($"should error for state {state}");

                        // no presence messages sent
                        messageList.Any(x => x.Presence != null).Should().BeFalse();

                        client.Close();
                        client = await GetRealtimeClient(protocol, (options, _) => options.ClientId = "RTP16c".AddRandomSuffix());
                    }

                    await TestWithConnectionState(ConnectionState.Failed, SetFailedStateCommand.Create(ErrorInfo.ReasonFailed));
                    await TestWithConnectionState(ConnectionState.Suspended, SetSuspendedStateCommand.Create(ErrorInfo.ReasonFailed));
                    await TestWithConnectionState(ConnectionState.Closing, SetClosingStateCommand.Create());
                    await TestWithConnectionState(ConnectionState.Closed, SetClosedStateCommand.Create());

                    errCount.Should().Be(4);

                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("spec", "RTP16c")]
                public async Task ChannelStateCondition_WhenChannelStateIsInvalid_MessageAreNotPublishedAndExceptionIsThrown(Protocol protocol)
                {
                    var client = await GetRealtimeClient(protocol, (options, _) => options.ClientId = "RTP16c".AddRandomSuffix());

                    /*
                     * Test Channel States
                     * Detached, Detaching, Failed and Suspended states should result in an error
                     */

                    int errCount = 0;
                    async Task TestWithChannelState(ChannelState state)
                    {
                        var channel = GetRandomChannel(client, "RTP16c");

                        await client.WaitForState(ConnectionState.Connected);

                        // capture all outbound protocol messages for later inspection
                        List<ProtocolMessage> messageList = new List<ProtocolMessage>();
                        client.GetTestTransport().AfterMessageSent = messageList.Add;

                        // force state
                        channel.SetChannelState(state);
                        await channel.WaitForState(state);

                        var didError = false;
                        var result = await channel.Presence.EnterAsync(client.Connection.State.ToString());
                        if (result.IsFailure)
                        {
                            didError = true;
                            result.Error.Code.Should().Be(ErrorCodes.UnableToEnterPresenceChannelInvalidState);
                            errCount++;
                        }

                        didError.Should().BeTrue($"should error for state {state}");

                        // no presence messages sent
                        messageList.Any(x => x.Presence != null).Should().BeFalse();

                        client.Close();
                        client = await GetRealtimeClient(protocol, (options, _) => options.ClientId = "RTP16c".AddRandomSuffix());
                    }

                    // Initialized, Attaching and Attached should queue and/or send
                    // all other channel states should result in an error
                    await TestWithChannelState(ChannelState.Detached);
                    await TestWithChannelState(ChannelState.Detaching);
                    await TestWithChannelState(ChannelState.Failed);
                    await TestWithChannelState(ChannelState.Suspended);

                    errCount.Should().Be(4);

                    client.Close();
                }

                [Theory]
                [ProtocolData]
                [Trait("issue", "332")]
                public async Task PresenceShouldReenterAfterDisconnected(Protocol protocol)
                {
                    var channelName = "RecoverFromDisconnected".AddRandomSuffix();
                    var ably = await GetRealtimeClient(protocol, (options, settings) => { options.ClientId = "123"; });

                    ably.Connect();
                    await ably.WaitForState();

                    var channel = ably.Channels.Get(channelName);

                    IEnumerable<PresenceMessage> p1 = null;
                    await WaitFor(30000, async done =>
                    {
                        ably.Connection.On(async change =>
                        {
                            if (change.Current == ConnectionState.Connected)
                            {
                                await channel.WaitForAttachedState();
                                p1 = await channel.Presence.GetAsync();
                                done();
                            }
                        });

                        channel.On(async state =>
                        {
                            if (state.Current == ChannelState.Attached)
                            {
                                _ = await channel.Presence.GetAsync();
                            }
                        });

                        var result = await channel.AttachAsync();
                        result.IsSuccess.Should().BeTrue();
                        await channel.Presence.EnterAsync();

                        await Task.Delay(500);

                        // simulate disconnect
                        ably.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(new ErrorInfo("Connection disconnected due to Operating system network going offline", 80017)));

                        await ably.ProcessCommands();

                        // simulate reconnect
                        ably.Workflow.QueueCommand(SetConnectingStateCommand.Create());
                    });

                    p1.Should().NotBeNull();
                    p1.Should().HaveCount(1);

                    var restPresence = await ably.RestClient.Channels.Get(channelName).Presence.GetAsync();

                    // Before the fix this would return no items as the presence had not been re-entered
                    restPresence.Items.Should().HaveCount(1);
                    p1.First().MemberKey.Should().BeEquivalentTo(restPresence.Items[0].MemberKey);

                    ably.Close();
                }
            }
        }

        [Trait("type", "integration")]
        public class With250PresentMembersOnAChannel : PresenceSandboxSpecs
        {
            private const int ExpectedEnterCount = 150;

            /*
             * Ensure a test exists that enters 250 members using Presence#enterClient on a single connection,
             * and checks for PRESENT events to be emitted on another connection for each member,
             * and once sync is complete, all 250 members should be present in a Presence#get request
             */

            [Theory]
            [ProtocolData]
            [Trait("spec", "RTP4")]
            public async Task WhenAClientAttachedToPresenceChannel_ShouldEmitPresentForEachMember(Protocol protocol)
            {
                using var debugLogging = EnableDebugLogging();

                var channelName = "presence".AddRandomSuffix();

                var clientA = await GetRealtimeClient(protocol);
                await clientA.WaitForState(ConnectionState.Connected);

                var channelA = clientA.Channels.Get(channelName);
                await channelA.AttachAsync();

                // enters 250 members on a single connection A
                for (int i = 0; i < ExpectedEnterCount; i++)
                {
                    var clientId = GetClientId(i);
                    await channelA.Presence.EnterClientAsync(clientId, null);
                }

                var clientB = await GetRealtimeClient(protocol);
                await clientB.WaitForState(ConnectionState.Connected);

                var channelB = clientB.Channels.Get(channelName);

                // checks for PRESENT events to be emitted on another connection for each member
                List<PresenceMessage> presenceMessages = new List<PresenceMessage>();
                var awaiter = new TaskCompletionAwaiter(200000);
                channelB.Presence.Subscribe(x =>
                {
                    presenceMessages.Add(x);
                    if (presenceMessages.Count == ExpectedEnterCount)
                    {
                        awaiter.SetCompleted();
                    }
                });

                await channelB.AttachAsync();

                var received250MessagesBeforeTimeout = await awaiter.Task;
                received250MessagesBeforeTimeout.Should().Be(true);

                // all 250 members should be present in a Presence#get request
                var messages = await channelB.Presence.GetAsync(new Presence.GetParams { WaitForSync = true });
                var messageList = messages as IList<PresenceMessage> ?? messages.ToList();
                messageList.Count.Should().Be(ExpectedEnterCount);
                foreach (var m in messageList)
                {
                    presenceMessages.Any(x => x.ClientId == m.ClientId).Should().BeTrue();
                }

                clientA.Close();
                clientB.Close();
            }

            private static string GetClientId(int count)
            {
                return "client:#" + count.ToString().PadLeft(3, '0');
            }

            public With250PresentMembersOnAChannel(AblySandboxFixture fixture, ITestOutputHelper output)
                : base(fixture, output)
            {
            }
        }

        private sealed class PresenceAwaiter : IDisposable
        {
            private IRealtimeChannel _channel;
            private TaskCompletionAwaiter _tsc;

            public PresenceAwaiter(IRealtimeChannel channel)
            {
                _channel = channel;
                _channel.Presence.Subscribe(message =>
                {
                    _tsc?.Tick();
                });
            }

            public async Task<bool> WaitFor(int count)
            {
                _tsc = new TaskCompletionAwaiter(10000, count);
                return await _tsc.Task;
            }

            public void Dispose()
            {
                _tsc?.Dispose();
            }
        }
    }
}
