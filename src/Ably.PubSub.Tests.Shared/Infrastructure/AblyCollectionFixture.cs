using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Ably.PubSub.Tests
{
    public class AblyCollectionFixture : ICollectionFixture<AblySandboxFixture> { }

    [CollectionDefinition("PubSubHttpClient SandBox Collection")]
    public class SandboxRestFixture : AblyCollectionFixture
    {
    }

    [CollectionDefinition("SandBox Connection")]
    public class SandBoxConnectionFixture : AblyCollectionFixture
    {
    }

    [CollectionDefinition("Presence Sandbox")]
    public class SandBoxPresenceFixture : AblyCollectionFixture
    {
    }

    [CollectionDefinition("Channel SandBox")]
    public class SandBoxChannelFixture : AblyCollectionFixture
    {
    }

    public class Key
    {
        public string KeyName { get; set; }

        public string KeySecret { get; set; }

        public string KeyStr { get; set; }

        public string Capability { get; set; }
    }

    public class TestEnvironmentSettings
    {
        public string AppId { get; set; }

        public List<Key> Keys { get; set; }

        public const string PresenceChannelName = "persisted:presence_fixtures";

        public string FirstValidKey => Keys.FirstOrDefault()?.KeyStr;

        public string KeyWithChannelLimitations => Keys[2].KeyStr;

        public bool Tls { get; set; }

        // The endpoint every sandbox test runs against: ABLY_ENDPOINT when set, otherwise nonprod:sandbox.
        public static readonly string DefaultEndpoint = GetDefaultEndpoint();

        public string Endpoint { get; set; } = DefaultEndpoint;

        public CipherParams CipherParams { get; set; }

        public TestEnvironmentSettings()
        {
            Keys = new List<Key>();
        }

        public ClientOptions CreateDefaultOptions(string key = null, string endpoint = null)
        {
            return new ClientOptions { Key = key ?? FirstValidKey, Tls = Tls, Endpoint = endpoint ?? Endpoint };
        }

        private static string GetDefaultEndpoint()
        {
            var ablyEndpoint = System.Environment.GetEnvironmentVariable("ABLY_ENDPOINT");
            return string.IsNullOrEmpty(ablyEndpoint) ? "nonprod:sandbox" : ablyEndpoint.Trim();
        }

        internal AblyHttpRequester GetHttpClient(string endpoint = null)
        {
            var options = CreateDefaultOptions(null, endpoint);
            var ablyHttpOptions = new AblyHttpOptions
            {
                IsSecure = Tls,
                Host = Defaults.GetPrimaryDomain(options.Endpoint),
                FallbackHosts = options.GetFallbackHosts(),
            };
            return new AblyHttpRequester(ablyHttpOptions);
        }
    }
}
