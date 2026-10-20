// ChannelOptions and Derived Channels Tests (TB2, TB3, TB4, RTS3b, RTS3c, RTS3c1, RTS5, RTL16)
// Derived from uts/realtime/unit/channels/channel_options.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: dummy key ValidKey where the spec uses key "appId.keyId:keySecret"; the mock answers CONNECTED and ATTACHED (infra-driving) in the tests that attach, which the spec leaves implicit; SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
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
    public class ChannelOptionsTests : UtsUnitTestBase
    {
        // 256-bit key as base64
        private const string CipherKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";

        public ChannelOptionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // A mock that connects and answers every ATTACH with ATTACHED and every DETACH with DETACHED.
        private MockWebSocket ConnectingAttachingMock()
        {
            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn => conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage()),
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(msg.Channel));
                    }
                    else if ((int)msg.Action == ProtocolMessages.Detach)
                    {
                        mockWs.SendToClient(ProtocolMessages.DetachedMessage(msg.Channel));
                    }
                });
            return mockWs;
        }

        // UTS: realtime/unit/TB2/channel-options-attributes-0
        [Fact]
        [Trait("spec", "TB2")]
        public void TB2_ChannelOptionsAttributes()
        {
            // Setup
            var options = new ChannelOptions();

            // Assertions
            // DEVIATION: spec says cipherParams, params and modes are null; the SDK defaults CipherParams to Crypto.GetDefaultParams() with Encrypted false, and Params and Modes to empty collections - see deviations.md
            options.Encrypted.Should().BeFalse();
            options.CipherParams.Should().NotBeNull();
            options.Params.Should().BeEmpty();
            options.Modes.Should().BeEmpty();

            // NOTE: attachOnSubscribe (TB4) is not in this SDK: no assertion is emitted
            // ASSERT options.attachOnSubscribe == true
        }

        // UTS: realtime/unit/TB2c/options-with-params-0
        [Fact]
        [Trait("spec", "TB2c")]
        public void TB2c_OptionsWithParams()
        {
            // Setup
            var options = new ChannelOptions(channelParams: new ChannelParams { ["rewind"] = "1", ["delta"] = "vcdiff" });

            // Assertions
            options.Params["rewind"].Should().Be("1");
            options.Params["delta"].Should().Be("vcdiff");
        }

        // UTS: realtime/unit/TB2d/options-with-modes-0
        [Fact]
        [Trait("spec", "TB2d")]
        public void TB2d_OptionsWithModes()
        {
            // Setup
            var options = new ChannelOptions(modes: new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe));

            // Assertions
            options.Modes.Should().Contain(ChannelMode.Publish);
            options.Modes.Should().Contain(ChannelMode.Subscribe);
            options.Modes.Should().HaveCount(2);
        }

        // UTS: realtime/unit/TB3/with-cipher-key-0
        [Fact]
        [Trait("spec", "TB3")]
        public void TB3_WithCipherKey()
        {
            // Setup
            // 256-bit key as base64
            var options = new ChannelOptions(Convert.FromBase64String(CipherKey));

            // Assertions
            options.CipherParams.Should().NotBeNull();
            // NOTE: features TZ2a spells the default algorithm "AES"; the spec's lowercase "aes" is compared case-insensitively
            options.CipherParams.Algorithm.Should().BeEquivalentTo("aes");
            options.CipherParams.KeyLength.Should().Be(256);
        }

        // UTS: realtime/unit/TB4/attach-on-subscribe-default-0
        [Fact(Skip = "Missing SDK public API: ChannelOptions#attachOnSubscribe (TB4); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "TB4")]
        public void TB4_AttachOnSubscribeDefault()
        {
            // NOTE: the whole test is about the attachOnSubscribe option, which ChannelOptions does not have
            // options1 = RealtimeChannelOptions()
            // options2 = RealtimeChannelOptions(attachOnSubscribe: false)
            // ASSERT options1.attachOnSubscribe == true
            // ASSERT options2.attachOnSubscribe == false
        }

        // UTS: realtime/unit/RTS3b/options-set-on-new-0
        [UtsFact]
        [Trait("spec", "RTS3b")]
        public async Task RTS3b_OptionsSetOnNew()
        {
            // Setup
            var channelName = "test-RTS3b-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            var channelOptions = new ChannelOptions(
                channelParams: new ChannelParams { ["rewind"] = "1" },
                modes: new ChannelModes(ChannelMode.Subscribe));

            // Test Steps
            var channel = client.Channels.Get(channelName, channelOptions);

            // Assertions
            channel.Options.Params["rewind"].Should().Be("1");
            channel.Options.Modes.Should().Contain(ChannelMode.Subscribe);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3c/options-updated-existing-0
        [UtsFact]
        [Trait("spec", "RTS3c")]
        public async Task RTS3c_OptionsUpdatedExisting()
        {
            // Setup
            var channelName = "test-RTS3c-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            // Create channel with initial options
            // NOTE: attachOnSubscribe (TB4) is not in this SDK; the initial options are RealtimeChannelOptions(attachOnSubscribe: false) in the spec, rendered as default options
            var initialOptions = new ChannelOptions();
            var channel = client.Channels.Get(channelName, initialOptions);

            // Test Steps
            // Update with new options that don't require reattachment
            // NOTE: the spec's someKey is not defined; the 256-bit key of TB3 is used
            var newOptions = new ChannelOptions(new CipherParams(Convert.FromBase64String(CipherKey)));
            var sameChannel = client.Channels.Get(channelName, newOptions);

            // Assertions
            sameChannel.Should().BeSameAs(channel);
            channel.Options.CipherParams.Should().NotBeNull();

            // NOTE: attachOnSubscribe (TB4) is not in this SDK: no assertion is emitted
            // ASSERT channel.options.attachOnSubscribe == true
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3c1/error-reattach-params-0
        [UtsFact]
        [Trait("spec", "RTS3c1")]
        public async Task RTS3c1_ErrorReattachParams()
        {
            // Setup
            var channelName = "test-RTS3c1-" + Guid.NewGuid().ToString("N");

            // NOTE: the spec installs no mock but awaits attach; the mock answers CONNECTED and ATTACHED and the client connects first (infra-driving)
            var mockWs = ConnectingAttachingMock();
            InstallMock(mockWs);
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Create and attach channel
            var channel = client.Channels.Get(channelName);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Test Steps
            // Try to update with options that require reattachment
            // params triggers reattachment
            var newOptions = new ChannelOptions(channelParams: new ChannelParams { ["rewind"] = "1" });

            Action get = () => client.Channels.Get(channelName, newOptions);
            var error = get.Should().Throw<AblyException>().Which.ErrorInfo;
            error.Code.Should().Be(40000);

            // Channel options should not have changed
            // DEVIATION: spec says params IS null; the SDK's default ChannelOptions.Params is an empty collection - see deviations.md (TB2)
            channel.Options.Params.Should().BeEmpty();
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS3c1/error-reattach-modes-1
        [UtsFact]
        [Trait("spec", "RTS3c1")]
        public async Task RTS3c1_ErrorReattachModes()
        {
            // Setup
            var channelName = "test-RTS3c1-attaching-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });

            var channel = client.Channels.Get(channelName);

            // Put channel in attaching state (implementation detail)
            channel.Attach();
            await UtsWait.AwaitChannelState(channel, ChannelState.Attaching);

            // Test Steps
            // modes triggers reattachment
            var newOptions = new ChannelOptions(modes: new ChannelModes(ChannelMode.Subscribe));

            Action get = () => client.Channels.Get(channelName, newOptions);
            var error = get.Should().Throw<AblyException>().Which.ErrorInfo;
            error.Code.Should().Be(40000);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL16/set-options-updates-0
        [UtsFact]
        [Trait("spec", "RTL16")]
        public async Task RTL16_SetOptionsUpdates()
        {
            // Setup
            var channelName = "test-RTL16-" + Guid.NewGuid().ToString("N");

            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            // NOTE: attachOnSubscribe (TB4) is not in this SDK; the new options carry only the params
            var newOptions = new ChannelOptions(channelParams: new ChannelParams { ["delta"] = "vcdiff" });
            var setOptionsResult = await UtsWait.WithTimeout(channel.SetOptionsAsync(newOptions), TimeSpan.FromSeconds(10), "SetOptionsAsync");
            setOptionsResult.IsSuccess.Should().BeTrue("AWAIT channel.setOptions() succeeds ({0})", setOptionsResult.Error?.Message);

            // Assertions
            channel.Options.Params["delta"].Should().Be("vcdiff");

            // NOTE: attachOnSubscribe (TB4) is not in this SDK: no assertion is emitted
            // ASSERT channel.options.attachOnSubscribe == false
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTL16a/triggers-reattach-0
        [UtsFact]
        [Trait("spec", "RTL16a")]
        public async Task RTL16a_TriggersReattach()
        {
            // Setup
            var channelName = "test-RTL16a-" + Guid.NewGuid().ToString("N");

            // NOTE: the spec installs no mock but awaits attach; the mock answers CONNECTED and ATTACHED and the client connects first (infra-driving)
            var mockWs = ConnectingAttachingMock();
            InstallMock(mockWs);
            var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(channelName);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);
            channel.State.Should().Be(ChannelState.Attached);

            // Test Steps
            var stateChanges = UtsRecord.ChannelStateChanges(channel);

            var newOptions = new ChannelOptions(channelParams: new ChannelParams { ["rewind"] = "1" });
            var setOptionsResult = await UtsWait.WithTimeout(channel.SetOptionsAsync(newOptions), TimeSpan.FromSeconds(10), "SetOptionsAsync");
            setOptionsResult.IsSuccess.Should().BeTrue("AWAIT channel.setOptions() succeeds ({0})", setOptionsResult.Error?.Message);

            // Assertions
            // Should have gone through attaching state
            stateChanges.Snapshot().Should().Contain(change => change.Current == ChannelState.Attaching);
            channel.State.Should().Be(ChannelState.Attached);
            channel.Options.Params["rewind"].Should().Be("1");
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTS5a/creates-derived-channel-0
        [Fact(Skip = "Missing SDK public API: Channels#getDerived (RTS5); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTS5a")]
        public void RTS5a_CreatesDerivedChannel()
        {
            // NOTE: getDerived and DeriveOptions do not exist in this SDK
            // base_channel_name = "test-RTS5a-${random_id()}"
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
            // deriveOptions = DeriveOptions(filter: "name == 'foo'")
            // channel = client.channels.getDerived(base_channel_name, deriveOptions)
            // # Channel name should be encoded with filter
            // ASSERT channel.name STARTS WITH "[filter="
            // ASSERT channel.name ENDS WITH "]" + base_channel_name
        }

        // UTS: realtime/unit/RTS5a1/filter-base64-encoded-0
        [Fact(Skip = "Missing SDK public API: Channels#getDerived (RTS5); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTS5a1")]
        public void RTS5a1_FilterBase64Encoded()
        {
            // NOTE: getDerived and DeriveOptions do not exist in this SDK
            // filter = "name == 'test'"
            // deriveOptions = DeriveOptions(filter: filter)
            // channel = client.channels.getDerived(base_channel_name, deriveOptions)
            // expectedEncoded = base64_encode(filter)  # "bmFtZSA9PSAndGVzdCc="
            // ASSERT channel.name == "[filter=" + expectedEncoded + "]" + base_channel_name
        }

        // UTS: realtime/unit/RTS5a2/derived-with-params-0
        [Fact(Skip = "Missing SDK public API: Channels#getDerived (RTS5); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTS5a2")]
        public void RTS5a2_DerivedWithParams()
        {
            // NOTE: getDerived and DeriveOptions do not exist in this SDK
            // deriveOptions = DeriveOptions(filter: "type == 'message'")
            // channelOptions = RealtimeChannelOptions(params: {"rewind": "1", "delta": "vcdiff"})
            // channel = client.channels.getDerived(base_channel_name, deriveOptions, channelOptions)
            // # Expected format: [filter=<base64>?param1=val1&param2=val2]baseName
            // ASSERT channel.name ENDS WITH "]" + base_channel_name
            // qualifier = extract_between(channel.name, "[", "]")
            // ASSERT qualifier STARTS WITH "filter="
            // IF qualifier CONTAINS "?": parse the params after the ? and assert rewind == "1", delta == "vcdiff", length == 2
            // ELSE: FAIL("Expected params in qualifier")
        }

        // UTS: realtime/unit/RTS5/get-derived-with-options-0
        [Fact(Skip = "Missing SDK public API: Channels#getDerived (RTS5); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RTS5")]
        public void RTS5_GetDerivedWithOptions()
        {
            // NOTE: getDerived and DeriveOptions do not exist in this SDK
            // deriveOptions = DeriveOptions(filter: "true")
            // channelOptions = RealtimeChannelOptions(modes: [ChannelMode.subscribe], attachOnSubscribe: false)
            // channel = client.channels.getDerived(base_channel_name, deriveOptions, channelOptions)
            // ASSERT channel.options.modes CONTAINS ChannelMode.subscribe
            // ASSERT channel.options.attachOnSubscribe == false
        }

        // UTS: realtime/unit/DO2a/filter-attribute-0
        [Fact(Skip = "Missing SDK public API: DeriveOptions (DO2a); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "DO2a")]
        public void DO2a_FilterAttribute()
        {
            // NOTE: DeriveOptions does not exist in this SDK
            // deriveOptions = DeriveOptions(filter: "name == 'event' && data.count > 10")
            // ASSERT deriveOptions.filter == "name == 'event' && data.count > 10"
        }
    }
}
