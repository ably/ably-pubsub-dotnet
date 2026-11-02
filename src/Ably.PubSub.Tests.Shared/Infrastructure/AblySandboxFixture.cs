using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub.Tests
{
    public class AblySandboxFixture
    {
        private static readonly DateTimeOffset StartInterval = DateHelper.CreateDate(DateTimeOffset.UtcNow.Year - 1, 2, 3, 15, 5);

        /// <summary>Channels in this namespace (mutable:*) have mutable messages enabled.</summary>
        public const string MutableMessagesNamespace = "mutable";

        // One provisioning task per endpoint, created atomically. xunit.runner.json sets
        // parallelizeTestCollections, and the sandbox is reached from several collections at once -
        // ChannelSubscriptionsTests declares none at all, so it gets its own - which meant
        // concurrent writes to a plain Dictionary. That can leave the indexer returning null for a
        // key ContainsKey has just accepted, and the caller dereferences it: SandboxSpecs
        // .GetRestClient throws NullReferenceException on the settings it was handed. The await
        // between the check and the write also let two collections provision an app each.
        //
        // Lazy over a ConcurrentDictionary gives one Initialise per endpoint however many
        // callers race, and hands every one of them the same task.
        private static readonly ConcurrentDictionary<string, Lazy<Task<TestEnvironmentSettings>>> Settings =
            new ConcurrentDictionary<string, Lazy<Task<TestEnvironmentSettings>>>();

        public static async Task<TestEnvironmentSettings> GetSettings(string endpoint = null)
        {
            endpoint = endpoint ?? TestEnvironmentSettings.DefaultEndpoint;

            var provisioning = Settings.GetOrAdd(
                endpoint,
                ep => new Lazy<Task<TestEnvironmentSettings>>(() => Initialise(ep)));

            try
            {
                return await provisioning.Value;
            }
            catch
            {
                // A failure must not be what gets cached. The Dictionary this replaced assigned only
                // on success, so a provisioning attempt that threw was retried by whoever asked
                // next; holding the faulted task instead would fail every remaining sandbox test in
                // the run with the same exception.
                //
                // Removed by key and value together, so a caller that has already raced in a
                // replacement keeps it - ConcurrentDictionary's ICollection.Remove compares both,
                // and the value comparison is reference equality on this exact Lazy.
                ((ICollection<KeyValuePair<string, Lazy<Task<TestEnvironmentSettings>>>>)Settings)
                    .Remove(new KeyValuePair<string, Lazy<Task<TestEnvironmentSettings>>>(endpoint, provisioning));
                throw;
            }
        }

        private static async Task<TestEnvironmentSettings> Initialise(string endpoint = null)
        {
            var settings = new TestEnvironmentSettings
            {
                Tls = true,
            };

            if (endpoint != null)
            {
                settings.Endpoint = endpoint;
            }

            JObject testAppSpec = JObject.Parse(ResourceHelper.GetResource("test-app-setup.json"));

            // The annotations and message retrieval sandbox tests need a namespace with mutable messages enabled.
            var namespaces = (JArray)testAppSpec["post_apps"]["namespaces"];
            if (namespaces != null && namespaces.All(x => (string)x["id"] != MutableMessagesNamespace))
            {
                namespaces.Add(new JObject { ["id"] = MutableMessagesNamespace, ["mutableMessages"] = true });
            }

            var cipher = testAppSpec["cipher"];
            settings.CipherParams = new CipherParams(
                (string)cipher["algorithm"],
                ((string)cipher["key"]).FromBase64(),
                CipherMode.CBC,
                ((string)cipher["iv"]).FromBase64());

            AblyHttpRequester client = settings.GetHttpClient(endpoint);
            AblyRequest request = new AblyRequest("/apps", HttpMethod.Post);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Content-Type", "application/json");
            request.RequestBody = testAppSpec["post_apps"].ToString().GetBytes();
            request.Protocol = Protocol.Json;

            var response = await RetryExecute(() => client.Execute(request));

            var json = JObject.Parse(response.TextResponse);

            string appId = settings.AppId = (string)json["appId"];
            foreach (var key in json["keys"])
            {
                var testKey = new Key
                {
                    KeyName = appId + "." + (string)key["keyName"],
                    KeySecret = (string)key["keySecret"],
                    KeyStr = (string)key["keyStr"],
                    Capability = (string)key["capability"]
                };
                settings.Keys.Add(testKey);
            }

            // await SetupSampleStats(settings);
            return settings;
        }

        private static async Task<AblyResponse> RetryExecute(Func<Task<AblyResponse>> execute)
        {
            int count = 0;
            while (true)
            {
                try
                {
                    var result = await execute();
                    return result;
                }
                catch (Exception)
                {
                    if (count > 1)
                    {
                        throw;
                    }
                }

                count++;
            }
        }

        public async Task SetupStats()
        {
            await SetupSampleStats(await GetSettings());
        }

        private static async Task SetupSampleStats(TestEnvironmentSettings settings)
        {
            var lastInterval = StartInterval;
            var interval1 = lastInterval - TimeSpan.FromMinutes(120);
            var interval2 = lastInterval - TimeSpan.FromMinutes(60);
            var interval3 = lastInterval;
            var json = ResourceHelper.GetResource("StatsFixture.json");
            json = json.Replace("[[Interval1]]", interval1.ToString("yyyy-MM-dd:HH:mm"));
            json = json.Replace("[[Interval2]]", interval2.ToString("yyyy-MM-dd:HH:mm"));
            json = json.Replace("[[Interval3]]", interval3.ToString("yyyy-MM-dd:HH:mm"));

            PubSubHttpClient ablyRest = new PubSubHttpClient(settings.FirstValidKey);
            AblyHttpRequester client = settings.GetHttpClient();
            var request = new AblyRequest("/stats", HttpMethod.Post);
            request.Protocol = Protocol.Json;
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Content-Type", "application/json");
            await ablyRest.AblyAuth.AddAuthHeader(request);
            request.RequestBody = json.GetBytes();

            await client.Execute(request);
        }
    }
}
