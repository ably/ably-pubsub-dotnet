// Batch Publish Tests (RSC22c, RSC22d, BSP2a, BSP2b, BPR2a, BPR2b, BPR2c, BPF2a, BPF2b)
// Derived from uts/rest/unit/batch_publish.md
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
    public class BatchPublishTests : UtsUnitTestBase
    {
        public BatchPublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC22c/single-spec-post-messages-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_SingleSpecPostMessages()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_1 = "test-RSC22c1-a-${random_id()}"
            // channel_name_2 = "test-RSC22c1-b-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to capture requests and respond with success
            // When batchPublish is called with a single BatchPublishSpec:
            //   - channels: [channel_name_1, channel_name_2]
            //   - messages: [Message(name: "event", data: "hello")]
            // Then a POST request is sent to "/messages"
            // And the captured request body contains:
            //   - channels: [channel_name_1, channel_name_2]
            //   - messages: [{ name: "event", data: "hello" }]

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/array-specs-post-messages-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_ArraySpecsPostMessages()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_1 = "test-RSC22c2-a-${random_id()}"
            // channel_name_2 = "test-RSC22c2-b-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to capture requests and respond with success
            // When batchPublish is called with an array of BatchPublishSpecs:
            //   - BatchPublishSpec(channels: [channel_name_1], messages: [Message(name: "e1", data: "d1")])
            //   - BatchPublishSpec(channels: [channel_name_2], messages: [Message(name: "e2", data: "d2")])
            // Then a POST request is sent to "/messages"
            // And the captured request body is an array containing both specs

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/single-spec-single-result-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_SingleSpecSingleResult()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22c3-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to respond with:
            //   {
            //     "channel": channel_name,
            //     "messageId": "msg123",
            //     "serials": ["serial1"]
            //   }
            // When batchPublish is called with a single BatchPublishSpec
            // Then a single BatchResult is returned (not an array)
            // And the result contains the success result for channel_name

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/array-specs-array-results-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_ArraySpecsArrayResults()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_1 = "test-RSC22c4-a-${random_id()}"
            // channel_name_2 = "test-RSC22c4-b-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to respond with an array of results:
            //   [
            //     { "channel": channel_name_1, "messageId": "msg1", "serials": ["s1"] },
            //     { "channel": channel_name_2, "messageId": "msg2", "serials": ["s2"] }
            //   ]
            // When batchPublish is called with an array of BatchPublishSpecs
            // Then an array of BatchResults is returned
            // And each result corresponds to the respective spec

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/multiple-channels-multiple-results-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_MultipleChannelsMultipleResults()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_1 = "test-RSC22c5-a-${random_id()}"
            // channel_name_2 = "test-RSC22c5-b-${random_id()}"
            // channel_name_3 = "test-RSC22c5-c-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And a BatchPublishSpec with channels: [channel_name_1, channel_name_2, channel_name_3]
            // And the mock is configured to respond with results for each channel
            // When batchPublish is called
            // Then the BatchResult contains results for all three channels

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/messages-encoded-per-rsl4-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_MessagesEncodedPerRsl4()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22c6-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called with messages containing:
            //   - String data
            //   - Binary data (Uint8List/[]byte)
            //   - JSON object data
            // Then the captured request shows each message is encoded per RSL4:
            //   - String: data as-is, no encoding
            //   - Binary: base64 encoded, encoding: "base64"
            //   - JSON: JSON stringified, encoding: "json"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/uses-configured-auth-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_UsesConfiguredAuth()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22c7-${random_id()}"
            //
            // Given a REST client with token auth and mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called
            // Then the captured POST request includes Authorization: Bearer <token>
            //
            // channel_name = "test-RSC22c7-basic-${random_id()}"
            //
            // Given a REST client with basic auth and mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called
            // Then the captured POST request includes Authorization: Basic <base64(key)>

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22d/idempotent-ids-generated-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22d")]
        public Task RSC22d_IdempotentIdsGenerated()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // Given a REST client with idempotentRestPublishing: true and mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called with messages that have no id
            // Then the captured request shows each message in each BatchPublishSpec has a unique id generated
            // And the id format follows RSL1k1 (baseId:serial)
            // And each BatchPublishSpec gets a separate base ID

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22d/explicit-ids-preserved-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22d")]
        public Task RSC22d_ExplicitIdsPreserved()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // Given a REST client with idempotentRestPublishing: true and mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called with messages that have explicit ids
            // Then the captured request shows the explicit ids are preserved (not overwritten)

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22d/ids-not-generated-disabled-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22d")]
        public Task RSC22d_IdsNotGeneratedDisabled()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // Given a REST client with idempotentRestPublishing: false and mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called with messages that have no id
            // Then the captured request shows messages are sent without id fields

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BSP2a/channels-array-strings-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BSP2a")]
        public Task BSP2a_ChannelsArrayStrings()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_1 = "test-BSP2a-a-${random_id()}"
            // channel_name_2 = "test-BSP2a-b-${random_id()}"
            // channel_name_3 = "test-BSP2a-c-${random_id()}"
            //
            // Given a BatchPublishSpec with mock HTTP
            // When channels is set to [channel_name_1, channel_name_2, channel_name_3]
            // Then the serialized spec in the captured request contains channels as a string array

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BSP2b/messages-array-objects-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BSP2b")]
        public Task BSP2b_MessagesArrayObjects()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BSP2b-${random_id()}"
            //
            // Given a BatchPublishSpec with mock HTTP
            // And the mock is configured to capture requests
            // When messages contains multiple Message objects with:
            //   - Message(name: "event1", data: "data1")
            //   - Message(name: "event2", data: { "key": "value" })
            // Then the serialized spec in the captured request contains messages as an array of message objects
            // And each message is serialized according to TM* rules

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BPR2a/success-channel-name-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BPR2a")]
        public Task BPR2a_SuccessChannelName()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BPR2a-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with:
            //   { "channel": channel_name, "messageId": "msg123", "serials": ["s1"] }
            // When the response is parsed into BatchPublishSuccessResult
            // Then result.channel equals channel_name

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BPR2b/success-message-id-prefix-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BPR2b")]
        public Task BPR2b_SuccessMessageIdPrefix()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BPR2b-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with:
            //   { "channel": channel_name, "messageId": "unique-id-prefix", "serials": ["s1", "s2"] }
            // When the response is parsed into BatchPublishSuccessResult
            // Then result.messageId equals "unique-id-prefix"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BPR2c/serials-array-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BPR2c")]
        public Task BPR2c_SerialsArray()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BPR2c-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with:
            //   { "channel": channel_name, "messageId": "msg", "serials": ["serial1", "serial2", "serial3"] }
            // When the response is parsed into BatchPublishSuccessResult
            // Then result.serials equals ["serial1", "serial2", "serial3"]
            // And serials.length matches the number of messages published

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BPR2c/serials-null-conflated-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BPR2c")]
        public Task BPR2c_SerialsNullConflated()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BPR2c1-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with a response where some messages were conflated:
            //   { "channel": channel_name, "messageId": "msg", "serials": ["serial1", null, "serial3"] }
            // When the response is parsed into BatchPublishSuccessResult
            // Then result.serials equals ["serial1", null, "serial3"]
            // And the null indicates the second message was discarded due to conflation

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BPF2a/failure-channel-name-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BPF2a")]
        public Task BPF2a_FailureChannelName()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BPF2a-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with a failure:
            //   {
            //     "channel": channel_name,
            //     "error": { "code": 40160, "statusCode": 401, "message": "Not permitted" }
            //   }
            // When the response is parsed into BatchPublishFailureResult
            // Then result.channel equals channel_name

            return Task.CompletedTask;
        }

        // UTS: rest/unit/BPF2b/failure-error-info-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "BPF2b")]
        public Task BPF2b_FailureErrorInfo()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BPF2b-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with a detailed error:
            //   {
            //     "channel": channel_name,
            //     "error": {
            //       "code": 40160,
            //       "statusCode": 401,
            //       "message": "Channel operation not permitted",
            //       "href": "https://help.ably.io/error/40160"
            //     }
            //   }
            // When the response is parsed into BatchPublishFailureResult
            // Then result.error is an ErrorInfo
            // And result.error.code equals 40160
            // And result.error.statusCode equals 401
            // And result.error.message contains "not permitted"

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/partial-success-mixed-results-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_PartialSuccessMixedResults()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_allowed = "test-BatchResult1-allowed-${random_id()}"
            // channel_name_restricted = "test-BatchResult1-restricted-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And a BatchPublishSpec with channels: [channel_name_allowed, channel_name_restricted]
            // And the mock responds with mixed results:
            //   [
            //     { "channel": channel_name_allowed, "messageId": "msg1", "serials": ["s1"] },
            //     { "channel": channel_name_restricted, "error": { "code": 40160, ... } }
            //   ]
            // When batchPublish is called
            // Then the BatchResult contains both results
            // And result[0] is a BatchPublishSuccessResult
            // And result[1] is a BatchPublishFailureResult

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22c/distinguish-success-failure-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22c")]
        public Task RSC22c_DistinguishSuccessFailure()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-BatchResult2-${random_id()}"
            //
            // Given a BatchResult from batchPublish with mock HTTP
            // When iterating through results
            // Then each result can be identified as success or failure:
            //   - Success results have messageId and serials fields
            //   - Failure results have error field

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/empty-channels-rejected-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_EmptyChannelsRejected()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // Given a REST client with mock HTTP
            // When batchPublish is called with an empty channels array
            // Then an error is returned
            // And the error indicates invalid request

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/empty-messages-rejected-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_EmptyMessagesRejected()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22-Error2-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // When batchPublish is called with an empty messages array
            // Then an error is returned
            // And the error indicates invalid request

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/server-error-propagated-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_ServerErrorPropagated()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22-Error3-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock responds with HTTP 500:
            //   { "error": { "code": 50000, "statusCode": 500, "message": "Internal error" } }
            // When batchPublish is called
            // Then an AblyException is thrown
            // And exception.code equals 50000
            // And exception.statusCode equals 500

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/auth-error-propagated-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_AuthErrorPropagated()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22-Error4-${random_id()}"
            //
            // Given a REST client with invalid credentials and mock HTTP
            // And the mock responds with HTTP 401:
            //   { "error": { "code": 40101, "statusCode": 401, "message": "Invalid credentials" } }
            // When batchPublish is called
            // Then an AblyException is thrown
            // And exception.code equals 40101
            // And exception.statusCode equals 401

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/standard-headers-included-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_StandardHeadersIncluded()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22-Headers1-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called
            // Then the captured request includes:
            //   - X-Ably-Version: 2
            //   - Ably-Agent: <library-agent-string>
            //   - Content-Type: application/json

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/request-id-included-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_RequestIdIncluded()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22-Headers2-${random_id()}"
            //
            // Given a REST client with addRequestIds: true and mock HTTP
            // And the mock is configured to capture requests
            // When batchPublish is called
            // Then the captured request includes a request_id query parameter
            // And the request_id is a unique identifier

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/multiple-messages-per-channel-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_MultipleMessagesPerChannel()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name = "test-RSC22-Batch1-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to capture requests
            // And a BatchPublishSpec with:
            //   - channels: [channel_name]
            //   - messages: [100 Message objects]
            // When batchPublish is called
            // Then all 100 messages are included in the captured request body
            // And the mock response confirms all messages were processed

            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSC22/multiple-channels-multiple-messages-0
        [UtsFact(Skip = "Missing SDK public API: RestClient#batchPublish (RSC22); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSC22")]
        public Task RSC22_MultipleChannelsMultipleMessages()
        {
            // NOTE: missing SDK API: RestClient#batchPublish does not exist in this SDK, so the whole test is a missing-API stub
            // channel_name_1 = "test-RSC22-Batch2-a-${random_id()}"
            // channel_name_2 = "test-RSC22-Batch2-b-${random_id()}"
            // channel_name_3 = "test-RSC22-Batch2-c-${random_id()}"
            //
            // Given a REST client with mock HTTP
            // And the mock is configured to respond with results for each channel
            // And a BatchPublishSpec with:
            //   - channels: [channel_name_1, channel_name_2, channel_name_3]
            //   - messages: [msg1, msg2, msg3]
            // When batchPublish is called
            // Then the batch publishes all 3 messages to all 3 channels (9 total publications)
            // And the result contains 3 BatchPublishSuccessResult entries (one per channel)

            return Task.CompletedTask;
        }
    }
}
