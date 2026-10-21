// Revoke Tokens Tests (RSA17, RSA17b, RSA17c, RSA17d, RSA17e, RSA17f, RSA17g, BAR2, TRS2, TRF2)
// Derived from uts/rest/unit/auth/revoke_tokens.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class RevokeTokensTests : UtsUnitTestBase
    {
        public RevokeTokensTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA17g/sends-post-correct-path-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17g")]
        public Task RSA17g_SendsPostCorrectPath()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // result = AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ])
            // Assertions
            // ASSERT captured_requests.length == 1
            // ASSERT captured_requests[0].method == "POST"
            // ASSERT captured_requests[0].url.path == "/keys/appId.keyName/revokeTokens"
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17b/single-specifier-targets-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17b")]
        public Task RSA17b_SingleSpecifierTargets()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ])
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT request_body["targets"] == ["clientId:alice"]
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17b/multiple-specifier-types-1
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17b")]
        public Task RSA17b_MultipleSpecifierTypes()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 },
            // { "target": "revocationKey:group-1", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 },
            // { "target": "channel:secret", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice"),
            // TokenRevocationTargetSpecifier(type: "revocationKey", value: "group-1"),
            // TokenRevocationTargetSpecifier(type: "channel", value: "secret")
            // ])
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT request_body["targets"] == ["clientId:alice", "revocationKey:group-1", "channel:secret"]
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17c/all-success-result-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17c")]
        public Task RSA17c_AllSuccessResult()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 },
            // { "target": "clientId:bob", "issuedBefore": 1700000000000, "appliesAt": 1700000002000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // result = AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice"),
            // TokenRevocationTargetSpecifier(type: "clientId", value: "bob")
            // ])
            // Assertions
            // ASSERT result.successCount == 2
            // ASSERT result.failureCount == 0
            // ASSERT result.results.length == 2
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17c/mixed-success-failure-1
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17c")]
        public Task RSA17c_MixedSuccessFailure()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // req.respond_with(200, {
            // "successCount": 1,
            // "failureCount": 1,
            // "results": [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 },
            // { "target": "invalidType:abc", "error": { "code": 40000, "statusCode": 400, "message": "Invalid target type" } }
            // ]
            // })
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // result = AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice"),
            // TokenRevocationTargetSpecifier(type: "invalidType", value: "abc")
            // ])
            // Assertions
            // ASSERT result.successCount == 1
            // ASSERT result.failureCount == 1
            // ASSERT result.results.length == 2
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17c/all-failure-result-2
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17c")]
        public Task RSA17c_AllFailureResult()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // req.respond_with(200, {
            // "successCount": 0,
            // "failureCount": 2,
            // "results": [
            // { "target": "invalidType:foo", "error": { "code": 40000, "statusCode": 400, "message": "Invalid target type" } },
            // { "target": "invalidType:bar", "error": { "code": 40000, "statusCode": 400, "message": "Invalid target type" } }
            // ]
            // })
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // result = AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "invalidType", value: "foo"),
            // TokenRevocationTargetSpecifier(type: "invalidType", value: "bar")
            // ])
            // Assertions
            // ASSERT result.successCount == 0
            // ASSERT result.failureCount == 2
            // ASSERT result.results.length == 2
            return Task.CompletedTask;
        }

        // UTS: rest/unit/TRS2/success-result-attributes-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "TRS2")]
        public Task TRS2_SuccessResultAttributes()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // result = AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ])
            // Assertions
            // success = result.results[0]
            // ASSERT success IS TokenRevocationSuccessResult
            // ASSERT success.target == "clientId:alice"
            // ASSERT success.issuedBefore == 1700000000000
            // ASSERT success.appliesAt == 1700000001000
            return Task.CompletedTask;
        }

        // UTS: rest/unit/TRF2/failure-result-attributes-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "TRF2")]
        public Task TRF2_FailureResultAttributes()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // req.respond_with(200, {
            // "successCount": 0,
            // "failureCount": 1,
            // "results": [
            // { "target": "invalidType:abc", "error": { "code": 40000, "statusCode": 400, "message": "Invalid target type" } }
            // ]
            // })
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // result = AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "invalidType", value: "abc")
            // ])
            // Assertions
            // failure = result.results[0]
            // ASSERT failure IS TokenRevocationFailureResult
            // ASSERT failure.target == "invalidType:abc"
            // ASSERT failure.error IS ErrorInfo
            // ASSERT failure.error.code == 40000
            // ASSERT failure.error.statusCode == 400
            // ASSERT failure.error.message CONTAINS "Invalid target type"
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17d/token-auth-revoke-rejected-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17d")]
        public Task RSA17d_TokenAuthRevokeRejected()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(token: "a.token.string"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ]) FAILS WITH error
            // Assertions
            // ASSERT error.code == 40162
            // ASSERT error.statusCode == 401
            //
            // # No HTTP request should have been made
            // ASSERT captured_requests.length == 0
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17d/use-token-auth-revoke-rejected-1
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17d")]
        public Task RSA17d_UseTokenAuthRevokeRejected()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret", useTokenAuth: true))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ]) FAILS WITH error
            // Assertions
            // ASSERT error.code == 40162
            // ASSERT error.statusCode == 401
            // ASSERT captured_requests.length == 0
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17e/issued-before-included-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17e")]
        public Task RSA17e_IssuedBeforeIncluded()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1699999000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens(
            // [TokenRevocationTargetSpecifier(type: "clientId", value: "alice")],
            // options: { issuedBefore: 1699999000000 }
            // )
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT request_body["issuedBefore"] == 1699999000000
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17e/issued-before-omitted-1
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17e")]
        public Task RSA17e_IssuedBeforeOmitted()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ])
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT "issuedBefore" NOT IN request_body
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17f/reauth-margin-included-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17f")]
        public Task RSA17f_ReauthMarginIncluded()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000030000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens(
            // [TokenRevocationTargetSpecifier(type: "clientId", value: "alice")],
            // options: { allowReauthMargin: true }
            // )
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT request_body["allowReauthMargin"] == true
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17f/reauth-margin-omitted-1
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17f")]
        public Task RSA17f_ReauthMarginOmitted()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ])
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT "allowReauthMargin" NOT IN request_body
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17f/both-options-together-2
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17f")]
        public Task RSA17f_BothOptionsTogether()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1699999000000, "appliesAt": 1700000030000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens(
            // [TokenRevocationTargetSpecifier(type: "clientId", value: "alice")],
            // options: { issuedBefore: 1699999000000, allowReauthMargin: true }
            // )
            // Assertions
            // request_body = JSON_PARSE(captured_requests[0].body)
            // ASSERT request_body["targets"] == ["clientId:alice"]
            // ASSERT request_body["issuedBefore"] == 1699999000000
            // ASSERT request_body["allowReauthMargin"] == true
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17/server-error-propagated-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17")]
        public Task RSA17_ServerErrorPropagated()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // req.respond_with(500, {
            // "error": { "code": 50000, "statusCode": 500, "message": "Internal error" }
            // })
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ]) FAILS WITH error
            // Assertions
            // ASSERT error.code == 50000
            // ASSERT error.statusCode == 500
            return Task.CompletedTask;
        }

        // UTS: rest/unit/RSA17/request-uses-basic-auth-0
        [UtsFact(Skip = "Missing SDK public API: Auth#revokeTokens (RSA17); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "RSA17")]
        public Task RSA17_RequestUsesBasicAuth()
        {
            // NOTE: missing SDK API: IAblyAuth has no revokeTokens and the SDK has no TokenRevocationTargetSpecifier, TokenRevocationSuccessResult
            // or TokenRevocationFailureResult types (rest-mapping.md section 8, Missing APIs), so the whole test is a stub. Spec lines:
            // Setup
            // captured_requests = []
            //
            // mock_http = MockHttpClient(
            // onConnectionAttempt: (conn) => conn.respond_with_success(),
            // onRequest: (req) => {
            // captured_requests.append(req)
            // req.respond_with(200, [
            // { "target": "clientId:alice", "issuedBefore": 1700000000000, "appliesAt": 1700000001000 }
            // ])
            // }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyName:keySecret"))
            // Test Steps
            // AWAIT client.auth.revokeTokens([
            // TokenRevocationTargetSpecifier(type: "clientId", value: "alice")
            // ])
            // Assertions
            // ASSERT captured_requests[0].headers["Authorization"] STARTS WITH "Basic "
            return Task.CompletedTask;
        }
    }
}
