// RealtimeChannel UpdateMessage/DeleteMessage/AppendMessage Tests (RTL32, RTL32a, RTL32b, RTL32b1, RTL32b2, RTL32c, RTL32d, RTL32e)
// Derived from uts/realtime/unit/channels/channel_update_delete_message.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: the spec's MockWebSocketClient(...)/webSocketClient: option is the standard MockWebSocket + InstallMock; the ACK res field is built in its wire form (an array of PublishResult), the spec writes a single object; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

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
    public class ChannelUpdateDeleteMessageTests : UtsUnitTestBase
    {
        public ChannelUpdateDeleteMessageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // The spec's shared mock: CONNECTED on connect, ATTACHED (flags: PUBLISH) for ATTACH, and the given reply for MESSAGE
        // NOTE: the ACK res field is built in the wire form, an array of PublishResult ({ "serials": [...] }); the spec writes it as one object
        private MockWebSocket BuildMock(string channelName, CaptureList<ProtocolMessage> capturedMessages, Func<ProtocolMessage, JObject> onMessage)
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    capturedMessages?.Add(msg);
                    if (msg.Action == ProtocolMessage.MessageAction.Attach)
                    {
                        mockWs.ActiveConnection.SendToClient(ProtocolMessages.AttachedMessage(
                            channelName,
                            new Dictionary<string, JToken> { ["flags"] = (int)ProtocolMessage.Flag.Publish }));
                    }
                    else if (msg.Action == ProtocolMessage.MessageAction.Message && onMessage != null)
                    {
                        mockWs.ActiveConnection.SendToClient(onMessage(msg));
                    }
                });
            InstallMock(mockWs);
            return mockWs;
        }

        private static JObject AckWithSerial(ProtocolMessage msg, string serial)
        {
            var ack = ProtocolMessages.AckMessage(msg.MsgSerial, 1);
            ack["res"] = new JArray(new JObject { ["serials"] = new JArray(serial) });
            return ack;
        }

        private async Task<IRealtimeChannel> ConnectAndAttach(PubSubRealtimeClient client, string channelName)
        {
            var channel = client.Channels.Get(channelName);
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            return channel;
        }

        private static ProtocolMessage FindMessageProtocolMessage(CaptureList<ProtocolMessage> capturedMessages)
        {
            // Find the MESSAGE ProtocolMessage (not the ATTACH)
            ProtocolMessage messagePm = null;
            foreach (var pm in capturedMessages.Snapshot())
            {
                if (pm.Action == ProtocolMessage.MessageAction.Message)
                {
                    messagePm = pm;
                }
            }

            return messagePm;
        }

        // UTS: realtime/unit/RTL32b/update-message-action-0
        [UtsFact]
        [Trait("spec", "RTL32b")]
        public async Task RTL32b_UpdateMessageAction()
        {
            // Setup
            var channelName = "test-RTL32-update-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();
            BuildMock(channelName, capturedMessages, msg => AckWithSerial(msg, "version-serial-1"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var result = await UtsWait.WithTimeout(channel.UpdateMessageAsync(new Message { Serial = "msg-serial-1", Name = "updated", Data = "new-data" }), TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.updateMessage() succeeds ({0})", result.Error?.Message);

            // Assertions
            var messagePm = FindMessageProtocolMessage(capturedMessages);
            messagePm.Should().NotBeNull();

            messagePm.Channel.Should().Be(channelName);
            messagePm.Messages.Should().HaveCount(1);

            var msg = messagePm.Messages[0];

            // numeric: 1
            msg.Action.Should().Be(MessageAction.MessageUpdate);
            msg.Serial.Should().Be("msg-serial-1");
            msg.Name.Should().Be("updated");
            msg.Data.Should().Be("new-data");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32b/delete-message-action-1
        [UtsFact]
        [Trait("spec", "RTL32b")]
        public async Task RTL32b_DeleteMessageAction()
        {
            // Setup
            var channelName = "test-RTL32-delete-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();
            BuildMock(channelName, capturedMessages, msg => AckWithSerial(msg, "version-serial-1"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var result = await UtsWait.WithTimeout(channel.DeleteMessageAsync(new Message { Serial = "msg-serial-1" }), TimeSpan.FromSeconds(10), "DeleteMessageAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.deleteMessage() succeeds ({0})", result.Error?.Message);

            // Assertions
            var messagePm = FindMessageProtocolMessage(capturedMessages);
            messagePm.Should().NotBeNull();

            var msg = messagePm.Messages[0];

            // numeric: 2
            msg.Action.Should().Be(MessageAction.MessageDelete);
            msg.Serial.Should().Be("msg-serial-1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32b/append-message-action-2
        [UtsFact]
        [Trait("spec", "RTL32b")]
        public async Task RTL32b_AppendMessageAction()
        {
            // Setup
            var channelName = "test-RTL32-append-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();
            BuildMock(channelName, capturedMessages, msg => AckWithSerial(msg, "version-serial-1"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var appendMessageTask = channel.AppendMessageAsync(
                new Message { Serial = "msg-serial-1", Data = "appended-data" },
                new MessageOperation { Description = "appended content" });
            var result = await UtsWait.WithTimeout(appendMessageTask, TimeSpan.FromSeconds(10), "AppendMessageAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.appendMessage() succeeds ({0})", result.Error?.Message);

            // Assertions
            var messagePm = FindMessageProtocolMessage(capturedMessages);
            messagePm.Should().NotBeNull();

            var msg = messagePm.Messages[0];

            // numeric: 5
            msg.Action.Should().Be(MessageAction.MessageAppend);
            msg.Serial.Should().Be("msg-serial-1");
            msg.Data.Should().Be("appended-data");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32b2/version-from-operation-0
        [UtsFact]
        [Trait("spec", "RTL32b2")]
        public async Task RTL32b2_VersionFromOperation()
        {
            // Setup
            var channelName = "test-RTL32b2-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();
            BuildMock(channelName, capturedMessages, msg => AckWithSerial(msg, "version-serial-1"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            // With operation
            var updateMessageTask = channel.UpdateMessageAsync(
                new Message { Serial = "msg-serial-1", Data = "v2" },
                new MessageOperation
                {
                    Description = "edited content",
                    Metadata = new Dictionary<string, string> { ["reason"] = "typo" },
                });
            var withOperation = await UtsWait.WithTimeout(updateMessageTask, TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            withOperation.IsSuccess.Should().BeTrue("AWAIT channel.updateMessage() succeeds ({0})", withOperation.Error?.Message);

            // Without operation
            var withoutOperation = await UtsWait.WithTimeout(channel.UpdateMessageAsync(new Message { Serial = "msg-serial-2", Data = "v2" }), TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            withoutOperation.IsSuccess.Should().BeTrue("AWAIT channel.updateMessage() succeeds ({0})", withoutOperation.Error?.Message);

            // Assertions
            var messagePms = capturedMessages.Snapshot().Where(pm => pm.Action == ProtocolMessage.MessageAction.Message).ToList();
            messagePms.Should().HaveCount(2);

            // With operation: version field present
            var msgWithOp = messagePms[0].Messages[0];
            msgWithOp.Version.Should().NotBeNull();
            msgWithOp.Version.Description.Should().Be("edited content");
            msgWithOp.Version.Metadata["reason"].Should().Be("typo");

            // Without operation: version field absent
            var msgWithoutOp = messagePms[1].Messages[0];
            msgWithoutOp.Version.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32c/no-message-mutation-0
        [UtsFact]
        [Trait("spec", "RTL32c")]
        public async Task RTL32c_NoMessageMutation()
        {
            // Setup
            var channelName = "test-RTL32c-" + Guid.NewGuid().ToString("N");
            BuildMock(channelName, null, msg => AckWithSerial(msg, "version-serial-1"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var originalMessage = new Message { Serial = "msg-serial-1", Name = "original", Data = "original-data" };
            var result = await UtsWait.WithTimeout(channel.UpdateMessageAsync(originalMessage), TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.updateMessage() succeeds ({0})", result.Error?.Message);

            // Assertions
            // Original message unchanged
            originalMessage.Name.Should().Be("original");
            originalMessage.Data.Should().Be("original-data");
            originalMessage.Serial.Should().Be("msg-serial-1");
            originalMessage.Action.Should().BeNull();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32d/ack-returns-result-0
        [UtsFact]
        [Trait("spec", "RTL32d")]
        public async Task RTL32d_AckReturnsResult()
        {
            // Setup
            var channelName = "test-RTL32d-" + Guid.NewGuid().ToString("N");
            BuildMock(channelName, null, msg => AckWithSerial(msg, "01770000000000-000@abcdef:000"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var result = await UtsWait.WithTimeout(channel.UpdateMessageAsync(new Message { Serial = "msg-serial-1", Data = "updated" }), TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.updateMessage() succeeds ({0})", result.Error?.Message);

            // Assertions
            result.Value.Should().BeAssignableTo<UpdateDeleteResult>();
            result.Value.VersionSerial.Should().Be("01770000000000-000@abcdef:000");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32d/nack-returns-error-1
        [UtsFact]
        [Trait("spec", "RTL32d")]
        public async Task RTL32d_NackReturnsError()
        {
            // Setup
            var channelName = "test-RTL32d-nack-" + Guid.NewGuid().ToString("N");
            BuildMock(channelName, null, msg => ProtocolMessages.NackMessage(msg.MsgSerial, 40160, "Not permitted"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var result = await UtsWait.WithTimeout(channel.UpdateMessageAsync(new Message { Serial = "msg-serial-1", Data = "updated" }), TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            result.IsFailure.Should().BeTrue();

            // Assertions
            result.Error.Code.Should().Be(40160);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32e/params-in-protocol-message-0
        [UtsFact]
        [Trait("spec", "RTL32e")]
        public async Task RTL32e_ParamsInProtocolMessage()
        {
            // Setup
            var channelName = "test-RTL32e-" + Guid.NewGuid().ToString("N");
            var capturedMessages = new CaptureList<ProtocolMessage>();
            BuildMock(channelName, capturedMessages, msg => AckWithSerial(msg, "version-serial-1"));

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps
            var channel = await ConnectAndAttach(client, channelName);

            var updateMessageTask2 = channel.UpdateMessageAsync(
                new Message { Serial = "msg-serial-1", Data = "v2" },
                null,
                new Dictionary<string, string> { ["key1"] = "value1", ["key2"] = "value2" });
            var result = await UtsWait.WithTimeout(updateMessageTask2, TimeSpan.FromSeconds(10), "UpdateMessageAsync");
            result.IsSuccess.Should().BeTrue("AWAIT channel.updateMessage() succeeds ({0})", result.Error?.Message);

            // Assertions
            var messagePm = FindMessageProtocolMessage(capturedMessages);
            messagePm.Should().NotBeNull();

            messagePm.Params["key1"].Should().Be("value1");
            messagePm.Params["key2"].Should().Be("value2");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL32a/serial-validation-required-0
        [UtsFact]
        [Trait("spec", "RTL32a")]
        public async Task RTL32a_SerialValidationRequired()
        {
            // Setup
            var channelName = "test-RTL32a-" + Guid.NewGuid().ToString("N");
            BuildMock(channelName, null, null);

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
            });

            // Test Steps and Assertions
            var channel = await ConnectAndAttach(client, channelName);

            // Empty serial
            // NOTE: an invalid serial throws an AblyException (RealtimeChannel, RTL32a) instead of returning a failed Result
            Func<Task> updateWithEmptySerial = () => channel.UpdateMessageAsync(new Message { Serial = string.Empty, Data = "v2" });
            var updateError = (await updateWithEmptySerial.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            updateError.Code.Should().Be(40003);

            // Null serial (if applicable in language)
            Func<Task> deleteWithNullSerial = () => channel.DeleteMessageAsync(new Message { Data = "v2" });
            var deleteError = (await deleteWithNullSerial.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            deleteError.Code.Should().Be(40003);
            await CloseClient(client);
        }
    }
}
