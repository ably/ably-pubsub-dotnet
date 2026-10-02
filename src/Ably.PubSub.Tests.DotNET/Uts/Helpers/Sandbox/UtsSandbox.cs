using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Xunit;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The xUnit collection the UTS integration tier shares.
    ///
    /// It reuses the repo's existing <see cref="AblySandboxFixture"/> rather than provisioning a UTS
    /// app of its own: the fixture already self-provisions from the vendored
    /// <c>test-app-setup.json</c> and caches per environment, and every UTS integration test suffixes
    /// its channel names, client ids and device ids with <see cref="UtsSandbox.RandomId"/>, so sharing
    /// one app costs nothing in isolation and saves a second app creation per run.
    /// </summary>
    [CollectionDefinition(UtsSandbox.CollectionName)]
    public class UtsSandboxCollection : ICollectionFixture<AblySandboxFixture>
    {
    }

    /// <summary>
    /// The UTS specs' <c>app_config</c>, plus the wall-clock helpers the integration tier needs.
    ///
    /// The waits here are deliberately real time, the inverse of the unit tier's rule: an integration
    /// test is waiting on a real server over a real network, so there is no clock to fake and
    /// shortening a timeout would only make the tier flaky.
    /// </summary>
    public sealed class UtsSandbox
    {
        public const string CollectionName = "UTS Sandbox";

        /// <summary>The channel the app setup pre-populates with presence members the specs read.</summary>
        public const string PresenceFixturesChannel = "persisted:presence_fixtures";

        private readonly TestEnvironmentSettings _settings;

        private UtsSandbox(TestEnvironmentSettings settings)
        {
            _settings = settings;
        }

        public string AppId => _settings.AppId;

        /// <summary>The full-access key — the specs' <c>app_config.keys[0].keyStr</c>.</summary>
        public string KeyStr => Key(0).KeyStr;

        /// <summary>The cipher the app setup encrypted the <c>client_encoded</c> presence fixture with.</summary>
        public CipherParams CipherParams => _settings.CipherParams;

        public static async Task<UtsSandbox> Create(AblySandboxFixture fixture)
        {
            if (fixture == null)
            {
                throw new ArgumentNullException(nameof(fixture));
            }

            return new UtsSandbox(await AblySandboxFixture.GetSettings());
        }

        /// <summary>
        /// The specs' <c>app_config.keys[i]</c>. The index means what it means in a spec, as provisioned
        /// by <c>common/test-resources/test-app-setup.json</c>: 0 full access, 1 push admin,
        /// 2 per-channel capabilities, 3 subscribe-only, 4 revocable tokens.
        /// </summary>
        public Key Key(int index)
        {
            if (index < 0 || index >= _settings.Keys.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    $"The sandbox app has {_settings.Keys.Count} keys; the spec asked for index {index}.");
            }

            return _settings.Keys[index];
        }

        /// <summary>The specs' <c>random_id()</c> — url-safe, so it is valid in a channel name.</summary>
        public static string RandomId(int length = 6)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        /// <summary>
        /// This tier's <c>poll_until</c>. Sleeps between attempts on the real clock and returns whatever
        /// the condition answered with, so a condition that fetched a page saves fetching it again.
        ///
        /// Needed because nothing is consistent immediately after a write — history and presence lag a
        /// publish or an enter, and device deletion is asynchronous. A fixed delay instead of a poll
        /// either flakes or spends the whole test budget.
        /// </summary>
        public static async Task<T> WallClockPollUntil<T>(
            Func<Task<T>> condition,
            string description,
            TimeSpan? timeout = null,
            TimeSpan? interval = null)
            where T : class
        {
            var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(20));
            var step = interval ?? TimeSpan.FromMilliseconds(500);
            Exception last = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    var result = await condition().ConfigureAwait(false);
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    // The specs' poll_until_success: an error is a "not yet", not a failure, until the
                    // deadline. The last one is reported so a timeout is not silent about why.
                    last = ex;
                }

                await Task.Delay(step).ConfigureAwait(false);
            }

            var suffix = last == null ? string.Empty : $" Last error: {last.Message}";
            throw new TimeoutException($"Timed out waiting for {description}.{suffix}");
        }

        /// <summary>The boolean form, for a premise that is not fetching anything.</summary>
        public static async Task WallClockPollUntil(
            Func<Task<bool>> condition,
            string description,
            TimeSpan? timeout = null,
            TimeSpan? interval = null)
            => await WallClockPollUntil<string>(
                async () => await condition().ConfigureAwait(false) ? "ok" : null,
                description,
                timeout,
                interval).ConfigureAwait(false);

        /// <summary>The two halves of <c>appId.keyId:secret</c>, which several auth specs need separately.</summary>
        public static string ExtractKeyName(string apiKey) => apiKey.Split(':').First();

        public static string ExtractKeySecret(string apiKey) => apiKey.Split(':').Last();

        /// <summary>
        /// The auth specs' <c>generate_jwt</c>, signed HS256 locally. Signing it here rather than asking
        /// the sandbox for a token costs no round trip, which matters in the proxy tier where every
        /// extra request lands in the event log a test is counting.
        ///
        /// When <paramref name="expiresAt"/> is given, <c>iat</c> is backdated by the TTL: Ably reads a
        /// JWT's lifetime as <c>exp - iat</c> and rejects a negative one as malformed (40003) before
        /// expiry is ever considered, so an already-expired JWT cannot be made by leaving <c>iat</c> at
        /// now and putting <c>exp</c> in the past.
        /// </summary>
        public static string GenerateJwt(
            string keyName,
            string keySecret,
            TimeSpan? ttl = null,
            string clientId = null,
            string capability = null,
            DateTimeOffset? expiresAt = null)
        {
            var lifetime = ttl ?? TimeSpan.FromHours(1);
            var expiry = expiresAt ?? DateTimeOffset.UtcNow.Add(lifetime);
            var issuedAt = expiry.Subtract(lifetime);

            var header = new Dictionary<string, object>
            {
                ["typ"] = "JWT",
                ["alg"] = "HS256",
                ["kid"] = keyName,
            };

            var claims = new Dictionary<string, object>
            {
                ["iat"] = issuedAt.ToUnixTimeSeconds(),
                ["exp"] = expiry.ToUnixTimeSeconds(),
            };

            if (clientId != null)
            {
                claims["x-ably-clientId"] = clientId;
            }

            if (capability != null)
            {
                claims["x-ably-capability"] = capability;
            }

            var signingInput =
                $"{Base64Url(JsonHelper.Serialize(header))}.{Base64Url(JsonHelper.Serialize(claims))}";

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keySecret)))
            {
                var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
                return $"{signingInput}.{Base64Url(signature)}";
            }
        }

        private static string Base64Url(string value) => Base64Url(Encoding.UTF8.GetBytes(value));

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
