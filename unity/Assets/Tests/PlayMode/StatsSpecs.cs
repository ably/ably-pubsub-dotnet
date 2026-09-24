using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Assets.Tests.AblySandbox;
using Cysharp.Threading.Tasks;
using Ably.PubSub;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Assets.Tests.PlayMode
{
    [TestFixture]
    [Category("EditorPlayer")]
    public class StatsSpecs
    {
        private AblySandboxFixture _sandboxFixture;

        [OneTimeSetUp]
        public void OneTimeInit()
        {
            _sandboxFixture = new AblySandboxFixture();
        }

        [UnitySetUp]
        public IEnumerator Init()
        {
            AblySandbox = new AblySandbox.AblySandbox(_sandboxFixture);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AblySandbox.Dispose();
            yield return null;
        }

        public AblySandbox.AblySandbox AblySandbox { get; set; }

        private static readonly DateTimeOffset StartInterval =
            DateHelper.CreateDate(DateTimeOffset.UtcNow.Year - 1, 2, 3, 15, 5);

        private async Task<List<Stats>> GetStats(Protocol protocol)
        {
            var client = await AblySandbox.GetRestClient(protocol);
            var result = await client.StatsAsync(new StatsRequestParams
            {
                Start = StartInterval.AddMinutes(-2),
                End = StartInterval.AddMinutes(1)
            });

            return result.Items;
        }

        static Protocol[] _protocols = { Protocol.Json };

        [Property("spec", "G3")]
        [UnityTest]
        public IEnumerator ShouldHaveCorrectStatsAsPerStatsSpec([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                await _sandboxFixture.SetupStats();

                async Task GetAndValidateStats()
                {
                    var allStats = await GetStats(protocol);
                    var stats = allStats.First();
                    Assert.AreEqual(40 + 70, stats.All.Messages.Count);
                    Assert.AreEqual(4000 + 7000, stats.All.Messages.Data);
                    Assert.AreEqual(70, stats.Inbound.Realtime.All.Count);
                    Assert.AreEqual(7000, stats.Inbound.Realtime.All.Data);
                    Assert.AreEqual(70, stats.Inbound.Realtime.Messages.Count);
                    Assert.AreEqual(7000, stats.Inbound.Realtime.Messages.Data);
                    Assert.AreEqual(40, stats.Outbound.Realtime.All.Count);
                    Assert.AreEqual(4000, stats.Outbound.Realtime.All.Data);
                    Assert.AreEqual(20, stats.Persisted.Presence.Count);
                    Assert.AreEqual(2000, stats.Persisted.Presence.Data);
                    Assert.AreEqual(20, stats.Connections.Tls.Peak);
                    Assert.AreEqual(10, stats.Connections.Tls.Opened);
                    Assert.AreEqual(50, stats.Channels.Peak);
                    Assert.AreEqual(30, stats.Channels.Opened);
                    Assert.AreEqual(50, stats.ApiRequests.Succeeded);
                    Assert.AreEqual(10, stats.ApiRequests.Failed);
                    Assert.AreEqual(60, stats.TokenRequests.Succeeded);
                    Assert.AreEqual(20, stats.TokenRequests.Failed);
                }

                await AblySandbox.AssertWithRetries(GetAndValidateStats, 5, TimeSpan.FromSeconds(5));
            });
        }
    }
}
