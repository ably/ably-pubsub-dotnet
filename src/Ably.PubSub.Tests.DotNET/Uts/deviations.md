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
