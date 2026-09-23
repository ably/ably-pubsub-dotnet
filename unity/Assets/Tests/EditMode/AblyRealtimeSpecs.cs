using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Assets.Tests.AblySandbox;
using Cysharp.Threading.Tasks;
using IO.Ably;
using IO.Ably.Realtime;
using IO.Ably.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Assets.Tests.EditMode
{
    [TestFixture]
    public class AblyRealtimeSpecs
    {
        private AblySandboxFixture _sandboxFixture;

        [OneTimeSetUp]
        public void OneTimeInit()
        {
            _sandboxFixture = new AblySandboxFixture();
        }

        [UnitySetUp]
        public IEnumerator Init()
        {
            AblySandbox = new AblySandbox.AblySandbox(_sandboxFixture);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AblySandbox.Dispose();
            yield return null;
        }

        public AblySandbox.AblySandbox AblySandbox { get; set; }

        static Protocol[] _protocols = { Protocol.Json };

        [UnityTest]
        public IEnumerator TestConnectionStateChange([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, (options, _) => options.AutoConnect = false);
                Assert.AreEqual(ConnectionState.Initialized, realtimeClient.Connection.State);

                // Check for active connection
                var connectionStates = new List<ConnectionState>();
                realtimeClient.Connection.On(change =>
                {
                    connectionStates.Add(change.Current);
                });
                realtimeClient.Connect();
                await realtimeClient.WaitForState(ConnectionState.Connected);
                Assert.AreEqual(ConnectionState.Connecting, connectionStates[0]);
                Assert.AreEqual(ConnectionState.Connected, connectionStates[1]);

                // Check for connection close
                connectionStates.Clear();
                realtimeClient.Close();
                await realtimeClient.WaitForState(ConnectionState.Closed);
                Assert.AreEqual(ConnectionState.Closing, connectionStates[0]);
                Assert.AreEqual(ConnectionState.Closed, connectionStates[1]);
            });
        }

        [UnityTest]
        public IEnumerator TestChannelStateChange([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol);
                await realtimeClient.WaitForState(ConnectionState.Connected);

                var channel = realtimeClient.Channels.Get("TestChannel");
                Assert.AreEqual(ChannelState.Initialized, channel.State);

                var channelStates = new List<ChannelState>();
                channel.On(change =>
                {
                    channelStates.Add(change.Current);
                });

                channel.Attach();
                await channel.WaitForState();
                Assert.AreEqual(ChannelState.Attaching, channelStates[0]);
                Assert.AreEqual(ChannelState.Attached, channelStates[1]);

                channelStates.Clear();
                channel.Detach();
                await channel.WaitForState(ChannelState.Detached);
                Assert.AreEqual(ChannelState.Detaching, channelStates[0]);
                Assert.AreEqual(ChannelState.Detached, channelStates[1]);
            });
        }

        [UnityTest]
        public IEnumerator TestChannelPublishSubscribe([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol);
                await realtimeClient.WaitForState(ConnectionState.Connected);

                var channel = realtimeClient.Channels.Get("TestChannel");
                await channel.AttachAsync();

                var eventName = "chat";
                var messageList = new List<string>();
                channel.Subscribe(eventName, message => { messageList.Add(message.Data.ToString()); });

                const string HI_THERE = "Hi there";
                const string WHATS_UP = "What's up?";

                var result = await channel.PublishAsync(eventName, HI_THERE);
                AssertResultOk(result);
                result = await channel.PublishAsync(eventName, WHATS_UP);
                AssertResultOk(result);
                await new ConditionalAwaiter(() => messageList.Count >= 2);
                Assert.AreEqual(HI_THERE, messageList[0]);
                Assert.AreEqual(WHATS_UP, messageList[1]);

                messageList.Clear();
                var messageHistoryPage = await channel.HistoryAsync();
                while (true)
                {
                    foreach (var message in messageHistoryPage.Items)
                    {
                        messageList.Add(message.Data.ToString());
                    }
                    if (messageHistoryPage.IsLast)
                    {
                        break;
                    }
                    messageHistoryPage = await messageHistoryPage.NextAsync();
                }
                Assert.AreEqual(2, messageList.Count);
                Assert.AreEqual(WHATS_UP, messageList[0]);
                Assert.AreEqual(HI_THERE, messageList[1]);
            });
        }

        [UnityTest]
        public IEnumerator TestChannelPresence([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, (options, _) => options.ClientId = "sac");
                await realtimeClient.WaitForState(ConnectionState.Connected);

                var channel = realtimeClient.Channels.Get("TestChannel");
                await channel.AttachAsync();

                var presenceMessages = new Dictionary<PresenceAction, string>();
                channel.Presence.Subscribe(message =>
                {
                    presenceMessages[message.Action] = message.Data.ToString();
                });

                const string ENTERED_THE_CHANNEL = "Entered the channel";

                var result = await channel.Presence.EnterAsync(ENTERED_THE_CHANNEL);
                AssertResultOk(result);

                await new ConditionalAwaiter(() => presenceMessages.Count >= 1);
                Assert.Contains(PresenceAction.Enter, presenceMessages.Keys);
                Assert.AreEqual(ENTERED_THE_CHANNEL, presenceMessages[PresenceAction.Enter]);

                var presenceMembers = await channel.Presence.GetAsync();
                Assert.AreEqual(1, presenceMembers.ToList().Count);
                Assert.AreEqual("sac", presenceMembers.First().ClientId);

                const string LEFT_THE_CHANNEL = "left the channel";

                result = await channel.Presence.LeaveAsync(LEFT_THE_CHANNEL);
                AssertResultOk(result);

                await new ConditionalAwaiter(() => presenceMessages.Count >= 2);
                Assert.Contains(PresenceAction.Leave, presenceMessages.Keys);
                Assert.AreEqual(LEFT_THE_CHANNEL, presenceMessages[PresenceAction.Leave]);

                presenceMembers = await channel.Presence.GetAsync();
                Assert.Zero(presenceMembers.ToList().Count);

                presenceMessages.Clear();
                var presenceMessageHistoryPage = await channel.Presence.HistoryAsync();
                while (true)
                {
                    foreach (var presenceMessage in presenceMessageHistoryPage.Items)
                    {
                        presenceMessages[presenceMessage.Action] = presenceMessage.Data.ToString();
                    }
                    if (presenceMessageHistoryPage.IsLast)
                    {
                        break;
                    }
                    presenceMessageHistoryPage = await presenceMessageHistoryPage.NextAsync();
                }
                Assert.AreEqual(2, presenceMessages.Count);
                Assert.AreEqual(ENTERED_THE_CHANNEL, presenceMessages[PresenceAction.Enter]);
                Assert.AreEqual(LEFT_THE_CHANNEL, presenceMessages[PresenceAction.Leave]);
            });
        }

        [UnityTest]
        public IEnumerator TestHttpUnityAgentHeader([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
                var handler = new FakeHttpMessageHandler(response);
                var client = new AblyHttpClient(new AblyHttpOptions { HttpClient = new HttpClient(handler) });

                await client.Execute(new AblyRequest("/test", HttpMethod.Get));
                string[] values = handler.LastRequest.Headers.GetValues("Ably-Agent").ToArray();
                Assert.AreEqual(1, values.Length);
                string[] agentValues = values[0].Split(' ');

                Assert.IsTrue(Agent.OsIdentifier.StartsWith("unity-"));
                Assert.IsTrue(UnityHelper.UnityIdentifier.StartsWith("unity/"));

                var keys = new List<string>()
                {
                    "ably-pubsub-dotnet/",
                    Agent.DotnetRuntimeIdentifier,
                    UnityHelper.UnityIdentifier,
                    Agent.OsIdentifier
                };

                Assert.AreEqual(2, Agent.DotnetRuntimeIdentifier.Split('/').Length);

                keys.RemoveAll(s => s.IsEmpty());

                Assert.AreEqual(keys.Count, agentValues.Length);
                for (var i = 0; i < keys.Count; ++i)
                {
                    Assert.IsTrue(agentValues[i].StartsWith(keys[i]), $"'{agentValues[i]}' should start with '{keys[i]}'");
                }
            });
        }

        private static void AssertResultOk(Result result)
        {
            Assert.True(result.IsSuccess);
            Assert.False(result.IsFailure);
            Assert.Null(result.Error);
        }
    }
}
