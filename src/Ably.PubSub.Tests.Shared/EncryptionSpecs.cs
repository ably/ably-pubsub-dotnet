using FluentAssertions;
using IO.Ably.Encryption;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    public class EncryptionSpecs : AblySpecs
    {
        public class GetDefaultParamsSpecs : AblySpecs
        {
            private const string KeyBase64 = "WUP6u0K7MXI5Zeo0VppPwg==";
            private const string KeyBase64Url = "WUP6u0K7MXI5Zeo0VppPwg";
            private const string Key256Base64 = "o9qXZoPGDNla50VnRwH7cGqIrpyagTxGsRgimKJbY40=";
            private const string IvBase64 = "HO4cYSP8LybPYBPZPHQOtg==";

            [Fact]
            [Trait("spec", "RSE1a")]
            public void ShouldReturnCompleteCipherParamsInstance()
            {
                var result = Crypto.GetDefaultParams();
                result.Algorithm.Should().Be("AES");
                result.CipherType.Should().Be("AES-256-CBC");
                result.KeyLength.Should().Be(256);
                result.Mode.Should().Be(CipherMode.CBC);
            }

            [Fact]
            [Trait("spec", "RSE1b")]
            public void WithKeyIvAndMode_ShouldReturnCipherParamsWithThoseValuesSpecified()
            {
                var key = KeyBase64.FromBase64();
                var iv = IvBase64.FromBase64();
                const CipherMode mode = CipherMode.CFB;

                var result = Crypto.GetDefaultParams(key, iv, mode);
                result.Key.Should().BeEquivalentTo(key);
                result.Iv.Should().BeEquivalentTo(iv);
                result.Mode.Should().Be(mode);
            }

            [Fact]
            [Trait("spec", "RSE1d")]
            public void WithBinaryKey_CreatesParamWithSpecifiedKey()
            {
                var key = KeyBase64.FromBase64();
                var result = Crypto.GetDefaultParams(key);
                result.Key.Should().BeEquivalentTo(key);
                result.KeyLength.Should().Be(128);
            }

            [Fact]
            [Trait("spec", "RSE1c")]
            public void WithStringKey_ShouldConvertItFromBase64AndReturnParamsWithThatKey()
            {
                var result = Crypto.GetDefaultParams(KeyBase64);
                result.Key.Should().BeEquivalentTo(KeyBase64.FromBase64());
                result.KeyLength.Should().Be(128);
            }

            [Fact]
            [Trait("spec", "RSE1c")]
            public void WithBase64UrlKey_ShouldConvertCorrectly()
            {
                var result = Crypto.GetDefaultParams(KeyBase64Url);
                result.Key.Should().BeEquivalentTo(KeyBase64Url.FromBase64());
                result.KeyLength.Should().Be(128);
            }

            [Fact]
            [Trait("spec", "RSE1d")]
            public void WithBinaryKey_CalculatesKeyLengthAutomatically()
            {
                var key = Key256Base64.FromBase64();
                var result = Crypto.GetDefaultParams(key);
                result.KeyLength.Should().Be(256);
            }

            [Fact]
            [Trait("spec", "RSE1e")]
            public void WithInvalidKeyLength_ShouldThrows()
            {
                Assert.Throws<AblyException>(() => Crypto.GetDefaultParams(new byte[] { 193, 24, 123 }));
            }

            public GetDefaultParamsSpecs(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class GenerateRandomKeyTests : EncryptionSpecs
        {
            [Fact]
            [Trait("spec", "RSE2a")]
            [Trait("spec", "RSE2b")]
            public void WithoutKeyLength_GeneratesAES256bitKey()
            {
                var key = Crypto.GenerateRandomKey();
                (key.Length * 8).Should().Be(256);
            }

            [Theory]
            [InlineData(128)]
            [InlineData(256)]
            [Trait("spec", "RSE2a")]
            [Trait("spec", "RSE2b")]
            public void WithKeyLength_GenerateAESKeyWithCorrectLength(int length)
            {
                var key = Crypto.GenerateRandomKey(length);
                (key.Length * 8).Should().Be(length);
            }

            [Fact]
            [Trait("spec", "RSE2a")]
            public void WithInvalidKeyLength_Throws()
            {
                Assert.Throws<AblyException>(() => Crypto.GenerateRandomKey(111));
            }

            public GenerateRandomKeyTests(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public EncryptionSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
