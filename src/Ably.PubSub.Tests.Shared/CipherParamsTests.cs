using Ably.PubSub.Encryption;

using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests
{
    public class CipherParamsTests
    {
        [Fact]
        public void Ctor_WithKeyAndNoAlgorithmSpecified_DefaultsToAES()
        {
            // Act
            var cipherParams = new CipherParams(string.Empty, new byte[] { });

            // Assert
            cipherParams.Algorithm.Should().Be(Crypto.DefaultAlgorithm);
            Assert.Equal(new byte[] { }, cipherParams.Key);
        }
    }
}
