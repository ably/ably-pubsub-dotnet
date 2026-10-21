// RealtimeChannel Annotations Tests (RTL26, RTAN1, RTAN1a, RTAN1b, RTAN1c, RTAN1d, RTAN2, RTAN2a, RTAN3, RTAN3a, RTAN4, RTAN4a, RTAN4b, RTAN4c, RTAN4d, RTAN4e, RTAN4e1, RTAN5, RTAN5a)
// Derived from uts/realtime/unit/channels/channel_annotations.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: spec helpers MockWebSocketClient, CONNECTED() and ATTACHED() are rendered with MockWebSocket and ProtocolMessages; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ChannelAnnotationsTests : UtsUnitTestBase
    {
        private const int PublishAnnotationPublish = (int)ProtocolMessage.Flag.Publish | (int)ProtocolMessage.Flag.AnnotationPublish;

        private const int PublishAnnotationPublishSubscribe = (int)ProtocolMessage.Flag.Publish | (int)ProtocolMessage.Flag.AnnotationPublish | (int)ProtocolMessage.Flag.AnnotationSubscribe;

        public ChannelAnnotationsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's ATTACHED(channel:, flags:)
        private static JObject AttachedWithFlags(string channelName, int flags)
        {
            return ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = flags });
        }

        // One annotation of a server-sent ANNOTATION ProtocolMessage
        private static JObject AnnotationEntry(string type, string name, string serial, long timestamp, string id = null, string clientId = null)
        {
            var entry = new JObject
            {
                ["action"] = 0,
                ["type"] = type,
                ["name"] = name,
                ["messageSerial"] = "msg-serial-1",
                ["serial"] = serial,
                ["timestamp"] = timestamp,
            };

            if (id != null)
            {
                entry["id"] = id;
            }

            if (clientId != null)
            {
                entry["clientId"] = clientId;
            }

            return entry;
        }

        // The spec's ProtocolMessage(action: ANNOTATION, channel:, annotations: [...])
        private static JObject AnnotationProtocolMessage(string channelName, params JObject[] annotations)
        {
            return new JObject
            {
                ["action"] = ProtocolMessages.Annotation,
                ["channel"] = channelName,
                ["annotations"] = new JArray(annotations),
            };
        }

        // UTS: realtime/unit/RTL26/annotations-attribute-type-0
        [UtsFact]
        [Trait("spec", "RTL26")]
        public async Task RTL26_AnnotationsAttributeType()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get("test-RTL26");

            // Assertions
            // ASSERT channel.annotations IS RealtimeAnnotations: the property is declared as RealtimeAnnotations (sealed), a compile-time guarantee
            // NOTE: a non-null check is added since the type check is guaranteed by the compiler
            channel.Annotations.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN1a/publish-sends-annotation-0
        [UtsFact]
        [Trait("spec", "RTAN1a")]
        public async Task RTAN1a_PublishSendsAnnotation()
        {
            // Setup
            var channelName = "test-RTAN1-publish-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    capturedMessages.Add(msg);
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublish));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Annotation)
                    {
                        mockWs.ActiveConnection.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var publishResult = await UtsWait.WithTimeout(channel.Annotations.PublishAsync("msg-serial-1", new Annotation("com.example.reaction", "like")), TimeSpan.FromSeconds(10), "PublishAsync");
            publishResult.IsSuccess.Should().BeTrue("AWAIT channel.annotations.publish() succeeds ({0})", publishResult.Error?.Message);

            // Assertions
            var annotationPm = capturedMessages.Snapshot().LastOrDefault(pm => pm.Action == ProtocolMessage.MessageAction.Annotation);
            annotationPm.Should().NotBeNull();

            annotationPm.Channel.Should().Be(channelName);
            annotationPm.Annotations.Length.Should().Be(1);

            var ann = annotationPm.Annotations[0];
            ann.Action.Should().Be(AnnotationAction.Create); // numeric: 0
            ann.MessageSerial.Should().Be("msg-serial-1");
            ann.Type.Should().Be("com.example.reaction");
            ann.Name.Should().Be("like");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN1a/validates-type-required-1
        [UtsFact]
        [Trait("spec", "RTAN1a")]
        public async Task RTAN1a_ValidatesTypeRequired()
        {
            // Setup
            var channelName = "test-RTAN1a-validate-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublish));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps and Assertions
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // NOTE: annotation validation (RSAN1a3) throws AblyException from PublishAsync instead of returning a failed Result
            Func<Task> act = () => channel.Annotations.PublishAsync("msg-serial-1", new Annotation { Name = "like" });
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Error code is implementation-defined; RSAN1a3 does not mandate a specific code
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN1a/encodes-data-json-2
        [UtsFact]
        [Trait("spec", "RTAN1a")]
        public async Task RTAN1a_EncodesDataJson()
        {
            // Setup
            var channelName = "test-RTAN1a-encode-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    capturedMessages.Add(msg);
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublish));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Annotation)
                    {
                        mockWs.ActiveConnection.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var data = new JObject { ["key"] = "value", ["nested"] = new JObject { ["a"] = 1 } };
            var publishResult = await UtsWait.WithTimeout(channel.Annotations.PublishAsync("msg-serial-1", new Annotation("com.example.data", data: data)), TimeSpan.FromSeconds(10), "PublishAsync");
            publishResult.IsSuccess.Should().BeTrue("AWAIT channel.annotations.publish() succeeds ({0})", publishResult.Error?.Message);

            // Assertions
            var annotationPm = capturedMessages.Snapshot().LastOrDefault(pm => pm.Action == ProtocolMessage.MessageAction.Annotation);
            annotationPm.Should().NotBeNull();

            var ann = annotationPm.Annotations[0];
            ann.Data.Should().BeOfType<string>();
            ann.Encoding.Should().Be("json");
            JToken.DeepEquals(JObject.Parse((string)ann.Data), new JObject { ["key"] = "value", ["nested"] = new JObject { ["a"] = 1 } }).Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN1b/publish-channel-state-0
        [UtsFact]
        [Trait("spec", "RTAN1b")]
        public async Task RTAN1b_PublishChannelState()
        {
            // Setup
            var channelName = "test-RTAN1b-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Send ERROR to put channel in FAILED state
                        // NOTE: hand-built: ChannelErrorMessage defaults statusCode to code / 100 and the spec gives none
                        mockWs.ActiveConnection.SendToClient(new JObject
                        {
                            ["action"] = ProtocolMessages.Error,
                            ["channel"] = channelName,
                            ["error"] = ProtocolMessages.ErrorObject(40160, "Not permitted"),
                        });
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps and Assertions
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Attempt attach — will fail, putting channel in FAILED
            // Expected — channel is now FAILED (the Result API reports the failure; bound here)
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsFailure.Should().BeTrue();

            channel.State.Should().Be(ChannelState.Failed);

            // NOTE: a state which refuses publishing throws AblyException from PublishAsync instead of returning a failed Result
            Func<Task> act = () => channel.Annotations.PublishAsync("msg-serial-1", new Annotation("com.example.reaction", "like"));
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Should().NotBeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN1d/publish-ack-nack-0
        [UtsFact]
        [Trait("spec", "RTAN1d")]
        public async Task RTAN1d_PublishAckNack()
        {
            // Setup (ACK case)
            var ackChannelName = "test-RTAN1d-ack-" + Guid.NewGuid().ToString("N");

            MockWebSocket ackMockWs = null;
            ackMockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        ackMockWs.ActiveConnection.SendToClient(AttachedWithFlags(ackChannelName, PublishAnnotationPublish));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Annotation)
                    {
                        ackMockWs.ActiveConnection.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });
            InstallMock(ackMockWs);

            var ackClient = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var ackChannel = ackClient.Channels.Get(ackChannelName);

            // Test Steps (ACK)
            ackClient.Connect();
            await UtsWait.AwaitConnectionState(ackClient.Connection, ConnectionState.Connected);
            var ackAttachResult = await UtsWait.WithTimeout(ackChannel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            ackAttachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", ackAttachResult.Error?.Message);

            // Should resolve without error
            var ackPublishResult = await UtsWait.WithTimeout(ackChannel.Annotations.PublishAsync("msg-serial-1", new Annotation("com.example.reaction", "like")), TimeSpan.FromSeconds(10), "PublishAsync");
            ackPublishResult.IsSuccess.Should().BeTrue("AWAIT channel.annotations.publish() succeeds ({0})", ackPublishResult.Error?.Message);

            // If we get here, publish succeeded (no assertion needed beyond no throw)
            await CloseClient(ackClient);

            // Setup (NACK case)
            var nackChannelName = "test-RTAN1d-nack-" + Guid.NewGuid().ToString("N");

            MockWebSocket nackMockWs = null;
            nackMockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        nackMockWs.ActiveConnection.SendToClient(AttachedWithFlags(nackChannelName, PublishAnnotationPublish));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Annotation)
                    {
                        nackMockWs.ActiveConnection.SendToClient(ProtocolMessages.NackMessage(msg.MsgSerial, 40160, "Not permitted", count: 1));
                    }
                });
            InstallMock(nackMockWs);

            var nackClient = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var nackChannel = nackClient.Channels.Get(nackChannelName);

            // Test Steps and Assertions (NACK)
            nackClient.Connect();
            await UtsWait.AwaitConnectionState(nackClient.Connection, ConnectionState.Connected);
            var nackAttachResult = await UtsWait.WithTimeout(nackChannel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            nackAttachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", nackAttachResult.Error?.Message);

            var nackPublishResult = await UtsWait.WithTimeout(nackChannel.Annotations.PublishAsync("msg-serial-1", new Annotation("com.example.reaction", "like")), TimeSpan.FromSeconds(10), "PublishAsync");
            nackPublishResult.IsFailure.Should().BeTrue();
            nackPublishResult.Error.Code.Should().Be(40160);
            await CloseClient(nackClient);
        }

        // UTS: realtime/unit/RTAN2a/delete-sends-annotation-0
        [UtsFact]
        [Trait("spec", "RTAN2a")]
        public async Task RTAN2a_DeleteSendsAnnotation()
        {
            // Setup
            var channelName = "test-RTAN2-delete-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    capturedMessages.Add(msg);
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublish));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Annotation)
                    {
                        mockWs.ActiveConnection.SendToClient(ProtocolMessages.AckMessage(msg.MsgSerial, 1));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var deleteResult = await UtsWait.WithTimeout(channel.Annotations.DeleteAsync("msg-serial-1", new Annotation("com.example.reaction", "like")), TimeSpan.FromSeconds(10), "DeleteAsync");
            deleteResult.IsSuccess.Should().BeTrue("AWAIT channel.annotations.delete() succeeds ({0})", deleteResult.Error?.Message);

            // Assertions
            var annotationPm = capturedMessages.Snapshot().LastOrDefault(pm => pm.Action == ProtocolMessage.MessageAction.Annotation);
            annotationPm.Should().NotBeNull();

            var ann = annotationPm.Annotations[0];
            ann.Action.Should().Be(AnnotationAction.Delete); // numeric: 1
            ann.MessageSerial.Should().Be("msg-serial-1");
            ann.Type.Should().Be("com.example.reaction");
            ann.Name.Should().Be("like");
            await CloseClient(client);
        }

        // RTAN3a has no Test ID: get is identical to RestAnnotations#get; the tests in uts/rest/unit/channel/annotations.md (RSAN3) apply on a RealtimeChannel.
        // NOTE: no Test ID in the spec: RTAN3a

        // UTS: realtime/unit/RTAN4a/subscribe-delivers-annotations-0
        [UtsFact]
        [Trait("spec", "RTAN4a")]
        public async Task RTAN4a_SubscribeDeliversAnnotations()
        {
            // Setup
            var channelName = "test-RTAN4-subscribe-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublishSubscribe));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var receivedAnnotations = new CaptureList<Annotation>();
            channel.Annotations.Subscribe(annotation => receivedAnnotations.Add(annotation));

            // Server sends ANNOTATION ProtocolMessage with two annotations
            mockWs.ActiveConnection.SendToClient(AnnotationProtocolMessage(
                channelName,
                AnnotationEntry("com.example.reaction", "like", "ann-serial-1", 1700000000000, id: "ann-1", clientId: "user-1"),
                AnnotationEntry("com.example.reaction", "heart", "ann-serial-2", 1700000001000, id: "ann-2", clientId: "user-2")));

            // NOTE: the listeners are notified asynchronously; a drain of pending events stands in for the delivery wait the spec leaves implicit
            await ProcessPendingEvents();

            // Assertions
            receivedAnnotations.Count.Should().Be(2);

            var ann1 = receivedAnnotations[0];
            ann1.Should().BeAssignableTo<Annotation>();
            ann1.Id.Should().Be("ann-1");
            ann1.Action.Should().Be(AnnotationAction.Create);
            ann1.Type.Should().Be("com.example.reaction");
            ann1.Name.Should().Be("like");
            ann1.ClientId.Should().Be("user-1");
            ann1.Serial.Should().Be("ann-serial-1");
            ann1.MessageSerial.Should().Be("msg-serial-1");
            ann1.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));

            var ann2 = receivedAnnotations[1];
            ann2.Name.Should().Be("heart");
            ann2.ClientId.Should().Be("user-2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN4c/subscribe-type-filter-0
        [UtsFact]
        [Trait("spec", "RTAN4c")]
        public async Task RTAN4c_SubscribeTypeFilter()
        {
            // Setup
            var channelName = "test-RTAN4c-filter-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublishSubscribe));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var reactionAnnotations = new CaptureList<Annotation>();
            channel.Annotations.Subscribe("com.example.reaction", annotation => reactionAnnotations.Add(annotation));

            // Server sends mixed annotation types
            mockWs.ActiveConnection.SendToClient(AnnotationProtocolMessage(
                channelName,
                AnnotationEntry("com.example.reaction", "like", "ann-serial-1", 1700000000000),
                AnnotationEntry("com.example.comment", "text", "ann-serial-2", 1700000001000),
                AnnotationEntry("com.example.reaction", "heart", "ann-serial-3", 1700000002000)));

            // NOTE: the listeners are notified asynchronously; a drain of pending events stands in for the delivery wait the spec leaves implicit
            await ProcessPendingEvents();

            // Assertions
            // Only reaction annotations delivered
            reactionAnnotations.Count.Should().Be(2);
            reactionAnnotations[0].Name.Should().Be("like");
            reactionAnnotations[1].Name.Should().Be("heart");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN4d/subscribe-implicit-attach-0
        [UtsFact]
        [Trait("spec", "RTAN4d")]
        public async Task RTAN4d_SubscribeImplicitAttach()
        {
            // Setup
            var channelName = "test-RTAN4d-attach-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublishSubscribe));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Default attachOnSubscribe is true
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            channel.State.Should().Be(ChannelState.Initialized);

            channel.Annotations.Subscribe(annotation => { });

            // Wait for implicit attach to complete
            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);

            // Assertions
            channel.State.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN4e/subscribe-warns-no-mode-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTAN4e", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTAN4e")]
        public async Task RTAN4e_SubscribeWarnsNoMode()
        {
            // Setup
            var channelName = "test-RTAN4e-warn-" + Guid.NewGuid().ToString("N");
            var logSink = new CapturingLogSink();

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        // Respond with ATTACHED but WITHOUT ANNOTATION_SUBSCRIBE flag
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, (int)ProtocolMessage.Flag.Publish));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                LogHandler = logSink,
                LogLevel = LogLevel.Debug,
            });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            channel.Annotations.Subscribe(annotation => { });

            // Assertions
            // A warning should have been logged about ANNOTATION_SUBSCRIBE mode
            var logMessages = logSink.Logs.Snapshot().Where(log => log.Level == LogLevel.Warning).Select(log => log.Message).ToList();
            logMessages.Count.Should().BeGreaterThanOrEqualTo(1);
            var foundWarning = false;
            foreach (var message in logMessages)
            {
                // NOTE: the .NET mode is named ChannelMode.AnnotationSubscribe; the warning is matched on the SDK's spelling of the mode (idiomatic naming)
                if (message.Contains("AnnotationSubscribe"))
                {
                    foundWarning = true;
                }
            }

            foundWarning.Should().BeTrue();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN4e1/no-warn-unattached-0
        [UtsFact(Skip = "Missing SDK public API: ChannelOptions#attachOnSubscribe (TB4); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTAN4e1")]
        public async Task RTAN4e1_NoWarnUnattached()
        {
            // NOTE: the test subscribes to annotations on an unattached channel and relies on RealtimeChannelOptions(attachOnSubscribe: false)
            // to stop Subscribe from attaching it; this SDK has no such option, and Subscribe attaches the channel. See realtime-mapping.md section 12.
            // channel_name = "test-RTAN4e1-${random_id()}"
            // log_messages = []
            // mock_ws = MockWebSocketClient(onConnectionAttempt: (conn) => { conn.respond_with_success(CONNECTED()) })
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false, logHandler: (level, message) => { IF level == WARN: log_messages.append(message) }), webSocketClient: mock_ws)
            // channel = client.channels.get(channel_name, RealtimeChannelOptions(attachOnSubscribe: false))
            // client.connect()
            // AWAIT_STATE connection == CONNECTED
            // # Channel is INITIALIZED, not attached
            // ASSERT channel.state == INITIALIZED
            // channel.annotations.subscribe((annotation) => {})
            // # No warning about ANNOTATION_SUBSCRIBE should be logged
            // found_warning = false
            // FOR msg IN log_messages: IF msg CONTAINS "ANNOTATION_SUBSCRIBE": found_warning = true
            // ASSERT found_warning == false
            // CLOSE_CLIENT(client)
            await Task.CompletedTask;
        }

        // UTS: realtime/unit/RTAN5a/unsubscribe-removes-listeners-0
        [UtsFact]
        [Trait("spec", "RTAN5a")]
        public async Task RTAN5a_UnsubscribeRemovesListeners()
        {
            // Setup
            var channelName = "test-RTAN5-unsub-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublishSubscribe));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var receivedAnnotations = new CaptureList<Annotation>();
            Action<Annotation> listener = annotation => receivedAnnotations.Add(annotation);
            channel.Annotations.Subscribe(listener);

            // Send first annotation — should be received
            mockWs.ActiveConnection.SendToClient(AnnotationProtocolMessage(
                channelName,
                AnnotationEntry("com.example.reaction", "like", "ann-serial-1", 1700000000000)));

            // NOTE: the listeners are notified asynchronously; a drain of pending events stands in for the delivery wait the spec leaves implicit
            await ProcessPendingEvents();
            receivedAnnotations.Count.Should().Be(1);

            // Unsubscribe
            channel.Annotations.Unsubscribe(listener);

            // Send second annotation — should NOT be received
            mockWs.ActiveConnection.SendToClient(AnnotationProtocolMessage(
                channelName,
                AnnotationEntry("com.example.reaction", "heart", "ann-serial-2", 1700000001000)));
            await ProcessPendingEvents();

            // Assertions
            // Only the first annotation was received
            receivedAnnotations.Count.Should().Be(1);
            receivedAnnotations[0].Name.Should().Be("like");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTAN5a/unsubscribe-type-filter-1
        [UtsFact]
        [Trait("spec", "RTAN5a")]
        public async Task RTAN5a_UnsubscribeTypeFilter()
        {
            // Setup
            var channelName = "test-RTAN5a-typed-" + Guid.NewGuid().ToString("N");

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(AttachedWithFlags(channelName, PublishAnnotationPublishSubscribe));
                    }
                });
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged
            var channel = client.Channels.Get(channelName);

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            var reactionReceived = new CaptureList<Annotation>();
            var commentReceived = new CaptureList<Annotation>();

            Action<Annotation> reactionListener = ann => reactionReceived.Add(ann);
            Action<Annotation> commentListener = ann => commentReceived.Add(ann);

            channel.Annotations.Subscribe("com.example.reaction", reactionListener);
            channel.Annotations.Subscribe("com.example.comment", commentListener);

            // Unsubscribe only reactions
            channel.Annotations.Unsubscribe("com.example.reaction", reactionListener);

            // Send both types
            mockWs.ActiveConnection.SendToClient(AnnotationProtocolMessage(
                channelName,
                AnnotationEntry("com.example.reaction", "like", "ann-serial-1", 1700000000000),
                AnnotationEntry("com.example.comment", "text", "ann-serial-2", 1700000001000)));

            // NOTE: the listeners are notified asynchronously; a drain of pending events stands in for the delivery wait the spec leaves implicit
            await ProcessPendingEvents();

            // Assertions
            // Reactions unsubscribed, comments still active
            reactionReceived.Count.Should().Be(0);
            commentReceived.Count.Should().Be(1);
            commentReceived[0].Type.Should().Be("com.example.comment");
            await CloseClient(client);
        }
    }
}
