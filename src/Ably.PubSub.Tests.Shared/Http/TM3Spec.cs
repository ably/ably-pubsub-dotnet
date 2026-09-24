using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using IO.Ably.Encryption;
using Xunit;

namespace IO.Ably.Tests.Rest
{
    [Trait("spec", "TM3")]
    public class TM3Spec
    {
        [Fact]
        [Trait("spec", "TM3")]
        public void Message_FromEncoded_WithNoEncoding()
        {
            var msg = new Message("name", "some-data");
            var fromEncoded = Message.FromEncoded(msg);

            fromEncoded.Name.Should().Be("name");
            fromEncoded.Data.Should().Be("some-data");
            fromEncoded.Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "TM3")]
        public void Message_FromEncoded_WithEncoding()
        {
            var d = new Dictionary<string, string>
            {
                { "foo", "bar" },
                { "baz", "qux" }
            };

            var msg = new Message("name", JsonHelper.Serialize(d)) { Encoding = "json" };
            var fromEncoded = Message.FromEncoded(msg);

            fromEncoded.Name.Should().Be("name");
            JsonHelper.Serialize(fromEncoded.Data).Should().Be(JsonHelper.Serialize(d));
            fromEncoded.Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "TM3")]
        public void Message_FromEncoded_WithCustomEncoding()
        {
            var d = new Dictionary<string, string>
            {
                { "foo", "bar" },
                { "baz", "qux" }
            };

            var msg = new Message("name", JsonHelper.Serialize(d)) { Encoding = "foo/json" };
            var fromEncoded = Message.FromEncoded(msg);

            fromEncoded.Name.Should().Be("name");
            JsonHelper.Serialize(fromEncoded.Data).Should().Be(JsonHelper.Serialize(d));
            fromEncoded.Encoding.Should().Be("foo");
        }

        [Fact]
        [Trait("spec", "TM3")]
        public void Message_FromEncoded_WithCipherEncoding()
        {
            var cipherParams = Crypto.GetDefaultParams(Crypto.GenerateRandomKey(128), null, CipherMode.CBC);

            var crypto = Crypto.GetCipher(cipherParams);
            var payload = "payload".AddRandomSuffix();

            var msg = new Message("name", crypto.Encrypt(Encoding.UTF8.GetBytes(payload))) { Encoding = "utf-8/cipher+aes-128-cbc" };
            var fromEncoded = Message.FromEncoded(msg, new ChannelOptions(cipherParams));

            fromEncoded.Name.Should().Be("name");
            fromEncoded.Data.Should().BeEquivalentTo(payload);
            fromEncoded.Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "TM3")]
        public void Message_FromEncoded_WithInvalidCipherEncoding()
        {
            var cipherParams = Crypto.GetDefaultParams(Crypto.GenerateRandomKey(128), null, CipherMode.CBC);
            var payload = "some-invalid-payload".AddRandomSuffix();

            var msg = new Message("name", Encoding.UTF8.GetBytes(payload)) { Encoding = "utf-8/cipher+aes-128-cbc" };

            Assert.Throws<AblyException>(() => Message.FromEncoded(msg, new ChannelOptions(cipherParams)));
        }

        [Fact]
        [Trait("spec", "TM3")]
        public void Message_FromEncodedArray_WithNoEncoding()
        {
            var msg = new[]
            {
                new Message("name1", "some-data1"),
                new Message("name2", "some-data2")
            };

            var fromEncoded = Message.FromEncodedArray(msg);

            fromEncoded.Should().HaveCount(2);

            fromEncoded[0].Name.Should().Be("name1");
            fromEncoded[0].Data.Should().Be("some-data1");
            fromEncoded[0].Encoding.Should().BeNullOrEmpty();

            fromEncoded[1].Name.Should().Be("name2");
            fromEncoded[1].Data.Should().Be("some-data2");
            fromEncoded[1].Encoding.Should().BeNullOrEmpty();
        }

        public class WithJsonString
        {
            [Fact]
            [Trait("spec", "TM3")]
            public void Message_FromEncoded_WithJsonString()
            {
                var d = new Dictionary<string, string>
                {
                    { "foo", "bar" },
                    { "baz", "qux" }
                };

                var msg = new Message("name", JsonHelper.Serialize(d)) { Encoding = "foo/json" };
                var msgJson = JsonHelper.Serialize(msg);
                var fromEncoded = Message.FromEncoded(msgJson);

                fromEncoded.Name.Should().Be("name");
                JsonHelper.Serialize(fromEncoded.Data).Should().Be(JsonHelper.Serialize(d));
                fromEncoded.Encoding.Should().Be("foo");
            }

            [Fact]
            [Trait("spec", "TM3")]
            public void Message_FromEncoded_WithCipherEncoding()
            {
                var cipherParams = Crypto.GetDefaultParams(Crypto.GenerateRandomKey(128), null, CipherMode.CBC);

                var crypto = Crypto.GetCipher(cipherParams);
                var payload = "payload".AddRandomSuffix();

                var encryptedString = crypto.Encrypt(payload.GetBytes());
                var msg = new Message("name", encryptedString.ToBase64()) { Encoding = "utf-8/cipher+aes-128-cbc/base64" };
                var msgJson = JsonHelper.Serialize(msg);
                var fromEncoded = Message.FromEncoded(msgJson, new ChannelOptions(cipherParams));

                fromEncoded.Name.Should().Be("name");
                fromEncoded.Data.Should().BeEquivalentTo(payload);
                fromEncoded.Encoding.Should().BeNullOrEmpty();
            }

            [Fact]
            [Trait("spec", "TM3")]
            public void Message_FromEncodedArray_WithJsonArray()
            {
                var messages = new[]
                {
                    new Message("name1", "some-data1"),
                    new Message("name2", "some-data2")
                };

                var messagesJson = JsonHelper.Serialize(messages);

                var fromEncoded = Message.FromEncodedArray(messagesJson);

                fromEncoded.Should().HaveCount(2);

                fromEncoded[0].Name.Should().Be("name1");
                fromEncoded[0].Data.Should().Be("some-data1");
                fromEncoded[0].Encoding.Should().BeNullOrEmpty();

                fromEncoded[1].Name.Should().Be("name2");
                fromEncoded[1].Data.Should().Be("some-data2");
                fromEncoded[1].Encoding.Should().BeNullOrEmpty();
            }
        }
    }
}
