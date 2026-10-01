using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    [Collection("Channel SandBox")]
    [Trait("type", "integration")]
    public class AnnotationsSandboxSpecs : SandboxSpecs
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        public AnnotationsSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        private static string MutableChannelName(string prefix) =>
            $"{AblySandboxFixture.MutableMessagesNamespace}:{prefix}".AddRandomSuffix();

        private static async Task<T> Within<T>(Task<T> task, string what)
        {
            var winner = await Task.WhenAny(task, Task.Delay(Timeout));
            winner.Should().BeSameAs(task, what);
            return await task;
        }

        [Fact]
        [Trait("requires", "protocol-v4")]
        [Trait("spec", "RTAN1")]
        [Trait("spec", "RTAN2")]
        [Trait("spec", "RTAN3")]
        [Trait("spec", "RTAN4")]
        public async Task PublishedAnnotations_ShouldBeDeliveredToSubscribersAndRetrievable()
        {
            var client = await GetRealtimeClient(Protocol.Json);
            var channel = client.Channels.Get(
                MutableChannelName("annotations"),
                new ChannelOptions(modes: new ChannelModes(
                    ChannelMode.Publish,
                    ChannelMode.Subscribe,
                    ChannelMode.AnnotationPublish,
                    ChannelMode.AnnotationSubscribe)));

            var messageReceived = new TaskCompletionSource<Message>();
            channel.Subscribe(message => messageReceived.TrySetResult(message));
            var annotationsReceived = new List<Annotation>();
            var annotationDelivered = new TaskCompletionSource<Annotation>();
            channel.Annotations.Subscribe("reaction:distinct.v1", annotation =>
            {
                annotationsReceived.Add(annotation);
                annotationDelivered.TrySetResult(annotation);
            });
            (await channel.AttachAsync()).IsSuccess.Should().BeTrue();

            (await channel.PublishAsync("greeting", "hello")).IsSuccess.Should().BeTrue();
            var message = await Within(messageReceived.Task, "the published message was not received");
            message.Serial.Should().NotBeNullOrEmpty();

            var published = await channel.Annotations.PublishAsync(
                message.Serial,
                new Annotation { Type = "reaction:distinct.v1", Name = "like" });
            published.IsSuccess.Should().BeTrue();

            var delivered = await Within(annotationDelivered.Task, "the published annotation was not delivered");
            delivered.Action.Should().Be(AnnotationAction.Create);
            delivered.MessageSerial.Should().Be(message.Serial);
            delivered.Type.Should().Be("reaction:distinct.v1");
            delivered.Name.Should().Be("like");
            delivered.Serial.Should().NotBeNullOrEmpty();

            await AssertMultipleTimes(
                async () =>
                {
                    var page = await channel.Annotations.GetAsync(message.Serial);
                    page.Items.Should().ContainSingle(x => x.Name == "like" && x.Action == AnnotationAction.Create);
                },
                10,
                TimeSpan.FromSeconds(1));

            (await channel.Annotations.DeleteAsync(message.Serial, new Annotation { Type = "reaction:distinct.v1", Name = "like" })).IsSuccess.Should().BeTrue();
        }

        [Fact]
        [Trait("requires", "protocol-v4")]
        [Trait("spec", "RSL11")]
        [Trait("spec", "RSL14")]
        [Trait("spec", "RSAN1")]
        [Trait("spec", "RSAN3")]
        public async Task RestChannel_ShouldRetrieveAPublishedMessageItsVersionsAndAnnotations()
        {
            var rest = await GetRestClient(Protocol.Json);
            var channel = rest.Channels.Get(MutableChannelName("retrieval"));

            await channel.PublishAsync("greeting", "hello");

            string serial = null;
            await AssertMultipleTimes(
                async () =>
                {
                    var history = await channel.HistoryAsync();
                    serial = history.Items.Single().Serial;
                    serial.Should().NotBeNullOrEmpty();
                },
                10,
                TimeSpan.FromSeconds(1));

            var message = await channel.GetMessageAsync(serial);
            message.Serial.Should().Be(serial);
            message.Name.Should().Be("greeting");
            message.Data.Should().Be("hello");
            message.Version.Serial.Should().Be(serial);

            var versions = await channel.GetMessageVersionsAsync(serial);
            versions.Items.Should().HaveCount(1);
            versions.Items[0].Serial.Should().Be(serial);

            await channel.Annotations.PublishAsync(serial, new Annotation { Type = "reaction:distinct.v1", Name = "like" });
            await AssertMultipleTimes(
                async () =>
                {
                    var page = await channel.Annotations.GetAsync(serial);
                    page.Items.Should().ContainSingle(x => x.Name == "like");
                },
                10,
                TimeSpan.FromSeconds(1));
        }
    }
}
