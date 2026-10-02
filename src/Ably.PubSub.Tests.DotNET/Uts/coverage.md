# UTS coverage — what is not covered, and why

The Universal Test Suite defines **1125 test specs across 130 files** (127 test specs plus three
mock-helper specs), verified against `ably/specification@d9a04cac`. This document records what the
.NET SDK does **not** cover and the reason for each omission, grouped by tier.

It is not a list of failures. Everything here is a spec that **cannot be written** against the SDK
as it stands, almost always because the client API it needs does not exist — and in C# a test
calling a method that does not exist is a **compile error**, not a failing assertion, so it cannot
be shipped as a skipped test the way a dynamic language would. Where the SDK does the wrong thing
rather than lacking the API, the entry is in [`deviations.md`](deviations.md) instead.

Every row below was verified against the source during this work. The census in
[`ably/ably-pubsub-dotnet#1345`](https://github.com/ably/ably-pubsub-dotnet/issues/1345)
(Jira ECO-5754) is the background; where this document disagrees with it, this one was measured
later and against this specific SDK.

**How this document grows.** Like [`deviations.md`](deviations.md), it is built tier by tier,
alongside the tests: each tier's derived tests arrive together with the record of what that tier
could not cover. The **Summary** is the exception — a total is only true once every tier is in, so
it arrives with the last of them rather than being revised four times.

---

## Unit tier

### Fully blocked

| Spec file | Tests | Absent API |
|---|---|---|
| `rest/unit/channel/get_message.md` | 4 | `HttpChannel#getMessage`; `Message.serial`, `Message.version` |
| `rest/unit/channel/message_versions.md` | 3 | `getMessageVersions`; `Message.action` |
| `rest/unit/channel/update_delete_message.md` | 12 | as the realtime equivalent |
| `rest/unit/channel/annotations.md` | 10 | the whole annotations surface |
| `rest/unit/channel/publish_result.md` | 3 | `PublishAsync` returns a bare `Task`; no `PublishResult` |
| `rest/unit/types/mutable_message_types.md` | 8 | `MessageVersion`, `MessageAnnotations`, `MessageOperation`, `UpdateDeleteResult`, `Annotation` |
| `rest/unit/batch_publish.md` | 28 | `batchPublish`; `BatchPublishSpec`; `BatchResult` |
| `rest/unit/batch_presence.md` | 13 | `batchPresence`; `BatchResult` |
| `rest/unit/auth/revoke_tokens.md` | 17 | `Auth#revokeTokens`; `TokenRevocationTargetSpecifier`; the revocation result types |
| `rest/unit/encoding/msgpack_interop.md` | 2 sections | msgpack compiled out — see below |

**Note on `rest/unit/channel/annotations.md`:** it contains **10** tests, not the 6 its
`**Test ID**` markers suggest. Four fully specified tests (at lines 280, 331, 382 and 459) carry no
marker. Reported upstream.

### Partially blocked — the file is translated, these tests are skipped

| Spec file | Tests | Skipped | Which, and why |
|---|---|---|---|
| `rest/unit/rest_client.md` | 15 | 3 | `RSC8/error-decoded-from-msgpack-0`, `RSC8d/mismatched-response-content-type-0`, `RSC8a/protocol-selection-0` (msgpack half) |
| `rest/unit/channel/publish.md` | 8 | 2 | `RSL1i/message-size-limit-0` — there is no `ClientOptions.MaxMessageSize`, only the server-sent read-only `ConnectionDetails.MaxMessageSize`; `RSL1l/params-as-querystring-0` — no publish overload takes params |
| `rest/unit/encoding/message_encoding.md` | 21 | 4 | `RSL4c/binary-direct-msgpack-protocol-1`, `RSL6/msgpack-binary-stays-binary-0`, `RSL6/msgpack-string-stays-string-1`, `RSL4/msgpack-protocol-content-type-3` |
| `rest/unit/auth/token_renewal.md` | 9 | 1 | `RSA4b/renewal-msgpack-response-4` — the 401 token-error body is msgpack, so it can neither be produced nor decoded in this build |
| `rest/unit/presence/rest_presence.md` | 42 | 1 | `RSP5/decode-msgpack-binary-3` — needs a msgpack response body carrying a binary presence payload |

---

## The REC endpoint model is not implemented

This one was not on the inherited list and is the largest single finding of the translation work.

The `REC` clauses of the features spec describe client addressing through a single
**`ClientOptions.endpoint`** option — a hostname, an IP literal, or a routing policy such as
`nonprod:sandbox` — from which the SDK derives the primary REST and realtime domains and the
fallback domains. This SDK is still on the older `restHost` / `realtimeHost` / `environment` model:
`ClientOptions.Endpoint` does not exist, and nor do `FallbackHostsUseDefault` (REC2a1, REC2b) or
`ConnectivityCheckUrl` (REC3b). Verified: zero occurrences of any of the three in product code.

16 tests across `rest/unit/fallback.md` and `rest/unit/request_endpoint.md` cannot be expressed:

| Spec point | Tests | What is absent |
|---|---|---|
| REC1b1, REC1b2, REC1b3, REC1b4 | 8 | `ClientOptions.Endpoint`, and the `nonprod:`/production routing policies derived from it |
| REC2a1, REC2b | 2 | `ClientOptions.FallbackHostsUseDefault` |
| REC2c2, REC2c3, REC2c4 | 3 | `Endpoint`, plus the non-production fallback-domain derivation |
| REC3b | 1 | `ClientOptions.ConnectivityCheckUrl` — the URL is the compile-time constant `Defaults.InternetCheckUrl` |
| REC1c1, REC1d2 | 2 | see *deviations.md* — these two **do** compile; the SDK's behaviour diverges |

The rest of `fallback.md` — 28 of its 43 tests, covering the RSC15 retry and fallback-host
behaviour the SDK does implement — is translated and passing.

---

## Other absent API

Gaps that do not belong to a larger theme:

| Spec point | Test | What is absent |
|---|---|---|
| TG4 | `rest/unit/TG4/first-returns-first-page-0` | `PaginatedResult<T>` has no `First()` / `FirstAsync()`. It carries `FirstQueryParams` but exposes no method to fetch that page; `First()` exists only on the `HttpPaginatedResponse` subclass |
| TO3c2 | `rest/unit/TO3c2/context-contains-expected-keys-0` | there is no structured log context. The sink contract is `ILoggerSink.LogEvent(LogLevel, string)` — a level and a flat message, with no context map to assert `method` / `host` / `path` against |
| TP5 | `rest/unit/TP5/presence-message-size-0` | no message-size API. There is no `PresenceMessage.Size`, no `Message.Size` and no TM6-style size calculation; the only `MaxMessageSize` is the server-sent read-only `ConnectionDetails.MaxMessageSize`, which no REST path consults |

One further gap is a parameter rather than a member: `PubSubHttpClient.Request` / `RequestV2` take
no `version` argument (RSC19f1's entire subject), because `X-Ably-Version` is set once per client
from the `Defaults.ProtocolVersion` constant.

---

## Out of scope entirely

`uts/objects/**` — LiveObjects. 19 spec files carrying test ids (15 unit, 3 integration, 1 proxy),
plus a helper spec and a PLAN, for **339 tests** — none counted in the figures above.

Verified rather than assumed: searching product code for `LiveObject`, `LiveMap`, `LiveCounter` or
an `objects` member on the channel returns nothing. There is no objects plugin, no
`RealtimeChannel.Objects`, no OBJECT or OBJECT_SYNC protocol action (`ProtocolMessage.MessageAction`
stops at `Auth = 17`), and no object-id or value types to assert against. Every one of the 339
would be a compile error.

This is the single largest block of untranslated UTS, and it is a product gap rather than a test
gap: it moves when LiveObjects is implemented, not before.

---

## msgpack is compiled out of this build

This is a **build configuration**, not an SDK defect, so it is recorded here and under *Mock
Infrastructure Limitations* in `deviations.md` rather than as a deviation.

`ClientOptions.UseBinaryProtocol`'s getter returns a hard-coded `false` and its setter discards its
argument outside `#if MSGPACK`; `MSGPACK` is defined in **no** csproj, props, targets or Cake file
in the repository; `Ably.PubSub.Core.csproj` imports the MsgPack shared project only under that
define; and `Defaults.MsgPackEnabled` is `false`. So:

- Every request body, response body and WebSocket frame is JSON.
- Setting `UseBinaryProtocol = true` is a silent no-op rather than an error.
- A spec's `## Protocol Variants` section has exactly one runnable variant here.
- `rest/unit/encoding/msgpack_interop.md` has nothing to exercise.

Two details for whoever re-enables it: `Defaults.cs` defines `DefaultProtocol` in the `MSGPACK`
branch but `Protocol` in the live branch, so turning the define on would not compile as-is; and
`common/test-resources/msgpack_test_fixtures.json` exists on disk but is not declared as an
`EmbeddedResource` in `Ably.PubSub.Tests.DotNET.csproj`.

---

## Platform coverage

UTS tests run on **`net6.0` and `net7.0`**. They do not run on `net462`: the .NET Framework test
head is a separate, non-globbing project that does not compile this tree. They also do not run on
the `net8.0` and `net9.0` CI legs, which discover zero tests because the test project does not
target those frameworks — a pre-existing defect, noted but not fixed here.

---

## Spec defects reported upstream

Found while deriving; each is a bug in the spec rather than in this SDK.

1. `rest/unit/channel/annotations.md` — four fully specified tests carry no `**Test ID**` marker
   (lines 280, 331, 382, 459), so a count taken from the markers is wrong by four.
2. `rest/unit/encoding/message_encoding.md` — the final section, "RSL4 - Empty object encoding"
   (line 966), has no `**Test ID**`.
3. `rest/unit/rest_client.md` — "Additional Test - Token auth over HTTP allowed" (line 571) has no
   `**Test ID**`.
4. `rest/unit/encoding/message_encoding.md` — `RSL4/encoding-fixtures-ably-common-0` branches on
   `fixture.use_binary_protocol`, but the real `common/test-resources/messages-encoding.json` has
   no such field and uses different field names entirely, so that branch can never be taken.
