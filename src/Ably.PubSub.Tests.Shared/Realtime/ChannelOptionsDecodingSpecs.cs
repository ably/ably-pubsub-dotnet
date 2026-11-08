using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Infrastructure;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Realtime
{
    /// <summary>
    /// Received messages are decoded with the channel's current options, however they were changed.
    /// </summary>
    public class ChannelOptionsDecodingSpecs : AblyRealtimeSpecs
    {
        private const string ChannelName = "options-decoding";

        public ChannelOptionsDecodingSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RSL5a")]
        public async Task SetOptions_AddingACipher_DecryptsReceivedMessagesWithIt()
        {
            var (client, channel, received) = await GetAttachedChannel(null);

            var cipher = CreateCipher(256, 1);
            (await channel.SetOptionsAsync(new ChannelOptions(cipher))).IsSuccess.Should().BeTrue();
            await Receive(client, Encrypted("m1", cipher, "hello"));

            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");
            received[0].Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RSL5a")]
        public async Task SetOptions_ChangingTheCipher_DecryptsReceivedMessagesWithTheNewOne()
        {
            var (client, channel, received) = await GetAttachedChannel(new ChannelOptions(CreateCipher(256, 1)));

            var newCipher = CreateCipher(256, 90);
            (await channel.SetOptionsAsync(new ChannelOptions(newCipher))).IsSuccess.Should().BeTrue();
            await Receive(client, Encrypted("m1", newCipher, "hello"));

            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");
            received[0].Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTS3c")]
        [Trait("spec", "RSL5a")]
        public async Task ChannelsGet_WithOptionsForAnExistingChannel_DecryptsReceivedMessagesWithTheNewCipher()
        {
            var (client, channel, received) = await GetAttachedChannel(null);

            var cipher = CreateCipher(256, 1);
            client.Channels.Get(ChannelName, new ChannelOptions(cipher)).Should().BeSameAs(channel);
            await Receive(client, Encrypted("m1", cipher, "hello"));

            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");
            received[0].Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RSL5a")]
        public async Task OptionsSetter_DecryptsReceivedMessagesWithTheNewCipher()
        {
            var (client, channel, received) = await GetAttachedChannel(new ChannelOptions(CreateCipher(256, 1)));

            var newCipher = CreateCipher(128, 50);
            channel.Options = new ChannelOptions(newCipher);
            await Receive(client, Encrypted("m1", newCipher, "hello"));

            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");
            received[0].Encoding.Should().BeNullOrEmpty();
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RSL6b")]
        public async Task SetOptions_WithoutACipher_StopsDecryptingReceivedMessages()
        {
            var oldCipher = CreateCipher(256, 1);
            var (client, channel, received) = await GetAttachedChannel(new ChannelOptions(oldCipher));

            (await channel.SetOptionsAsync(new ChannelOptions())).IsSuccess.Should().BeTrue();
            await Receive(client, Encrypted("m1", oldCipher, "hello"));

            await WaitForCount(received, 1);
            received[0].Data.Should().BeOfType<byte[]>();
            received[0].Encoding.Should().Be("utf-8/cipher+aes-256-cbc");
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RTL19")]
        [Trait("spec", "RTL20")]
        public async Task SetOptions_ChangingTheCipher_KeepsTheDeltaBasePayload()
        {
            var oldCipher = CreateCipher(256, 1);
            var (client, channel, received) = await GetAttachedChannel(new ChannelOptions(oldCipher));

            // The base payload of an encrypted message is its ciphertext, so it does not depend on the key.
            var baseCiphertext = Crypto.GetCipher(oldCipher).Encrypt(Encoding.UTF8.GetBytes("hello"));
            await Receive(client, new Message { Id = "m1", Name = "name", Data = baseCiphertext.ToBase64(), Encoding = "utf-8/cipher+aes-256-cbc/base64" });
            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");

            // A cipher-only change does not reattach the channel (RTL16a), so the next delta still applies to the stored base.
            (await channel.SetOptionsAsync(new ChannelOptions(CreateCipher(256, 90)))).IsSuccess.Should().BeTrue();

            // The delta reuses the base's IV (its first 16 bytes) and adds the rest, encrypted with the new key.
            var iv = baseCiphertext.Take(16).ToArray();
            var target = EncryptWithIv(Key(256, 90), iv, "hello world");
            var delta = new Message
            {
                Id = "m2",
                Name = "name",
                Data = VcdiffCopyThenAdd(baseCiphertext.Length, iv.Length, target).ToBase64(),
                Encoding = "utf-8/cipher+aes-256-cbc/vcdiff/base64",
                Extras = DeltaFrom("m1"),
            };
            await Receive(client, delta);

            await WaitForCount(received, 2);
            received[1].Data.Should().Be("hello world");
            channel.State.Should().Be(ChannelState.Attached);
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RSL5a")]
        public async Task SetOptions_ChangingTheCipher_DecryptsReceivedPresenceMessagesWithTheNewOne()
        {
            var (client, channel, _) = await GetAttachedChannel(null);
            var received = new List<PresenceMessage>();
            channel.Presence.Subscribe(received.Add);

            var cipher = new ChannelOptions(CreateCipher(256, 1));
            (await channel.SetOptionsAsync(cipher)).IsSuccess.Should().BeTrue();
            var presenceMessage = new PresenceMessage(PresenceAction.Enter, "client1", "hello") { Id = "other:0:0", ConnectionId = "other", Timestamp = DateTimeOffset.UtcNow };
            MessageHandler.EncodePayloads(cipher.ToDecodingContext(), new[] { presenceMessage }).IsSuccess.Should().BeTrue();
            await client.ProcessMessage(new ProtocolMessage(ProtocolMessage.MessageAction.Presence, ChannelName)
            {
                Id = "other:0",
                ConnectionId = "other",
                Presence = new[] { presenceMessage },
            });

            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");
        }

        [Fact]
        [Trait("spec", "RTL16")]
        [Trait("spec", "RTAN4b")]
        public async Task SetOptions_ChangingTheCipher_DecryptsReceivedAnnotationsWithTheNewOne()
        {
            var (client, channel, _) = await GetAttachedChannel(null);
            var received = new List<Annotation>();
            channel.Annotations.Subscribe(received.Add);

            var cipher = new ChannelOptions(CreateCipher(256, 1));
            (await channel.SetOptionsAsync(cipher)).IsSuccess.Should().BeTrue();
            var annotation = new Annotation { Type = "reaction:distinct.v1", Name = "like", Serial = "s1", MessageSerial = "ms1", Action = AnnotationAction.Create, Data = "hello" };
            MessageHandler.EncodePayloads(cipher.ToDecodingContext(), new[] { annotation }).IsSuccess.Should().BeTrue();
            await client.ProcessMessage(new ProtocolMessage(ProtocolMessage.MessageAction.Annotation, ChannelName)
            {
                Id = "pm:1",
                ConnectionId = "other",
                Annotations = new[] { annotation },
            });

            await WaitForCount(received, 1);
            received[0].Data.Should().Be("hello");
        }

        private static byte[] Key(int bits, byte seed) =>
            Enumerable.Range(0, bits / 8).Select(i => (byte)(i + seed)).ToArray();

        private static CipherParams CreateCipher(int bits, byte seed) => Crypto.GetDefaultParams(Key(bits, seed));

        private static Message Encrypted(string id, CipherParams cipher, string data)
        {
            var message = new Message { Id = id, Name = "name", Data = data };
            MessageHandler.EncodePayloads(new ChannelOptions(cipher).ToDecodingContext(), new[] { message }).IsSuccess.Should().BeTrue();
            return message;
        }

        // AES-CBC with PKCS7 padding and the IV prepended, the format Ably uses (RSE2).
        private static byte[] EncryptWithIv(byte[] key, byte[] iv, string text)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                var plaintext = Encoding.UTF8.GetBytes(text);
                var ciphertext = aes.CreateEncryptor(key, iv).TransformFinalBlock(plaintext, 0, plaintext.Length);
                return iv.Concat(ciphertext).ToArray();
            }
        }

        // A VCDIFF delta (RFC 3284) that copies the first `copyLength` bytes of the base and adds the rest of `target`.
        private static byte[] VcdiffCopyThenAdd(int baseLength, int copyLength, byte[] target)
        {
            var added = target.Skip(copyLength).ToArray();
            var instructions = new List<byte> { 0x13 }; // COPY, size follows, mode 0
            instructions.AddRange(Varint(copyLength));
            instructions.Add(0x01); // ADD, size follows
            instructions.AddRange(Varint(added.Length));
            var addresses = new byte[] { 0x00 };

            var encoding = new List<byte>();
            encoding.AddRange(Varint(target.Length));
            encoding.Add(0x00); // delta indicator
            encoding.AddRange(Varint(added.Length));
            encoding.AddRange(Varint(instructions.Count));
            encoding.AddRange(Varint(addresses.Length));
            encoding.AddRange(added);
            encoding.AddRange(instructions);
            encoding.AddRange(addresses);

            var delta = new List<byte> { 0xD6, 0xC3, 0xC4, 0x00, 0x00, 0x01 }; // header, then a window with VCD_SOURCE
            delta.AddRange(Varint(baseLength));
            delta.AddRange(Varint(0));
            delta.AddRange(Varint(encoding.Count));
            delta.AddRange(encoding);
            return delta.ToArray();
        }

        private static byte[] Varint(int value)
        {
            var bytes = new List<byte> { (byte)(value & 0x7F) };
            value >>= 7;
            while (value > 0)
            {
                bytes.Insert(0, (byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }

            return bytes.ToArray();
        }

        private static MessageExtras DeltaFrom(string id)
        {
            var extras = new JObject
            {
                [MessageExtras.DeltaProperty] = JObject.FromObject(new DeltaExtras(id, "vcdiff")),
            };
            return new MessageExtras(extras);
        }

        private static async Task Receive(PubSubRealtimeClient client, Message message)
        {
            await client.ProcessMessage(new ProtocolMessage(ProtocolMessage.MessageAction.Message, ChannelName)
            {
                Id = "pm:" + message.Id,
                ConnectionId = "other",
                Messages = new[] { message },
            });
        }

        private static async Task WaitForCount<T>(List<T> received, int count)
        {
            for (var i = 0; i < 100 && received.Count < count; i++)
            {
                await Task.Delay(20);
            }

            received.Should().HaveCount(count);
        }

        private async Task<(PubSubRealtimeClient Client, RealtimeChannel Channel, List<Message> Received)> GetAttachedChannel(ChannelOptions options)
        {
            var client = await GetConnectedClient();
            var channel = (RealtimeChannel)client.Channels.Get(ChannelName, options);
            channel.SetChannelState(ChannelState.Attached);
            var received = new List<Message>();
            channel.Subscribe(received.Add);
            return (client, channel, received);
        }
    }
}
