# UTS deviations

Where a derived test does not simply pass, this is the record of why.

**How this document grows.** It is built tier by tier, alongside the tests: each tier's derived
tests arrive together with the entries they produced, so the record and the evidence for it are
never in different places. Identifiers — `D1`, `S2`, `A3` and so on — are assigned in the order
findings were made and are never reused, so a gap in the numbering means an entry that lands with a
later tier.

Every entry below was diagnosed with the decision tree in
`uts/docs/writing-derived-tests.md` §2: is the **UTS spec** wrong (2a), is the **translation** wrong
(2b), or is the **SDK** wrong (2c)? Only 2c is a deviation. Each claim was then given to an
independent reviewer told to **refute** it, with `specifications/features.md` as the authority. Three
claims did not survive that review and are recorded here as what they turned out to be, not as
deviations.

Entries are grouped by **root cause**, not by test: five tests failing for one reason are one entry.

The first four sections below are the ones `writing-derived-tests.md` prescribes, in the order it
prescribes them. The fifth is an addition: a suspicion that was chased and turned out not to be a
defect leaves no entry anywhere in the standard four, and writing it down is what stops the next
reader reaching the same first conclusion.

Reproduce any single one with:

```bash
RUN_DEVIATIONS=1 dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj \
  -f net6.0 -c Release --filter "FullyQualifiedName~<spec point>"
```

**The invariant to re-check, not a count to copy forward:** every gated test skips by default and
**fails** under `RUN_DEVIATIONS=1`, and none passes under both behaviours. A gated test that passes
under the gate is a fixed bug whose entry should be removed, not a count that drifted.

---

## UTS Spec Errors

The spec itself is wrong — it contradicts the features spec or over-specifies beyond it. The fix
belongs upstream in `ably/specification`, not in this SDK.

`writing-derived-tests.md` prescribes a fail-fast test for these. Every one below is instead gated
or adapted, deliberately: each rests on the features spec being **silent** rather than on a
contradiction, so hard-failing the suite would claim more confidence than the evidence supports.
Each is written up here so the question reaches the spec.

### S1 — RSC7d's agent-header pattern cannot match a multi-segment family name

**Spec point:** RSC7d, RSC7d1. **Test:** `RestClientTests.RSC7d_AblyAgentHeaderFormat` (passes, widened).

`rest/unit/rest_client.md` asserts the `Ably-Agent` header matches
`ably-[a-z]+/[0-9]+\.[0-9]+\.[0-9]+`. Features spec RSC7d1 (features.md:100) defines the identifier
as "`key[/value]` entries joined by spaces where `key` is the name of a product", with the example
`ably-flutter/1.2.0 ably-java/1.2.1 android/24`. Nothing restricts a product name to a single
hyphen.

This SDK emits `ably-pubsub-dotnet/<version>` (`Agent.cs:49`), registered in `ably-common` as a
versioned `sdk` entry. `[a-z]+` cannot span the second hyphen, so the pattern rejects a legitimate
name — and would also reject a hypothetical `ably-chat-js`.

**Suggested upstream fix:** `ably-[a-z-]+/\d+\.\d+\.\d+`. The derived test widens the character
class and asserts the shape the spec actually means (family name, slash, semver).

### S2 — RSL1k's mixed-id batch expectation contradicts RSL1k3

**Spec point:** RSL1k. **Test:** `IdempotencyTests` (`RSL1k/mixed-ids-in-batch-1` is not derived).

`rest/unit/channel/idempotency.md` expects the id-less message in a mixed batch to receive a
library-generated `base:serial` id. Features spec **RSL1k3** (features.md:313) says the opposite in
as many words: "If more than one `Message` is passed to `publish()` and one or more of those
messages contains a non-empty `id` attribute, then **all message ids (present or absent) are
preserved** on sending the batch of messages." RSL1k1 gates generation on *all* ids being empty.

`HttpChannel.cs:94` implements exactly that (`messages.All(m => m.Id == null)`). **The SDK is right
and the UTS spec is wrong.** Worth reporting promptly: an SDK that "fixed" itself to satisfy the UTS
spec would become non-compliant with RSL1k3.

### S3 — RSC19f's leading-slash leniency is not in the features spec

**Spec point:** RSC19f. **Test:** `RequestTests.RSC19f_PathLeadingSlashHandlingWithoutSlash` (gated).

The UTS spec adds a second case passing `"channels/test"` and expects it to reach the service as
`/channels/test`. Features spec RSC19f (features.md:150) says only that `path` is "the path component
of the URL such as `/channels`" — the example carries the slash and nothing requires tolerating its
absence.

The SDK's handling is nonetheless poor and worth a separate low-priority robustness issue:
`AblyHttpRequester.GetRequestUrl` (`AblyHttpRequester.cs:472`) interpolates scheme, host, port and
path with no separator, producing `https://rest.ably.io:443channels/test`, whose authority has no
parseable port — so the call dies with an `AblyException` wrapping a `UriFormatException` about
ports, which says nothing about the real cause. Either prepend the slash or raise a clear error.

### S4 — RSA10k's setup cannot sign a token under a literal reading of RSA10j

**Spec point:** RSA10k. **Test:** `AuthorizeTests.RSA10k_AuthorizeQueryTime` (adapted, passing).

RSA10k's setup constructs the client with a key, then calls `authorize(authOptions:
AuthOptions(queryTime: true))` and expects a token request to follow. That only works if the
constructor's key stays usable through a supplied `AuthOptions` — which is what the file's own
RSA10i section ("the API key from `ClientOptions` is preserved even when `authOptions` are
provided") asserts.

RSA10j (features.md:255) says the opposite plainly enough: "When the arguments are present, **even if
empty**, the `TokenParams` and `AuthOptions` supersede any previously client library configured
`TokenParams` and `AuthOptions`." This SDK implements that literally —
`AblyAuth.AuthorizeAsync` (`AblyAuth.cs:557`) assigns the supplied options to `CurrentAuthOptions`
wholesale, and the token request reads `authOptions.Key` (`AblyAuth.cs:286`) — so a bare
`AuthOptions(queryTime: true)` leaves no signing key and the call raises 80019 "TokenAuth is on but
there is no way to generate one".

Not recorded as SDK non-compliance: RSA10j's text supports what the SDK does. What needs settling
upstream is RSA10j vs. RSA10e/RSA10i — whether "supersede even if empty" is meant to include
`AuthOptions#key`, given RSA10e makes a key one of the three "ways of obtaining a token" that
`authorize` is supposed to find in its `authOptions`. Until it is settled, the derived test restates
the key so it can reach RSA10k's actual subject (that `queryTime` sources the timestamp from
`/time`).

Worth noting either way: because the superseded options are *stored*, one `authorize` call with a
keyless `AuthOptions` leaves the client unable to authorize again for its lifetime, even though
`ClientOptions.Key` is untouched. RSA10g also exempts `AuthOptions#queryTime` from being stored as a
default, which this SDK does not honour — it stores the whole object.

### S5 — RSP5e repeats the invalid `json/base64` chain

**Spec point:** RSP5e. **Test:** `RestPresenceTests.RSP5_DecodeChainedEncoding` (passing, asserting
the compliant outcome).

The same spec error already set out under [D8](#d8--fromencoded-throws-on-a-failed-decode-step-instead-of-degrading):
RSL4d1 defines base64 as the transform for binary and RSL4d3 defines json as producing a string, so
`json/base64` hands the json step raw bytes and there is nothing it can legally do. The canonical
chain is `json/utf-8/base64`, which the neighbouring RSP5d test uses and which this SDK decodes
correctly.

On the presence path the SDK does the RSL6b-correct thing with the invalid chain — payload as of the
last successful decoding, residual transform left in `encoding`. Measured: `byte[]` holding
`{"key":"value"}` with encoding `json`. The derived test asserts that, so it runs and pins the
compliant behaviour instead of skipping indefinitely on a chain the spec should not be asking for.

Worth fixing upstream in both places at once.

### S6 — RSP5g's ciphertext cannot be the plaintext it claims

**Spec point:** RSP5g. **Test:** `RestPresenceTests.RSP5_DecodeCipherChannel` (passing, with an
adapted fixture).

The fixture gives `encrypted_data = "HO4cYSP8LybPYBPZPHQOtuD53yrD3YV3NBoTEYBh4U0="` as the
AES-128-CBC encryption of `{"secret":"data"}` under key `WUP6u0K7MXI5Zeo0VppPwg==`. It cannot be.
That base64 decodes to **32 bytes**, which under CBC is a 16-byte IV plus a single 16-byte block —
at most 15 bytes of plaintext once PKCS7 padding is accounted for. The claimed plaintext is 17
bytes and needs 48 bytes (IV + two blocks). Whatever that string encrypts, it is something else.

Measured against the SDK: decoding stopped after the base64 step with
`json/utf-8/cipher+aes-128-cbc` still in `encoding` — again the RSL6b-correct response to a payload
it cannot decrypt. Nothing here is an SDK defect; the cipher params do reach the presence decoder
(`MessageHandler.cs:307` builds the decoding context from `request.ChannelOptions`, and
`HttpChannel` passes the channel's `Options` on both the presence and presence-history requests).

The derived test keeps the spec's assertion — the full chain decodes to an object — and builds the
ciphertext with the SDK's own cipher from the spec's key and plaintext, so it tests what RSP5g is
about: that presence data is decrypted using the channel's cipher options. Upstream should
regenerate the fixture.

### S7 — RTN15h1's expected `errorReason.code` contradicts RSA4a2

**Spec points:** RTN15h1, RSA4a2. **Test:** `ConnectionFailuresTests.RTN15h1_TokenErrorNoRenew`
(passing, asserting the RSA4a2 code).

The spec test asserts `errorReason.code == 40142` — the code the server put on the DISCONNECTED.
RSA4a2 (features.md:190) says what the library must report instead: when a token or tokenDetails was
used and there is no means to renew it, and the server responds with a token error, "the client
library should indicate an error with error code **40171**, not retry the request and, in the case
of the realtime library, transition the connection to the `FAILED` state". RTN15h1 itself requires
only that the connection goes to FAILED and that `Connection#errorReason` "will be set" — it names
no code.

Measured: this SDK reports 40171 with status 401, which is the compliant answer. The derived test
asserts that, with the spec's 40142 recorded here. Upstream should either drop the code assertion or
change it to 40171.

---

## Failing Tests

SDK non-compliance where the spec-correct assertion is preserved and env-gated. Each maps to an
issue worth filing.

### D1 — `StatusAsync` sends the channel name unescaped

**Spec point:** RSL8. **Test:** `RestChannelAttributesTests.RSL8_StatusSpecialCharsEncoded`.

**Spec:** a variable path segment is percent-encoded (`uts/docs/writing-test-specs.md:580` prescribes
`encode_uri_component`, applied across 10+ spec files).

**SDK:** `HttpChannel.cs:30` builds `_basePath` as `$"/channels/{name.EncodeUriPart()}"` —
`Uri.EscapeDataString`, so `:` → `%3A`, `/` → `%2F`. Every other channel operation uses it.
`HttpChannel.cs:233` — `StatusAsync` — ignores `_basePath` and concatenates `"/channels/" + Name`.
A grep for `"/channels/` over product code returns exactly those two lines, so this is the **only**
raw-name path concatenation in the SDK.

**Why it matters more than the colon suggests.** `StatusAsync` performs *no* encoding, so a channel
named `a/b` emits `/channels/a/b` — two segments, a structurally different URL hitting a different
endpoint — and `?` or `#` would truncate the path into a query or fragment. A literal `:` is legal
in a path segment per RFC 3986, so that character alone is interoperable; it is simply the cheapest
one that exposes the missing escape.

**Not a spec over-reach.** A passing, non-gated sibling pins the other spelling:
`HistoryTests.RSL2_RequestUrlFormat` asserts exact equality that `with:colon` → `with%3Acolon` on the
publish/history path. The SDK contradicts itself.

**Root cause:** `StatusAsync` arrived already concatenating the raw name and never reused the
`_basePath` its own constructor had built. Nothing in the test tree exercised it before this UTS
file, so the drift was never caught. **Fix:** one line — `_ablyRest.CreateGetRequest(_basePath)`.

**Status:** open bug. Not fixed here: this work adds test seams only.

**Same root cause, not yet exercised:** `PushAdmin.cs:127,313,340,413` interpolate a raw `deviceId`
into `/push/deviceRegistrations/{...}` with no `EncodeUriPart`, while
`uts/rest/unit/push/push_device_registrations.md:148` (RSH1b1) asserts
`encode_uri_component("device/with special:chars")`. Expect a second instance when that spec file is
derived.

### D2 — a CloudFront 4xx does not trigger a fallback host

**Spec point:** RSC15l4. **Test:** `FallbackTests.RSC15l4_CloudFrontErrorTriggersFallback`.

**Spec:** features.md:145 — fallback is required for "a response with a `Server: CloudFront` header
and an HTTP status code `>= 400`". It is its own numbered sub-point, so this is not the UTS
over-specifying.

**SDK:** `AblyHttpRequester.IsRetryableResponse` (`AblyHttpRequester.cs:370-373`) delegates entirely
to `ErrorInfo.IsRetryableStatusCode` (`ErrorInfo.cs:258-261`), which is `statusCode >= 500 && <= 504`.
A case-insensitive grep for "cloudfront" over `src/` hits only the test file. RSC15l3 is implemented;
RSC15l4 is not implemented at all.

**Status:** open bug, clean and standalone. The mock does deliver the header and `AblyResponse.Headers`
already preserves response headers, so nothing structural blocks a fix. Note for the implementer:
RSC15l4's literal `>= 400` would make a CloudFront **401** trigger a fallback too, which collides
with the token-renewal path — worth a sentence in the ticket.

### D3 — `ErrorMessage` is populated from the error *code* header

**Spec point:** RSC19d, HP6, HP7. **Test:** `RequestTests.RSC19d_ResponseErrorMessageHeader`.

**Spec:** features.md:1650-1651 — `errorCode` comes from `X-Ably-Errorcode`, `errorMessage` from
`X-Ably-Errormessage`. (HP7's own prose says "populated with the error code", which is a slip in the
features spec, but it names the message header and types the field `String`.)

**SDK:** `HttpPaginatedResponse.cs:72` reads `AblyErrorCodeHeader` — the *code* header — into
`ErrorMessage`. The string `X-Ably-Errormessage` appears nowhere in the SDK. Three consequences:
`ErrorMessage` returns the code; the message header is never read; and `ErrorMessage` is falsely
populated whenever the code header is present even if no message header was sent.

**Status:** open bug. One-line fix.

### D4 — `request_id` is a header, not a query parameter, and is not url-safe

**Spec point:** RSC7c, TI1. **Test:** `RestClientTests.RSC7c_RequestIdIncluded`.

**Spec:** features.md:98 — a `request_id` **query string parameter** holding "a random string
obtained by url-safe base64-encoding a sequence of at least 9 bytes", which must survive a fallback
retry and "must be included in the `ErrorInfo` returned to the user".

**SDK:** three of five sub-requirements fail.

| Requirement | Holds? | Evidence |
|---|---|---|
| query string parameter | **no** | sent as a header: `PubSubHttpClient.cs:186-187` calls `AddHeaders`, not `AddQueryParameters` |
| url-safe base64 | **no** | `StringUtils.cs:26-29` uses `Convert.ToBase64String` — standard base64, with `+`, `/` and `=` |
| included in the returned `ErrorInfo` | **no** | spliced into `ErrorInfo.Message` text only. **`ErrorInfo` has no `requestId` property at all**, though TI1 (features.md:1723) and the type listing (features.md:2850) both require one |
| ≥ 9 bytes of randomness | yes | 16-byte GUID |
| survives a fallback retry | yes | minted once before the retry loop; covered by the passing `RSC7c_RequestIdPreservedOnFallbackRetry` |
| appears in log messages | yes | `WrapWithRequestId` |

`ClientOptions.cs:427` documents the option as "a `request_id` query string parameter", so the doc
comment and the code disagree.

**Status:** open bug, wider than it first looks — the missing `ErrorInfo.requestId` is a public-API
gap, not a one-liner.

### D5 — an unprocessable response content type crashes instead of raising 40013

**Spec point:** RSC8e2. **Test:** `RestClientTests.RSC8e_UnsupportedContentTypeOnSuccessStatus`.

**Spec:** features.md:117 — a response with `statusCode` < 400 whose content type the library cannot
process must raise statusCode 400, code **40013**, with a message naming the original status and a
base64 of the truncated body.

**SDK:** there is no content-type gate anywhere. `AblyResponse.GetResponseType` maps an unknown type
to `Binary`, leaving `TextResponse` null; `PubSubHttpClient.ExecuteRequest<T>` calls
`MessageHandler.ParseResponse<T>` **outside any try/catch** (the catch blocks live in the non-generic
overload, which has already returned); and `JsonHelper.Deserialize(null)` raises a raw
`ArgumentNullException` out of the public API. `ErrorCodes.InvalidMessageDataOrEncoding = 40013`
(`ErrorCodes.cs:22`) is declared and referenced nowhere in product code.

**This is a crash, not a wrong value** — it escapes the SDK's `AblyException` contract entirely.

**Status:** open bug. A fix should cover RSC8e1 too, which is only accidentally satisfied: the
sibling passing test asserts a status 500 that falls out of generic error handling, and the spec's
"message indicating that the content type is unsupported" plus truncated body is not implemented on
that path either.

### D6 — `memberKey` joins the ids in the wrong order

**Spec point:** TP3h. **Test:** `PresenceMessageTypesTests.TP3h_MemberKeyCombinesIds`.

**Spec:** features.md:1299 — `memberKey` "combines the `connectionId` and `clientId`", in that
order. Five sibling SDKs agree, three of them citing TP3h at the call site: ably-js
(`presencemessage.ts:69`), ably-java (`PresenceMessage.java:288`), ably-cocoa
(`ARTPresenceMessage.m:32`), ably-python (`presence.py:81`), ably-flutter
(`presence_message.dart:42`).

**SDK:** `PresenceMessage.cs:125` — `public string MemberKey => $"{ClientId}:{ConnectionId}"`, with
the XML doc one line above stating the reversed order outright.

`api-docstrings.md:939` says "the combined `clientId` and `connectionId`", which looks like support
for the SDK — but ably-java carries that exact docstring above an implementation returning
`connectionId:clientId`, so the prose is loose about *which* fields, not about order.

**Status:** open bug. The uniqueness property RTP2a relies on survives the reversal and the presence
map is internal, so there is no interop breakage — but `MemberKey` is public API with a
cross-SDK-stable documented format. Note for whoever fixes it:
`Tests.Shared/Realtime/PresenceSandboxSpecs.cs:749` pins the reversed order and must be updated with
it, and the fix is an observable change for anyone parsing `MemberKey`.

### D7 — a `Link` header with no query string crashes the paginated result

**Spec point:** TG, TG2. **Test:** `PaginatedResultTests.TG_LinkHeaderParsingRelationWithNoQueryString`
(a Theory, both cases).

**SDK:** `PaginatedRequestParams.cs:244` — `var queryString = url.Split('?')[1];`. For a relation
whose URL has no `?` (`</path>; rel="first"`), `Split` returns one element and the index throws
`IndexOutOfRangeException`. It fires inside the `PaginatedResult` constructor
(`PaginatedResult.cs:64`), so it escapes `HistoryAsync()` and **destroys the whole 200-OK response** —
the items the caller asked for are lost along with the unusable relation.

**Severity, stated honestly.** The live Ably API does not emit this shape — the real form carries
`./` and a query string, as the repo's own pre-existing fixture shows
(`DataRequestQueryTests.cs:16`) — so no user is hitting it today through history/stats/presence. The
exposure is `request()` / `HttpPaginatedResponse` against arbitrary paths. The features spec says
nothing about Link syntax; the nearest clause, TG2 (features.md:1636), requires that instantiating a
`PaginatedResult` "should not result in an error if paging headers are not returned", which points
the same way. Both reference SDKs degrade instead of crashing: ably-js drops the unparseable
relation, ably-java raises a typed `ErrorInfo` only if you follow it.

**Status:** open bug, low severity, defensive rather than compliance. Two-line guard
(`var parts = url.Split('?'); if (parts.Length < 2) return Empty;`), after which both UTS cases pass.

### D8 — `FromEncoded` throws on a failed decode step instead of degrading

**Spec point:** TM3, RSL6b. **Test:** `MessageTypesTests.TM3_FromEncodedDecodesEncodingJsonBase64`.

**Spec:** features.md:1246 — TM3 returns a message "decoded and decrypted as specified in RSL6 with
any residual transforms ... left in the `encoding` property per RSL6b"; features.md:344 — RSL6b
requires "an error message will be sent to the logger, but the message will still be delivered with
last successful decoding and the `encoding` field".

**SDK:** `MessageHandler.cs:541` converts the failed decode step into a thrown `AblyException`. Its
neighbour `FromEncodedArray`, twelve lines below, discards the same `Result` and degrades correctly —
so the SDK contradicts itself — and ably-js's `fromEncoded` logs and delivers rather than throwing.

**Read this with S-note.** The *chain* the spec uses to provoke it (`json/base64`) is itself a spec
error: RSL4d1 defines base64 for binary and RSL4d3 defines json as producing a string, so the
canonical chain is `json/utf-8/base64`, which this SDK decodes correctly (covered by the passing
`RSL6_ComplexChainedEncoding`). Do **not** "fix" `JsonEncoder` to accept a `byte[]` on the strength
of this test. The deviation is the *failure mode*, not the decoder.

The same mechanism on the REST history path is **compliant** and its test passes:
`MessageEncodingTests.RSL6a_DecodeChainedEncodings` asserts the RSL6b outcome — payload as of the
last successful decoding, residual transform left in `encoding`, error logged.

**Status:** open bug. One-line fix plus a decision on how to surface the error.

### D9 — REC1/REC2 endpoint configuration is not adopted

**Spec points:** REC1c1, REC1d2. **Tests:** `FallbackTests.REC1c1_EnvironmentConflictsRestHost`,
`REC1c1_EnvironmentConflictsRealtimeHost`, `REC1d2_RealtimeHostSetsPrimaryDomain`.

These three are symptoms of one gap, not three bugs. The 16 REC tests that cannot be written at all
are in [`coverage.md`](coverage.md); these three *compile*, so they are here.

**REC1c1 — a conflicting option set must be rejected.** features.md:45 says that if `restHost` or
`realtimeHost` is given alongside `environment`, "the options as a set are **invalid**"; TO3k2 and
TO3k3 (features.md:1942-1943) say "It is never valid to provide both". The parallel REC1b1 clause
calls the identical construction "**this check**", so it is a check the library performs, and
precedence is a separate branch (REC1c2). The SDK validates nothing: `ClientOptions.FullRestHost()`
(`ClientOptions.cs:148-152`) silently prefers `_restHost` and the `Environment` branch below is
unreachable.

**REC1d2 — `realtimeHost` should set the primary domain.** features.md:49 names `realtimeHost`, and
"primary domain" governs REST in the current spec (RSC25 at :120, RTN2 at :507, and both host options
now default to the same value at :2190). The SDK implements only the mirror direction:
`FullRealtimeHost()` falls back to `_restHost` with a warning, while `FullRestHost()` never reads
`_realtimeHost`.

**Status:** report as one issue, "the .NET SDK has not adopted REC1/REC2 endpoint configuration",
not as isolated regressions.

Two cautions for that issue:

- **REC1c1's current behaviour is pinned by an existing test.**
  `Tests.Shared/Http/HttpSpecs.cs:323-333` (`WithEnvironmentAndCustomHost_ShouldUseCustomHostAsIs`)
  asserts the non-compliant outcome deliberately. Fixing REC1c1 is therefore a **breaking change**
  and that test must be retired with it. Do not over-specify the error code: the features spec names
  none, and the 40000 in the derived test comes only from the UTS test's loose disjunction.
- **REC1d2 carries a genuine spec tension.** The UTS spec's own notes under REC1a
  (`fallback.md:630`) and REC2c1 (`fallback.md:1063`) tell legacy SDKs to expect *two* separate
  domains, which applied consistently would make this test not-applicable. It is not ruled a spec
  error because the tension lives in other clauses' notes and the features spec — the authority —
  grants no such concession. Worth one sentence upstream either way. Unlike REC1c1, nothing in the
  repo pins the non-compliant direction.

### D10 — `request()` does not renew a token on a token error

**Spec point:** RSC10. **Test:** `Rest.Integration.AuthTests.RSC10_TokenRenewalExpiredJwt`.
Measured against the live sandbox.

**Spec:** RSC10 — when a REST request fails with a token error (40140-40149) the client renews the
token and retries. The spec drives this through `client.request(...)`.

**SDK:** the renewal branch lives in `PubSubHttpClient.ExecuteRequest`'s `catch (AblyException)`
(`PubSubHttpClient.cs:208-231`), so it only fires when the 401 is *thrown*. `Request()` goes through
`HttpPaginatedRequestInternal`, which sets `NoExceptionOnHttpError` (`PubSubHttpClient.cs:306`), and
`AblyHttpRequester.Execute` then **returns** the error response instead of throwing
(`AblyHttpRequester.cs:141`). No renewal is attempted, the auth callback is invoked once, and the 401
reaches the caller.

**Status:** open bug, and specifically a `Request()`-shaped hole — the renewal path *is* reachable
from publish, history and stats, which do not set that flag. The spec chose `request()` as its
vehicle.

A second, independent reason a REST client cannot renew proactively, worth noting in the same issue:
`TokenDetailsExtensions.IsValidToken` returns true whenever `serverTime` is null
(`TokenDetails.cs:138-143`) and `AblyAuth.ServerNow` is null unless `QueryTime` is set, so an
already-expired token is sent as-is; and a `TokenDetails` built from a bare token string has
`Expires == DateTimeOffset.MinValue`, so `CanBeUsedToCheckExpiry` is false.

### D11 — a token whose `clientId` conflicts with `ClientOptions.clientId` is never rejected

**Spec points:** RSA15a, RSA15c, and case 2 of RSA7's consistency table.
**Tests:** `ClientIdTests.RSA15a_MismatchedClientIdIsAnError` (gated).

**Spec:** RSA15a — "any `clientId` provided in `ClientOptions` must match any non-wildcard `clientId`
value in `TokenDetails`"; RSA15c — a REST client must surface the incompatibility as an error, and
the spec file asserts code 40102.

**SDK:** the comparison does not exist. `ErrorCodes.IncompatibleCredentials` (40102) is declared at
`Types/ErrorCodes.cs:31` and referenced nowhere in product code. `AblyAuth.ClientId`
(`AblyAuth.cs:100`) resolves the identity as `ConnectionClientId ?? CurrentToken?.ClientId ??
CurrentTokenParams?.ClientId ?? Options.GetClientId()`, so a mismatched pair is resolved silently in
the *token's* favour and the configured `clientId` is discarded. `AblyAuth.ValidateClientIds`
(`AblyAuth.cs:656`) does catch an incompatible clientId, but only per-published-message and against
whatever `ClientId` already resolved to — so it cannot detect this conflict at all.

**Status:** open bug. Low blast radius for a REST client, but it silently changes who the caller is
publishing as, which is the kind of thing RSA15 exists to prevent.

### D12 — a wildcard token `clientId` overrides the client's own identity

**Spec point:** RSA15b. **Test:** `ClientIdTests.RSA15b_WildcardTokenPermitsAnyClientId` (gated).

**Spec:** RSA15b — if the `clientId` from `TokenDetails` is the wildcard `'*'`, the client "is
permitted to be either unidentified or identified by providing a `clientId`". The spec file asserts
that with `ClientOptions.clientId = "any-client"` and a `'*'` token, `auth.clientId` is
`"any-client"`.

**SDK:** `AblyAuth.ClientId` (`AblyAuth.cs:100`) prefers `CurrentToken?.ClientId`, so it reports
`"*"` and the explicit identity is lost. The wildcard is meant to *widen* what the client may be,
not to make it anonymous.

Same accessor as D11 and almost certainly the same fix, but a separate requirement: D11 is a missing
check, D12 is a wrong precedence. `ClientOptions.ClientId` already rejects `"*"` outright
(`ClientOptions.cs:44`), so the configured value is known to be a concrete identity whenever it is
set — which is exactly when it should win.

### D13 — a token string returned by `authCallback` is only ever read as a `TokenRequest`

**Spec points:** RSA8d, RSA8g, RSA16b.
**Tests:** `AuthCallbackTests.RSA8d_CallbackReturnsJwt`,
`TokenDetailsAccessorTests.RSA16b_TokenStringFromCallback` (both gated).

**Spec:** RSA8d — an `authCallback` may return "a token string, a `TokenDetails` object or a
`TokenRequest` object"; RSA8g requires coverage of a returned *Ably token string* and a *JWT string*;
RSA16b then requires `tokenDetails` to hold a `TokenDetails` with only `token` populated.

**SDK:** `AblyAuth.RequestTokenAsync` routes `callbackResult is string` straight into
`GetTokenRequest` (`AblyAuth.cs:335`), which deserialises it as a `TokenRequest`. There is no branch
that treats a string as a token, so both an Ably token string and a JWT fail with "AuthCallback
returned a string which can't be converted to TokenRequest", wrapped as 80019. Measured.

**Status:** open bug. Note the `authUrl` path does handle this correctly — `CallAuthUrl`'s
`ResponseType.Text`/`.Jwt` branch returns `new TokenDetails(response.TextResponse)`
(`AblyAuth.cs:381-386`) — so the fix is to give the callback path the same treatment. The callback
has no content type to go on, but it does not need one: a string that does not parse as a
`TokenRequest` is a token.

### D14 — an expired token is not renewed pre-emptively

**Spec points:** RSA4b1, RSA16c.
**Tests:** `TokenRenewalTests.RSA4b1_PreemptiveRenewal`,
`TokenDetailsAccessorTests.RSA16c_UpdatedAfterExpiryRenewal` (both gated).

**Spec:** RSA4b1 — a token known to be expired must be renewed *before* the request is sent, without
first making a request that is bound to fail. RSA16c requires `tokenDetails` to be replaced by such a
renewal.

**SDK:** the check is there but inert. `TokenDetailsExtensions.IsValidToken`
(`TokenDetails.cs:131`) returns true whenever the server time it is handed is null, and
`AblyAuth.ServerNow` is null unless `QueryTime` is set — so for an ordinary client the expiry is
never compared against anything. The expired token goes out, and renewal happens only if the server
rejects it. Measured with a `TestClock` advanced past expiry: the same token was reused and the auth
callback was invoked once.

**Status:** open bug, and the same root cause as the second note on D10. Two consequences worth
stating together in the issue: every client wastes a round trip on a token it already knows is dead,
and a client whose requests happen to succeed never renews at all.

### D15 — a token whose renewal failed is kept rather than cleared

**Spec point:** RSA16d. **Test:** `TokenDetailsAccessorTests.RSA16d_NullAfterInvalidation` (gated).

**Spec:** RSA16d — `tokenDetails` is null "if there is no current token, including after a previous
token has been determined to be invalid or expired".

**SDK:** `AblyAuth` only ever assigns `CurrentToken` on a successful token acquisition; no path
clears it. After a 40142 whose renewal attempt throws, the dead token is still in `CurrentToken` and
is used again on the next request. Measured: `CurrentToken.Token` was still `first-token` after the
renewal failed.

**Status:** open bug, low severity on its own — the next request fails the same way rather than
anything worse — but it makes `tokenDetails` an unreliable answer to "am I authenticated?", which is
what RSA16d is for.

### D16 — `authorize()` cannot return a client to basic auth

**Spec point:** RSA16d. **Test:** `TokenDetailsAccessorTests.RSA16d_NullAfterSwitchToBasic` (gated).

**Spec:** RSA16d — `tokenDetails` is null "if the library is using basic auth". The spec reaches that
state by calling `authorize(authOptions: AuthOptions(key: ..., useTokenAuth: false))`.

**SDK:** `AblyAuth.AuthorizeAsync` (`AblyAuth.cs:554`) goes straight to `RequestTokenAsync` and never
re-runs `CheckAndGetAuthMethod`, so `AuthMethod` is fixed at construction and `AuthOptions
.UseTokenAuth` is ignored on this path. The supplied key is used to sign a fresh token request
instead, and `CurrentToken` is left holding it. Measured.

**Status:** arguably a spec-reading question rather than a plain bug, and worth raising as such.
RSA10a says `authorize` "ensures Token Auth is used for all future requests", which reads as flatly
incompatible with honouring `useTokenAuth: false` in the same call — so the spec test may be asking
for something RSA10a forbids. What is *not* defensible either way is silently ignoring the option.

### D17 — `ttl` and `capability` are defaulted client-side in a token request

**Spec points:** RSA5, RSA6.
**Tests:** `TokenRequestParamsTests.RSA5_TtlNullWhenUnspecified`,
`TokenRequestParamsTests.RSA6_CapabilityNullWhenUnspecified` (both gated).

**Spec:** RSA5 and RSA6 both forbid client-side defaulting. If `tokenParams` specifies no TTL the
field must be null or absent in the token request so that Ably applies its own 60-minute default;
likewise an unspecified capability must be left absent so the token inherits the key's own
capabilities. RSA5 names the forbidden value explicitly: "implementations MUST NOT default this to
3600000 client-side".

**SDK:** `AblyAuth.CreateTokenRequestAsync` falls back to `TokenParams.WithDefaultsApplied()`
(`TokenParams.cs:96-104`), which sets `Ttl = Defaults.DefaultTokenTtl` (one hour) and `Capability =
Defaults.DefaultTokenCapability` (`Capability.AllowAll`, i.e. the `{"*":["*"]}` the spec calls out).
Both land in the signed request on the wire. Measured by parsing the request JSON.

**Status:** open bug, and the capability half is the one that matters: a token minted from a
restricted key gets a request asking for `{"*":["*"]}`. Ably intersects the request with the key's
own capability so the resulting token is not over-privileged, which is why this has gone unnoticed —
but the client is asking for more than it should, and any server-side policy that inspects the
requested capability sees the wrong thing.

### D18 — push device authentication sends the wrong header name

**Spec points:** RSH6a, and the RSH7a3/RSH7c3 halves of RSH7a2/RSH7c2.
**Tests:** `PushChannelTests.RSH7a3_SubscribeDeviceSendsDeviceAuth`,
`PushChannelTests.RSH7c3_UnsubscribeDeviceSendsDeviceAuth` (both gated).

**Spec:** RSH6a (features.md:1169) names the header and then goes out of its way to pre-empt exactly
this mistake: push device authentication adds an **`X-Ably-DeviceToken`** header, and "this header
has always been `X-Ably-DeviceToken`, but has previously been mistakenly documented as
`X-Ably-DeviceIdentityToken` in the hope of renaming it to avoid confusion with APNs device token.
It was never renamed."

**SDK:** `Defaults.DeviceIdentityTokenHeader` (`Defaults.cs:75`) is
`"X-Ably-DeviceIdentityToken"` — the name the spec identifies as the mistaken one — and
`PushAdmin.AddDeviceAuthenticationToRequest` (`PushAdmin.cs:459`) sends it. Measured on the wire:
the subscribe and unsubscribe requests carry `X-Ably-DeviceIdentityToken:
test-device-identity-token` and no `X-Ably-DeviceToken`. `Defaults.DeviceSecretHeader` is correct.

**Status:** open bug, and the highest-severity one in this set. The service is not expected to
recognise the header, so push device authentication from this SDK should be failing outright — any
`PushChannel` operation by a device holding an identity token, plus the admin
`deviceRegistrations.get`/`save`/`remove` and `channelSubscriptions.save`/`remove` paths that attach
device auth. Worth checking against a real app before assuming otherwise; a one-constant fix either
way.

The derived tests for RSH7a2 and RSH7c2 are split in two so this is the only part that skips: the
POST/DELETE path, body and query assertions in those spec tests pass and stay green.

### D19 — a `deviceId` is interpolated into the request path unescaped

**Spec point:** RSH1b1. **Test:** `PushDeviceRegistrationsTests.RSH1b1_GetUrlEncodesDeviceId`
(gated).

**Spec:** the device-registration path is `/push/deviceRegistrations/{deviceId}` with the id
URL-encoded, which the spec test states as `encode_uri_component("device/with special:chars")`.

**SDK:** `PushAdmin`'s device-registration paths are built by raw interpolation —
`$"/push/deviceRegistrations/{deviceId}"` (`PushAdmin.cs:340`, and the same shape on the save and
remove paths). Measured: a deviceId of `device/with special:chars` produced
`/push/deviceRegistrations/device/with%20special:chars` — the space was escaped by `Uri`
normalisation but the `/` and `:` were not, so the request addressed a two-segment path and a
different resource.

**Status:** open bug, same class as D1 (`StatusAsync` and the channel name) and a candidate for the
same fix: the SDK already has `EncodeUriPart`, used for channel names at `HttpChannel.cs:30` and
simply not used here.

### D20 — an empty push recipient or payload is accepted

**Spec point:** RSH1a.
**Tests:** `PushAdminPublishTests.RSH1a_RejectsEmptyRecipient`,
`PushAdminPublishTests.RSH1a_RejectsEmptyData` (both gated).

**Spec:** RSH1a — empty values for `recipient` and for `data` "should be immediately rejected",
without an HTTP request, with code 40000.

**SDK:** `PushAdmin.PublishAsync`'s `ValidateRequest` (`PushAdmin.cs:171`) checks `is null` and
nothing else, so an empty `JObject` passes both gates and a POST goes out carrying
`{"recipient":{}}`. Measured: no exception and a request on the wire. The null checks the spec also
asks for are present and correct, so `RSH1a_RejectsNullRecipient` passes.

**Status:** open bug, low severity — the server rejects the request anyway — but it costs a round
trip and the resulting error is a server 40000 about a malformed request rather than the local,
immediate one the spec asks for.

### D21 — the connection leaves FAILED on its own

**Spec points:** RTN14g, RTN15h1, RTN15j, and every test that samples a property after FAILED.
**Tests:** referenced by `ConnectionOpenFailuresTests`, `ConnectionFailuresTests` and
`ErrorReasonTests`, which read the FAILED transition out of a recorder rather than from
`Connection.State`.

FAILED is a terminal state: RTN11b lists it among the states only an explicit `connect()` leaves,
and nothing in RTN14/RTN15 reconnects from it. This SDK re-enters CONNECTING unprompted. Measured
twice today, with `DisconnectedRetryTimeout` pushed to ten minutes so the ordinary retry timer
cannot be the cause:

- RTN15h1: `Connected -> Disconnected(80003) -> Failed(40171) -> Connecting -> Connected`
- RTN15j: `Connected -> Disconnected(80003) -> Failed(50000) -> Connecting -> Connected`

The consequence for testing is what makes it worth a numbered entry: because
`RealtimeState.ConnectionData.UpdateState` reassigns `ErrorReason` on every transition and CONNECTING
carries none, a test that reaches FAILED and then samples `Connection.State` or
`Connection.ErrorReason` finds neither. Every affected derived test therefore reads the transition
out of `UtsClients.RecordConnectionStateChanges`, which is also the shape `mock_websocket.md`
prescribes.

**It is not only a testing problem.** RSA4a2 says that a client with no means to renew its token
"should ... not retry the request" when the server rejects it. The connection reaches FAILED
correctly - `TokenExpiryNonRenewableTests.RSA4a2_TokenErrorNonRenewableFailed` passes, with 40171 -
and then re-enters CONNECTING and tries again: three connection attempts measured where the spec
requires one, with `DisconnectedRetryTimeout` at ten minutes so the ordinary retry timer is ruled
out (`RSA4a2_TokenErrorNonRenewableNoRetry`, gated). A client holding a dead non-renewable token
reconnects in a loop instead of stopping. The same re-entry also makes
`RealtimeAuthorizeTests.RTC8c_AuthorizeFromFailedInitiatesConnection` untestable - by the time
`authorize()` is called the connection has already recovered, so the test is no longer exercising
RTC8c - and it is what stops RTC8a2's error reaching the caller (D24).

**Likely mechanism, not isolated.** Both measured paths pass through a DISCONNECTED that qualifies
for the RTN15a/RTN17j instant retry, which *queues* a `SetConnectingStateCommand`
(`RealtimeWorkflow.cs:1022`); the token-error or ERROR handler then sets FAILED, and the already
queued CONNECTING runs after it. If that is right, the fix is to drop queued reconnect work when a
terminal state is entered. I have not confirmed it by instrumenting the command queue, so treat the
mechanism as a lead and the measurements as the finding.

### D22 — a fatal connection-level ERROR during CONNECTING goes to DISCONNECTED, not FAILED

**Spec points:** RTN14g, RTN15c4.
**Tests:** `ConnectionOpenFailuresTests.RTN14g_ErrorEmptyChannelFailed`,
`ConnectionFailuresTests.RTN15c4_FatalErrorDuringResume` (both gated).

**Spec:** RTN14g (features.md:577) is unconditional — an ERROR with an empty channel attribute, for
any reason other than RTN14b, transitions the connection to FAILED. RTN15c4 says the same for an
ERROR arriving on a resume attempt.

**SDK:** `HandleConnectingErrorCommand` (`RealtimeWorkflow.cs:551-569`) conflates "retryable HTTP
status" with "recoverable connection failure": any non-token error whose status is 500-504 goes to
DISCONNECTED and is retried. Measured for both tests — a 50000/500 connection-level ERROR gives
`Connecting -> Disconnected(50000) -> Connecting -> ...` and never reaches FAILED.

**The gap is specific to CONNECTING, and that is now confirmed rather than assumed.** The same ERROR
arriving while CONNECTED *does* reach FAILED: `ConnectionFailuresTests.RTN15j_ErrorEmptyChannelFailed`
passes, measuring `Connected -> Disconnected(80003) -> Failed(50000)`. So RTN14g is implemented once
and missing once, which narrows the fix to the CONNECTING handler.

**Status:** open bug. The 500-504 branch should not apply to an ERROR that carries no channel.

### D23 — a token error on a resume opens one more transport than the spec expects

**Spec point:** RTN15c5. **Test:**
`ConnectionFailuresTests.RTN15c5_TokenErrorDuringResumeAttemptCount` (gated).

The substance of RTN15c5 is met and is asserted by the passing
`RTN15c5_TokenErrorDuringResume`: the token error on the resume attempt is routed to renewal
(`RealtimeWorkflow.cs:554` sends any token error during CONNECTING to
`HandleConnectingTokenErrorCommand`), exactly one new token is obtained, and the connection returns
to CONNECTED. Only the attempt count is wrong — four transports where the spec expects three.

Measured: `Connected -> Disconnected(80003) -> Connecting -> Disconnected(50000) -> Connecting ->
Connected -> Connected`, four connection attempts, two token requests. The trailing
`Connected -> Connected` is a second CONNECTED arriving on a second transport, which is what gives
it away.

**Status:** worth filing, low severity — a redundant connection attempt, not a wrong outcome. Kept
separate from D22 because the renewal path taken here is the correct one, so this is not the same
mis-routing; `HandleConnectingTokenErrorCommand` calls `AttemptANewConnection()` directly
(`RealtimeWorkflow.cs:509`) while the DISCONNECTED it passes through may also queue one, which would
explain it, but I have not isolated which of the two opens the extra transport.

### D24 — a FAILED state change during `authorize()` is never delivered to listeners

**Spec point:** RTC8a2. **Test:** `RealtimeAuthorizeTests.RTC8a2_FailedReauthFailsConnection`
(gated).

This is the most consequential finding in the realtime auth area, because the state change is not
merely late — it is **lost**.

**Spec:** RTC8a2 — if the reauth fails, `authorize()` fails with the error and the connection
transitions to FAILED with that error as its `errorReason`.

**Measured:** the SDK's own debug log records `Changing state from Disconnected => Failed` and
`Updating state to \`Failed\``, and the *next* transition reports `Failed -> Connecting` — so the
state really did become FAILED. But a listener armed **before** the reauth and given five seconds
never fires, and a recorder attached beforehand shows `Connected -> Disconnected(80003)` followed by
`Failed -> Connecting` with no `-> Failed` in between. `authorize()` also returns a token rather
than failing.

**Mechanism, and this one is nailed down:**

1. `ConnectionChangeAwaiter.Wait` builds its `TaskCompletionSource` with no
   `TaskCreationOptions.RunContinuationsAsynchronously` (`ConnectionChangeAwaiter.cs:28`) and
   completes it from an `InternalStateChanged` handler, so the awaiting continuation runs
   **synchronously, inline**.
2. That continuation is `ConnectionManager.OnAuthUpdated`'s wait loop, which for a failed state does
   `throw new AblyException(Connection.ErrorReason)` (`ConnectionManager.cs:213`).
3. `Connection.NotifyUpdate` calls `internalHandlers(this, stateChange)` **unguarded**
   (`Connection.cs:306`) and only wraps the *external* handlers in a try/catch two lines later.

So the throw unwinds `NotifyUpdate` before `RealtimeClient.NotifyExternalClients` on the next line
ever runs, and the application is never told the connection failed.

**Status:** open bug, and worth more than its single spec point. Any library-internal
`InternalStateChanged` handler that throws silently suppresses a state change the application is
relying on. Two independent fixes, either of which closes it: give the `TaskCompletionSource`
`RunContinuationsAsynchronously` so the continuation cannot run inside the emit, and guard
`internalHandlers` in `NotifyUpdate` the way the external handlers already are. Both are worth doing.

A third, smaller bug sits on the same line: `ChangeListener` calls `SetResult` rather than
`TrySetResult`, so a second transition arriving before the `-=` in the `finally` would throw
`InvalidOperationException` into the emit path as well.

### D25 — `authorize()` while CONNECTING neither halts nor restarts the attempt

**Spec points:** RTC8b, RTC8b1.
**Tests:** `RealtimeAuthorizeTests.RTC8b_AuthorizeWhileConnectingHaltsAttempt`,
`RTC8b1_AuthorizeWhileConnectingFailsOnFailed` (both gated).

**Spec:** RTC8b — `authorize()` on a CONNECTING connection halts the attempt in flight and, once the
new token is in hand, immediately starts a new attempt with it. RTC8b1 — if that new attempt ends in
FAILED, `authorize()` fails with the error.

**SDK:** the token is obtained and then dropped on the floor.
`ConnectionManager.OnAuthUpdated` takes its `Connection.State != Connected` branch and issues
`Connect()`, and `ConnectionConnectingState.Connect()` returns `EmptyCommand.Instance`
(`ConnectionConnectingState.cs:29-32`) — correct for an ordinary duplicate `connect()` call, wrong
here, because the existing attempt is using the *old* token. Measured: the connection stays
CONNECTING and no second transport is opened. RTC8b1 then fails for the same reason — the attempt
never fails, so there is nothing to report.

**Status:** open bug. The practical effect is that a `authorize()` issued during a slow connect is a
no-op: the attempt completes with the token it started with, and the caller's new token is only used
on some later reconnect.

### D26 — a 403 from `authCallback` during a reauth does not fail the connection

**Spec point:** RSA4d.
**Tests:** `AuthCallbackErrorsTests.RSA4d_Callback403ReauthFailed`,
`ConnectionAuthTests.RSA4d_Callback403ReauthCausesFailed` (both gated).

**Spec:** RSA4d is not conditional on connection state — an `authCallback` that produces an
`ErrorInfo` with statusCode 403 transitions the connection to FAILED. It is the one case that
overrides RSA4c3's "a failed reauth while connected changes nothing".

**SDK:** no distinction is made. Measured: a 403 from the callback during an RTN22 server-initiated
reauth is swallowed exactly like any other reauth failure and the connection stays CONNECTED. The
non-403 half is handled correctly — `AuthCallbackErrorsTests.RSA4c3_CallbackErrorConnectedStays`
passes, including its assertion that *no* state change at all is emitted — and a 403 at
connect time is handled correctly too (`RSA4d_Callback403ConnectingFailed` passes). The gap is
specifically a 403 arriving while CONNECTED.

**Status:** open bug. A client whose credentials have been revoked keeps running on its old token
until it expires, where the spec wants it to fail fast.

### D27 — REST auth-callback failures report 80019 instead of 40170, and the invalid-type status is 400

**Spec points:** RSA4e, RSA4c2 (via RSA4f).
**Tests:** `AuthCallbackErrorsTests.RSA4e_RestCallbackError40170`,
`AuthCallbackErrorsTests.RSA4f_CallbackInvalidTypeFormat` (both gated).

Two small pieces of wrong error metadata on the same code path.

**RSA4e** requires a REST request whose `authCallback` fails to "result in an error with code
**40170**, statusCode 401". Measured: code 80019, status Unauthorized, cause code 0, message "Error
calling AuthCallback, token request failed.". 80019 is the code RSA4c1 specifies for the *realtime*
case, and one shared wrapper in `AblyAuth.RequestTokenAsync` serves both, always raising
`ErrorCodes.ClientAuthProviderRequestFailed`. The SDK already knows about 40170 —
`ErrorCodes.ClientCallbackError` — and uses it for the inner null/timeout case
(`AblyAuth.cs:326-328`), so what is missing is a REST/realtime split at the outer wrapper.

**RSA4c2** requires "code 80019, statusCode 401" for an invalid token format. The code is right and
the connection does reach DISCONNECTED; only the status is wrong, because the
unsupported-callback-type branch raises it with `HttpStatusCode.BadRequest` (`AblyAuth.cs:343-347`),
giving 400.

**Status:** both open, both small. Worth one issue between them.

### D28 — `presence.get()` does not wait for the implicit attach it starts

**Spec points:** RTP11e, RTL33b (RTP11b in the spec file's numbering).
**Test:** `RealtimePresenceGetTests.RTP11b_GetImplicitlyAttaches` (gated).

**Spec:** RTP11b has been replaced by RTP11e (features.md:937), which requires `get()` to run the
*ensure-active-channel* procedure, RTL33. For a channel in INITIALIZED, RTL33b is unambiguous:
"perform an implicit attach per RTL4 **and wait for it to complete**", and RTL33b1 says the
procedure rejects with whatever error the attach failed on.

**SDK:** the attach is started and not awaited. Measured: with the channel in INITIALIZED,
`get(waitForSync: false)` resolved while the channel was still ATTACHING.

**Status:** open bug, and a narrow one - the attach does happen, so the member list arrives a beat
later rather than never. Worth filing because the caller cannot tell the difference between "no
members" and "not attached yet", which is exactly what RTL33b exists to prevent. Note also that the
rest of the RTP11e family is newer than this SDK: RTP11f's `strictMode` and its 91008 have no
counterpart here either, though no spec test in this file reaches them.

**A second RTL33b case, found while translating RTP5a.** RTL33b lists DETACHED alongside
INITIALIZED as a state that should trigger an implicit attach; this SDK raises 90001 "Invalid
channel state (Detached)" from `get()` instead. There is no UTS test that calls `get()` on a
detached channel directly — `realtime/unit/RTP5a/detached-clears-presence-maps-0` does so only as
its way of reading the map, and its derived test reads the map directly instead so it stays about
RTP5a. Recorded here so the RTL33b fix covers both states.

### D29 — the presence map never marks a member ABSENT, so a LEAVE during a sync deletes it

**Spec points:** RTP2h, RTP2h2, RTP2h2a, RTP2h2b.
**Tests:** `PresenceMapTests.RTP2h2a_LeaveDuringSyncStoresAbsent`,
`PresenceMapTests.RTP2_ValuesExcludesAbsent` (both gated).

**Spec:** RTP2h2 splits on whether a sync is running. RTP2h2a: if one is, "the incoming message must
be stored in the presence map with the action set to `ABSENT`". RTP2h2b: "when the `SYNC` completes,
then all `ABSENT` members in the presence map must be deleted". The ABSENT marker is what stops a
later SYNC message in the same sequence resurrecting a member that has just left.

**SDK:** `PresenceMap.Remove` (`PresenceMap.cs:118`) has no sync check at all — it deletes the entry
and returns. Nothing anywhere in the SDK ever writes `PresenceAction.Absent` into the map; the only
three references to it are the two places that *read* it. Which means the two halves that are
implemented correctly are both dead code:

- `Values` filters ABSENT members out (`PresenceMap.cs:77`) — nothing to filter.
- `EndSync` deletes ABSENT members (`PresenceMap.cs:174`) — nothing to delete.

**Status:** open bug. The failure it allows is specific: a member who leaves part-way through a
presence sync is deleted, and if an earlier-generated SYNC message in the same sequence still lists
them as PRESENT, `Put` puts them back — the newness check does not help, because the SYNC entry can
legitimately carry a higher `msgSerial` than the LEAVE. The member then stays in the map until the
next sync. RTP19's before-sync bookkeeping does not cover it either, since that only removes members
the sync never mentioned.

### D30 — `PresenceMap.Remove` reports a removal that did not happen

**Spec point:** RTP2h. **Test:** `PresenceMapTests.RTP2h1_LeaveNonexistentReturnsNull` (gated).

RTP2h opens "if and only if there is a member with a matching `memberKey` currently in the presence
map", so a LEAVE for an unknown member must do nothing and emit nothing.

`PresenceMap.Remove` (`PresenceMap.cs:118`) returns `true`. With no existing entry the newness check
is skipped, `TryRemove` quietly does nothing, the `existingItem?.Action == Absent` guard is false on
a null, and the method falls through to `return true`. Measured.

**Status:** open bug, and the cheapest fix in this file — the `TryGetValue` result is already in
hand. The user-visible effect is a spurious LEAVE event for a member the client never saw join.

### D31 — `PresenceMap.Clear` leaves the sync flag set

**Spec point:** RTP2 (the spec file's `clear-resets-state-3`).
**Test:** `PresenceMapTests.RTP2_ClearResetsState` (gated).

The spec's clear() test asserts `values()` is empty, the member is gone, **and**
`isSyncInProgress == false`. `PresenceMap.Clear` (`PresenceMap.cs:214`) empties `_members` and
`_beforeSyncMembers` and does not touch `SyncInProgress`, so a map cleared mid-sync still believes a
sync is running — which is what `get()` waits on.

**Status:** open bug, two lines. Grouped with D29 and D30 as one PresenceMap tidy-up.

### D32 — automatic presence re-entry is queued and never sent

**Spec points:** RTP17e, RTP17f, RTP17g, RTP17g1, RTP17i.
**Tests:** `RealtimePresenceReentryTests.RTP17i_AutoReentryOnAttached`,
`RTP17g_ReentryPublishesEnterWithStoredData`,
`RTP17g1_ReentryOmitsIdWhenConnectionIdChanged`,
`RTP17e_FailedReentryEmitsUpdateWithError` (all gated).

Four spec tests, one cause, and it is a three-line ordering mistake with a large consequence.

**Spec:** RTP17f/RTP17i — on receiving an ATTACHED that is not a RESUMED re-attach, the client must
re-enter every member it holds in its internal presence map, because the server has forgotten them.

**SDK:** the machinery is all there and correct — the internal map is maintained (`RTP17b`), it
survives a reconnect, and `EnterMembersFromInternalPresenceMap` (`Presence.cs:650`) builds exactly
the right ENTER for each member. The messages just never go out. `Presence.ChannelAttached`
(`Presence.cs:731-748`) runs in this order:

1. `StartSync()`
2. `EndSync()` if the ATTACHED carried no HAS_PRESENCE
3. `SendQueuedMessages()` — flushes the pending presence queue
4. `EnterMembersFromInternalPresenceMap()` — the re-entries

and it is called from `ChannelMessageProcessor` (`ChannelMessageProcessor.cs:82-83`) *before*
`SetChannelState(ChannelState.Attached)`. So at step 4 the channel is still ATTACHING, and
`UpdatePresence`'s ATTACHING branch (`Presence.cs:491-493`) enqueues each re-entry per RTP16b — into
the queue that was flushed one step earlier. Nothing flushes it again until the *next* attach.

**Measured.** With the member confirmed in the internal map (`internal=1`) and the channel going
`Attaching -> Attached` on the reconnect, no presence message reached the transport in half a
second, and none had arrived by the five-second deadline.

**Status:** open bug, and the most user-visible one in the presence area. After any non-resumed
reconnection — which is every reconnection that fails to resume — the client believes it is present
on the channel and the server does not, with no error raised anywhere. The fix is to enqueue the
re-entries before the flush, or to flush again after the state has moved to ATTACHED.

Worth knowing when reading the tests: while this stands,
`RTP17i_NoReentryWithResumedFlag` cannot fail, because nothing is ever re-entered. It is kept as
written rather than deleted — it is a correct assertion that starts doing real work the moment this
is fixed — but it should not be read as evidence that the RESUMED branch works.

### D33 — presence operations put the clientId on the wire where the spec forbids it

**Spec points:** RTP8c, RTP9d, RTP10c.
**Tests:** `RealtimePresenceEnterTests.RTP8a_EnterSendsPresenceEnter`,
`RTP9a_UpdateSendsPresenceUpdate`, `RTP10a_LeaveSendsPresenceLeave` (all gated).

All three clauses say the same thing in the same words. RTP8c (features.md:910): "A `PRESENCE
ProtocolMessage` with a `PresenceMessage` with the action `ENTER` is sent to the Ably service. The
`clientId` attribute of the `PresenceMessage` **must not be present**. Entering without an explicit
`PresenceMessage#clientId`, implicitly uses the `clientId` for the current connection." RTP9d and
RTP10c repeat it for UPDATE and LEAVE.

**SDK:** `Presence.EnterAsync` is `EnterClientAsync(_clientId, data)` (`Presence.cs:307`), and
`Update`/`Leave` do the same, so the connection's own clientId is written into the message. Measured
on the wire: `clientId: "my-client"` on all three.

**Status:** open bug, low severity but easy. The server derives the identity from the connection
anyway, so nothing breaks; it is redundant bytes on every presence operation and a divergence from
what the spec says the frame looks like. The fix is to pass null for the self-operations and keep
the explicit clientId only on the `*Client` variants, which already work correctly and are covered
by the passing RTP14a/RTP15a tests.

### D34 — an anonymous client's `enter()` is sent rather than refused

**Spec point:** RTP8j. **Test:** `RealtimePresenceEnterTests.RTP8j_EnterWithNoClientIdErrors`
(gated).

RTP8j (features.md:914-917) says that when `RealtimeClient#clientId` is `'*'` or `null` — "the
client is anonymous and is not permitted to associate a client identifier with the operations it
performs" — then "the `enter` request results in an error immediately".

This SDK sends it. `EnterAsync` passes its own empty `_clientId` through with no check, and the
request goes to the service, which refuses it under RTP8i. So the outcome is right and the place is
wrong: a round trip and a NACK instead of a local failure.

The wildcard half of RTP8j cannot arise here at all — `ClientOptions.ClientId` throws for `"*"`
(`ClientOptions.cs:42-48`), which the derived
`RTP8j_WildcardClientIdIsRejectedAtConstruction` asserts and which passes.

**Status:** open bug, low severity.

### D35 — a pending channel retry cannot be cancelled and ignores the channel's state

**Spec point:** RTL14. **Test:** `ChannelErrorTests.RTL14_CancelsPendingChannelRetryTimer` (gated).

**Spec:** RTL14 — a channel-scoped ERROR fails the channel, and the spec test that goes with it
checks that a channel retry already armed is cancelled rather than allowed to fire.

**SDK:** there is nothing to cancel. `RealtimeChannel.ReattachAfterTimeout`
(`RealtimeChannel.cs:785-804`) schedules the retry as a detached
`Task.Run(async () => { await Task.Delay(retryTimeout); ... })` and keeps no handle on it - no
`CountdownTimer`, no `CancellationToken` - so no later transition can stop it. Its only guard
before reattaching is `Connection.State == ConnectionState.Connected`; the channel's own state is
never consulted.

Measured: with `ChannelRetryTimeout` at 200ms, the channel reached FAILED on the ERROR and was back
in SUSPENDED a second later, having been reattached out of a terminal state.

**Status:** open bug. RTL13c's "only retry if the connection is connected" is implemented and the
channel half of the same question is missing, which is a small fix - check the channel state too,
and keep a handle so the transition can abort it.

Worth checking in the same pass, though I have not tested it: the same unguarded retry would
reattach a channel the application explicitly detached while the timer was armed, since DETACHED is
no more consulted than FAILED is.

### D36 — `attachSerial` is updated from a resumed ATTACHED

**Spec point:** RTL15c. **Test:**
`ChannelPropertiesTests.RTL15c_AttachSerialNotUpdatedWhenResumed` (gated).

RTL15c (features.md:793) scopes the update precisely: `attachSerial` "is updated with the
`channelSerial` from each `ATTACHED` `ProtocolMessage` received from Ably with a matching `channel`
attribute **whose [RTL2f] `resumed` attribute is `false`**".

`ChannelMessageProcessor.cs:59` assigns `channel.Properties.AttachSerial =
protocolMessage.ChannelSerial` for every ATTACHED, with no resumed check - the comment next to it
cites RTL15a rather than RTL15c. Measured: an unsolicited ATTACHED carrying the RESUMED flag moved
`attachSerial` from `initial-serial` to `resumed-serial`.

**Status:** open bug. `attachSerial` exists to anchor `untilAttach` queries, so moving it on a
resumed re-attach means a later `untilAttach` history query starts from the wrong point and silently
skips messages. The channel's own history has no `untilAttach` overload here (see coverage.md), but
`Presence.HistoryAsync(query, untilAttach: true)` does, and it reads this field.

### D37 — a server-initiated DETACHED routes through DETACHED, clearing `channelSerial`

**Spec points:** RTL13a, RTL15b2. **Test:**
`ChannelPropertiesTests.RTL15b2_ChannelSerialRetainedInSuspended` (gated).

This one is worth reading carefully, because the clause that appears to be broken is implemented
correctly. `RealtimeChannel.cs:692` clears `channelSerial` on DETACHED and FAILED only, and the
comment above it quotes RTL15b2's "(Unlike previous spec versions, it must not clear it when
entering the `SUSPENDED` state)". Nothing clears it on SUSPENDED.

What breaks it is the route taken to get there. RTL13a: on a server-initiated DETACHED while
ATTACHED, "an attempt to reattach the channel should be made immediately by sending a new `ATTACH`
message and the channel **should transition to the `ATTACHING` state**". This SDK enters DETACHED
first and reattaches from there, so the RTL15b2 clear fires on the way past. Measured state
sequence from ATTACHED, with the reattach left unanswered so it times out:
`Attaching, Detached, Suspended`, with `channelSerial` null on arrival.

(The emitted order puts Attaching before Detached because the reattach is kicked off from inside the
DETACHED state handler and emits before the outer transition does; the state is set to Detached
either way, which is what matters here.)

**Status:** open bug. The consequence is the one RTL15b2 was changed to prevent: a channel suspended
by a failed reattach cannot resume from where it left off, because the serial it would resume from
has been discarded. Fixing RTL13a's transition fixes RTL15b2 with it.

### D38 — a connection going away takes its channels through DETACHING

**Spec point:** RTL3b.
**Tests:** `ChannelConnectionStateTests.RTL3b_ClosedConnectionDetachesAttachedChannel`,
`RTL3b_ClosedConnectionDetachesAttachingChannel` (both gated).

RTL3b (features.md:695): "If the connection state enters the `CLOSED` state, then an `ATTACHING` or
`ATTACHED` channel state will **transition to** `DETACHED`". One transition, from the state the
channel was in.

`RealtimeChannel.DetachForConnectionGoingAway` (`RealtimeChannel.cs:252-261`) calls the ordinary
`Detach(...)`, which sets DETACHING first and only then DETACHED. Measured: the DETACHED change
arrives with `previous == Detaching` rather than Attached or Attaching, so a listener sees a
DETACHING the application never asked for.

That matters beyond the extra event, because RTL2's state model gives DETACHING a specific meaning —
the client has explicitly requested a detach — which is exactly what did not happen here. Code that
distinguishes "we are detaching because I asked" from "the connection went away" is misled.

**Status:** open bug, same family as D37: the destination is right and the route invents a state.
Both are the ordinary multi-step helper being reused where the spec describes a single transition.

### D39 — `attach()` on a DETACHING channel does not wait for the detach

**Spec point:** RTL4h. **Test:**
`ChannelAttachTests.RTL4h_AttachWhileDetachingWaitsThenAttaches` (gated).

RTL4h (features.md:704) covers both pending states in one sentence: "If the channel is in a pending
state `DETACHING` or `ATTACHING`, do the attach operation **after the completion of the pending
request**."

The ATTACHING half is implemented - `RTL4h_AttachWhileAttachingWaitsForCompletion` passes, with the
second `attach()` joining the first and only one ATTACH on the wire. The DETACHING half is not.
Measured with the server holding the DETACH unanswered: `attach()` took the channel straight from
DETACHING to ATTACHING and put a second ATTACH on the wire before the detach had completed.

**Status:** open bug. The practical damage shows up when the server's DETACHED finally lands: the
channel is in ATTACHING by then, so it is read as a server-initiated detach during an attach and
RTL13b sends the channel to SUSPENDED. So an `attach()` racing a `detach()` ends suspended rather
than attached, which is what the derived test records.

### D40 — `detach()` on an ATTACHING channel is dropped

**Spec point:** RTL5i. **Test:**
`ChannelDetachTests.RTL5i_DetachWhileAttachingWaitsThenDetaches` (gated).

RTL5i (features.md:723) is the mirror of RTL4h: "If the channel is in a pending state `DETACHING`
or `ATTACHING`, do the detach operation after the completion of the pending request."

The DETACHING half works - `RTL5i_DetachWhileDetachingWaitsForCompletion` passes, with the second
detach joining the first and one DETACH on the wire. The ATTACHING half does nothing at all:
measured, `detach()` issued while ATTACHING resolved with the channel still ATTACHING and **no
DETACH ever sent**, even after the ATTACHED arrived. The caller is told the detach completed and
the channel stays attached.

**Status:** open bug, and read it with D39 - RTL4h's DETACHING half and RTL5i's ATTACHING half are
the two cross-cases, and both are missing. Each spec point has one direction implemented and one
not.

### D41 — `detach()` on a SUSPENDED channel leaves it suspended

**Spec point:** RTL5j. **Test:**
`ChannelDetachTests.RTL5j_DetachFromSuspendedGoesToDetached` (gated).

RTL5j (features.md:725) is one sentence with no conditions: "If the channel state is `SUSPENDED`,
the `detach` request transitions the channel immediately to the `DETACHED` state."

Measured: the channel stayed SUSPENDED. Nothing was sent, which is right, but the transition never
happened.

**Status:** open bug. A suspended channel is one the library keeps retrying (RTL13b), so a caller
who detaches it to stop that retrying does not get what they asked for.

### D42 — an ATTACHED arriving on a detaching or detached channel is ignored

**Spec point:** RTL5k.
**Tests:** `ChannelDetachTests.RTL5k_AttachedWhileDetachingSendsNewDetach`,
`RTL5k_AttachedWhileDetachedSendsDetach` (both gated).

RTL5k (features.md:731): "If the channel receives an `ATTACHED` message while in the `DETACHING` or
`DETACHED` state, it should send a new `DETACH` message and remain in (or transition to) the
`DETACHING` state."

Neither case is handled. Measured:

- **DETACHING**: with the server answering the DETACH with an ATTACHED instead of a DETACHED, the
  client sent no further DETACH and the channel sat in DETACHING until the request timed out.
- **DETACHED**: an unsolicited ATTACHED for an already-detached channel was ignored entirely, with
  no second DETACH in five seconds.

**Status:** open bug. RTL5k exists to settle precisely this disagreement — the server believes the
channel is attached and the client does not — and without it the client silently stops receiving
messages it is still subscribed to server-side, or hangs in DETACHING.

### D43 — a server-initiated DETACHED emits a spurious, out-of-order DETACHED state change

**Spec point:** RTL13, RTL13a, RTL13b.
**Test:** `ChannelServerInitiatedDetachTests.RTL13_NoIntermediateDetachedStateChange` (gated).

RTL13 (features.md:797-799) gives a server-initiated DETACHED exactly two destinations: ATTACHING
if the channel was ATTACHED or SUSPENDED (RTL13a), SUSPENDED if it was already ATTACHING (RTL13b).
DETACHED is not one of them — it is the state the spec reserves for a detach the client asked for.

Measured on the RTL13a path, a listener sees three changes where the spec describes two:

| # | Current | Previous |
|---|---------|----------|
| 1 | ATTACHING | DETACHED |
| 2 | DETACHED | ATTACHED |
| 3 | ATTACHED | ATTACHING |

Not merely one change too many: the DETACHED arrives *after* the ATTACHING that claims to have come
from it, so the emitted sequence is not a walk of the state machine in either order.

The mechanism is an ordering bug in `SetChannelState` (RealtimeChannel.cs:641-671). It builds the
`ChannelStateChange` at :660, then calls `HandleStateChange` at :661, and only emits at :663/:666.
`HandleStateChange` assigns `State` (:698) and then, for DETACHED-from-ATTACHED, calls `Reattach`
(:721) synchronously — so the nested `SetChannelState(Attaching)` runs to completion, emit
included, before the outer DETACHED emit is reached.

The RTL13b path has the same shape: ATTACHING to SUSPENDED goes via DETACHED, so the SUSPENDED
change reports its previous state as DETACHED rather than ATTACHING.

**Status:** open bug. The recovery itself is right - the ATTACH goes out, the retry loop runs, the
channel ends up ATTACHED - so this is about what listeners are told, not about whether the channel
recovers. It still matters: application code that treats DETACHED as "the server is done with this
channel" acts on a channel that is already reattaching.

### D44 — a message with no envelope id to inherit is given the id `":0"`

**Spec point:** TM2a. **Test:** `MessageFieldPopulationTests.TM2a_NoIdWhenProtocolMessageHasNoId`
(gated).

TM2a derives a message's id as `protocolMsgId:index` when the message has none. With no
`protocolMsgId` there is nothing to derive, and the spec leaves the id unset.

`MessageHandler.DecodeMessages` (MessageHandler.cs:487-490) does not check:

```csharp
if (message.Id.IsEmpty())
{
    message.Id = $"{protocolMessage.Id}:{i}";
}
```

A null `protocolMessage.Id` interpolates to the empty string, so subscribers are handed `":0"`.
Measured: exactly that.

**Status:** open bug, small but not cosmetic. RTL20's delta continuity check compares message ids,
and `":0"` is a value that compares equal across unrelated envelopes rather than an absence that
cannot.

### D45 — a message's own timestamp is always overwritten by the envelope's

**Spec point:** TM2f. **Test:** `MessageFieldPopulationTests.TM2f_ExistingTimestampIsNotOverwritten`
(gated).

TM2f, like TM2a and TM2c, fills the field in only when the message does not already carry one. The
SDK has that logic — `DecodeMessages` guards the assignment with
`if (message.Timestamp.HasValue == false)` (MessageHandler.cs:499-502) — and it is dead code on the
realtime path.

`MessageHandler.ParseRealtimeData` (MessageHandler.cs:420-431) runs first, immediately after
deserialising the frame, and assigns unconditionally:

```csharp
foreach (var presenceMessage in protocolMessage.Presence)
{
    presenceMessage.Timestamp = protocolMessage.Timestamp;
}

foreach (var message in protocolMessage.Messages)
{
    message.Timestamp = protocolMessage.Timestamp;
}
```

By the time the guarded assignment is reached, `HasValue` is always true and the value is always
the envelope's. Measured both halves: a message carrying its own timestamp was delivered with the
envelope's instead, and a message carrying its own inside an envelope with none was delivered with
`Timestamp` null — the field was not merely ignored, it was destroyed.

Note the first loop: presence messages are overwritten the same way, so this is not confined to
TM2f.

**Status:** open bug. Deleting the two loops in `ParseRealtimeData` would leave the correct,
already-written guarded assignment in `DecodeMessages` to do the job.

### D46 — publishing a message with every field null sends a MESSAGE with no messages

**Spec point:** RTL6i3.
**Test:** `ChannelPublishTests.RTL6i3_AMessageWithEveryFieldNullStillReachesTheWire` (gated).

RTL6i3 requires null message fields to be left off the wire rather than sent as nulls, and the
spec's test covers three cases: a null `data`, a null `name`, and both null. The first two pass -
`JsonHelper`'s `NullValueHandling.Ignore` does exactly what is asked.

The third does not. Measured, the frame was:

```json
{"action":15,"channel":"test-RTL6i3-all-null","msgSerial":2}
```

No `messages` array at all, where the spec asserts a message object with neither key. The cause is
`ProtocolMessage.OnSerializing` (ProtocolMessage.cs:206-222):

```csharp
if (Messages != null)
{
    Messages = Messages.Where(m => !m.IsEmpty).ToArray();
    if (Messages.Length == 0)
    {
        Messages = null;
    }
}
```

`Message.IsEmpty` (Message.cs:103) is equality against a default-constructed `Message`, so a
message with no name and no data is one. Two things follow from this being a serialisation hook
rather than a filter at publish time:

- The caller is not told. The publish is accepted, gets a `msgSerial`, and the awaited task
  completes successfully once the ACK arrives - for a frame that carried nothing.
- It mutates. `Messages` is assigned on the live object, so the in-memory `ProtocolMessage` loses
  its payload too, not just the copy on the wire. Anything that re-reads it afterwards - RTN19a's
  resend of unacked messages is the obvious one - sees the emptied version.

**Status:** open bug. Publishing an empty message is legitimate: the server assigns it an id and
delivers it, and a subscriber sees a message with no name and no data. Here it is silently
discarded.

### D47 — a pending attach or detach is told it failed, then succeeds

**Spec points:** RTL4d, and RTN19b's caller-facing half.
**Test:** `PendingPublishTests.RTL4d_APendingAttachReportsTheOutcomeOfTheResentAttach` (gated).

RTL4d (features.md:711) is specific about when the attach callback fires: "when the channel next
moves to one of `ATTACHED`, `DETACHED`, `SUSPENDED`, or `FAILED` states. In the case of `ATTACHED`
the callback is called with no argument."

RealtimeChannel.cs:187-190 fires it somewhere else entirely:

```csharp
case ConnectionState.Disconnected:
    AttachedAwaiter.Fail(new ErrorInfo("Connection is Disconnected"));
    DetachedAwaiter.Fail(new ErrorInfo("Connection is Disconnected"));
    break;
```

DISCONNECTED is not one of the four states, and the channel has not moved at all - it is still
ATTACHING. Measured: `AttachAsync` resolved with `IsSuccess == false` and
`"Connection is Disconnected"`, and the channel then reached ATTACHED. The caller was told the
attach failed by an attach that worked.

What makes it plainly a bug rather than a judgement call is that the correct behaviour is
implemented twenty lines above, in the same `switch`. On CONNECTED, RealtimeChannel.cs:174-184
resends the ATTACH or DETACH for exactly these pending states - RTN19b, and it works: the test
above confirms the resend reaches the new transport and the channel settles. So one half of the
class knows the operation is still in flight while the other has already given up on it.

Both directions are affected; the detach case is identical, with `DetachedAwaiter` and DETACHING.

**Status:** open bug. The fix is to leave the awaiters alone on DISCONNECTED and let RTN19b's
resend run to its conclusion - RTL4f's `realtimeRequestTimeout` is still there to bound it if the
new transport never answers either.

### D48 — a clientId change throws if another client in the process has used push

**Spec point:** none directly; found by running the unit tier in one process.
**Test:** `TokenDetailsAccessorTests.AuthorizeDoesNotThrowWhenAnotherClientHasInitialisedTheLocalDevice`
(gated).

`PubSubHttpClient.OnAuthClientIdChanged` (PubSubHttpClient.cs:165-177) runs whenever the resolved
clientId changes — which `AuthorizeAsync` does on every token:

```csharp
if (LocalDevice.IsLocalDeviceInitialized)
{
    Device.UpdateClientId(clientIdArgs.newClientId, MobileDevice);
}
```

The guard and the thing guarded are not the same object. `LocalDevice.IsLocalDeviceInitialized` is
`Instance != null` on a **static** (LocalDevice.cs:131-133), shared by every client in the process.
`Device` is an **instance** property that returns `null` when this client has no `MobileDevice`
(PubSubHttpClient.cs:102-121). So a client that never asked for push dereferences null, and the
`NullReferenceException` comes out of `AuthorizeAsync`.

Measured: a REST client with a `FakeMobileDevice` authorizes, which sets the static; a second,
unrelated REST client then authorizes and throws
`System.NullReferenceException` from `OnAuthClientIdChanged`, through
`AblyAuth.NotifyClientIdIfChanged` (AblyAuth.cs:475) and `AblyAuth.set_CurrentToken` (:83).

**Status:** open bug, and a nasty one to meet in the wild: the crash is in a client that has
nothing to do with push, and it only appears once *some other* client in the same process has
touched the device. The fix is to test the instance, not the static — `if (Device != null)`.

**Harness note.** Because the static persists for the lifetime of the process, this made the whole
unit tier order-dependent: `RSA16c_UpdatedAfterAuthorize` passed alone and failed after any push
test, in roughly one Release run in two.

The fix is that every client `UtsClients` builds is given its own `LocalDevice`
(`UtsClients.WithOwnDevice`), so no UTS client ever reads or writes the static. Clearing the static
in teardown was tried first and is the wrong half of the fix: it can null an instance another test
is still using. The gated test above is now the only UTS code that touches `LocalDevice.Instance`,
and it saves and restores it.

What that does **not** fix is the same hazard between the repo's own tests, and that one is
pre-existing and reproducible with none of this work present. Measured on the untouched base commit
`54eff1bd`, running only `Ably.PubSub.Tests.Push.*` (142 tests), three times:

| Run | Result |
|---|---|
| 1 | 1 failed — `LocalDeviceTests.WithoutClientId_WhenAuthorizedWithTokenParamsWithClientId_…` |
| 2 | 2 failed — `ActivationStateMachineTests.UpdateRegistrationTokenWithNewToken_…` and `LocalDeviceTests.WhenClientIdChangesAfterRegisteringDevice_…` |
| 3 | 1 failed — the same as run 1 |

A different set each time. It is **not** a parallelism problem: repeated with
`xUnit.ParallelizeTestCollections=false`, it still fails, so it is order dependence inside the push
suite itself — one test leaves `LocalDevice.Instance` set and the next assumes it is null, and
xUnit's ordering varies between runs.

The consequence for anyone reading a test result here: the **UTS** unit tests are stable (869
passed, 77 skipped, 0 failed, three consecutive runs), while the **full** unit leg, which also
contains the repo's own 1,250 tests, surfaces one or two of these push failures at random. Fixing
that means auditing the repo's push test setup and teardown, or taking the one-word SDK fix above,
which would make the whole hazard moot. Both are outside this work.

---

## Adapted Tests

SDK non-compliance where the test asserts the SDK's *actual* behaviour, with the spec expectation in
a comment. These pass, guard against regressions, and are preferred wherever the behaviour is stable —
a running adapted test is worth more than a spec-correct assertion skipped indefinitely.

### A1 — `expires` and `issued` are value types, so "null" is their default

**Spec point:** RSA16b. **Tests:** `TokenDetailsAccessorTests.RSA16b_TokenStringInOptions` and the
other `TokenDetails` assertions in that class.

RSA16b says that when only a token string is available, `tokenDetails` holds a `TokenDetails` "in
which only the `token` attribute is populated", and the spec asserts the rest `IS null`.
`TokenDetails.Expires` and `.Issued` are non-nullable `DateTimeOffset`s here (`TokenDetails.cs:28`
and `:34`), so they cannot be null; unset means `default(DateTimeOffset)`. Those two fields are
asserted against the default, and `ClientId`/`Capability` — which are reference types — against null
as written.

Not recorded as a deviation: the requirement is that nothing beyond the token is invented, and the
assertions still catch that. The same shape would be worth a second look if the SDK ever needed to
distinguish "no expiry" from "expires at `DateTimeOffset.MinValue`", but nothing in the spec asks it
to.

### A2 — an `authUrl` must include `issued` for its body to be read as a `TokenDetails`

**Spec points:** RSA8c, and the fixture of RSA4b's authUrl case.
**Test:** `TokenRenewalTests.RSA4b_RenewalViaAuthUrl` (passing, with an adapted fixture).

RSA8c requires a JSON `authUrl` response to be "taken to be a `TokenRequest` or `TokenDetails`
object", and leaves the discrimination to the implementation. This SDK discriminates on one field:
`TokenDetails.IsToken` (`TokenDetails.cs:72`) is `json["issued"] != null`. The spec's fixture answers
with `{"token": ..., "expires": ...}` — a perfectly valid `TokenDetails` that omits `issued` — so the
SDK takes it for a `TokenRequest`, finds no `keyName`, and POSTs to `/keys//requestToken`. Measured:
the request went out with an empty key name and the response then failed to deserialise.

The fixture adds an `issued` field so the test can exercise RSA4b's subject, which is renewal via
`authUrl` rather than response parsing. The discriminator is worth fixing independently and is cheap
to fix: a body containing `token` cannot be a `TokenRequest`, since a `TokenRequest` has `keyName`,
`nonce`, `mac` and `timestamp` and never a token. Recorded here rather than under Failing Tests
because the spec grants the latitude — but the chosen field is the wrong one, and the failure mode it
produces (a POST to `/keys//requestToken`, then a deserialisation error) tells the caller nothing
about what went wrong.

### A3 — "absent" is an empty string, not null

**Spec points:** RTN8d, RTN9d, and RTN4c's `errorReason`.
**Tests:** `Realtime.Integration.ConnectionLifecycleTests.RTN4c_GracefulClose`;
`Realtime.Unit.Connection.ConnectionOpenFailuresTests.RTN14a_InvalidKeyFailed` (parked with the
realtime unit tier).

The spec writes `connection.id IS null`, `connection.key IS null` and `errorReason IS null`. This SDK
spells all three absences as values rather than as null:

- `RealtimeState.ConnectionData.ClearKeyAndId()` sets both `Id` and `Key` to `string.Empty`.
- `ConnectionClosedState` sets `Error = error ?? ErrorInfo.ReasonClosed`, and `ReasonClosed` carries
  `ErrorCodes.NoError` — a constant literally named for the absence of an error.

The behaviour each spec point protects holds: after a close there is no id and no key, so nothing to
resume with, and a graceful close reports no *real* error. Only the representation differs, and it is
deliberate, so the assertions read "no id / no key / no real error" rather than "null". They still
catch a regression that put a genuine error on a clean close, which is what RTN4c is for.

Not recorded as a deviation: `writing-derived-tests.md` is explicit that a differently-spelled
observable is idiomatic translation, and the governing preference is an assertion that runs.

### A4 — `ChannelOptions` has no unset state; nothing on it is ever null

**Spec points:** TB2, TB2b, TB2c, TB2d. **Test:** `ChannelOptionsTests.TB2_ChannelOptionsAttributeDefaults`.

TB2 asserts a freshly constructed `ChannelOptions` has `cipherParams`, `params` and `modes` all
null. None of the three can be null here:

- `Params` and `Modes` are backed by fields initialised to empty collections, and both setters
  coalesce an assigned null back to a fresh empty one (ChannelOptions.cs:30-43).
- `CipherParams` is assigned `@params ?? Crypto.GetDefaultParams()` in the only constructor that
  takes one (ChannelOptions.cs:74), so an unencrypted channel still has a populated cipher. The
  "is a cipher configured" answer lives on `Encrypted` instead.

The test asserts the same thing in this SDK's terms: nothing is configured. An empty collection and
a null one are the same answer to "which params were set", so the assertion still catches an
option leaking in from somewhere.

Not recorded as a deviation: `writing-derived-tests.md` treats a differently-spelled observable as
translation rather than non-compliance, and empty-not-null is the ordinary .NET spelling. The
`CipherParams` half is the weaker of the two - a caller reading `options.CipherParams` on an
unencrypted channel gets a real object - but `Encrypted` is unambiguous and is what the SDK's own
encryption path reads.

### A5 — the vcdiff deltas are real, because there is nowhere to put a mock decoder

**Spec points:** RTL18, RTL19a, RTL19b, RTL19c, RTL20, RTL21.
**Tests:** all ten in `ChannelDeltaDecodingTests`.

`channel_delta_decoding.md` builds its deltas with a `MockVCDiffEncoder` and installs a matching
`MockVCDiffDecoder` through `ClientOptions.plugins`, so the two agree by construction and the tests
never need a real delta. This SDK has no plugin seam: `VcDiffEncoder` calls
`IO.Ably.DeltaCodec.DeltaDecoder.ApplyDelta` directly, and the codec is compiled in.

So the deltas have to be ones the real decoder accepts, and that library decodes only — it has no
encoder. `Uts/Helpers/VcdiffDeltas.cs` supplies one: a small RFC 3284 encoder, single window,
default code table, emitting a COPY for any run of four or more bytes it finds in the source and an
ADD for the rest. It is deliberately naive, and being a real encoder is the point — a delta that
ignored its source would decode the same against a wrong base as a right one, and most of these
tests are about which base the SDK kept.

Two consequences worth naming:

- **The encodings differ from the spec's.** The specs write `encoding: "vcdiff"` with the raw delta
  as data. On a JSON transport that cannot travel, and the spec's own transport note says so: these
  tests use `utf-8/vcdiff/base64` where a string is expected and `vcdiff/base64` where bytes are.
- **RTL19a's payloads differ.** The spec patches `"Hello"` to `"World"`, which share no run long
  enough to copy, so the delta would not reference the source at all and the test could not detect
  a wrong base. `"Hello world"` to `"Hello there, world"` keeps the binary-via-base64 shape the
  test is about and makes the base payload load-bearing.

The RTL18 failures are provoked the same way round: instead of a decoder rigged to throw, the
message carries bytes that are not a VCDIFF stream, so the real decoder throws. That is the
production failure path rather than a simulation of it.

Not recorded as a deviation: nothing about the SDK's behaviour is being accommodated here. All ten
tests pass.

---

## Mock Infrastructure Limitations

Skipped stubs, not SDK deviations.

### M1 — msgpack is compiled out of this build

`ClientOptions.UseBinaryProtocol`'s getter returns a hard-coded `false` and its setter discards its
argument outside `#if MSGPACK`; `MSGPACK` is defined in **no** csproj, props, targets or Cake file in
the repository; `Ably.PubSub.Core.csproj` imports the MsgPack shared project only under that define;
and `Defaults.MsgPackEnabled` is `false`.

So every request body, response body and WebSocket frame is JSON; setting `UseBinaryProtocol = true`
is a silent no-op; a spec's `## Protocol Variants` section has exactly one runnable variant; and
there is no `msgpack_encode` / `msgpack_decode` for a test to call, because `MsgPackHelper` lives
entirely in the never-compiled `Ably.PubSub.Shared.MsgPack` project.

This is a **build configuration**, not an SDK defect. 18 tests across `rest_client.md`,
`channel/publish.md`, `message_encoding.md` and `request.md`, plus the whole of
`encoding/msgpack_interop.md`, are affected. The full list is in [`coverage.md`](coverage.md).

Two details for whoever re-enables it: `Defaults.cs` defines `DefaultProtocol` in the `MSGPACK`
branch but `Protocol` in the live branch, so turning the define on would not compile as-is; and
`common/test-resources/msgpack_test_fixtures.json` exists on disk but is not declared as an
`EmbeddedResource` in `Ably.PubSub.Tests.DotNET.csproj`.

### M2 — WebSocket ping frames are not observable

**Spec point:** RTN23b. Affects the realtime unit tier (parked).

.NET's `ClientWebSocket` answers ping frames inside the protocol and surfaces no event to
`ITransport`, which is the same reason the spec gives for Dart. `MockConnection.SendPingFrame()`
records the attempt so a test can see it was made, but there is nothing for it to drive. RTN23a via a
HEARTBEAT protocol message works and is translated.

---

## Investigated and not defects

Recorded so the next reader does not reach the same first conclusion.

### N1 — basic auth over plain HTTP *is* rejected (RSC18)

Initially filed as a deviation; **withdrawn**. `AblyAuth.EnsureSecureConnection`
(`AblyAuth.cs:535-541`) throws `AblyInsecureRequestException` from `AddAuthHeader`, which
`PubSubHttpClient.ExecuteRequest` calls for every request that is not `SkipAuthentication` — so the
key never crosses a plain connection, which is the harm RSC18 exists to prevent.

The check is **lazy**, not eager: the client constructs fine and the first *authenticated* request
fails. Neither RSC18 (features.md:109) nor RSA1 (features.md:179) mandates a constructor check, and
RSA1's "any attempt to use Basic Auth over HTTP" positively endorses the lazy form. The original
translation only constructed a client and made no request, so it could not observe the rejection
that does happen — a textbook 2b. `client.TimeAsync()` would not have worked either: `/time` sets
`SkipAuthentication`.

Residual nit, not worth filing: the error is `ErrorInfo(message, 500)` with the message "Current
action cannot be performed over http" — code 500 for a client configuration error, and naming
neither "insecure" nor "TLS". The features spec mandates no code for this clause, so this is
cosmetic. The UTS spec's `40103` appears nowhere in `features.md`.

### N2 — `DeviceRegistrations.GetAsync`'s not-found branch is unreachable

Found while deriving `rest/integration/push_admin.md`. `IDeviceRegistrations.GetAsync` returns
`Task<Result<DeviceDetails>>` and `PushAdmin.cs:345` has an explicit
`if (response.StatusCode == NotFound) return Result.Fail(...)` branch — but that branch can never
run: the 404 comes back from `_restClient.ExecuteRequest`, which throws on an error status
(`NoExceptionOnHttpError` is set only on the paginated request path), so an `AblyException` escapes
first.

**Not a UTS deviation:** the spec requires only "get FAILS WITH error" with status 404, which the
throw satisfies. It is an internal inconsistency in the SDK — a declared contract the code does not
deliver — worth a tidy-up ticket but not a compliance failure. The derived tests assert the thrown
exception.

### N3 — publish reports failure two different ways, and the state check misnames the state

**Spec points:** RTL6c2, RTL6c4. **Tests:** the five refusal tests in `ChannelPublishTests`.

Not a spec deviation - the spec says a publish that cannot proceed must fail, and it does - but
two things found while translating RTL6c are worth writing down.

**The failure arrives two ways.** `PublishAsync` returns `Task<Result>`, and a NACK from the server
resolves it as a failed `Result` (RTL6j's test reads `result.Error.Code == 40160` that way). A
state check that refuses to publish at all does not: `PublishImpl` (RealtimeChannel.cs:613-621)
throws, `TaskWrapper.Wrap` catches it and calls `SetException`, and the caller gets an
`AblyException` out of the `await`. So a caller who only checks `IsSuccess` misses every RTL6c4
case, and one who only catches misses every NACK. Worth noting too that `TaskWrapper.SetException`
re-wraps, so the thrown error is a 50000 "Unexpected error" with the real 40000 as its inner - the
code a caller reads first is the one that says nothing.

**The message names the wrong state.** RealtimeChannel.cs:620:

```csharp
throw new AblyException(new ErrorInfo(
    $"Message cannot be published. Client is not allowed to queue messages when connection is in {State} state", ...));
```

`State` is the *channel's* state; the sentence is about the *connection's*. The guard above it is
`Connection.CanPublishMessages`, which is correct. Measured on a CLOSED connection with a channel
that had never attached: "connection is in Initialized state". A one-word fix
(`Connection.State`), but until then the diagnostic actively misleads.

### N4 — the integration tier's three groups contend in one process

Not an SDK defect and not a translation bug, but it shapes how the tier must be run. There are
three groups — the repo's own sandbox specs, the UTS REST integration tier, and the UTS realtime
integration tier — and **no two of them can share a process**. Each is green alone:

| Group | Alone |
|---|---|
| the repo's own sandbox specs (`tier!=realtime&tier!=uts-rest`) | 227 passed, 12 skipped, 6 failed, in 4 minutes |
| `tier=uts-rest` | 58 passed, 1 skipped, 0 failed |
| `tier=realtime` | 33 passed, 0 failed |

Combined, the failures are always the same shape: "timed out waiting for Connected". Measured, UTS
REST and UTS realtime together: every realtime test fails. Measured, the repo's own specs sharing
a pass with UTS REST: **52 failures**, 10 of them in `ChannelSandboxSpecs` — the class that passes
49/50 by itself. It is not ephemeral-port exhaustion (32 sockets in `TIME_WAIT` against a
16384-port range), so it is contention on the shared sandbox app all three reach.

`cake-build/tasks/test.cake` therefore runs the integration target as **three** passes in separate
processes, split on the `tier` trait: `tier!=realtime&tier!=uts-rest`, then `tier=uts-rest`, then
`tier=realtime`. Collapsing any two back together reintroduces the failures — and because CI runs
this leg through `.WithRetry`, which exits 0 regardless, it would do so silently.

The six that remain in the first pass are the repo's own residual sandbox flakiness, not
contention: a different handful on each run, all of them token-renewal or presence-timing specs,
none of them UTS. They are what the retry wrapper exists for. Splitting took that pass from 52
failures in over forty minutes to 6 in four.
