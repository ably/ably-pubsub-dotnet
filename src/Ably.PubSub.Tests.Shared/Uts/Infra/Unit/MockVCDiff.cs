using System;
using System.Linq;
using System.Text;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>A mock delta decoder the unit base can install as the client's delta decoder (hook H2).</summary>
    public interface IMockDeltaDecoder
    {
        /// <summary>The <c>ClientOptions.DeltaDecoder</c> value: <c>(base, delta) =&gt; result</c>.</summary>
        Func<byte[], byte[], byte[]> AsDeltaDecoder();
    }

    /// <summary>
    /// The specs' <c>MockVCDiffEncoder</c> (<c>mock_vcdiff.md</c>): a deterministic, inspectable stand-in for a
    /// vcdiff encoder.
    /// </summary>
    public sealed class MockVCDiffEncoder
    {
        public string Encode(string @base, string value)
            => MockVCDiffCodec.EncodeUriComponent(@base) + "/" + MockVCDiffCodec.EncodeUriComponent(value);

        public byte[] Encode(byte[] @base, byte[] value)
            => Encoding.UTF8.GetBytes(MockVCDiffCodec.Base64UrlEncode(@base) + "/" + MockVCDiffCodec.Base64UrlEncode(value));
    }

    /// <summary>
    /// The specs' <c>MockVCDiffDecoder</c> (<c>mock_vcdiff.md</c>, VD2a): decodes a delta made by <see
    /// cref="MockVCDiffEncoder"/>, after checking the delta was made against the base the SDK passes (catching
    /// base-payload storage bugs).
    /// </summary>
    public sealed class MockVCDiffDecoder : IMockDeltaDecoder
    {
        private readonly object _lock = new object();
        private readonly Action<byte[], byte[]> _onDecode;
        private int _numberOfCalls;
        private byte[] _lastBase;
        private byte[] _lastDelta;

        /// <param name="onDecode">The spec's <c>onDecode: (delta, base) =&gt; ...</c>, called before each decode.</param>
        public MockVCDiffDecoder(Action<byte[], byte[]> onDecode = null)
        {
            _onDecode = onDecode;
        }

        public int NumberOfCalls
        {
            get
            {
                lock (_lock)
                {
                    return _numberOfCalls;
                }
            }
        }

        /// <summary>The base of the latest call, as the SDK passed it.</summary>
        public byte[] LastBase
        {
            get
            {
                lock (_lock)
                {
                    return _lastBase;
                }
            }
        }

        /// <summary>The delta of the latest call, as the SDK passed it.</summary>
        public byte[] LastDelta
        {
            get
            {
                lock (_lock)
                {
                    return _lastDelta;
                }
            }
        }

        /// <summary>The spec's <c>decode(delta, base)</c> (VD2 argument order).</summary>
        public byte[] Decode(byte[] delta, byte[] @base)
        {
            lock (_lock)
            {
                _numberOfCalls++;
                _lastBase = @base;
                _lastDelta = delta;
            }

            _onDecode?.Invoke(delta, @base);
            return MockVCDiffCodec.Decode(delta, @base);
        }

        /// <summary>The spec's string-form <c>decode</c>, for test setup (delta first, as the binary form).</summary>
        public string Decode(string delta, string @base)
            => Encoding.UTF8.GetString(Decode(Encoding.UTF8.GetBytes(delta), Encoding.UTF8.GetBytes(@base)));

        public Func<byte[], byte[], byte[]> AsDeltaDecoder() => (@base, delta) => Decode(delta, @base);
    }

    /// <summary>
    /// The specs' <c>FailingMockVCDiffDecoder</c> (RTL18): every decode throws <c>Simulated vcdiff decode
    /// failure</c>.
    /// </summary>
    public sealed class FailingMockVCDiffDecoder : IMockDeltaDecoder
    {
        private int _numberOfCalls;

        public int NumberOfCalls => System.Threading.Volatile.Read(ref _numberOfCalls);

        public byte[] Decode(byte[] delta, byte[] @base)
        {
            System.Threading.Interlocked.Increment(ref _numberOfCalls);
            throw new InvalidOperationException("Simulated vcdiff decode failure");
        }

        public Func<byte[], byte[], byte[]> AsDeltaDecoder() => (@base, delta) => Decode(delta, @base);
    }

    /// <summary>The mock codec's encoding helpers.</summary>
    internal static class MockVCDiffCodec
    {
        private const string UriUnreserved = "-_.!~*'()";

        internal static byte[] Decode(byte[] delta, byte[] @base)
        {
            var parts = Encoding.UTF8.GetString(delta).Split('/');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Invalid delta format");
            }

            // Binary form: base64url parts.
            if (TryBase64UrlDecode(parts[0], out var binaryBase)
                && TryBase64UrlDecode(parts[1], out var binaryValue)
                && binaryBase.SequenceEqual(@base))
            {
                return binaryValue;
            }

            // String form: URI-component parts; the SDK passed the string base as UTF-8.
            var stringBase = Uri.UnescapeDataString(parts[0]);
            if (Encoding.UTF8.GetBytes(stringBase).SequenceEqual(@base))
            {
                return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(parts[1]));
            }

            throw new InvalidOperationException("Base mismatch: expected base does not match delta");
        }

        /// <summary>
        /// JavaScript's <c>encodeURIComponent</c>: UTF-8, every byte but A-Z a-z 0-9 <c>-_.!~*'()</c> as
        /// <c>%XX</c>.
        /// </summary>
        internal static string EncodeUriComponent(string value)
        {
            var builder = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(value))
            {
                var c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || UriUnreserved.IndexOf(c) >= 0)
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('%').Append(b.ToString("X2"));
                }
            }

            return builder.ToString();
        }

        internal static string Base64UrlEncode(byte[] value)
            => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        internal static bool TryBase64UrlDecode(string value, out byte[] result)
        {
            result = null;
            if (value.Any(c => !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')))
            {
                return false;
            }

            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 1:
                    return false;
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            try
            {
                result = Convert.FromBase64String(padded);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
