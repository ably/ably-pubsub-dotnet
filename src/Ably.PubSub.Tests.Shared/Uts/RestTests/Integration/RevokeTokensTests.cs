// Revoke Tokens Integration Tests (RSA17, RSA17b, RSA17c, RSA17d, RSA17e, RSA17f, RSA17g, TRS2, TRF2)
// Derived from uts/rest/integration/revoke_tokens.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: integration
// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Integration
{
    /// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
    [Collection(UtsSandboxCollection.Name)]
    [Trait("uts", "integration")]
    [Trait("type", "integration")]
    public class RevokeTokensTests : UtsIntegrationTestBase
    {
        private const int TestTimeoutMs = 60000;

        public RevokeTokensTests(SandboxAppFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        // UTS: rest/integration/RSA17g/revoke-token-prevents-use-0
        [Fact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA17g")]
        public Task RSA17g_RevokeTokenPreventsUse()
        {
            // NOTE: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier / TokenRevocationSuccessResult
            // types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // client_id = "revoke-client-" + random_id()
            // # Create a key-auth REST client (using the revocable key) for revoking and token issuance
            // key_client = Rest(options: ClientOptions(key: revocable_key, endpoint: "nonprod:sandbox"))
            // # Request a native token for the clientId
            // token_details = AWAIT key_client.auth.requestToken(clientId: client_id)
            // # Create a Realtime client using the token, and wait for it to connect
            // realtime_client = Realtime(options: ClientOptions(token: token_details, endpoint: "nonprod:sandbox"))
            // AWAIT realtime_client.connection.once("connected")
            // # Step 1: Set up a disconnected listener BEFORE revoking (to not miss the event)
            // disconnected_promise = realtime_client.connection.once("disconnected")
            // # Step 2: Revoke the token by clientId
            // revoke_result = AWAIT key_client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: client_id)
            // ])
            // # Step 3: Verify the revokeTokens response structure (RSA17c, TRS2)
            // ASSERT revoke_result.successCount == 1
            // ASSERT revoke_result.failureCount == 0
            // ASSERT revoke_result.results.length == 1
            // success = revoke_result.results[0]
            // ASSERT success IS TokenRevocationSuccessResult
            // ASSERT success.target == "clientId:" + client_id
            // ASSERT success.issuedBefore IS number
            // ASSERT success.appliesAt IS number
            // # Step 4: Verify the Realtime client is disconnected with 40141 (token revoked)
            // state_change = AWAIT disconnected_promise
            // ASSERT state_change.reason.code == 40141
            return Task.CompletedTask;
        }

        // UTS: rest/integration/RSA17d/token-auth-revoke-rejected-0
        [Fact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA17d")]
        public Task RSA17d_TokenAuthRevokeRejected()
        {
            // NOTE: IAblyAuth has no revokeTokens (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // # Generate a JWT using the revocable key
            // jwt = generate_jwt(key_name: extract_key_name(revocable_key), key_secret: extract_key_secret(revocable_key), ttl: 3600000)
            // # Create a client using token auth (JWT)
            // token_rest = Rest(options: ClientOptions(token: jwt, endpoint: "nonprod:sandbox", useBinaryProtocol: false))
            // AWAIT token_rest.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "anyone")
            // ]) FAILS WITH error
            // ASSERT error.code == 40162
            // ASSERT error.statusCode == 401
            return Task.CompletedTask;
        }

        // UTS: rest/integration/RSA17e/issued-before-reauth-margin-0
        [Fact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA17e")]
        public Task RSA17e_IssuedBeforeReauthMargin()
        {
            // NOTE: IAblyAuth has no revokeTokens (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // client_id = "revoke-margin-client-" + random_id()
            // key_client = Rest(options: ClientOptions(key: revocable_key, endpoint: "nonprod:sandbox"))
            // # Step 1: Revoke with issuedBefore and allowReauthMargin
            // server_time = AWAIT key_client.time()
            // # Use an issuedBefore in the past to avoid affecting any active tokens
            // issued_before = server_time - (20 * 60 * 1000)
            // revoke_result = AWAIT key_client.auth.revokeTokens(
            // [TokenRevocationTargetSpecifier(type: "clientId", value: client_id)],
            // options: { issuedBefore: issued_before, allowReauthMargin: true }
            // )
            // ASSERT revoke_result.successCount == 1
            // ASSERT revoke_result.results.length == 1
            // # RSA17e: issuedBefore should reflect what we sent
            // ASSERT revoke_result.results[0].issuedBefore == issued_before
            // # RSA17f: allowReauthMargin delays appliesAt by ~30 seconds
            // ASSERT revoke_result.results[0].appliesAt > server_time + (30 * 1000)
            return Task.CompletedTask;
        }

        // UTS: rest/integration/RSA17c/mixed-success-failure-0
        [Fact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)", Timeout = TestTimeoutMs)]
        [Trait("spec", "RSA17c")]
        public Task RSA17c_MixedSuccessFailure()
        {
            // NOTE: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationFailureResult type
            // (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // client_id = "revoke-mixed-client-" + random_id()
            // # Create a key-auth REST client for revoking and token issuance
            // key_client = Rest(options: ClientOptions(key: revocable_key, endpoint: "nonprod:sandbox"))
            // # Request a native token for the clientId
            // token_details = AWAIT key_client.auth.requestToken(clientId: client_id)
            // # Create a Realtime client using the token, and wait for it to connect
            // realtime_client = Realtime(options: ClientOptions(token: token_details, endpoint: "nonprod:sandbox"))
            // AWAIT realtime_client.connection.once("connected")
            // # Step 1: Set up a disconnected listener BEFORE revoking
            // disconnected_promise = realtime_client.connection.once("disconnected")
            // # Step 2: Revoke with one valid and one invalid specifier
            // revoke_result = AWAIT key_client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: client_id),
            // TokenRevocationTargetSpecifier(type: "invalidType", value: "abc")
            // ])
            // # Step 3: Verify the response contains both success and failure
            // ASSERT revoke_result.successCount == 1
            // ASSERT revoke_result.failureCount == 1
            // ASSERT revoke_result.results.length == 2
            // # Valid specifier succeeds
            // success = revoke_result.results[0]
            // ASSERT success IS TokenRevocationSuccessResult
            // ASSERT success.target == "clientId:" + client_id
            // ASSERT success.issuedBefore IS number
            // ASSERT success.appliesAt IS number
            // # Invalid specifier fails
            // failure = revoke_result.results[1]
            // ASSERT failure IS TokenRevocationFailureResult
            // ASSERT failure.target == "invalidType:abc"
            // ASSERT failure.error IS ErrorInfo
            // ASSERT failure.error.statusCode == 400
            // # Step 4: Verify the Realtime client is disconnected with 40141 (token revoked)
            // state_change = AWAIT disconnected_promise
            // ASSERT state_change.reason.code == 40141
            return Task.CompletedTask;
        }
    }
}
