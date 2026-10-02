using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.Http;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Integration
{
    /// <summary>
    /// Derived from uts/rest/integration/presence.md in ably/specification.
    ///
    /// Spec points: RSP1, RSP3, RSP3a1, RSP3a2, RSP4, RSP4b1, RSP4b2, RSP4b3, RSP5
    ///
    /// Five file-wide notes, each restated at the site that depends on it:
    ///
    /// 1. msgpack is compiled out of this build, so only the JSON protocol variant is runnable and
    ///    each test appears once rather than twice.
    /// 2. The spec's "ASSERT x IS SomeType" lines are static guarantees in C#. GetAsync and
    ///    HistoryAsync return a typed paginated result of PresenceMessage, and channel.Presence
    ///    returns IPresence, so those runtime checks cannot fail. They are kept where they compile
    ///    and the value assertions beside them carry the coverage.
    /// 3. "AWAIT realtime.close()" has no awaitable form here: Close() sends a CLOSE and returns, so
    ///    it is paired with a wait on ConnectionState.Closed. It sits in a finally so a failing
    ///    assertion above it still leaves the shared sandbox app clean. The clients themselves are
    ///    registered for teardown by SandboxRealtimeClient, so nothing leaks either way.
    /// 4. Every channel name and clientId carries UtsSandbox.RandomId(), because the sandbox app is
    ///    shared by the whole run. The one exception is persisted:presence_fixtures, the channel the
    ///    app provisioning pre-populates, which these tests only read.
    /// 5. Waits after a write go through UtsSandbox.WallClockPollUntil on the real clock with the
    ///    spec's 500ms interval and 10s timeout. Presence history lags an enter, so a fixed delay
    ///    would either flake or spend the whole budget.
    /// </summary>
    [Collection(UtsSandbox.CollectionName)]
    public class PresenceTests : UtsIntegrationTestBase
    {
        private const string FixtureStringData = "This is a string clientData payload";

        private const string FixtureJsonStringData = "{ \"test\": \"This is a JSONObject clientData payload\"}";

        /// <summary>The cipher key the spec names for the client_encoded fixture, verbatim.</summary>
        private const string FixtureCipherKeyBase64 = "WUP6u0K7MXI5Zeo0VppPwg==";

        public PresenceTests(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSP1/access-presence-from-channel-0
        [Fact]
        public async Task RSP1_AccessPresenceFromChannel()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);
            var presence = channel.Presence;

            presence.Should().NotBeNull();

            // The spec's "presence IS RestPresence": .NET has no RestPresence type. HttpChannel
            // implements IPresence explicitly and channel.Presence hands back itself as that
            // interface, so this is the same assertion spelled the way this SDK spells it.
            presence.Should().BeAssignableTo<IPresence>();
        }

        // UTS: rest/integration/RSP3/get-presence-members-0
        [Fact]
        public async Task RSP3_GetPresenceMembers()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);
            var result = await channel.Presence.GetAsync();

            // "result IS PaginatedResult" is static here — see note 2 on the class.
            result.Should().NotBeNull();
            result.Items.Count.Should().BeGreaterOrEqualTo(5, "at least the non-encrypted fixtures");

            var clientIds = result.Items.Select(member => member.ClientId).ToList();
            clientIds.Should().Contain("client_bool");
            clientIds.Should().Contain("client_string");
            clientIds.Should().Contain("client_json");
        }

        // UTS: rest/integration/RSP3/presence-message-fields-1
        [Fact]
        public async Task RSP3_PresenceMessageFields()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);
            var result = await channel.Presence.GetAsync();

            var member = result.Items.FirstOrDefault(message => message.ClientId == "client_string");

            member.Should().NotBeNull();
            member.Should().BeOfType<PresenceMessage>();
            member.Action.Should().Be(PresenceAction.Present);
            member.ClientId.Should().Be("client_string");
            member.Data.Should().Be(FixtureStringData);
            member.ConnectionId.Should().NotBeNull();
        }

        // UTS: rest/integration/RSP3a1/get-with-limit-0
        [Fact]
        public async Task RSP3a1_GetWithLimit()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);

            // Request with small limit.
            var result = await channel.Presence.GetAsync(limit: 2);

            result.Items.Count.Should().BeLessOrEqualTo(2);

            // If more members exist, pagination should be available.
            if (result.HasNext)
            {
                result.Items.Count.Should().Be(2);
            }
        }

        // UTS: rest/integration/RSP3a2/get-with-clientid-filter-0
        [Fact]
        public async Task RSP3a2_GetWithClientIdFilter()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);
            var result = await channel.Presence.GetAsync(clientId: "client_json");

            result.Items.Should().HaveCount(1);
            result.Items[0].ClientId.Should().Be("client_json");

            // The fixture has no encoding field, so data is returned as a raw string.
            result.Items[0].Data.Should().BeOfType<string>();
            result.Items[0].Data.Should().Be(FixtureJsonStringData);
        }

        // UTS: rest/integration/RSP3/get-empty-channel-2
        [Fact]
        public async Task RSP3_GetEmptyChannel()
        {
            var client = await SandboxRestClient();

            // Use a unique channel name that has no presence members.
            var channelName = $"presence-empty-{UtsSandbox.RandomId()}";
            var channel = client.Channels.Get(channelName);

            var result = await channel.Presence.GetAsync();

            // "result.items IS List" is static here — Items is a List of PresenceMessage.
            result.Items.Should().NotBeNull();
            result.Items.Should().BeEmpty();
            result.HasNext.Should().BeFalse();
        }

        // UTS: rest/integration/RSP4/history-returns-events-0
        [Fact]
        public async Task RSP4_HistoryReturnsEvents()
        {
            var client = await SandboxRestClient();

            var channelName = $"presence-history-{UtsSandbox.RandomId()}";

            // Use a realtime client to generate presence history.
            var realtime = await SandboxRealtimeClient(configure: options =>
                options.ClientId = $"test-client-{UtsSandbox.RandomId()}");

            try
            {
                await AwaitConnectionState(realtime.Connection, ConnectionState.Connected);

                var realtimeChannel = realtime.Channels.Get(channelName);

                var entered = await realtimeChannel.Presence.EnterAsync("entered");
                entered.IsSuccess.Should().BeTrue("enter must succeed: {0}", entered.Error);

                var updated = await realtimeChannel.Presence.UpdateAsync("updated");
                updated.IsSuccess.Should().BeTrue("update must succeed: {0}", updated.Error);

                var left = await realtimeChannel.Presence.LeaveAsync("left");
                left.IsSuccess.Should().BeTrue("leave must succeed: {0}", left.Error);
            }
            finally
            {
                realtime.Close();
                await AwaitConnectionState(realtime.Connection, ConnectionState.Closed);
            }

            // Poll REST history until events appear.
            var restChannel = client.Channels.Get(channelName);

            Func<Task<PaginatedResult<PresenceMessage>>> fetchHistory = async () =>
            {
                var result = await restChannel.Presence.HistoryAsync();
                return result.Items.Count >= 3 ? result : null;
            };

            var history = await UtsSandbox.WallClockPollUntil(
                fetchHistory,
                "presence history to carry the enter, the update and the leave",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500));

            history.Items.Count.Should().BeGreaterOrEqualTo(3);

            // Check for expected actions (order depends on direction).
            var actions = history.Items.Select(message => message.Action).ToList();
            actions.Should().Contain(PresenceAction.Enter);
            actions.Should().Contain(PresenceAction.Update);
            actions.Should().Contain(PresenceAction.Leave);
        }

        // UTS: rest/integration/RSP4b1/history-time-range-0
        [Fact]
        public async Task RSP4b1_HistoryTimeRange()
        {
            var client = await SandboxRestClient(configure: options =>
                options.ClientId = $"test-client-{UtsSandbox.RandomId()}");

            var channelName = $"presence-history-time-{UtsSandbox.RandomId()}";

            // Record time before any presence events.
            //
            // The spec writes now_millis(), i.e. the test runner's clock — but the timestamps the
            // bounds are matched against are the *server's*, and the two need not agree. Measured:
            // with a local clock a little ahead of the sandbox, Start = local-now lands after the
            // events and the range query returns nothing while an unbounded history returns both.
            // Taking the bounds from the server's own clock expresses the same window the spec
            // means — "around the events" — without depending on the machine's clock being right.
            var timeBefore = (await client.TimeAsync()).AddSeconds(-5);

            // Generate presence events via realtime.
            var realtime = await SandboxRealtimeClient(configure: options =>
                options.ClientId = $"time-test-client-{UtsSandbox.RandomId()}");

            try
            {
                await AwaitConnectionState(realtime.Connection, ConnectionState.Connected);

                var realtimeChannel = realtime.Channels.Get(channelName);

                var entered = await realtimeChannel.Presence.EnterAsync("test");
                entered.IsSuccess.Should().BeTrue("enter must succeed: {0}", entered.Error);

                var left = await realtimeChannel.Presence.LeaveAsync();
                left.IsSuccess.Should().BeTrue("leave must succeed: {0}", left.Error);
            }
            finally
            {
                realtime.Close();
                await AwaitConnectionState(realtime.Connection, ConnectionState.Closed);
            }

            var timeAfter = (await client.TimeAsync()).AddSeconds(5);

            // Poll until events appear.
            var restChannel = client.Channels.Get(channelName);

            Func<Task<bool>> historyHasBothEvents = async () =>
                (await restChannel.Presence.HistoryAsync()).Items.Count >= 2;

            await UtsSandbox.WallClockPollUntil(
                historyHasBothEvents,
                "presence history to carry the enter and the leave",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500));

            // Query with time range.
            var history = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = timeBefore,
                End = timeAfter,
            });

            history.Items.Count.Should().BeGreaterOrEqualTo(2);
        }

        // UTS: rest/integration/RSP4b2/history-direction-forwards-0
        [Fact]
        public async Task RSP4b2_HistoryDirectionForwards()
        {
            var client = await SandboxRestClient();

            var channelName = $"presence-direction-{UtsSandbox.RandomId()}";

            // Generate ordered presence events.
            var realtime = await SandboxRealtimeClient(configure: options =>
                options.ClientId = $"direction-client-{UtsSandbox.RandomId()}");

            try
            {
                await AwaitConnectionState(realtime.Connection, ConnectionState.Connected);

                var realtimeChannel = realtime.Channels.Get(channelName);

                var entered = await realtimeChannel.Presence.EnterAsync("first");
                entered.IsSuccess.Should().BeTrue("enter must succeed: {0}", entered.Error);

                var second = await realtimeChannel.Presence.UpdateAsync("second");
                second.IsSuccess.Should().BeTrue("update must succeed: {0}", second.Error);

                var third = await realtimeChannel.Presence.UpdateAsync("third");
                third.IsSuccess.Should().BeTrue("update must succeed: {0}", third.Error);
            }
            finally
            {
                realtime.Close();
                await AwaitConnectionState(realtime.Connection, ConnectionState.Closed);
            }

            // Poll until events appear.
            var restChannel = client.Channels.Get(channelName);

            Func<Task<bool>> historyHasThreeEvents = async () =>
                (await restChannel.Presence.HistoryAsync()).Items.Count >= 3;

            await UtsSandbox.WallClockPollUntil(
                historyHasThreeEvents,
                "presence history to carry the three ordered events",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500));

            // Get history forwards (oldest first).
            var historyForwards = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Direction = QueryDirection.Forwards,
            });

            historyForwards.Items.Count.Should().BeGreaterOrEqualTo(3);
            historyForwards.Items[0].Data.Should().Be("first");

            // Get history backwards (newest first) - default.
            //
            // NOTE: the spec never leaves the presence set, and Ably synthesises a leave for every
            // member of a connection that closes. That leave carries no data and is newer than
            // "third", so the newest item here is very likely the synthesised leave rather than the
            // last update. Translated as the spec has it; reported as a suspected spec error.
            var historyBackwards = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Direction = QueryDirection.Backwards,
            });

            historyBackwards.Items[0].Data.Should().Be("third");
        }

        // UTS: rest/integration/RSP4b3/history-limit-pagination-0
        [Fact]
        public async Task RSP4b3_HistoryLimitPagination()
        {
            var client = await SandboxRestClient();

            var channelName = $"presence-limit-{UtsSandbox.RandomId()}";

            // Generate multiple presence events.
            var realtime = await SandboxRealtimeClient(configure: options =>
                options.ClientId = $"limit-client-{UtsSandbox.RandomId()}");

            try
            {
                await AwaitConnectionState(realtime.Connection, ConnectionState.Connected);

                var realtimeChannel = realtime.Channels.Get(channelName);
                for (var i = 1; i <= 5; i++)
                {
                    var update = await realtimeChannel.Presence.UpdateAsync($"update-{i}");
                    update.IsSuccess.Should().BeTrue("update must succeed: {0}", update.Error);
                }
            }
            finally
            {
                realtime.Close();
                await AwaitConnectionState(realtime.Connection, ConnectionState.Closed);
            }

            // Poll until all events appear.
            var restChannel = client.Channels.Get(channelName);

            Func<Task<bool>> historyHasFiveEvents = async () =>
                (await restChannel.Presence.HistoryAsync()).Items.Count >= 5;

            await UtsSandbox.WallClockPollUntil(
                historyHasFiveEvents,
                "presence history to carry the five updates",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500));

            // Request with small limit.
            var page1 = await restChannel.Presence.HistoryAsync(new PaginatedRequestParams
            {
                Limit = 2,
            });

            page1.Items.Should().HaveCount(2);
            page1.HasNext.Should().BeTrue();

            // Get next page.
            var page2 = await page1.NextAsync();

            page2.Should().NotBeNull();
            page2.Items.Count.Should().BeGreaterOrEqualTo(1);
        }

        // UTS: rest/integration/RSP5/decode-string-data-0
        [Fact]
        public async Task RSP5_DecodeStringData()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);
            var result = await channel.Presence.GetAsync(clientId: "client_string");

            result.Items.Should().HaveCount(1);
            result.Items[0].Data.Should().BeOfType<string>();
            result.Items[0].Data.Should().Be(FixtureStringData);
        }

        // UTS: rest/integration/RSP5/decode-json-data-1
        [Fact]
        public async Task RSP5_DecodeJsonData()
        {
            var client = await SandboxRestClient();

            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);
            var result = await channel.Presence.GetAsync(clientId: "client_decoded");

            result.Items.Should().HaveCount(1);

            // The spec's "Object/Map" is a JObject in .NET: JsonHelper.Deserialize with no target
            // type hands back Newtonsoft's object model.
            result.Items[0].Data.Should().BeOfType<JObject>();

            var data = (JObject)result.Items[0].Data;
            data["example"]["json"].Value<string>().Should().Be("Object");
        }

        // UTS: rest/integration/RSP5/decode-encrypted-data-2
        [Fact]
        public async Task RSP5_DecodeEncryptedData()
        {
            var client = await SandboxRestClient();

            var cipherKey = Convert.FromBase64String(FixtureCipherKeyBase64);

            // The spec's keyLength: 128 is derived rather than passed here — CipherParams.KeyLength
            // is Key.Length * 8, which is 128 for this key. No IV is needed to decrypt either: the
            // ciphertext carries its own.
            var channel = client.Channels.Get(
                UtsSandbox.PresenceFixturesChannel,
                new ChannelOptions(new CipherParams("aes", cipherKey, CipherMode.CBC)));

            var result = await channel.Presence.GetAsync(clientId: "client_encoded");

            // The encrypted fixture should be decrypted.
            result.Items.Should().HaveCount(1);
            result.Items[0].Data.Should().NotBeNull();
        }

        // UTS: rest/integration/RSP5/decode-history-messages-3
        [Fact]
        public async Task RSP5_DecodeHistoryMessages()
        {
            var client = await SandboxRestClient();

            var channelName = $"presence-decode-history-{UtsSandbox.RandomId()}";

            // Generate a presence event with JSON data.
            var realtime = await SandboxRealtimeClient(configure: options =>
                options.ClientId = $"decode-client-{UtsSandbox.RandomId()}");

            var jsonData = new JObject
            {
                ["key"] = "value",
                ["number"] = 123,
            };

            try
            {
                await AwaitConnectionState(realtime.Connection, ConnectionState.Connected);

                var realtimeChannel = realtime.Channels.Get(channelName);

                var entered = await realtimeChannel.Presence.EnterAsync(jsonData);
                entered.IsSuccess.Should().BeTrue("enter must succeed: {0}", entered.Error);
            }
            finally
            {
                realtime.Close();
                await AwaitConnectionState(realtime.Connection, ConnectionState.Closed);
            }

            // Poll and retrieve history.
            var restChannel = client.Channels.Get(channelName);

            Func<Task<PaginatedResult<PresenceMessage>>> fetchHistory = async () =>
            {
                var result = await restChannel.Presence.HistoryAsync();
                return result.Items.Count >= 1 ? result : null;
            };

            var history = await UtsSandbox.WallClockPollUntil(
                fetchHistory,
                "presence history to carry the entered member",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(500));

            // NOTE: the spec never leaves the presence set and then reads items[0] of a backwards
            // query, so the synthesised leave Ably publishes when the connection closes can take
            // that slot with a null data. Translated as the spec has it; reported as a suspected
            // spec error with the same root cause as RSP4b2.
            history.Items[0].Data.Should().BeOfType<JObject>();

            var data = (JObject)history.Items[0].Data;
            data["key"].Value<string>().Should().Be("value");
            data["number"].Value<int>().Should().Be(123);
        }

        // UTS: rest/integration/RSP3/full-pagination-3
        [Fact]
        public async Task RSP3_FullPagination()
        {
            var client = await SandboxRestClient();

            // The fixture channel has multiple members.
            var channel = client.Channels.Get(UtsSandbox.PresenceFixturesChannel);

            // Request with small limit to force pagination.
            var page1 = await channel.Presence.GetAsync(limit: 2);

            var allMembers = new List<PresenceMessage>();
            allMembers.AddRange(page1.Items);

            var currentPage = page1;

            // The spec's unbounded WHILE current_page.hasNext() is walked with a safety bound: the
            // loop is driven by the server's Link headers, and an integration leg that hangs is
            // worse than one that fails. Six fixture members at limit 2 need three pages.
            var pagesFetched = 1;
            while (currentPage.HasNext && pagesFetched < 20)
            {
                currentPage = await currentPage.NextAsync();
                allMembers.AddRange(currentPage.Items);
                pagesFetched++;
            }

            // Should have retrieved all fixture members.
            allMembers.Count.Should().BeGreaterOrEqualTo(5);

            // Verify no duplicates.
            var clientIds = allMembers.Select(member => member.ClientId).ToList();
            clientIds.Distinct().Count().Should().Be(clientIds.Count);
        }

        // UTS: rest/integration/RSP3/invalid-credentials-rejected-4
        [Fact]
        public async Task RSP3_InvalidCredentialsRejected()
        {
            var client = await SandboxRestClient(key: "invalid.key:secret");

            var channel = client.Channels.Get($"test-{UtsSandbox.RandomId()}");

            Func<Task> act = () => channel.Presence.GetAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.StatusCode.Should().NotBeNull();
            ((int)error.StatusCode.Value).Should().Be(401);
            error.Code.Should().BeGreaterOrEqualTo(40100);
            error.Code.Should().BeLessThan(40200);
        }

        // UTS: rest/integration/RSP3/subscribe-capability-sufficient-5
        [Fact]
        public async Task RSP3_SubscribeCapabilitySufficient()
        {
            // Use the key with limited capabilities (keys[3] has subscribe only).
            var sandbox = await Sandbox();
            var client = await SandboxRestClient(key: sandbox.Key(3).KeyStr);

            // This should work - subscribe capability is sufficient for presence.get.
            var result = await client.Channels.Get(UtsSandbox.PresenceFixturesChannel).Presence.GetAsync();

            result.Should().NotBeNull();
        }
    }
}
