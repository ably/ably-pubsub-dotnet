using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Integration
{
    /// <summary>
    /// The direct-sandbox smoke test (guide 2.7 "Direct sandbox"): the integration infra provisions an app, connects
    /// a client, round-trips messages and reads them back over REST, with every wait wall-clock and bounded.
    ///
    /// <para>Guide item "it runs once per protocol variant": not applicable. This build compiles msgpack out
    /// (<c>ClientOptions.UseBinaryProtocol</c> is a no-op; decision log #6), so JSON is the only variant and each test
    /// runs once.</para>
    ///
    /// <para>Guide item "every wait is wall-clock and bounded": by construction. Every wait goes through
    /// <see cref="UtsWait"/> (a <c>Stopwatch</c> deadline, never the SDK's clock) with an explicit bound; no client
    /// here has a fake clock; and each test has an xUnit timeout as the suite bound (guide 5.6).</para>
    /// </summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("uts-infra", "integration")]
    [Trait("type", "integration")]
    public class SandboxSmokeTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);

        public SandboxSmokeTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Fact]
        public void AppIsProvisionedOncePerCollectionAndItsKeysLookRight()
        {
            // The collection fixture provisioned exactly once, whichever test of the collection runs first.
            Fixture.InitializeCount.Should().Be(1);

            App.AppId.Should().NotBeNullOrEmpty();
            App.Endpoint.Should().Be(SandboxApp.DefaultEndpoint);
            App.Host.Should().Be(Defaults.GetPrimaryDomain(SandboxApp.DefaultEndpoint));

            var expectedKeys = ((JArray)SandboxApp.AppSetupBody()["keys"]).Count;
            App.Keys.Should().HaveCount(expectedKeys);
            foreach (var key in App.Keys)
            {
                key.KeyStr.Should().MatchRegex("^[^.:]+\\.[^.:]+:.+$");
                key.KeyStr.Should().Be(key.KeyName + ":" + key.KeySecret);
                key.KeyName.Should().StartWith(App.AppId + ".");
                ExtractKeyName(key.KeyStr).Should().Be(key.KeyName);
                ExtractKeySecret(key.KeyStr).Should().Be(key.KeySecret);
            }

            ApiKey.Should().Be(App.Keys[0].KeyStr);
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task AFixtureDeletesItsAppOnTeardown()
        {
            // A fixture of its own (a second app), driven through the same lifecycle xUnit drives the collection's,
            // so the "deleted in teardown" half is observed rather than assumed.
            var fixture = new SandboxAppFixture();
            await fixture.InitializeAsync();
            var app = fixture.App;
            app.Should().NotBeNull();
            app.Deleted.Should().BeFalse();

            await fixture.DisposeAsync();

            app.Deleted.Should().BeTrue($"DELETE /apps/{app.AppId} answered {app.LastDeleteStatus?.ToString() ?? "nothing"} ({app.LastDeleteError?.Message})");
            app.LastDeleteStatus.Should().BeInRange(200, 299);
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task AClientConnectsToTheSandbox()
        {
            var client = Realtime(new ClientOptions { AutoConnect = false });
            var states = UtsRecord.ConnectionStates(client.Connection);

            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            UtsAssert.ContainsInOrder(states, new[] { ConnectionState.Connecting, ConnectionState.Connected });
            states.Last().Should().Be(ConnectionState.Connected);
            client.Connection.Id.Should().NotBeNullOrEmpty();
        }

        [Fact(Timeout = TestTimeoutMs)]
        public async Task AttachSubscribeAndAckedPublishDeliverInOrderAndHistoryReturnsThem()
        {
            var channelName = UniqueChannelName("smoke");
            var names = new[] { "first", "second", "third" };

            var client = Realtime(new ClientOptions { AutoConnect = false });
            client.Connect();
            await AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(channelName);
            var received = new CaptureList<Message>();
            channel.Subscribe(message => received.Add(message));

            var attach = await UtsWait.WithTimeout(channel.AttachAsync(), StateTimeout, "the ATTACHED reply");
            AssertSucceeded(attach, "attach");
            channel.State.Should().Be(ChannelState.Attached);

            // Ack-awaited: each publish completes only when the server ACKs it, before the next is sent.
            foreach (var name in names)
            {
                var published = await UtsWait.WithTimeout(channel.PublishAsync(name, "data-" + name), StateTimeout, $"the ACK for '{name}'");
                AssertSucceeded(published, "published");
            }

            await UtsWait.PollUntil(
                () => received.Count >= names.Length,
                timeout: StateTimeout,
                description: "all published messages delivered to the subscriber");
            received.Select(m => m.Name).Should().Equal(names);
            received.Select(m => m.Data as string).Should().Equal(names.Select(n => "data-" + n));

            // REST history is eventually consistent: poll for the settled page and assert on that read only.
            var rest = Rest(new ClientOptions());
            var page = await UtsWait.PollUntil<PaginatedResult<Message>>(
                async () =>
                {
                    var result = await rest.Channels.Get(channelName).HistoryAsync(
                        new PaginatedRequestParams { Direction = QueryDirection.Forwards });
                    return result.Items.Count >= names.Length ? result : null;
                },
                timeout: PollTimeout,
                interval: TimeSpan.FromMilliseconds(500),
                description: "history to hold the published messages");
            page.Items.Select(m => m.Name).Should().Equal(names);
        }

        [Fact]
        public void UniqueChannelNamesAreDistinctAndUrlSafe()
        {
            var first = UniqueChannelName("RTL4");
            var second = UniqueChannelName("RTL4");

            first.Should().NotBe(second);
            first.Should().MatchRegex("^test-RTL4-[A-Za-z0-9_-]+$");
            Regex.IsMatch(RandomId(), "^[A-Za-z0-9_-]{8}$").Should().BeTrue();
        }
    }
}
