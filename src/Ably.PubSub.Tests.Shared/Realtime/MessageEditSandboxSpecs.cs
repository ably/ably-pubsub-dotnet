using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    [Collection("Channel SandBox")]
    [Trait("type", "integration")]
    public class MessageEditSandboxSpecs : SandboxSpecs
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        public MessageEditSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Fact]
        [Trait("requires", "protocol-v5")]
        [Trait("spec", "RSL15")]
        [Trait("spec", "RTL32")]
        public async Task PublishUpdateDeleteAndAppend_ShouldBeObservedByARealtimeSubscriberAndRetrievable()
        {
            var channelName = $"{AblySandboxFixture.MutableMessagesNamespace}:edits".AddRandomSuffix();
            var rest = await GetRestClient(Protocol.Json);
            var restChannel = rest.Channels.Get(channelName);

            var realtime = await GetRealtimeClient(Protocol.Json);
            var channel = realtime.Channels.Get(channelName);
            var received = new List<Message>();
            var updated = new TaskCompletionSource<Message>();
            var deleted = new TaskCompletionSource<Message>();
            var appended = new TaskCompletionSource<Message>();
            channel.Subscribe(message =>
            {
                received.Add(message);
                switch (message.Action)
                {
                    case MessageAction.MessageUpdate: updated.TrySetResult(message); break;
                    case MessageAction.MessageDelete: deleted.TrySetResult(message); break;
                    case MessageAction.MessageAppend: appended.TrySetResult(message); break;
                }
            });
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();

            var published = await restChannel.PublishAsync("greeting", "hello");
            var serial = published.Serials.Single();

            var updateResult = await restChannel.UpdateMessageAsync(
                new Message("greeting", "hello world") { Serial = serial },
                new MessageOperation { Description = "edited" });
            updateResult.VersionSerial.Should().NotBeNullOrEmpty();
            (await Within(updated.Task, "update")).Version.Description.Should().Be("edited");

            var latest = await restChannel.GetMessageAsync(serial);
            latest.Data.Should().Be("hello world");
            latest.Action.Should().Be(MessageAction.MessageUpdate);

            var appendResult = await channel.AppendMessageAsync(new Message { Serial = serial, Data = "!" });
            appendResult.IsSuccess.Should().BeTrue();
            appendResult.Value.VersionSerial.Should().NotBeNullOrEmpty();
            (await Within(appended.Task, "append")).Serial.Should().Be(serial);

            var deleteResult = await channel.DeleteMessageAsync(new Message { Serial = serial }, new MessageOperation { Description = "removed" });
            deleteResult.IsSuccess.Should().BeTrue();
            (await Within(deleted.Task, "delete")).Serial.Should().Be(serial);

            var versions = await restChannel.GetMessageVersionsAsync(serial);
            versions.Items.Count.Should().BeGreaterOrEqualTo(4);
        }

        private static async Task<T> Within<T>(Task<T> task, string what)
        {
            var winner = await Task.WhenAny(task, Task.Delay(Timeout));
            winner.Should().BeSameAs(task, what);
            return await task;
        }
    }
}
