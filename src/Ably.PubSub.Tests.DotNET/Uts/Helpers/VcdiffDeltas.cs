using System;
using System.Collections.Generic;
using System.IO;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The specs' <c>MockVCDiffEncoder</c>, as a real one.
    ///
    /// <para>
    /// The delta-decoding specs need a delta to feed the SDK, and the specs supply it with a mock
    /// encoder paired with a mock decoder installed as a plugin. This SDK has no plugin seam - the
    /// vcdiff decoder is <c>IO.Ably.DeltaCodec</c>, compiled in - so the delta that arrives has to
    /// be one the real decoder accepts, and that library decodes only. This produces one.
    /// </para>
    ///
    /// <para>
    /// It is a correct but deliberately naive RFC 3284 encoder: one window, no secondary
    /// compressors, no checksum, the default code table. It emits a COPY for every run of four or
    /// more bytes found in the source and an ADD for everything else, which is enough to make the
    /// delta genuinely depend on the base payload - the point of most of these tests is which base
    /// the SDK applied, and a delta that ignored its source could not tell a right base from a
    /// wrong one.
    /// </para>
    /// </summary>
    public static class VcdiffDeltas
    {
        /// <summary>The ADD opcode whose size is read from the instruction stream.</summary>
        private const byte AddOpcode = 1;

        /// <summary>The COPY opcode, mode 0 (absolute address), size read from the stream.</summary>
        private const byte CopyOpcode = 19;

        /// <summary>Shorter runs cost more to encode as a COPY than they save.</summary>
        private const int MinimumCopyLength = 4;

        /// <summary>
        /// Builds a VCDIFF delta that turns <paramref name="source"/> into
        /// <paramref name="target"/>.
        /// </summary>
        /// <param name="source">The base payload the delta is computed against.</param>
        /// <param name="target">The payload the delta should produce.</param>
        /// <returns>The delta, as the bytes that go on the wire.</returns>
        public static byte[] Encode(byte[] source, byte[] target)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var addData = new List<byte>();
            var instructions = new List<byte>();
            var addresses = new List<byte>();

            var pendingLiterals = new List<byte>();
            var i = 0;

            while (i < target.Length)
            {
                var (offset, length) = LongestMatch(source, target, i);

                if (length < MinimumCopyLength)
                {
                    pendingLiterals.Add(target[i]);
                    i++;
                    continue;
                }

                FlushAdd(pendingLiterals, addData, instructions);

                instructions.Add(CopyOpcode);
                instructions.AddRange(VarInt(length));
                addresses.AddRange(VarInt(offset));

                i += length;
            }

            FlushAdd(pendingLiterals, addData, instructions);

            // Fields from the target length onwards; the window header needs their total size.
            var windowBody = new List<byte>();
            windowBody.AddRange(VarInt(target.Length));
            windowBody.Add(0); // Delta indicator: no secondary compression.
            windowBody.AddRange(VarInt(addData.Count));
            windowBody.AddRange(VarInt(instructions.Count));
            windowBody.AddRange(VarInt(addresses.Count));
            windowBody.AddRange(addData);
            windowBody.AddRange(instructions);
            windowBody.AddRange(addresses);

            var delta = new List<byte> { 0xd6, 0xc3, 0xc4, 0x00, 0x00 };

            delta.Add(0x01); // Window indicator: the source is the base payload.
            delta.AddRange(VarInt(source.Length));
            delta.AddRange(VarInt(0)); // Source position.
            delta.AddRange(VarInt(windowBody.Count));
            delta.AddRange(windowBody);

            return delta.ToArray();
        }

        /// <summary>
        /// The same, for text payloads, which is what most of the specs use.
        /// </summary>
        /// <param name="source">The base payload.</param>
        /// <param name="target">The payload the delta should produce.</param>
        /// <returns>The delta bytes.</returns>
        public static byte[] Encode(string source, string target)
            => Encode(
                System.Text.Encoding.UTF8.GetBytes(source),
                System.Text.Encoding.UTF8.GetBytes(target));

        /// <summary>
        /// A delta as it travels on a JSON transport: base64, since the bytes are binary.
        /// </summary>
        /// <param name="source">The base payload.</param>
        /// <param name="target">The payload the delta should produce.</param>
        /// <returns>The base64 of the delta.</returns>
        public static string EncodeBase64(string source, string target)
            => Convert.ToBase64String(Encode(source, target));

        /// <summary>
        /// A delta over binary payloads as it travels on a JSON transport.
        /// </summary>
        /// <param name="source">The base payload.</param>
        /// <param name="target">The payload the delta should produce.</param>
        /// <returns>The base64 of the delta.</returns>
        public static string EncodeBase64(byte[] source, byte[] target)
            => Convert.ToBase64String(Encode(source, target));

        private static void FlushAdd(List<byte> literals, List<byte> addData, List<byte> instructions)
        {
            if (literals.Count == 0)
            {
                return;
            }

            instructions.Add(AddOpcode);
            instructions.AddRange(VarInt(literals.Count));
            addData.AddRange(literals);
            literals.Clear();
        }

        /// <summary>
        /// The longest run of <paramref name="target"/> at <paramref name="start"/> that also
        /// appears in <paramref name="source"/>. Brute force: these payloads are a few dozen bytes.
        /// </summary>
        private static (int Offset, int Length) LongestMatch(byte[] source, byte[] target, int start)
        {
            var bestOffset = 0;
            var bestLength = 0;

            for (var s = 0; s < source.Length; s++)
            {
                var length = 0;
                while (s + length < source.Length
                       && start + length < target.Length
                       && source[s + length] == target[start + length])
                {
                    length++;
                }

                if (length > bestLength)
                {
                    bestLength = length;
                    bestOffset = s;
                }
            }

            return (bestOffset, bestLength);
        }

        /// <summary>
        /// RFC 3284's integer encoding: seven bits a byte, most significant group first, with the
        /// high bit set on every byte but the last.
        /// </summary>
        private static byte[] VarInt(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            var groups = new Stack<byte>();
            groups.Push((byte)(value & 0x7f));
            value >>= 7;

            while (value > 0)
            {
                groups.Push((byte)((value & 0x7f) | 0x80));
                value >>= 7;
            }

            using (var buffer = new MemoryStream())
            {
                while (groups.Count > 0)
                {
                    buffer.WriteByte(groups.Pop());
                }

                return buffer.ToArray();
            }
        }
    }
}
