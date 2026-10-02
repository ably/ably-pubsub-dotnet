using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Channels
{
    /// <summary>
    /// Derived from uts/realtime/unit/channels/channel_options.md in ably/specification.
    ///
    /// Spec points: TB2, TB2c, TB2d, TB3, RTS3b, RTS3c, RTS3c1, RTL16, RTL16a
    ///
    /// <para>
    /// Channel options divide into the ones the server never sees - the cipher - and the ones it
    /// does: params and modes travel on the ATTACH, which is why changing them on a live channel
    /// has to reattach it, and why <c>Channels.Get</c> refuses to be the thing that does.
    /// </para>
    ///
    /// <para>
    /// Six of the file's sixteen tests are not translated, all for missing API rather than failing
    /// behaviour. <c>TB4/attach-on-subscribe-default-0</c> needs <c>attachOnSubscribe</c>, which
    /// this SDK's <c>ChannelOptions</c> does not have - <c>Subscribe</c> attaches unconditionally -
    /// and the five derived-channel tests (<c>RTS5a</c>, <c>RTS5a1</c>, <c>RTS5a2</c>, <c>RTS5</c>,
    /// <c>DO2a</c>) need <c>channels.getDerived</c> and <c>DeriveOptions</c>, neither of which
    /// exists here. Two further tests, RTS3c and RTL16, keep their remaining assertions and lose
    /// only their <c>attachOnSubscribe</c> lines. See Uts/coverage.md.
    /// </para>
    ///
    /// <para>
    /// A4: the spec's "unset means null" defaults do not hold here. <c>Params</c> and <c>Modes</c>
    /// are empty collections rather than null, and <c>CipherParams</c> is always populated, with
    /// <c>Encrypted</c> carrying the "is a cipher configured" answer instead. See Uts/deviations.md.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class ChannelOptionsTests : UtsTestBase
    {
        /// <summary>
        /// The spec's 256-bit key, base64 as it hands it over.
        /// </summary>
        private const string CipherKeyBase64 = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";

        public ChannelOptionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/TB2/channel-options-attributes-0
        //
        // ADAPTED, A4. The spec asserts the three attributes are null when unset. Here they are
        // never null: Params and Modes are empty collections, and CipherParams is populated with
        // Crypto.GetDefaultParams() regardless, with Encrypted saying whether it means anything.
        // The assertions below are the same question in this SDK's terms - nothing is configured.
        [Fact]
        public void TB2_ChannelOptionsAttributeDefaults()
        {
            var options = new ChannelOptions();

            options.Encrypted.Should().BeFalse("TB2b - no cipher was configured");
            options.Params.Should().BeEmpty("TB2c - no params were configured");
            options.Modes.Should().BeEmpty("TB2d - no modes were configured");
        }

        // UTS: realtime/unit/TB2c/options-with-params-0
        [Fact]
        public void TB2c_ChannelOptionsWithParams()
        {
            var options = new ChannelOptions
            {
                Params = new ChannelParams { { "rewind", "1" }, { "delta", "vcdiff" } },
            };

            options.Params["rewind"].Should().Be("1");
            options.Params["delta"].Should().Be("vcdiff");
        }

        // UTS: realtime/unit/TB2d/options-with-modes-0
        [Fact]
        public void TB2d_ChannelOptionsWithModes()
        {
            var options = new ChannelOptions
            {
                Modes = new ChannelModes(ChannelMode.Publish, ChannelMode.Subscribe),
            };

            options.Modes.Should().Contain(ChannelMode.Publish);
            options.Modes.Should().Contain(ChannelMode.Subscribe);
            options.Modes.Should().HaveCount(2);
        }

        // UTS: realtime/unit/TB3/with-cipher-key-0
        //
        // ADAPTED. The spec's withCipherKey() takes the key base64-encoded; this SDK's key-only
        // constructor takes the raw bytes, so the test decodes first. Same key, same question.
        [Fact]
        public void TB3_WithCipherKeyConstructor()
        {
            var options = new ChannelOptions(Convert.FromBase64String(CipherKeyBase64));

            options.Encrypted.Should().BeTrue();
            options.CipherParams.Should().NotBeNull();
            options.CipherParams.Algorithm.Should().Be(Crypto.DefaultAlgorithm);
            options.CipherParams.KeyLength.Should().Be(256);
        }

        // UTS: realtime/unit/RTS3b/options-set-on-new-0
        [Fact]
        public void RTS3b_OptionsAreSetOnANewChannel()
        {
            const string ChannelName = "test-RTS3b";

            var client = RealtimeClient(ConnectingMock());

            var channelOptions = new ChannelOptions
            {
                Params = new ChannelParams { { "rewind", "1" } },
                Modes = new ChannelModes(ChannelMode.Subscribe),
            };

            var channel = client.Channels.Get(ChannelName, channelOptions);

            channel.Options.Params["rewind"].Should().Be("1");
            channel.Options.Modes.Should().Contain(ChannelMode.Subscribe);
        }

        // UTS: realtime/unit/RTS3c/options-updated-existing-0
        //
        // The spec's second assertion is on attachOnSubscribe, which does not exist here; the
        // cipher half is what remains, and it is the half that makes the point - options that need
        // no reattachment are updated in place on the same channel object.
        [Fact]
        public void RTS3c_OptionsAreUpdatedOnAnExistingChannel()
        {
            const string ChannelName = "test-RTS3c";

            var client = RealtimeClient(ConnectingMock());

            var channel = client.Channels.Get(ChannelName);
            channel.Options.Encrypted.Should().BeFalse();

            var newOptions = new ChannelOptions(Convert.FromBase64String(CipherKeyBase64));
            var sameChannel = client.Channels.Get(ChannelName, newOptions);

            sameChannel.Should().BeSameAs(channel, "RTS3c - the channel is reused, not replaced");
            channel.Options.Encrypted.Should().BeTrue();
            channel.Options.CipherParams.KeyLength.Should().Be(256);
        }

        // UTS: realtime/unit/RTS3c1/error-reattach-params-0
        [Fact]
        public async Task RTS3c1_GetWithParamsThatWouldReattachThrows()
        {
            const string ChannelName = "test-RTS3c1-params";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var newOptions = new ChannelOptions
            {
                Params = new ChannelParams { { "rewind", "1" } },
            };

            var error = Assert.Throws<AblyException>(
                () => client.Channels.Get(ChannelName, newOptions));

            error.ErrorInfo.Code.Should().Be(40000);

            channel.Options.Params.Should().BeEmpty(
                "RTS3c1 - the rejected options were not applied");
        }

        // UTS: realtime/unit/RTS3c1/error-reattach-modes-1
        [Fact]
        public async Task RTS3c1_GetWithModesThatWouldReattachThrowsWhileAttaching()
        {
            const string ChannelName = "test-RTS3c1-modes";

            // The attach is never answered, so the channel is left ATTACHING for the whole test -
            // which is the state RTS3c1 is about here. The deadline is pushed out so the channel
            // cannot time out into SUSPENDED underneath the assertion.
            var mockWs = ConnectingMock();
            var client = RealtimeClient(
                mockWs,
                configure: options => options.RealtimeRequestTimeout = TimeSpan.FromMinutes(10));

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            var attaching = UtsClients.AwaitChannelState(channel, ChannelState.Attaching);

            _ = channel.AttachAsync();
            await attaching;

            var newOptions = new ChannelOptions
            {
                Modes = new ChannelModes(ChannelMode.Subscribe),
            };

            var error = Assert.Throws<AblyException>(
                () => client.Channels.Get(ChannelName, newOptions));

            error.ErrorInfo.Code.Should().Be(40000);
            channel.Options.Modes.Should().BeEmpty("RTS3c1 - the rejected options were not applied");
        }

        // UTS: realtime/unit/RTL16/set-options-updates-0
        //
        // The spec's attachOnSubscribe assertion is dropped; there is no such option here.
        [Fact]
        public async Task RTL16_SetOptionsUpdatesChannelOptions()
        {
            const string ChannelName = "test-RTL16";

            var client = RealtimeClient(ConnectingMock());
            var channel = client.Channels.Get(ChannelName);

            var newOptions = new ChannelOptions
            {
                Params = new ChannelParams { { "delta", "vcdiff" } },
            };

            var result = await channel.SetOptionsAsync(newOptions);

            result.IsSuccess.Should().BeTrue();
            channel.Options.Params["delta"].Should().Be("vcdiff");
        }

        // UTS: realtime/unit/RTL16a/triggers-reattach-0
        [Fact]
        public async Task RTL16a_SetOptionsTriggersReattachmentWhenNeeded()
        {
            const string ChannelName = "test-RTL16a";

            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs);

            var attachedParams = new List<ChannelParams>();
            mockWs.OnMessageFromClient = msg =>
            {
                if (msg.Action == ProtocolMessage.MessageAction.Attach)
                {
                    attachedParams.Add(msg.Params);
                    mockWs.SendToClient(ProtocolMessages.AttachedMessage(ChannelName));
                }
            };

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var channel = client.Channels.Get(ChannelName);
            await channel.AttachAsync();
            channel.State.Should().Be(ChannelState.Attached);

            var states = UtsClients.RecordChannelStates(channel);

            var newOptions = new ChannelOptions
            {
                Params = new ChannelParams { { "rewind", "1" } },
            };

            var result = await channel.SetOptionsAsync(newOptions);

            result.IsSuccess.Should().BeTrue();
            UtsClients.Snapshot(states).Should().Contain(
                ChannelState.Attaching,
                "RTL16a - changing params on an attached channel reattaches it");

            channel.State.Should().Be(ChannelState.Attached);
            channel.Options.Params["rewind"].Should().Be("1");

            // The point of the reattach is that the server is told. Anything less and the option
            // would only have been recorded locally.
            attachedParams.Should().HaveCount(2);
            attachedParams[1].Should().ContainKey("rewind");
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
