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

## Summary

Grouped by root cause, because that is how the gap gets closed — each row is one piece of work,
not a list of unrelated tests.

| Cause | Tests |
|---|---|
| Mutable messages, annotations and publish results — client API absent | 84 |
| Batch publish — client API absent | 28 |
| Token revocation — client API absent | 21 |
| Batch presence — client API absent | 16 |
| **The REC endpoint model — client API absent** | **16** |
| msgpack compiled out of this build | 20 (+2 untagged spec sections) |
| Message size limit and publish `params` — client API absent | 4 |
| `whenState` on Connection and RealtimeChannel — client API absent | 10 |
| Derived channels — client API absent | 5 |
| `MessageFilter` subscriptions — client API absent | 5 |
| Assorted smaller absences (see *Other absent API* below) | 13 |
| **Total not covered** | **222** |
| **In scope** | **903 of 1125** |

Reading the first row: it is one feature area, not six. Message mutation, message versions,
annotations and publish results all hang off the same absent surface — `Message.serial`,
`Message.version`, `Message.action`, `Message.annotations`, and the methods that return them — so a
single implementation effort moves all 84.

The declared wire protocol version stays at `"2"` (`Defaults.ProtocolVersion`). That is deliberate
and is **not** the cause of most of the gap: by mechanism, the version constant alone gates only a
handful of integration-tier assertions, while the great majority of what is missing is absent
client API surface that no version bump would supply. Bumping the version is a separate, larger
piece of work with its own risk — the server starts sending protocol shapes the SDK does not model.

### Two gaps that are not about this SDK at all

- **The `mutable:` channel namespace is not provisioned.** `rest/integration/mutable_messages.md`
  requires an app created with `mutableMessages: true`, and the vendored
  `common/test-resources/test-app-setup.json` declares only `persisted` and `pushenabled`. Even
  once the client API lands, this file needs an `ably-common` change too.
- **Two integration files assume a protocol header this SDK does not send.**
  `rest/integration/revoke_tokens.md` and `rest/integration/batch_presence.md` both state that
  their `BatchResult` envelope (`successCount` / `failureCount` / `results`) is returned only for
  `X-Ably-Version >= 3`. This SDK sends `2`, so the assertions as written presume a header it does
  not send — a second reason those files cannot simply be switched on once the methods exist.

---

## Unit tier

### Fully blocked

| Spec file | Tests | Absent API |
|---|---|---|
| `realtime/unit/channels/channel_get_message.md` | 1 | `RealtimeChannel#getMessage`; `Message.serial`, `Message.version` |
| `realtime/unit/channels/channel_message_versions.md` | 1 | `getMessageVersions`; `Message.action`, `Message.version` |
| `realtime/unit/channels/channel_update_delete_message.md` | 9 | `updateMessage` / `deleteMessage` / `appendMessage`; `MessageOperation`; `UpdateDeleteResult`; `ProtocolMessage.res` |
| `realtime/unit/channels/channel_annotations.md` | 14 | the whole annotations surface; ANNOTATION protocol action; annotation channel modes; `attachOnSubscribe` |
| `rest/unit/channel/get_message.md` | 4 | `HttpChannel#getMessage`; `Message.serial`, `Message.version` |
| `rest/unit/channel/message_versions.md` | 3 | `getMessageVersions`; `Message.action` |
| `rest/unit/channel/update_delete_message.md` | 12 | as the realtime equivalent |
| `rest/unit/channel/annotations.md` | 10 | the whole annotations surface |
| `rest/unit/channel/publish_result.md` | 3 | `PublishAsync` returns a bare `Task`; no `PublishResult` |
| `rest/unit/types/mutable_message_types.md` | 8 | `MessageVersion`, `MessageAnnotations`, `MessageOperation`, `UpdateDeleteResult`, `Annotation` |
| `rest/unit/batch_publish.md` | 28 | `batchPublish`; `BatchPublishSpec`; `BatchResult` |
| `rest/unit/batch_presence.md` | 13 | `batchPresence`; `BatchResult` |
| `rest/unit/auth/revoke_tokens.md` | 17 | `Auth#revokeTokens`; `TokenRevocationTargetSpecifier`; the revocation result types |
| `realtime/unit/connection/when_state_test.md` | 6 | `Connection#whenState`. `Connection` derives from `EventEmitter<ConnectionEvent, ConnectionStateChange>`, whose whole listener surface is `On` / `Once` / `Off`. `Once` is not a stand-in: it supplies only RTN26b's deferred half and never fires for the state the emitter is already in, which is exactly what RTN26a asserts |
| `realtime/unit/channels/channel_when_state_test.md` | 4 | `RealtimeChannel#whenState`. The nearest internal thing, `ChannelAwaiter`, has a `(bool success, ErrorInfo error)` callback and so cannot distinguish RTL25a's null result from RTL25b's `ChannelStateChange` — the exact observable both tests turn on — and it completes with a timeout failure rather than simply not resolving, which is what `RTL25a/past-state-does-not-resolve-1` asserts |
| `rest/unit/encoding/msgpack_interop.md` | 2 sections | msgpack compiled out — see below |
| `realtime/unit/connection/when_state_test.md` is listed above; `heartbeat_test.md`'s RTN23b/c/c1 tail is partial — see the note below this table | 10 | `ProtocolMessage.MessageAction` and observable ping frames |

**Note on `rest/unit/channel/annotations.md`:** it contains **10** tests, not the 6 its
`**Test ID**` markers suggest. Four fully specified tests (at lines 280, 331, 382 and 459) carry no
marker. Reported upstream.

### `heartbeat_test.md`'s RTN23b/RTN23c/RTN23c1 tail — three separate reasons

All seven RTN23a tests are translated and pass. The remaining ten are not covered, and it is worth
separating why, because only one of the three reasons is a gap in this SDK:

- **RTN23b, six tests** — blocked by the mock, see M1/M2 in [`deviations.md`](deviations.md).
  `send_ping_frame()` has nothing to drive: .NET's `ClientWebSocket` answers ping frames inside the
  protocol and raises no event an `ITransport` could see.
- **RTN23b/heartbeats-false-query-param-0, one test** — **not applicable**, not blocked. RTN23b
  (features.md:654) says a client that *can* observe websocket pings should send
  `heartbeats=false`, and one that cannot should send `heartbeats=true`. This SDK cannot, so
  `heartbeats=true` is the correct behaviour, and that is what the passing
  `HeartbeatTests.RTN23a_HeartbeatsTrueQueryParam` asserts. Translating this one would require the
  SDK to be wrong to pass.
- **RTN23c, one test** — **not applicable** by the spec's own words: `heartbeats=bounce` is for an
  environment "where client code execution might be suspended while leaving the transport itself
  alive (for example, a browser...)", and the spec file says "Only applies to browser (or
  equivalent) builds of an SDK".
- **RTN23c1, two tests** — absent protocol surface. The client must answer a `PING` protocol
  message with a `PONG`, and `ProtocolMessage.MessageAction`
  (`src/Ably.PubSub.Shared/Types/ProtocolMessage.cs:27-45`) stops at `Auth = 17`. TR2
  (features.md:1594) lists the actions in order from zero and continues `ACTIVATE`, `OBJECT`,
  `OBJECT_SYNC`, `ANNOTATION`, `PING`, `PONG` — so `PING` is 22 and `PONG` is 23, and neither
  exists here. The practical exposure is nil, since the server only sends `PING` to a client that
  asked for `heartbeats=bounce`, and an unrecognised action is ignored rather than fatal (covered by
  the passing `ForwardsCompatibilityTests`). Worth noting all the same: the enum is six values short
  of TR2.

### Partially blocked — the file is translated, these tests are skipped

| Spec file | Tests | Skipped | Which, and why |
|---|---|---|---|
| `realtime/unit/channels/channel_publish.md` | 35 | 3 | Re-measured during translation; the inherited figure of six was too pessimistic. Three are genuinely blocked: `RTL6j/publish-result-serials-0` and `RTL6j/batch-publish-serials-1` are wholly about the `PublishResult` that `PublishAsync` does not return (it returns `Task<Result>`), leaving nothing but a msgSerial check another test already makes; `RTL6i3/null-fields-msgpack-1` needs a msgpack frame. The other three the census listed — `RTL6j/incrementing-msg-serial-2`, `RTN7d/survive-disconnected-queue-1` and `RTN19a/resent-on-new-transport-0` — each have a `PublishResult` assertion *and* a substantive one (incrementing msgSerials; a publish surviving DISCONNECTED; a message resent on the new transport), so they are translated with the `PublishResult` line dropped and noted in the test |
| `rest/unit/rest_client.md` | 15 | 3 | `RSC8/error-decoded-from-msgpack-0`, `RSC8d/mismatched-response-content-type-0`, `RSC8a/protocol-selection-0` (msgpack half) |
| `rest/unit/channel/publish.md` | 8 | 2 | `RSL1i/message-size-limit-0` — there is no `ClientOptions.MaxMessageSize`, only the server-sent read-only `ConnectionDetails.MaxMessageSize`; `RSL1l/params-as-querystring-0` — no publish overload takes params |
| `rest/unit/encoding/message_encoding.md` | 21 | 4 | `RSL4c/binary-direct-msgpack-protocol-1`, `RSL6/msgpack-binary-stays-binary-0`, `RSL6/msgpack-string-stays-string-1`, `RSL4/msgpack-protocol-content-type-3` |
| `rest/unit/auth/token_renewal.md` | 9 | 1 | `RSA4b/renewal-msgpack-response-4` — the 401 token-error body is msgpack, so it can neither be produced nor decoded in this build |
| `rest/unit/presence/rest_presence.md` | 42 | 1 | `RSP5/decode-msgpack-binary-3` — needs a msgpack response body carrying a binary presence payload |
| `realtime/unit/channels/channel_state_events.md` | 13 | 2 | `RTL2i/has-backlog-flag-true-0` and `RTL2i/has-backlog-flag-false-1` — `ChannelStateChange` has no `hasBacklog` member; its surface is Previous, Current, Error, Resumed, Event. The HAS_BACKLOG flag exists on `ProtocolMessage.Flag` (`:58`) and is simply never surfaced on the state change |
| `realtime/unit/channels/channel_history.md` | 3 | 2 | `RTL10b/adds-from-serial-0` and `RTL10b/errors-when-not-attached-1` — there is no `untilAttach` overload on `IRealtimeChannel.HistoryAsync`, which takes only a `PaginatedRequestParams`. The SDK *has* the machinery — `RealtimeChannel.AddUntilAttachParameter` adds `fromSerial` from the attach serial — but only `Presence.HistoryAsync(query, untilAttach)` calls it, so the channel-level behaviour RTL10b describes cannot be invoked. RTL10a is covered |
| `realtime/unit/presence/realtime_presence_subscribe.md` | 10 | 1 | `RTP6e/subscribe-no-attach-option-0` — there is no `attachOnSubscribe` channel option; `ChannelOptions` carries Encrypted, CipherParams, Modes and Params and nothing else that bears on subscribe-time attaching |
| `realtime/unit/channels/channel_delta_decoding.md` | 12 | 2 | `PC3/vcdiff-plugin-decodes-0` and `PC3/no-plugin-fails-1` both require vcdiff to be a plugin passed through client options, so that a client can be built with it and another without. Here it is `IO.Ably.DeltaCodec`, referenced directly by `VcDiffEncoder` and compiled in; there is no plugin surface and no way to construct the "no plugin" client the second test needs. The other ten are translated and pass — see A5 in [`deviations.md`](deviations.md) for how the deltas are produced |
| `realtime/unit/channels/channel_subscribe.md` | 21 | 6 | Five are the RTL22 message-filter tests — `RTL22a/filter-matching-name-0`, `RTL22a/filter-matching-ref-timeserial-1`, `RTL22a/filter-matching-clientid-2`, `RTL22b/filter-isref-false-0` and `RTL22c/filter-multiple-criteria-0` — and there is no `MessageFilter` type: `IRealtimeChannel.Subscribe` takes a handler or a name and a handler, nothing else. The sixth, `RTL7h/no-attach-on-subscribe-0`, needs `attachOnSubscribe`. The remaining fifteen pass |
| `realtime/unit/channels/channel_options.md` | 16 | 6 | Five of the six are the derived-channel surface — `RTS5a/creates-derived-channel-0`, `RTS5a1/filter-base64-encoded-0`, `RTS5a2/derived-with-params-0`, `RTS5/get-derived-with-options-0` and `DO2a/filter-attribute-0` all need `channels.getDerived` and a `DeriveOptions` type, and neither exists: `IChannels<T>` offers `Get(name)` and `Get(name, options)` and nothing else. The sixth, `TB4/attach-on-subscribe-default-0`, needs `attachOnSubscribe`. RTS3c and RTL16 are translated and pass, each having lost only its `attachOnSubscribe` assertion |
| `realtime/unit/connection/backoff_jitter_test.md` | 4 | 1 | `RTB1/suspended-channel-retry-delay-1` asserts on `ChannelStateChange.retryIn`, and `ChannelStateChange` has no such member — its surface is Previous, Current, Error, Resumed, Event (`Realtime/ChannelStateChangedEventArgs.cs`). The SDK *does* apply RTB1 to a suspended channel (`RealtimeChannel.ReattachAfterTimeout` calls `ReconnectionStrategy.GetRetryTime(Options.ChannelRetryTimeout, retryCount)`) but keeps the figure in a local, so there is no observable to read — writing the test would be a compile error, not a failing assertion. The RTB1a/RTB1b arithmetic and RTB1's connection-level half are all covered and pass |

`realtime/unit/channels/channel_publish.md` is worth a note in the other direction:
`RTL6j/nack-results-error-3` **is** covered. It does not touch `PublishResult` — the server's
`ErrorInfo` reaches the publish callback directly — so it translates despite sitting among the
RTL6j tests that do not.

---

## Integration tier

| Spec file | Tests | Status | Absent API |
|---|---|---|---|
| `realtime/integration/mutable_messages_test.md` | 8 | fully blocked | the mutation and annotation surface; two of the eight cannot even construct their channel, since `ChannelMode` has no annotation variants |
| `rest/integration/mutable_messages.md` | 8 | fully blocked | as above, **plus** the `mutable:` namespace is not provisioned in the vendored app setup |
| `rest/integration/revoke_tokens.md` | 4 | fully blocked | `Auth#revokeTokens` |
| `rest/integration/batch_presence.md` | 3 | fully blocked | `RestClient#batchPresence` |
| `rest/integration/publish.md` | 5 | 2 skipped | `RSL1n` asserts on `PublishResult.serials`; `RSL1l1` needs a publish `params` overload to send `_forceNack` |

`rest/integration/batch_presence.md` is the cheapest of these to unblock: everything around it
already works — realtime connect, attach, `EnterClientAsync`, and `keys[2]` with the `channel6`
capability the spec needs. The gap is one client method and three result types.

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

## Derived channels

Five tests in `realtime/unit/channels/channel_options.md` describe `channels.getDerived(name,
DeriveOptions(filter: ...))` — a channel qualified by a server-side filter expression, with the
filter base64-encoded into the channel name on the wire. None of it exists here: searching product
code for `derive` in any casing returns nothing, and `DeriveOptions` has no counterpart among the
options types. This is one feature, so the five move together.

---

## Message filters

RTL22 subscribes with a `MessageFilter` — name, clientId, `isRef`, `refType` and
`refTimeserial` — and delivers only messages matching every criterion set on it. The type does not
exist here and nor does any overload that would take one; `IRealtimeChannel.Subscribe` offers
`(handler)` and `(name, handler)`. Five tests, one feature.

---

## Other absent API

Gaps that do not belong to a larger theme:

| Spec point | Test | What is absent |
|---|---|---|
| TB4, RTL7h | `realtime/unit/TB4/attach-on-subscribe-default-0` and `realtime/unit/RTL7h/no-attach-on-subscribe-0` | `ChannelOptions.attachOnSubscribe`. `ChannelOptions` carries Encrypted, CipherParams, Modes and Params; `RealtimeChannel.Subscribe` attaches unconditionally when the channel is neither ATTACHED nor ATTACHING, with nothing to opt out of it. The same absence skips `RTP6e/subscribe-no-attach-option-0` and trims two assertions from the RTS3c and RTL16 tests. Two tests here, so this is the one row in this table that is not a single test |
| TG4 | `rest/unit/TG4/first-returns-first-page-0` | `PaginatedResult<T>` has no `First()` / `FirstAsync()`. It carries `FirstQueryParams` but exposes no method to fetch that page; `First()` exists only on the `HttpPaginatedResponse` subclass |
| TO3c2 | `rest/unit/TO3c2/context-contains-expected-keys-0` | there is no structured log context. The sink contract is `ILoggerSink.LogEvent(LogLevel, string)` — a level and a flat message, with no context map to assert `method` / `host` / `path` against |
| TP5 | `rest/unit/TP5/presence-message-size-0` | no message-size API. There is no `PresenceMessage.Size`, no `Message.Size` and no TM6-style size calculation; the only `MaxMessageSize` is the server-sent read-only `ConnectionDetails.MaxMessageSize`, which no REST path consults |

One further gap is a parameter rather than a member: `PubSubHttpClient.Request` / `RequestV2` take
no `version` argument (RSC19f1's entire subject), because `X-Ably-Version` is set once per client
from the `Defaults.ProtocolVersion` constant.

---

## Proxy tier

Nothing is blocked by absent API. All 8 proxy spec files (7 realtime, 1 REST) exercise only the
public surface, and 37 of their 38 tests are translated.

The exception is `realtime/integration/proxy/connection_resume.md`'s
`RTN14h/resume-after-ttl-expiry-0`, which is blocked by the proxy rather than by the SDK: reaching
SUSPENDED needs a shortened `connectionStateTtl` injected into a replaced CONNECTED *and* the
client held out of CONNECTED while it expires, and neither the `__PASSTHROUGH__` sentinel nor a
counted `refuse_connection` works in the build used here. Measured; see M3 in
[`deviations.md`](deviations.md). The behaviour itself is covered at the unit tier by
`RTN14e_DisconnectedToSuspended`.

The tier needs `ably/uts-proxy` running, so it has its own CI leg and its own trait
(`requires=proxy` alongside `type=integration`). Without a proxy the tests skip rather than fail;
see the README.

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

## Translated so far, and what is still outstanding

This document separates two different things, and conflating them would be misleading: what
**cannot** be covered (everything above), and what simply **has not been translated yet**.

### In the stack and verified

Measured on `net6.0`, three consecutive clean runs of the unit tier
(`--filter "type!=integration"`): **0 failed, 2102 passed, 97 skipped**.

| Tier | Spec files | Tests | State |
|---|---|---|---|
| `rest/unit` | 31 of 41 | 407 passing, 29 env-gated or skipped | green |
| `realtime/unit` | 48 of 54 | 426 passing, 48 env-gated or skipped | green |
| `rest/integration` | 8 | 58 passing, 1 skipped | green |
| `realtime/integration` | 12 | 33 passing | green |
| proxy tier | 8 | 34 passing, 3 gated as deviations, 1 blocked by the proxy | green |
| harness self-tests | — | 36 passing | green |
| proxy harness self-tests | — | 4 passing (skip without a proxy) | green |

The skipped counts are almost entirely `[DeviationFact]` tests, which run under `RUN_DEVIATIONS=1`
and are each written up in [`deviations.md`](deviations.md). A handful are msgpack cases that
cannot execute in this build.

### Not yet translated

Nothing. Every spec file in scope now has derived tests.

Both tiers of unit tests are complete. The 16 spec files not translated - 10 under `rest/unit`,
6 under `realtime/unit` - are exactly the fully blocked ones tabulated earlier in this document;
every other unit spec file has a derived test class, with the per-file omissions accounted for in
the partially-blocked table.

`.claude/skills/uts-to-csharp/SKILL.md` is what makes the remainder repeatable: it carries the
layout rule, the pseudocode mapping, the harness API and the traps that cost the most time on this
pass.

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
4. `rest/integration/revoke_tokens.md` contradicts itself on who computes the result counts: its
   header says the server supplies `successCount` / `failureCount` and that "no client-side
   computation is needed" (lines 41-44), while its RSA17c row says the SDK computes them
   client-side (line 89). This changes what an implementation must do.
5. `rest/unit/encoding/message_encoding.md` — `RSL4/encoding-fixtures-ably-common-0` branches on
   `fixture.use_binary_protocol`, but the real `common/test-resources/messages-encoding.json` has
   no such field and uses different field names entirely, so that branch can never be taken.
