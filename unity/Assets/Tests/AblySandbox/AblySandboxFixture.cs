using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using IO.Ably;
using IO.Ably.Encryption;
using Newtonsoft.Json.Linq;

namespace Assets.Tests.AblySandbox
{
    public class AblySandboxFixture
    {
        public static readonly DateTimeOffset StartInterval = DateHelper.CreateDate(DateTimeOffset.UtcNow.Year - 1, 2, 3, 15, 5);

        private static Dictionary<string, TestEnvironmentSettings> _settings = new Dictionary<string, TestEnvironmentSettings>();

        public async Task<TestEnvironmentSettings> GetSettings(string environment = null)
        {
            environment = environment ?? "sandbox";
            if (_settings.ContainsKey(environment))
            {
                return _settings[environment];
            }

            _settings[environment] = await Initialise("sandbox");
            return _settings[environment];
        }

        private static async Task<TestEnvironmentSettings> Initialise(string environment)
        {
            var settings = new TestEnvironmentSettings
            {
                Tls = true,
            };

            if (environment != null)
            {
                settings.Environment = environment;
            }

            JObject testAppSpec = JObject.Parse(TestAppSetup._testAppSetup);

            var cipher = testAppSpec["cipher"];
            settings.CipherParams = new CipherParams(
                (string)cipher["algorithm"],
                ((string)cipher["key"]).FromBase64(),
                CipherMode.CBC,
                ((string)cipher["iv"]).FromBase64());

            var request = new AblyRequest("/apps", HttpMethod.Post);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Content-Type", "application/json");
            request.RequestBody = testAppSpec["post_apps"].ToString().GetBytes();
            request.Protocol = Protocol.Json;

            AblyHttpRequester client = settings.GetHttpClient(environment);
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

            var json = TestAppSetup._statsFixture;
            json = json.Replace("[[Interval1]]", interval1.ToString("yyyy-MM-dd:HH:mm"));
            json = json.Replace("[[Interval2]]", interval2.ToString("yyyy-MM-dd:HH:mm"));
            json = json.Replace("[[Interval3]]", interval3.ToString("yyyy-MM-dd:HH:mm"));

            AblyHttpRequester client = settings.GetHttpClient();
            var request = new AblyRequest("/stats", HttpMethod.Post);
            request.Protocol = Protocol.Json;
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Content-Type", "application/json");

            PubSubHttpClient ablyRest = new PubSubHttpClient(settings.FirstValidKey);
            await ablyRest.AblyAuth.AddAuthHeader(request);
            request.RequestBody = json.GetBytes();

            await client.Execute(request);
        }
    }
}
