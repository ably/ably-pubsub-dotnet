using System.Collections.Generic;
using IO.Ably.Shared.Realtime;
using Xunit;

namespace IO.Ably.Tests.Shared.Realtime
{
    public class RecoveryKeyContextSpec
    {
        [Fact]
        [Trait("spec", "RTN16i")]
        [Trait("spec", "RTN16f")]
        [Trait("spec", "RTN16j")]
        public void ShouldEncodeRecoveryKeyContextObject()
        {
            const string expectedRecoveryKey =
                "{\"connectionKey\":\"uniqueKey\",\"msgSerial\":1,\"channelSerials\":{\"channel1\":\"1\",\"channel2\":\"2\",\"channel3\":\"3\"}}";
            var recoveryKey = new RecoveryKeyContext()
            {
                ChannelSerials = new Dictionary<string, string>()
                {
                    { "channel1", "1" },
                    { "channel2", "2" },
                    { "channel3", "3" },
                },
                ConnectionKey = "uniqueKey",
                MsgSerial = 1,
            };

            var encodedRecoveryKey = recoveryKey.Encode();
            Assert.Equal(expectedRecoveryKey, encodedRecoveryKey);
        }

        [Fact]
        [Trait("spec", "RTN16i")]
        [Trait("spec", "RTN16f")]
        [Trait("spec", "RTN16j")]
        public void ShouldDecodeRecoveryKeyToRecoveryKeyContextObject()
        {
            const string recoveryKey =
                "{\"connectionKey\":\"key2\",\"msgSerial\":5,\"channelSerials\":{\"channel1\":\"98\",\"channel2\":\"32\",\"channel3\":\"09\"}}";
            var recoveryKeyContext = RecoveryKeyContext.Decode(recoveryKey);
            Assert.Equal("key2", recoveryKeyContext.ConnectionKey);
            Assert.Equal(5, recoveryKeyContext.MsgSerial);
            var expectedChannelSerials = new Dictionary<string, string>()
            {
                { "channel1", "98" },
                { "channel2", "32" },
                { "channel3", "09" },
            };
            Assert.Equal(expectedChannelSerials, recoveryKeyContext.ChannelSerials);
        }

        [Fact]
        [Trait("spec", "RTN16i")]
        [Trait("spec", "RTN16f")]
        [Trait("spec", "RTN16j")]
        public void ShouldReturnNullRecoveryContextWhileDecodingFaultyRecoveryKey()
        {
            const string recoveryKey =
                "{\"connectionKey\":\"key2\",\"msgSerial\":\"incorrectStringSerial\",\"channelSerials\":{\"channel1\":\"98\",\"channel2\":\"32\",\"channel3\":\"09\"}}";
            var recoveryKeyContext = RecoveryKeyContext.Decode(recoveryKey);
            Assert.Null(recoveryKeyContext);
        }
    }
}
