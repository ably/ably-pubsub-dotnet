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

            // The latest-version view is updated asynchronously after the update is acknowledged, so poll until it appears.
            await AssertEventually(
                async () =>
                {
                    var latest = await restChannel.GetMessageAsync(serial);
                    latest.Data.Should().Be("hello world");
                    latest.Action.Should().Be(MessageAction.MessageUpdate);
                },
                Timeout,
                TimeSpan.FromMilliseconds(500));

            var appendResult = await channel.AppendMessageAsync(new Message { Serial = serial, Data = "!" });
            appendResult.IsSuccess.Should().BeTrue();
            appendResult.Value.VersionSerial.Should().NotBeNullOrEmpty();
            (await Within(appended.Task, "append")).Serial.Should().Be(serial);

            await AssertEventually(
                async () => (await restChannel.GetMessageAsync(serial)).Data.Should().Be("hello world!"),
                Timeout,
                TimeSpan.FromMilliseconds(500));

            var deleteResult = await channel.DeleteMessageAsync(new Message { Serial = serial }, new MessageOperation { Description = "removed" });
            deleteResult.IsSuccess.Should().BeTrue();
            (await Within(deleted.Task, "delete")).Serial.Should().Be(serial);

            // The version history is populated asynchronously: wait for the create, update and delete versions to
            // appear in order, with the delete last.
            await AssertEventually(
                async () =>
                {
                    var actions = (await restChannel.GetMessageVersionsAsync(serial)).Items.Select(x => x.Action).ToList();
                    actions.Should().NotBeEmpty();
                    actions.Last().Should().Be(MessageAction.MessageDelete);
                    var expected = new Queue<MessageAction>(new[] { MessageAction.MessageCreate, MessageAction.MessageUpdate, MessageAction.MessageDelete });
                    foreach (var action in actions)
                    {
                        if (expected.Count > 0 && expected.Peek() == action)
                        {
                            expected.Dequeue();
                        }
                    }

                    expected.Should().BeEmpty("the versions {0} should contain create, update and delete in order", string.Join(", ", actions));
                },
                Timeout,
                TimeSpan.FromMilliseconds(500));
        }

        private static async Task<T> Within<T>(Task<T> task, string what)
        {
            var winner = await Task.WhenAny(task, Task.Delay(Timeout));
            winner.Should().BeSameAs(task, what);
            return await task;
        }
    }
}
