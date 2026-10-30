// Batch Presence Tests (RSC24, BAR2, BGR2, BGF2)
// Derived from uts/rest/unit/batch_presence.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class BatchPresenceTests : UtsUnitTestBase
    {
        public BatchPresenceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC24/get-presence-channels-param-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_GetPresenceChannelsParam()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // captured_requests = []
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     captured_requests.append(request)
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 2,
            //       "failureCount": 0,
            //       "results": [
            //         { "channel": "channel-a", "presence": [] },
            //         { "channel": "channel-b", "presence": [] }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["channel-a", "channel-b"])
            //
            // ASSERT captured_requests.length == 1
            // ASSERT captured_requests[0].method == "GET"
            // ASSERT captured_requests[0].url.path == "/presence"
            // ASSERT captured_requests[0].url.queryParameters["channels"] == "channel-a,channel-b"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC24/single-channel-param-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_SingleChannelParam()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // captured_requests = []
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     captured_requests.append(request)
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 1,
            //       "failureCount": 0,
            //       "results": [
            //         { "channel": "my-channel", "presence": [] }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["my-channel"])
            //
            // ASSERT captured_requests[0].url.queryParameters["channels"] == "my-channel"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC24/special-chars-comma-joined-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_SpecialCharsCommaJoined()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // captured_requests = []
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     captured_requests.append(request)
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 2,
            //       "failureCount": 0,
            //       "results": [
            //         { "channel": "foo:bar", "presence": [] },
            //         { "channel": "baz/qux", "presence": [] }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["foo:bar", "baz/qux"])
            //
            // ASSERT captured_requests[0].url.queryParameters["channels"] == "foo:bar,baz/qux"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BAR2/mixed-success-failure-counts-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BAR2")]
        public Task BAR2_MixedSuccessFailureCounts()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 3,
            //       "failureCount": 1,
            //       "results": [
            //         { "channel": "ch-1", "presence": [] },
            //         { "channel": "ch-2", "presence": [] },
            //         { "channel": "ch-3", "presence": [] },
            //         { "channel": "ch-4", "error": { "code": 40160, "statusCode": 401, "message": "Not permitted" } }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["ch-1", "ch-2", "ch-3", "ch-4"])
            //
            // ASSERT result.successCount == 3
            // ASSERT result.failureCount == 1
            // ASSERT result.results.length == 4

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BAR2/all-success-counts-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BAR2")]
        public Task BAR2_AllSuccessCounts()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 2,
            //       "failureCount": 0,
            //       "results": [
            //         { "channel": "ch-a", "presence": [] },
            //         { "channel": "ch-b", "presence": [] }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["ch-a", "ch-b"])
            //
            // ASSERT result.successCount == 2
            // ASSERT result.failureCount == 0
            // ASSERT result.results.length == 2

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BAR2/all-failure-counts-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BAR2")]
        public Task BAR2_AllFailureCounts()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 0,
            //       "failureCount": 2,
            //       "results": [
            //         { "channel": "ch-a", "error": { "code": 40160, "statusCode": 401, "message": "Not permitted" } },
            //         { "channel": "ch-b", "error": { "code": 40160, "statusCode": 401, "message": "Not permitted" } }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["ch-a", "ch-b"])
            //
            // ASSERT result.successCount == 0
            // ASSERT result.failureCount == 2
            // ASSERT result.results.length == 2

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BGR2/success-with-members-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BGR2")]
        public Task BGR2_SuccessWithMembers()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 1,
            //       "failureCount": 0,
            //       "results": [
            //         {
            //           "channel": "my-channel",
            //           "presence": [
            //             {
            //               "clientId": "client-1",
            //               "action": 1,
            //               "connectionId": "conn-abc",
            //               "id": "conn-abc:0:0",
            //               "timestamp": 1700000000000,
            //               "data": "hello"
            //             },
            //             {
            //               "clientId": "client-2",
            //               "action": 1,
            //               "connectionId": "conn-def",
            //               "id": "conn-def:0:0",
            //               "timestamp": 1700000000000,
            //               "data": { "key": "value" }
            //             }
            //           ]
            //         }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["my-channel"])
            //
            // ASSERT result.results.length == 1
            //
            // success = result.results[0]
            // ASSERT success IS BatchPresenceSuccessResult
            // ASSERT success.channel == "my-channel"
            // ASSERT success.presence.length == 2
            //
            // ASSERT success.presence[0].clientId == "client-1"
            // ASSERT success.presence[0].action == PRESENT
            // ASSERT success.presence[0].connectionId == "conn-abc"
            // ASSERT success.presence[0].data == "hello"
            //
            // ASSERT success.presence[1].clientId == "client-2"
            // ASSERT success.presence[1].data IS Object/Map
            // ASSERT success.presence[1].data["key"] == "value"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BGR2/success-empty-presence-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BGR2")]
        public Task BGR2_SuccessEmptyPresence()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 1,
            //       "failureCount": 0,
            //       "results": [
            //         { "channel": "empty-channel", "presence": [] }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["empty-channel"])
            //
            // success = result.results[0]
            // ASSERT success IS BatchPresenceSuccessResult
            // ASSERT success.channel == "empty-channel"
            // ASSERT success.presence.length == 0

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BGF2/failure-error-details-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BGF2")]
        public Task BGF2_FailureErrorDetails()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 0,
            //       "failureCount": 1,
            //       "results": [
            //         {
            //           "channel": "restricted-channel",
            //           "error": {
            //             "code": 40160,
            //             "statusCode": 401,
            //             "message": "Channel operation not permitted"
            //           }
            //         }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["restricted-channel"])
            //
            // ASSERT result.results.length == 1
            //
            // failure = result.results[0]
            // ASSERT failure IS BatchPresenceFailureResult
            // ASSERT failure.channel == "restricted-channel"
            // ASSERT failure.error IS ErrorInfo
            // ASSERT failure.error.code == 40160
            // ASSERT failure.error.statusCode == 401
            // ASSERT failure.error.message CONTAINS "not permitted"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC24/mixed-success-failure-results-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_MixedSuccessFailureResults()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 1,
            //       "failureCount": 1,
            //       "results": [
            //         {
            //           "channel": "allowed-channel",
            //           "presence": [
            //             {
            //               "clientId": "user-1",
            //               "action": 1,
            //               "connectionId": "conn-1",
            //               "id": "conn-1:0:0",
            //               "timestamp": 1700000000000
            //             }
            //           ]
            //         },
            //         {
            //           "channel": "restricted-channel",
            //           "error": {
            //             "code": 40160,
            //             "statusCode": 401,
            //             "message": "Not permitted"
            //           }
            //         }
            //       ]
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // result = AWAIT client.batchPresence(["allowed-channel", "restricted-channel"])
            //
            // ASSERT result.successCount == 1
            // ASSERT result.failureCount == 1
            // ASSERT result.results.length == 2
            //
            // ASSERT result.results[0] IS BatchPresenceSuccessResult
            // ASSERT result.results[0].channel == "allowed-channel"
            // ASSERT result.results[0].presence.length == 1
            // ASSERT result.results[0].presence[0].clientId == "user-1"
            //
            // ASSERT result.results[1] IS BatchPresenceFailureResult
            // ASSERT result.results[1].channel == "restricted-channel"
            // ASSERT result.results[1].error.code == 40160

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC24/server-error-propagated-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_ServerErrorPropagated()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 500, body: {
            //       "error": { "code": 50000, "statusCode": 500, "message": "Internal error" }
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // AWAIT client.batchPresence(["any-channel"]) FAILS WITH error
            // ASSERT error.code == 50000
            // ASSERT error.statusCode == 500

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC24/auth-error-propagated-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_AuthErrorPropagated()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     RETURN HttpResponse(status: 401, body: {
            //       "error": { "code": 40101, "statusCode": 401, "message": "Invalid credentials" }
            //     })
            //   }
            // )
            //
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            //
            // AWAIT client.batchPresence(["any-channel"]) FAILS WITH error
            // ASSERT error.code == 40101
            // ASSERT error.statusCode == 401

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC24/uses-configured-auth-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPresence (RSC24); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC24")]
        public Task RSC24_UsesConfiguredAuth()
        {
            // NOTE: missing SDK API: RestClient#batchPresence does not exist in this SDK, so the whole test is a missing-API stub
            // captured_requests = []
            // mock_http = MockHTTP(
            //   onRequest: (request) => {
            //     captured_requests.append(request)
            //     RETURN HttpResponse(status: 200, body: {
            //       "successCount": 1,
            //       "failureCount": 0,
            //       "results": [
            //         { "channel": "ch", "presence": [] }
            //       ]
            //     })
            //   }
            // )
            //
            // # Basic auth
            // client = Rest(options: ClientOptions(key: "fake.key:secret"), httpClient: mock_http)
            // AWAIT client.batchPresence(["ch"])
            //
            // ASSERT captured_requests[0].headers["Authorization"] STARTS_WITH "Basic "

            return Task.CompletedTask;
        }
    }
}
