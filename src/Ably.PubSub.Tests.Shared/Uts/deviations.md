# UTS deviations

Findings from evaluating the UTS-derived tests against the .NET SDK. Tests marked as env-gated (`[DeviationFact]`) are skipped by default and run with `RUN_DEVIATIONS=1`, for example `RUN_DEVIATIONS=1 dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --filter "FullyQualifiedName=<namespace>.<class>.<method>"`.

## UTS Spec Errors

Every test in this section is skipped (`Skip = "UTS spec error <point>: …; see deviations.md (UTS Spec Errors)"`) rather than left as a fail-fast placeholder, so CI stays green while the specs are fixed upstream. This departs from the manual's fail-fast convention by decision of the SDK team; each entry's draft fix says what the spec should change. Once a spec is fixed, regenerate the test from it and remove the skip.

### RTN4b — successful-connection test asserts INITIALIZED on a client that auto-connects

- **Spec point:** RTN4b, TO3e (test `realtime/integration/RTN4b/successful-connection-0`, `uts/realtime/integration/connection_lifecycle_test.md`).
- **What the features spec says:** `(TO3e)` "`autoConnect` boolean - defaults to true. If false, suppresses the automatic initiation of a connection when the library is instantiated".
- **What the UTS spec gets wrong:** the test builds the client without `autoConnect: false`, then asserts `connection.state == initialized` and calls `connect()`. With the default the connection starts at construction, so the INITIALIZED assertion is inconsistent with the spec's own options.
- **Observed:** the state is CONNECTING when the assertion runs (`Expected ConnectionState.Initialized, found Connecting`).
- **Draft fix:** add `autoConnect: false` to the client options.
- **Test impact:** `ConnectionLifecycleTests.RTN4b_SuccessfulConnection` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RSA7 — mismatched-clientid test expects a constructor throw, the features spec says FAILED after an auth request

- **Spec point:** RSA7 / RSA15 (test `realtime/integration/RSA7/mismatched-clientid-fails-1`, `uts/realtime/integration/auth.md`).
- **What the features spec says:** `(RSA15c)` "Following an auth request which uses a `TokenDetails` or `TokenRequest` object that contains an incompatible `clientId`, the library should in the case of Realtime transition the connection state to `FAILED`".
- **What the UTS spec gets wrong:** the Test Steps use `EXPECT THROW creating Realtime(...)`, but the clientId of a callback-provided token is only known after the token is obtained, so construction cannot throw. The spec's own Assertions note says "the connection enters FAILED state with error code 40102", contradicting its Test Steps.
- **Observed:** creating the client does not throw.
- **Draft fix:** connect, then `AWAIT_STATE failed` and assert the error code 40102 (as the note says).
- **Test impact:** `AuthTests.RSA7_MismatchedClientidFails` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RSA4c3 — callback-error-connected-stays expects errorReason to stay null after a failed reauth

- **Spec point:** RSA4c1, RSA4c3 (test `realtime/unit/RSA4c3/callback-error-connected-stays-0`, `uts/realtime/unit/auth/auth_callback_errors_test.md`).
- **What the features spec says:** `(RSA4c1)` "An `ErrorInfo` with `code` `80019`, `statusCode` 401, and `cause` set to the underlying cause should be emitted with the state change if there is one (per `RSA4c2/3`) and set as the connection `errorReason`"; `(RSA4c3)` "If the connection is `CONNECTED`, then the connection should remain `CONNECTED`".
- **What the UTS spec gets wrong:** it asserts that after an RTN22 reauth whose authCallback fails, `errorReason` is not set ("no side effects — no state change, no event, and errorReason is not set"), citing specification#466. The features.md at this sha still requires the 80019 errorReason. The sibling spec `connection_auth_test.md` (`realtime/unit/RSA4c3/callback-error-stays-connected-0`) asserts the features.md behaviour (errorReason 80019), so the two UTS specs contradict each other.
- **Draft fix:** either align this test with RSA4c1 (assert errorReason 80019/401 with the cause), or land specification#466 in features.md and fix `connection_auth_test.md` instead.
- **Test impact:** `AuthCallbackErrorsTests.RSA4c3_CallbackErrorConnectedStays` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip). (It passed against the SDK, which swallows the failure; see the RSA4c1/RSA4d entry under Failing Tests.)

### RTL4b — fails-connection-suspended test can't reach connection SUSPENDED

- **Spec point:** RTL4b, RTN14e, DF1a (test `realtime/unit/RTL4b/fails-connection-suspended-2`, `uts/realtime/unit/channels/channel_attach.md`).
- **What the features spec says:** `(RTN14e)` "Once the connection state has been in the `DISCONNECTED` state for more than the default `connectionStateTtl`, the state will change to `SUSPENDED`"; `(DF1a)` "`connectionStateTtl` integer - default 120s".
- **What the UTS spec gets wrong:** it refuses every connection with real timers and sets only `channelRetryTimeout: 100` ("short suspend timeout for testing"), then `AWAIT_STATE connection == suspended`. `channelRetryTimeout` governs channel retries (RTB1), not connection suspension, so SUSPENDED needs 120 s of real time; observed: `timed out after 10000 ms waiting for connection state Suspended (current: Disconnected, reason: 80000 Connection refused)`.
- **Draft fix:** `enable_fake_timers()` and `ADVANCE_TIME` past `connectionStateTtl` (loop until SUSPENDED), as other RTN14e tests do.
- **Test impact:** `ChannelAttachTests.RTL4b_FailsConnectionSuspended` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTL13c — retry-cancelled test expects no ATTACH after a disconnect the client immediately recovers from

- **Spec point:** RTL13c (test `realtime/unit/RTL13c/retry-cancelled-disconnected-0`, `uts/realtime/unit/channels/channel_server_initiated_detach.md`).
- **What the features spec says:** `(RTN15a)` "If a transport is disconnected unexpectedly ... it should respond as if it had received a non-token `DISCONNECTED` (following `RTN15h3`)."; `(RTN15h3)` "... the library must initiate an immediate reconnect attempt"; `(RTL3d)` "If the connection state enters the `CONNECTED` state, any channels in the `ATTACHING`, `ATTACHED`, or `SUSPENDED` states should be transitioned to `ATTACHING` ... and initiate an `RTL4c` attach sequence."
- **What the UTS spec gets wrong:** its mock answers every connection attempt with CONNECTED, so after `simulate_disconnect()` the client reconnects at once and RTL3d sends a new ATTACH for the SUSPENDED channel (and RTL4f/RTL13b retries follow). The assertions "no additional ATTACH messages" and "channel still SUSPENDED" contradict RTN15a/RTN15h3/RTL3d. Observed: `Expected attachCount to be 3, but found 5`.
- **Draft fix:** make the mock refuse every connection attempt after the first (`respond_with_refused()`), so the connection stays DISCONNECTED while time is advanced.
- **Test impact:** `ChannelServerInitiatedDetachTests.RTL13c_RetryCancelledDisconnected` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTN17e — the HTTP mock never answers the connectivity check with "yes", yet the test expects a fallback connection

- **Spec point:** RTN17e, RTN17j (test `realtime/unit/RTN17e/http-uses-same-fallback-0`, `uts/realtime/unit/connection/fallback_hosts_test.md`).
- **What the features spec says:** `(RTN17j)` "... the `Connection` manager should first check if an internet connection is available by issuing a `GET` request to the `connectivityCheckUrl` ... If the request succeeds and the text "yes" is included in the body, then the client library can assume it has a viable internet connection and should then immediately retry the connection against `fallback domains` ..."
- **What the UTS spec gets wrong:** its `MockHttpClient` answers every non-history request, the connectivity check included, with `{}`, so per RTN17j the client has no grounds to move to a fallback domain, yet the test takes `connection_attempts[1]` as the fallback host it connected to. Its history response `{"items": [], "start": 0, "end": 0}` is also not the REST wire format (history returns a JSON array of messages); the SDK fails to parse it (`Cannot deserialize the current JSON object ... into type List<Message>`).
- **Draft fix:** answer `GET` to the connectivity check URL with `200 "yes"`, and the history request with `[]`.
- **Test impact:** `FallbackHostsTests.RTN17e_HttpUsesSameFallback` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTN20b — the refused reconnect on the default endpoint is retried against a fallback at once, so the client is not DISCONNECTED when the network event arrives

- **Spec point:** RTN20b, RTN17j (test `realtime/unit/RTN20b/network-available-disconnected-connects-0`, `uts/realtime/unit/connection/network_change_test.md`).
- **What the features spec says:** `(RTN17j)` "In the case of an error necessitating use of an alternative host (see RTN17f), the `Connection` manager should first check if an internet connection is available ... and should then immediately retry the connection against `fallback domains` in random order". `(RSC15l1)` lists "host unresolvable or unreachable" among those errors, and `(REC2c1)` gives the default primary domain five fallback domains.
- **What the UTS spec gets wrong:** the client uses the default endpoint (fallbacks apply) and the second attempt is refused, which the spec says puts the client "into DISCONNECTED state with a 30-second retry timer"; per RTN17j the client instead retries a fallback domain immediately (the same corpus treats a refused primary as fallback-qualifying in `RTN17i/prefer-primary-domain-0`), and the mock answers that third attempt with CONNECTED, so the client is CONNECTED before `simulate_network_available()` (`attempts_before` is already 3; `Expected connectionAttemptCount to be greater than 3, but found 3`).
- **Draft fix:** give the client an empty fallback set (an explicit-hostname `endpoint`, REC2c2), or refuse every attempt after the first until the network event.
- **Test impact:** `NetworkChangeTests.RTN20b_NetworkAvailableDisconnectedConnects` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTP2b2 — newness-by-msgserial test asserts the stale value after the newer put

- **Spec point:** RTP2b2 (test `realtime/unit/RTP2b2/newness-by-msgserial-index-0`, `uts/realtime/unit/presence/presence_map.md`).
- **What the features spec says:** `(RTP2b2)` "Else split the `id` of both presence messages ... Compare them first by `msgSerial` numerically, then (if `msgSerial` is equal) by `index` numerically, larger being newer in both cases".
- **What the UTS spec gets wrong:** the Test Steps put msgSerial 5 ("first"), a stale msgSerial 3 and then a newer msgSerial 7 ("newer"). The Assertions section then asserts `map.get("conn-1:client-1").data == "first"` and, a few lines later, `== "newer"` for the same entry with no step in between. The first assertion can only hold before the newer put, so the spec is internally inconsistent.
- **Observed:** `Expected Get(map,"conn-1","client-1").Data to be "first", but found "newer"`.
- **Draft fix:** move `ASSERT stale_result IS null` and `ASSERT map.get(...).data == "first"` into the Test Steps, between the stale put and the newer put.
- **Test impact:** `PresenceMapTests.RTP2b2_NewnessByMsgserialIndex` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTP5f / RTL11 — channel SUSPENDED expected after a plain transport disconnect

- **Spec point:** RTP5f, RTL11 (tests `realtime/unit/RTP5f/suspended-maintains-presence-map-0` and `realtime/unit/RTL11/queued-presence-fail-suspended-1`, `uts/realtime/unit/presence/realtime_presence_channel_state.md`).
- **What the features spec says:** `(RTL3e)` "If the connection state enters the `DISCONNECTED` state, it will have no effect on the channel states." `(RTL3c)` "If the connection state enters the `SUSPENDED` state, then an `ATTACHING` or `ATTACHED` channel state will transition to `SUSPENDED`".
- **What the UTS spec gets wrong:** both tests call `mock_ws.active_connection.simulate_disconnect()` and then `AWAIT_STATE channel.state == ChannelState.suspended`. A disconnect only takes the connection to DISCONNECTED (and it then reconnects, since the mock accepts every attempt), so per RTL3e the channel never becomes SUSPENDED; the comment "Connection goes SUSPENDED" has no step that makes it happen.
- **Observed:** `timed out after 10000 ms waiting for channel ... state Suspended (current: Attaching)` (RTL11) and `(current: Attached)` (RTP5f).
- **Draft fix:** drive the connection to SUSPENDED (fake timers, refuse reconnects, advance past `connectionStateTtl`, as the connection specs do), then await the channel SUSPENDED.
- **Test impact:** `RealtimePresenceChannelStateTests.RTP5f_SuspendedMaintainsPresenceMap` and `RealtimePresenceChannelStateTests.RTL11_QueuedPresenceFailSuspended` are skipped placeholders (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the tests from the fixed spec and remove the skip).

### RTP11c1 — get-no-wait reads presence before the queued SYNC is processed

- **Spec point:** RTP11c1 (test `realtime/unit/RTP11c1/get-no-wait-returns-immediately-0`, `uts/realtime/unit/presence/realtime_presence_get.md:171`).
- **What the features spec says:** `(RTP11c1)` "`waitForSync` (default `true`). When `true`, method will wait until `SYNC` is complete before returning a list of members. When `false`, known set of presence members is returned immediately, which may be incomplete if the `SYNC` is not finished".
- **What the UTS spec gets wrong:** the mock answers ATTACH with ATTACHED and then a SYNC carrying "alice". The test runs `AWAIT channel.attach()` and straight away `get(waitForSync: false)`, then asserts one member. `attach()` resolves on the ATTACHED, and nothing guarantees the following SYNC has been processed by then: with `waitForSync: false` the "known set" may still be empty, which RTP11c1 allows ("may be incomplete").
- **Observed:** `Expected members {empty} to contain 1 item(s), but found 0` in every serial run of the unit tier. In a serial run there is no synchronization context, so the test resumes inline on the SDK workflow thread, inside the ATTACHED processing, before the SYNC message is handled. Under the default parallel runner the continuation is posted, and it passes.
- **Draft fix:** add `process_pending_events()` between `AWAIT channel.attach()` and the `get(waitForSync: false)`.
- **Test impact:** `RealtimePresenceGetTests.RTP11c1_GetNoWaitReturnsImmediately` is gated `[DeviationFact("RTP11c1")]` with the spec's assertions, not a fail-fast placeholder. It passes under the default (parallel) runner, and the user asked for every failing test to be gated.

### RTP16c — a server DETACHED during ATTACHING is expected to leave the channel DETACHED

- **Spec point:** RTP16c, RTL13b (test `realtime/unit/RTP16c/presence-errors-other-states-0`, `uts/realtime/unit/presence/realtime_presence_enter.md`).
- **What the features spec says:** `(RTL13b)` "If the attempt to re-attach fails, or if the channel was already in the `ATTACHING` state, the channel will transition to the `SUSPENDED` state and the error will be emitted in the `ChannelStateChange` event."
- **What the UTS spec gets wrong:** the setup answers ATTACH with a server DETACHED and asserts `channel.state == ChannelState.detached`; per RTL13 a server-initiated DETACHED while ATTACHING sends the channel to SUSPENDED.
- **Observed:** `Expected ChannelState.Detached, but found ChannelState.Suspended`.
- **Draft fix:** reach DETACHED with an explicit `channel.detach()` after attaching (or assert SUSPENDED, which RTP16c also covers).
- **Test impact:** `RealtimePresenceEnterTests.RTP16c_PresenceErrorsOtherStates` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTP15c — a wildcard-clientId client is expected to enter() successfully

- **Spec point:** RTP15c, RTP8j, RSA7c (test `realtime/unit/RTP15c/enterclient-no-side-effects-0`).
- **What the features spec says:** `(RTP8j)` "If the connection is in the `CONNECTED` state, and the value of `RealtimeClient#clientId` is one of the following: `'*'` ... `null` ... then the `enter` request results in an error immediately." `(RSA7c)` "A `clientId` provided in the `ClientOptions` ... cannot contain only a wildcard `'*'` string value as that client ID value is reserved".
- **What the UTS spec gets wrong:** the client is built with `clientId: "*"` and the test expects `channel.presence.enter("main-client")` to succeed (and send no clientId). RTP8j makes that enter an error, and the spec's own fallbacks (a concrete clientId, or key auth with no clientId) hit RTP15f or RTP8j respectively, so no faithful setup can make both enter() and enterClient("other-user") succeed on one client.
- **Draft fix:** split it: enter() on an identified client, and enterClient/leaveClient from an unidentified (key auth) client on the same channel, or obtain the identity from a token/connectionDetails clientId.
- **Test impact:** `RealtimePresenceEnterTests.RTP15c_EnterclientNoSideEffects` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTP17g — an identified "admin" client is expected to enterClient("alice")

- **Spec point:** RTP17g, RTP15f (test `realtime/unit/RTP17g/reentry-publishes-enter-with-data-0`, `uts/realtime/unit/presence/realtime_presence_reentry.md`).
- **What the features spec says:** `(RTP15f)` "If the client is identified and has a valid `clientId`, and the `clientId` argument does not match the client's `clientId`, then it should indicate an error."
- **What the UTS spec gets wrong:** the setup builds the client with `clientId: "admin"` ("rely on server-side permission for enterClient") and expects `enterClient("alice", …)` to succeed; RTP15f requires a client-side error for that call.
- **Observed:** `PresenceMessage has incompatible clientId 'alice' as the current client is configured with 'admin'`.
- **Draft fix:** build the client with key auth and no clientId (the alternative the presence-enter spec's notes give), so enterClient on behalf of "alice" is allowed.
- **Test impact:** `RealtimePresenceReentryTests.RTP17g_ReentryPublishesEnterWithData` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTN14b / RTN15h2 / RTN15c5 — token-renewal tests count an initial token request that a key-only client never makes

- **Spec point:** RTN14b, RTN15h2, RTN15c5 (tests `realtime/unit/RTN14b/token-error-with-renewal-0` in `uts/realtime/unit/connection/connection_open_failures_test.md`; `realtime/unit/RTN15h2/token-error-renew-success-0` and `realtime/unit/RTN15c5/token-error-during-resume-0` in `uts/realtime/unit/connection/connection_failures_test.md`).
- **What the features spec says:** `(RSA2)` "Basic Auth is the default authentication scheme if an API key exists"; `(RSA4)` "Token Auth is used if `useTokenAuth` is set to true, or if `useTokenAuth` is unspecified and any one of `authUrl`, `authCallback`, `token`, or `TokenDetails` is provided".
- **What the UTS spec gets wrong:** each test builds the client with only `key: "appId.keyId:keySecret"` and asserts `token_request_count == 2  # Initial + renewal`. A key-only client uses Basic auth, so there is no initial token request; only the renewal after the 40142 token error requests a token.
- **Observed:** `Expected tokenRequestCount to be 2, but found 1` in all three tests.
- **Draft fix:** assert `token_request_count == 1` (the renewal), or set `useTokenAuth: true` if an initial token request is intended.
- **Test impact:** `ConnectionOpenFailuresTests.RTN14b_TokenErrorWithRenewal`, `ConnectionFailuresTests.RTN15h2_TokenErrorRenewSuccess`, `ConnectionFailuresTests.RTN15c5_TokenErrorDuringResume`: skipped placeholders (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the tests from the fixed spec and remove the skip).

### RTN15h1 — unit test expects 40142 where RSA4a2 requires 40171

- **Spec point:** RTN15h1, RSA4a2 (test `realtime/unit/RTN15h1/token-error-no-renew-0`, `uts/realtime/unit/connection/connection_failures_test.md`).
- **What the features spec says:** `(RSA4a2)` "if the server responds with a token error (401 HTTP status code and an Ably error value `40140 <= code < 40150`), then the client library should indicate an error with error code `40171`, not retry the request and, in the case of the realtime library, transition the connection to the `FAILED` state" (applies because the client is built with only `token:`, RSA4a).
- **What the UTS spec gets wrong:** it asserts `client.connection.errorReason.code == 40142` (the injected code). The proxy-tier test for the same scenario (`realtime/proxy/RTN15h1/token-error-nonrenewable-failed-0`) asserts 40171.
- **Observed:** `Expected client.Connection.ErrorReason.Code to be 40142, but found 40171` (the SDK follows RSA4a2).
- **Draft fix:** assert `errorReason.code == 40171` (statusCode 401).
- **Test impact:** `ConnectionFailuresTests.RTN15h1_TokenErrorNoRenew`: a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTN14e / RTN14f / RTN25 — SUSPENDED tests rely on a connectionStateTtl the client cannot have

- **Spec point:** RTN14e, RTN14f, RTN25 (tests `realtime/unit/RTN14e/disconnected-to-suspended-0` and `realtime/unit/RTN14f/suspended-retries-indefinitely-0` in `uts/realtime/unit/connection/connection_open_failures_test.md`; `realtime/unit/RTN25/error-reason-suspended-2` in `uts/realtime/unit/connection/error_reason_test.md`).
- **What the features spec says:** `(DF1a)` "`connectionStateTtl` integer - default 120s. When the client is in the `DISCONNECTED` state, once this TTL has passed, the client should transition the state to the `SUSPENDED` state"; `(RTN14e)` "Once the connection state has been in the `DISCONNECTED` state for more than the default `connectionStateTtl`, the state will change to `SUSPENDED`".
- **What the UTS spec gets wrong:** RTN14e and error-reason-suspended-2 define a local `DEFAULT_CONNECTION_STATE_TTL = 5000` ("In real implementation, this comes from server in CONNECTED message") and advance only 5100 ms; RTN14f awaits SUSPENDED without advancing time at all. Every connection attempt is refused, so no CONNECTED ever supplies a shorter TTL and the 120 s default applies: SUSPENDED cannot be reached.
- **Observed:** RTN14e `poll_until timed out ... 'connection entered SUSPENDED'`; RTN14f the same after 10 s; error-reason-suspended-2 `timed out after 1000 ms waiting for connection state Suspended (current: Disconnected, reason: 80000 Connection refused)`.
- **Draft fix:** advance past the 120 s default (`ADVANCE_TIME(120000 + 100)`, as `realtime/unit/RTN13b/ping-error-suspended-1` does with `ADVANCE_TIME(121s)`), or first deliver a CONNECTED with a short `connectionStateTtl` and then drop the transport.
- **Test impact:** `ConnectionOpenFailuresTests.RTN14e_DisconnectedToSuspended`, `ConnectionOpenFailuresTests.RTN14f_SuspendedRetriesIndefinitely`, `ErrorReasonTests.RTN25_ErrorReasonSuspended`: skipped placeholders (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the tests from the fixed spec and remove the skip).

### RTN16g3 — SUSPENDED sub-case never refuses a connection

- **Spec point:** RTN16g3 (test `realtime/unit/RTN16g3/recovery-key-null-inactive-0`, `uts/realtime/unit/connection/connection_recovery_test.md`).
- **What the features spec says:** `(RTN16g3)` "It should return `Null` when the SDK is in the `CLOSED`, `CLOSING`, or `FAILED` states, or when it does not have a `connectionKey`". The test is wrong internally, not against the features spec.
- **What the UTS spec gets wrong:** the `mock_ws_suspended` handler is commented "All connections fail after initial, to force SUSPENDED" but branches on `connection_attempt_count == 1`, a counter only the first mock's handler increments. It stays 1, so every attempt succeeds and SUSPENDED is never reached.
- **Observed:** the run failed earlier, at `Expected client.Connection.CreateRecoveryKey() to be <null>, but found ""` before connecting (the SDK returns "" for null, see the Adapted entry RTN8a / RTN9a); the FAILED sub-case also injects a 50000/500 ERROR, which this SDK handles as retryable (Failing Tests, RTN14g).
- **Draft fix:** give the suspended mock its own counter (increment it in its handler), and use a non-5xx statusCode (e.g. 400) in the FAILED sub-case's ERROR.
- **Test impact:** `ConnectionRecoveryTests.RTN16g3_RecoveryKeyNullInactive`: a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

### RTB1 — suspended-channel retry test reads ChannelStateChange.retryIn, which the spec doesn't define

- **Spec point:** RTB1, TH1-TH6, TA2 (test `realtime/unit/RTB1/suspended-channel-retry-delay-1`, `uts/realtime/unit/connection/backoff_jitter_test.md`).
- **What the features spec says:** `(RTB1)` "For connections in the `DISCONNECTED` state and realtime channels in the `SUSPENDED` state the time until retry is calculated as the product of the initial retry timeout … the backoff coefficient … and the jitter coefficient …". The IDL `class ChannelStateChange` (TH*) has only `current`, `event`, `previous`, `reason`, `resumed`, `hasBacklog`; `retryIn` exists only on `ConnectionStateChange` (`(TA2)`, IDL `retryIn: Duration? // RTN14d, TA2`).
- **What the UTS spec gets wrong:** it observes the channel retry delay through `ChannelStateChange.retryIn`, an attribute the features spec doesn't define for channels.
- **Draft fix:** measure the channel re-attach delay without `retryIn`, e.g. record the fake-clock time of each ATTACH frame after SUSPENDED and assert the gaps fall within `channelRetryTimeout * backoff * jitter`.
- **Test impact:** `BackoffJitterTests` suspended-channel-retry test: skipped stub (`Skip = "UTS spec error RTB1: …"`, spec lines as comments). It's a stub rather than a fail-fast placeholder because the test can't be written against this SDK either (no channel `RetryIn`); moved from Mock Infrastructure Limitations.

## Failing Tests

### RTN4c — errorReason is set after a client-initiated close

- **Spec point:** RTN4c, RTN25 (test `realtime/integration/RTN4c/graceful-close-0`).
- **What the spec says:** the test asserts `connection.errorReason IS null` after a graceful close. `(RTN25)` lists the errors that set `Connection#errorReason` (RSA4c1, RSA4d, RTN11d, RTN14a, RTN14b, RTN14e, RTN14g, RTN15c7, RTN15c4, RTN15d, RTN15h, RTN15i, RTN16e); a client close is not among them.
- **What the SDK does:** after CLOSED, `ErrorReason` is `ErrorInfo` code 10000, "Connection closed by client".
- **Test impact:** `ConnectionLifecycleTests.RTN4c_GracefulClose`, env-gated `[DeviationFact]` with the spec-correct assertions.
- **Status:** open bug (to be confirmed with the SDK team); other assertions after the first failing one were not reached.

### RTN14g — a 5xx connection ERROR leaves the connection open instead of FAILED

- **Spec point:** RTN14g.
- **What the spec says:** `(RTN14g)` "If an `ERROR` `ProtocolMessage` with an empty `channel` attribute is received for any reason other than RTN14b, then the connection will transition to the `FAILED` state".
- **What the SDK does:** an ERROR with `statusCode` 500 is handled as retryable (`RealtimeWorkflow` `HandleConnectingErrorCommand`, `IsRetryableStatusCode`), so the connection goes DISCONNECTED and then reconnects (the proxy's rule fires once); FAILED is never reached (current: Connected).
- **Test impact:** `ConnectionOpenFailuresTests.RTN14g_ServerErrorCausesFailed`, env-gated.
  Unit tier, same root cause (ERROR with `statusCode` 500 handled as retryable): `ConnectionOpenFailuresTests.RTN14g_ErrorEmptyChannelFailed` (`realtime/unit/RTN14g/error-empty-channel-failed-0`, `[DeviationFact("RTN14g")]`), `ConnectionFailuresTests.RTN15c4_FatalErrorDuringResume` (`realtime/unit/RTN15c4/fatal-error-during-resume-0`, `[DeviationFact("RTN15c4")]`; `(RTN15c4)` "Any other `ERROR` `ProtocolMessage` indicating a fatal error in the connection ... The client should transition to the `FAILED` state"), `ErrorReasonTests.RTN25_ErrorReasonProtocolError` (`realtime/unit/RTN25/error-reason-protocol-error-5`, `[DeviationFact("RTN14g")]`). Observed in each: `timed out ... waiting for connection state Failed (current: Disconnected, reason: 50000 ...)`.
- **Status:** open bug (see also `realtime-mapping.md` section 13).

### RTN15h1 — non-renewable token error: FAILED does not hold

- **Spec point:** RTN15h1.
- **What the spec says:** `(RTN15h1)` "If the `DISCONNECTED` message contains a token error ... and the library does not have a means to renew the token, the connection will transition to the `FAILED` state and the `Connection#errorReason` will be set".
- **What the SDK does:** after the wait for FAILED returns, the state is CONNECTING again (`Expected Failed, found Connecting`): the RTN27h race in which a queued `SetConnectingStateCommand` runs after FAILED without a state check (`realtime-mapping.md` section 13).
- **Test impact:** `ConnectionResumeTests.RTN15h1_TokenErrorNonrenewableFailed`, env-gated.
  Unit tier, same root cause (FAILED followed by CONNECTING): `ConnectionFailuresTests.RTN15j_ErrorEmptyChannelFailed` (`realtime/unit/RTN15j/error-empty-channel-failed-0`, `[DeviationFact("RTN15j")]`; `(RTN15j)` "The client should transition to the `FAILED` state") and `ConnectionOpenFailuresTests.RSA4a_TokenErrorNoRenewal` (`realtime/unit/RSA4a/token-error-no-renewal-0`, `[DeviationFact("RSA4a2")]`; `(RSA4a2)` "transition the connection to the `FAILED` state"). Observed in both: `Expected ConnectionState.Failed, but found ConnectionState.Connecting` right after the wait for FAILED returned.
  Also unit tests whose fatal ERROR is followed by the server closing the transport, which flip between FAILED and CONNECTING (several pass or fail by timing; gated where a run showed it): `ConnectionPingTests.RTN13b_DeferredPingErrorFailed` (`timed out ... waiting for connection state Failed (current: Connecting)`), `TokenExpiryNonRenewableTests.RSA4a2_TokenErrorNonRenewableNoRetry` (2-4 connection attempts instead of 1, `(RSA4a2)` "not retry the request"), `ErrorReasonTests.RTN25_ErrorReasonTokenError` (errorReason cleared), `ErrorReasonTests.RTN25_ErrorReasonInStateChange` (2-5 FAILED events instead of 1), `ChannelPublishTests.RTL6c4_FailsConnFailed`, `ChannelPublishTests.RTN7e_ErrorRepresentsReason`, `ErrorReasonTests.RTN25_ErrorReasonOnFailed` (`Expected client.Connection.ErrorReason not to be <null>`, seen in serial runs) and `ConnectionOpenFailuresTests.RTN14a_InvalidKeyFailed` (`Expected ConnectionState.Failed, but found ConnectionState.Connecting`, seen in a serial run), all `[DeviationFact("RTN27h")]`; `(RTN27h)` "`FAILED` ... It will remain in this state indefinitely, unless and until an explicit call to `connect()` is made." Exposed but not seen failing: `ChannelConnectionStateTests.RTL3a_FailedAttachedToFailed`, `ChannelConnectionStateTests.RTL3a_FailedAttachingToFailed`, `ConnectionIdKeyTests.RTN8d_IdKeyNullAfterFailed`, `ConnectionPingTests.RTN13b_PingErrorFailed`.
- **Status:** open bug. The error-code assertion (40171, ably-js specific, NOTE in the test) was not reached and may also deviate.

### RTN14e — SUSPENDED is not entered at the state TTL

- **Spec point:** RTN14e, RTN14h (test `realtime/proxy/RTN14h/resume-after-ttl-expiry-0`).
- **What the spec says:** `(RTN14e)` "Once the connection state has been in the `DISCONNECTED` state for more than the default `connectionStateTtl`, the state will change to `SUSPENDED`".
- **What the SDK does:** `ConnectionDisconnectedState.ClampToStateTtl` wakes the retry timer at the TTL deadline and converts to SUSPENDED only if that attempt fails. The attempt (not refused by the proxy) succeeds, so the connection goes CONNECTED and SUSPENDED is never emitted (states: Connecting, Connected, Disconnected, Connecting, Disconnected, Connecting, Connected).
- **Test impact:** `ConnectionResumeTests.RTN14h_ResumeAfterTtlExpiry`, env-gated.
- **Status:** open bug.

### RTP17i — no automatic re-entry when the channel re-attaches after a reconnect

- **Spec point:** RTP17i, RTP17g (test `realtime/proxy/RTP17i/reenter-after-disconnect-1`).
- **What the spec says:** `(RTP17i)` "The `RealtimePresence` object should perform automatic re-entry whenever the channel receives an `ATTACHED` `ProtocolMessage`, except in the case where the channel is already attached and the `ProtocolMessage` has the `RESUMED` bit flag set."
- **What the SDK does:** after the reconnect no PRESENCE frame is sent. In `ChannelMessageProcessor`, for a channel that is not ATTACHED the SDK calls `Presence.ChannelAttached` before `SetChannelState(Attached)`; `EnterMembersFromInternalPresenceMap` then runs while the channel is still ATTACHING, so the ENTER is only queued (RTP16b) and never flushed. The sibling test (injected ATTACHED on an already attached channel) passes.
- **Test impact:** `PresenceReentryTests.RTP17i_ReenterAfterDisconnect`, env-gated.
  Unit tier: the tests `RealtimePresenceReentryTests.RTP17i_AutoReentryOnAttached`, `RealtimePresenceReentryTests.RTP17g1_ReentryOmitsIdNewConnid` and `RealtimePresenceReentryTests.RTP17e_FailedReentryEmitsUpdateError` (`realtime_presence_reentry.md`) fail the same way: after a disconnect and reconnect the new non-RESUMED ATTACHED arrives while the channel is ATTACHING, `EnterMembersFromInternalPresenceMap` queues the ENTER (RTP16b) after `SendQueuedMessages` has run, and nothing sends it (`poll_until timed out ... 're-entry PRESENCE captured'`; for RTP17e no NACK is ever provoked, so no 91004 UPDATE). Features `(RTP17g)` "Automatic re-entry consists of, for each member of the RTP17 internal PresenceMap, publishing a PresenceMessage with an ENTER action ..." and `(RTP17e)` (91004 UPDATE on failure) cannot be observed until it is fixed. All three are env-gated `[DeviationFact("RTP17i")]`.
- **Status:** open bug (diagnosis from source and the proxy log, not confirmed with a fix).

### RSA4c1 / RSA4d — a failed RTN22 reauth is swallowed: no errorReason, no FAILED on 403

- **Spec point:** RSA4c1, RSA4c3, RSA4d, RSA4d1 (tests `realtime/unit/RSA4c3/callback-error-stays-connected-0`, `realtime/unit/RSA4d/callback-403-reauth-causes-failed-1` in `connection_auth_test.md`; `realtime/unit/RSA4d/callback-403-reauth-failed-1` in `auth_callback_errors_test.md`).
- **What the features spec says:** `(RSA4d)` "If ... an `authCallback` ... result[s] in an `ErrorInfo` with `statusCode` 403, as part of an attempt by the realtime client to authenticate, then the client library should transition to the `FAILED` state, with an `ErrorInfo` (with `code` `80019`, `statusCode` 403, and `cause` set to the underlying cause) emitted with the state change and set as the connection `errorReason`"; `(RSA4d1)` "An 'attempt by the realtime client to authenticate' in `RSA4c` and `RSA4d` includes ... an `RTN22` online reauth"; RSA4c1 as quoted above (errorReason 80019 for a non-403 failure while CONNECTED).
- **What the SDK does:** after the server's AUTH the connection stays CONNECTED with `ErrorReason` null whatever the callback's error: "timed out after 5000 ms waiting for connection state Failed (current: Connected, reason: none)" (403) and "poll_until timed out ... waiting for 'connection errorReason is set'" (50000).
- **Root cause:** `ConnectionConnectedState.OnMessageReceived` queues `RetryAuthCommand.Create(false)`; `RealtimeWorkflow.ProcessCommand` runs `await Auth.RenewToken()`, whose `AblyException` escapes to the workflow loop's `catch (Exception e) { Logger.Error("Error Processing command: ...") }` and is dropped. Nothing sets `errorReason` or, for 403, moves to FAILED.
- **Test impact:** `ConnectionAuthTests.RSA4c3_CallbackErrorStaysConnected`, `ConnectionAuthTests.RSA4d_Callback403ReauthCausesFailed`, `AuthCallbackErrorsTests.RSA4d_Callback403ReauthFailed`, env-gated `[DeviationFact]`.
- **Status:** open bug.

### RSA4e — authCallback failure on a REST request is 80019, not 40170

- **Spec point:** RSA4e (test `realtime/unit/RSA4e/rest-callback-error-40170-0`).
- **What the features spec says:** `(RSA4e)` "If in the course of a REST request ... an attempt to authenticate using `authUrl` or `authCallback` fails due to a timeout, network error, a token in an invalid format (per RSA4f), or some other auth error condition other than an explicit `ErrorInfo` from Ably, the request should result in an error with `code` 40170, `statusCode` 401, and a suitable error message".
- **What the SDK does:** "Expected error.Code to be 40170, but found 80019".
- **Root cause:** `AblyAuth.RequestTokenAsync` wraps every authCallback exception as `ErrorCodes.ClientAuthProviderRequestFailed` (80019, the realtime RSA4c1 code) with the original as `Cause`, for REST and realtime alike (`rest-mapping.md` section 7).
- **Test impact:** `AuthCallbackErrorsTests.RSA4e_RestCallbackError40170`, env-gated.
- **Status:** open bug.

### RSA4f / RSA4c1 — an authCallback result of an invalid type gives statusCode 400, not 401

- **Spec point:** RSA4f, RSA4c, RSA4c1 (test `realtime/unit/RSA4f/callback-invalid-type-format-0`).
- **What the features spec says:** `(RSA4f)` "the object passed by `authCallback` is neither a `String`, `JsonObject`, `TokenRequest` object, nor `TokenDetails` object" is an invalid format; `(RSA4c)` makes an invalid-format token an auth failure, and `(RSA4c1)` "An `ErrorInfo` with `code` `80019`, `statusCode` 401 ...".
- **What the SDK does:** code 80019, but "Expected ... StatusCode to be 401, but found 400".
- **Root cause:** `AblyAuth.RequestTokenAsync` throws `new AblyException("AuthCallback returned an unsupported type ...", ErrorCodes.ClientAuthProviderRequestFailed, HttpStatusCode.BadRequest)` with `shouldCatch = false`, so it bypasses the 401 wrapper.
- **Test impact:** `AuthCallbackErrorsTests.RSA4f_CallbackInvalidTypeFormat`, env-gated.
- **Status:** open bug.

### RSA4f — the 128KiB token-size limit is not enforced

- **Spec point:** RSA4f, RSA4c (test `realtime/unit/RSA4f/callback-oversized-token-format-1`).
- **What the features spec says:** `(RSA4f)` "... the token string or the JSON stringified `JsonObject`, `TokenRequest` or `TokenDetails` is greater than 128KiB" implies an invalid format, which `(RSA4c)`/`(RSA4c1)` turn into an 80019 auth failure.
- **What the SDK does:** the 131073-character token is accepted and used for the connection; the connection ends DISCONNECTED with `ErrorReason` 80000 (a transport failure), not 80019: "Expected client.Connection.ErrorReason.Code to be 80019, but found 80000".
- **Root cause:** no size check anywhere in `src/Ably.PubSub.Shared` (no 128KiB/131072 constant; `AblyAuth.RequestTokenAsync` returns the callback's `TokenDetails` as is).
- **Test impact:** `AuthCallbackErrorsTests.RSA4f_CallbackOversizedTokenFormat`, env-gated.
- **Status:** open bug.

### RTB1 — first DISCONNECTED after a dropped connection reports retryIn 0

- **Spec point:** RTB1, RTB1a, RTB1b, RTN14d, TA2 (test `realtime/unit/RTB1/disconnected-retry-delay-0`, `uts/realtime/unit/connection/backoff_jitter_test.md`).
- **What the features spec says:** `(RTB1)` "For connections in the `DISCONNECTED` state ... the time until retry is calculated as the product of the initial retry timeout (for `DISCONNECTED` connections this is the `disconnectedRetryTimeout`...), the backoff coefficient as defined by `RTB1a`, and the jitter coefficient as defined by `RTB1b`." `(RTN14d)` "The `retryIn` attribute of the `ConnectionStateChange` object will contain the time in milliseconds until the next connection attempt. `retryIn` should be calculated as described in `RTB1`."
- **What the SDK does:** the first recorded DISCONNECTED `RetryIn` is 0 ms (`Expected delays[0] >= 1600, found 0.0`), not `disconnectedRetryTimeout * 1 * jitter` (1600-2000 ms). The SDK retries a dropped connection at once and, per `realtime-mapping.md` §13, also retries failed attempts at once up to `1 + fallback hosts` times (`CheckInstantRetryFlag`, citing RTN17j) before the RTB1 backoff applies. Only the first assertion was observed; the later delays may also be off by one step.
- **Root cause:** `ConnectionDisconnectedState` / `CheckInstantRetryFlag` (instant retry before RTB1 backoff).
- **Test impact:** `BackoffJitterTests.RTB1_DisconnectedRetryDelay`, env-gated `[DeviationFact("RTB1")]`.
- **Status:** open bug (needs confirming against RTN15a/RTN15h3, which ask for an immediate reconnect after a dropped CONNECTED transport: if that immediate attempt is meant to emit no DISCONNECTED, the SDK's extra DISCONNECTED with retryIn 0 is the deviation).

### RTL4h — attach while DETACHING is not deferred

- **Spec point:** RTL4h (test `realtime/unit/RTL4h/attach-while-detaching-1`).
- **What the features spec says:** `(RTL4h)` "If the channel is in a pending state `DETACHING` or `ATTACHING`, do the attach operation after the completion of the pending request".
- **What the SDK does:** `AttachAsync()` while DETACHING moves the channel to ATTACHING at once and sends ATTACH, failing the pending detach (`detachResult.IsSuccess` false: "Channel transitioned to Attaching").
- **Root cause:** `RealtimeChannel.Attach` has `/* TODO: Handle RTL4h where Attach operation should be queued if another attach is in progress. */`; `HandleStateChange` fails `DetachedAwaiter` on ATTACHING.
- **Test impact:** `ChannelAttachTests.RTL4h_AttachWhileDetaching`, env-gated `[DeviationFact("RTL4h")]`.
- **Status:** open bug.

### RTL3b — channels pass through DETACHING when the connection closes

- **Spec point:** RTL3b (tests `realtime/unit/RTL3b/closed-attached-to-detached-0`, `realtime/unit/RTL3b/closed-attaching-to-detached-1`).
- **What the features spec says:** `(RTL3b)` "If the connection state enters the `CLOSED` state, then an `ATTACHING` or `ATTACHED` channel state will transition to `DETACHED`".
- **What the SDK does:** the channel goes ATTACHED/ATTACHING -> DETACHING -> DETACHED, so the DETACHED state change has `previous` DETACHING (`Expected Attached, found Detaching`; `Expected Attaching, found Detaching`).
- **Root cause:** `RealtimeChannel.DetachForConnectionGoingAway` calls `Detach(...)`, which always sets DETACHING before the RTL5l immediate DETACHED.
- **Test impact:** both `ChannelConnectionStateTests.RTL3b_*` tests, env-gated `[DeviationFact("RTL3b")]`.
- **Status:** open bug.

### RTL5j — detach from SUSPENDED sends DETACH instead of going straight to DETACHED

- **Spec point:** RTL5j (test `realtime/unit/RTL5j/detach-suspended-to-detached-0`, `uts/realtime/unit/channels/channel_detach.md`).
- **What the features spec says:** `(RTL5j)` "If the channel state is `SUSPENDED`, the `detach` request transitions the channel immediately to the `DETACHED` state".
- **What the SDK does:** after `AdvanceTime(150)` the channel is SUSPENDED and the attach failed (50000) as expected; then `DetachAsync()` never completes: 3 s later the channel is DETACHING and one DETACH frame was sent. The SDK sends DETACH from SUSPENDED (the connection is CONNECTED) instead of transitioning to DETACHED; the mock never answers and the detach timeout runs on the fake clock, which the test does not advance, so the unbounded await hung the whole run.
- **Root cause:** `RealtimeChannel.Detach` has no SUSPENDED case: it sets DETACHING and sends DETACH whenever the connection is CONNECTED.
- **Test impact:** `ChannelDetachTests.RTL5j_DetachSuspendedToDetached`, env-gated `[DeviationFact("RTL5j")]`; the detach await is bounded (`UtsWait.WithTimeout`, 5 s) so a run with `RUN_DEVIATIONS=1` fails instead of hanging.
- **Status:** open bug.

### RTL5i — detach while ATTACHING is not deferred

- **Spec point:** RTL5i (test `realtime/unit/RTL5i/detach-while-attaching-1`).
- **What the features spec says:** `(RTL5i)` "If the channel is in a pending state `DETACHING` or `ATTACHING`, do the detach operation after the completion of the pending request".
- **What the SDK does:** `DetachAsync()` while ATTACHING sets DETACHING and sends DETACH at once; the detach then fails (`detachResult.IsSuccess` false: "Channel transitioned to Attaching") when the ATTACHED for the pending attach arrives.
- **Root cause:** `RealtimeChannel.Detach` only no-ops for INITIALIZED/DETACHING/DETACHED and otherwise starts the detach immediately; `HandleStateChange` fails `DetachedAwaiter` on ATTACHING.
- **Test impact:** `ChannelDetachTests.RTL5i_DetachWhileAttaching`, env-gated `[DeviationFact("RTL5i")]`.
- **Status:** open bug.

### RTL5k — ATTACHED received while DETACHING or DETACHED re-attaches the channel

- **Spec point:** RTL5k (tests `realtime/unit/RTL5k/attached-while-detaching-0`, `realtime/unit/RTL5k/attached-while-detached-1`).
- **What the features spec says:** `(RTL5k)` "If the channel receives an `ATTACHED` message while in the `DETACHING` or `DETACHED` state, it should send a new `DETACH` message and remain in (or transition to) the `DETACHING` state".
- **What the SDK does:** no second DETACH is sent. While DETACHING the ATTACHED moves the channel to ATTACHED and the detach never completes (`timed out after 10000 ms waiting for DetachAsync`); while DETACHED the DETACH count stays 1 (`Expected 2, found 1`).
- **Root cause:** `ChannelMessageProcessor` (ATTACHED case) calls `SetChannelState(ChannelState.Attached, ...)` for every non-ATTACHED state; there is no DETACHING/DETACHED branch.
- **Test impact:** both `ChannelDetachTests.RTL5k_*` tests, env-gated `[DeviationFact("RTL5k")]`.
- **Status:** open bug.

### RTL14 — a channel retry timer still fires after the channel entered FAILED

- **Spec point:** RTL14, RTL13b (test `realtime/unit/RTL14/cancels-pending-timers-4`, `uts/realtime/unit/channels/channel_error.md`).
- **What the features spec says:** `(RTL14)` "If an `ERROR ProtocolMessage` is received for this channel ... then the channel should immediately transition to the FAILED state"; `(RTL13b)` describes the automatic re-attach only for a channel in the SUSPENDED state ("An attempt to re-attach the channel automatically will then be made after the period defined by RTB1").
- **What the SDK does:** after the channel ERROR moves the SUSPENDED channel to FAILED, advancing time past `channelRetryTimeout` re-attaches it (`Expected Failed, found Attaching`).
- **Root cause:** `RealtimeChannel.ReattachAfterTimeout` schedules `Reattach` after `TimerScheduler.Delay` and only checks the connection state (RTL13c) when it fires, not the channel state; entering FAILED doesn't cancel it.
- **Test impact:** `ChannelErrorTests.RTL14_CancelsPendingTimers`, env-gated `[DeviationFact("RTL14")]`.
- **Status:** open bug.

### RTL15c — attachSerial is overwritten by an ATTACHED with the RESUMED flag

- **Spec point:** RTL15c (test `realtime/unit/RTL15c/attach-serial-not-updated-resumed-2`, `uts/realtime/unit/channels/channel_properties.md`).
- **What the features spec says:** `(RTL15c)` "`attachSerial` ... is updated with the `channelSerial` from each `ATTACHED` `ProtocolMessage` received from Ably with a matching `channel` attribute whose RTL2f `resumed` attribute is `false`."
- **What the SDK does:** a RESUMED ATTACHED replaces it (`Expected "initial-serial", but "resumed-serial"`).
- **Root cause:** `ChannelMessageProcessor` (ATTACHED case) sets `channel.Properties.AttachSerial = protocolMessage.ChannelSerial` unconditionally.
- **Test impact:** `ChannelPropertiesTests.RTL15c_AttachSerialNotUpdatedResumed`, env-gated `[DeviationFact("RTL15c")]`.
- **Status:** open bug.

### RTL15b2 — channelSerial is lost when a server DETACHED leads to SUSPENDED

- **Spec point:** RTL15b2, RTL13a (test `realtime/unit/RTL15b2/serial-retained-suspended-1`).
- **What the features spec says:** `(RTL15b2)` "If the channel enters the `DETACHED` or `FAILED` state, it must clear its `channelSerial`. (Unlike previous spec versions, it must not clear it when entering the `SUSPENDED` state)." `(RTL13a)` "If the channel is in the `ATTACHED` or `SUSPENDED` states, an attempt to reattach the channel should be made immediately by sending a new `ATTACH` message and the channel should transition to the `ATTACHING` state".
- **What the SDK does:** after the server DETACHED, reattach timeout and SUSPENDED, `ChannelSerial` is null (`Expected "serial-001", but found <null>`). The SDK enters DETACHED on the server DETACHED before re-attaching (see the RTL13a evidence in `ChannelServerInitiatedDetachTests`), and entering DETACHED clears the serial.
- **Root cause:** `ChannelMessageProcessor` (DETACHED case) calls `SetChannelState(ChannelState.Detached, ...)` for an ATTACHED channel; `RealtimeChannel.HandleStateChange` clears `Properties.ChannelSerial` on DETACHED.
- **Test impact:** `ChannelPropertiesTests.RTL15b2_SerialRetainedSuspended`, env-gated `[DeviationFact("RTL15b2")]`.
- **Status:** open bug.

### RTN19b — pending ATTACH/DETACH callbacks fail as soon as the connection goes DISCONNECTED

- **Spec point:** RTN19b, RTL3e, RTL4d, RTL5e (tests `realtime/unit/RTN19b/attach-resent-on-reconnect-0`, `realtime/unit/RTN19b/detach-resent-on-reconnect-1`, `uts/realtime/unit/channels/channel_publish.md`).
- **What the features spec says:** `(RTN19b)` "If there are any pending channels i.e. in the `ATTACHING` or `DETACHING` state, the respective `ATTACH` or `DETACH` message should be resent to Ably"; `(RTL3e)` "If the connection state enters the `DISCONNECTED` state, it will have no effect on the channel states."; `(RTL4d)` "A callback ... is called when the channel next moves to one of `ATTACHED`, `DETACHED`, `SUSPENDED`, or `FAILED` states."
- **What the SDK does:** the held `AttachAsync()` / `DetachAsync()` result fails with "Connection is Disconnected" the moment the transport drops, although the channel stays ATTACHING / DETACHING and is re-attached / re-detached after the reconnect (`Expected attachResult.IsSuccess to be true ... (Connection is Disconnected)`; same for detach).
- **Root cause:** `RealtimeChannel` connection-state handler, `case ConnectionState.Disconnected:` calls `AttachedAwaiter.Fail(new ErrorInfo("Connection is Disconnected"))` and `DetachedAwaiter.Fail(...)`.
- **Test impact:** `ChannelPublishTests.RTN19b_AttachResentOnReconnect`, `ChannelPublishTests.RTN19b_DetachResentOnReconnect`: env-gated `[DeviationFact("RTN19b")]`, spec-correct assertions kept.
- **Status:** open bug.

### RTL6i3 — a publish with both name and data null sends no message at all

- **Spec point:** RTL6i3 (test `realtime/unit/RTL6i3/null-fields-json-0`).
- **What the features spec says:** `(RTL6i3)` "Allows `name` and or `data` to be `null`. If any of the values are `null`, then key is not sent to Ably i.e. a payload with a `null` value for `data` would be sent as follows `{ "name": "click" }`".
- **What the SDK does:** `PublishAsync(null, null)` produces a MESSAGE ProtocolMessage with no `messages` array, so `frames[2].messages[0]` is absent (NullReferenceException in the test). The name-only and data-only publishes are correct.
- **Root cause:** `ProtocolMessage.OnSerializing` filters messages with `Message.IsEmpty` (equal to the default instance) and drops the array when it becomes empty.
- **Test impact:** `ChannelPublishTests.RTL6i3_NullFieldsJson`, env-gated `[DeviationFact("RTL6i3")]`.
- **Status:** open bug.

### RTL13a / RTL13b — a server-initiated DETACHED passes through an emitted DETACHED state

- **Spec point:** RTL13a, RTL13b, RTL2d (tests `realtime/unit/RTL13a/attached-reattach-triggered-0`, `realtime/unit/RTL13b/attaching-detached-to-suspended-1`).
- **What the features spec says:** `(RTL13a)` "If the channel is in the `ATTACHED` or `SUSPENDED` states, an attempt to reattach the channel should be made immediately by sending a new `ATTACH` message and the channel should transition to the `ATTACHING` state with the error emitted in the `ChannelStateChange` event."; `(RTL13b)` "... if the channel was already in the `ATTACHING` state, the channel will transition to the `SUSPENDED` state and the error will be emitted in the `ChannelStateChange` event."
- **What the SDK does:** it first sets the channel to DETACHED and then, from inside that transition, to ATTACHING (from ATTACHED) or SUSPENDED (from ATTACHING). The ATTACHING / SUSPENDED change is emitted with `Previous = Detached`, and a DETACHED change is emitted as well (`Expected changes[0].Previous to be Attached, but found Detached`; `... to be Attaching, but found Detached`).
- **Root cause:** `ChannelMessageProcessor` handles `Detached` with `channel.SetChannelState(ChannelState.Detached, protocolMessage)`; `RealtimeChannel.HandleStateChange` sets `State = Detached` and then calls `Reattach` / `SetChannelState(Suspended)` (RTL13a/RTL13b) before the outer DETACHED change is emitted.
- **Test impact:** `ChannelServerInitiatedDetachTests.RTL13a_AttachedReattachTriggered` and `ChannelServerInitiatedDetachTests.RTL13b_AttachingDetachedToSuspended`, env-gated `[DeviationFact]` with the spec-correct assertions.
- **Status:** open bug.

### RTS4e — release() of an ATTACHED channel detaches it instead of raising 90011

- **Spec point:** RTS4e, RTS4b (test `realtime/unit/RTS4e/release-attached-fails-0`, `uts/realtime/unit/channels/channels_collection.md`).
- **What the features spec says:** `(RTS4e)` "If the channel's state is anything else, `release()` must raise an `ErrorInfo` with `code` 90011 and `statusCode` 400, and take no other action."; `(RTS4b)` allows an SDK that implemented RTS4a to keep it until its next major version, "but must log a deprecation warning each time `release()` is called for a channel whose state is not `INITIALIZED`, `DETACHED` or `FAILED`".
- **What the SDK does:** `Channels.Release(name)` on an ATTACHED channel does not throw; it detaches the channel and removes it in the detach callback (the old RTS4a behaviour) and logs no deprecation warning.
- **Root cause:** `RealtimeChannels.Release` calls `channel.Detach(DetachedCallback)` for any state other than INITIALIZED/DETACHED/FAILED.
- **Test impact:** `ChannelsCollectionTests.RTS4e_ReleaseAttachedFails`, env-gated `[DeviationFact("RTS4e")]`.
- **Status:** open bug (RTS4e, or at minimum the RTS4b deprecation warning).

### RTN17g — with an empty fallback set, a failed connection attempt is retried at once against the same host

- **Spec point:** RTN17g, RTN14d (test `realtime/unit/RTN17g/empty-fallback-set-error-0`).
- **What the features spec says:** `(RTN17g)` "When the set of `fallback domains` is empty, failing HTTP requests that would have qualified for a retry against a fallback host (see RSC15d) will instead result in an error immediately." `(RTN14d)` "If a connection attempt fails for any recoverable reason ... the `Connection#state` will transition to `DISCONNECTED` ... and new connection attempts will periodically be made ... `retryIn` should be calculated as described in `RTB1`."
- **What the SDK does:** with `Endpoint = "custom.example.com"` (REC2c2, no fallbacks) and every attempt refused, it makes a second attempt to `custom.example.com` within 2 s (`Expected connectionAttempts.Count to be 1, but found 2`), instead of waiting the RTB1 delay (`disconnectedRetryTimeout`, 15 s).
- **Root cause:** `RealtimeWorkflow` `SetDisconnectedStateCommand`, `CheckInstantRetryFlag`: any DISCONNECTED carrying an exception earns an instant retry, bounded by `1 + FallbackHosts.Count`, so with no fallbacks the primary host is retried once immediately (also noted in `realtime-mapping.md` section 13, RTN14d retry timing).
- **Test impact:** `FallbackHostsTests.RTN17g_EmptyFallbackSetError`, env-gated `[DeviationFact("RTN17g")]`.
- **Status:** open bug.

### TM2a — a message in a ProtocolMessage without an id gets the id ":<index>"

- **Spec point:** TM2a (test `realtime/unit/TM2a/no-id-without-protocol-id-2`, `uts/realtime/unit/channels/message_field_population.md`).
- **What the features spec says:** `(TM2a)` "For messages received over Realtime, if the message does not contain an `id`, it should be set to `protocolMsgId:index`, where `protocolMsgId` is the id of the `ProtocolMessage` encapsulating it" (the derivation presupposes a ProtocolMessage id).
- **What the SDK does:** with no ProtocolMessage `id` the message gets `Id = ":0"` (`Expected receivedMessages[0].Id to be <null>, but found ":0"`).
- **Root cause:** `MessageHandler` `SetMessageIdConnectionIdAndTimestamp` sets `message.Id = $"{protocolMessage.Id}:{i}"` without checking that `protocolMessage.Id` is set.
- **Test impact:** `MessageFieldPopulationTests.TM2a_NoIdWithoutProtocolId`, env-gated `[DeviationFact("TM2a")]`.
- **Status:** open bug.

### TM2f — a message's own timestamp is overwritten by the ProtocolMessage timestamp

- **Spec point:** TM2f (test `realtime/unit/TM2f/existing-timestamp-kept-1`).
- **What the features spec says:** `(TM2f)` "If a message received from Ably over a realtime transport does not contain a `timestamp`, the SDK must set it to the `timestamp` of the encapsulating `ProtocolMessage`".
- **What the SDK does:** a message carrying `timestamp: 1600000000000` is delivered with the ProtocolMessage's 1700000000000 (`Expected ... 2020-09-13 12:26:40, but 2023-11-14 22:13:20 does not`).
- **Root cause:** `MessageHandler` protocol-message decoding (`foreach (var message in protocolMessage.Messages) message.Timestamp = protocolMessage.Timestamp;`) overwrites unconditionally, before the conditional TM2f defaulting in `SetMessageIdConnectionIdAndTimestamp`.
- **Test impact:** `MessageFieldPopulationTests.TM2f_ExistingTimestampKept`, env-gated `[DeviationFact("TM2f")]`.
- **Status:** open bug (the same loop also overwrites presence message timestamps).

### RTP2h / RTP2h2a — PresenceMap emits a LEAVE for an unknown member and deletes instead of storing ABSENT during a SYNC

- **Spec point:** RTP2h, RTP2h1, RTP2h2a, RTP2h2b (tests `realtime/unit/RTP2h1/leave-nonexistent-returns-null-*`, `realtime/unit/RTP2h2a/leave-during-sync-stores-absent-*`, `realtime/unit/RTP2/values-excludes-absent-2` in `presence_map.md`; `realtime/unit/RTP2h2a/leave-during-sync-absent-cleanup-0` in `presence_sync.md`).
- **What the features spec says:** `(RTP2h)` "When a presence message with an action of `LEAVE` arrives, if and only if there is a member with a matching `memberKey` currently in the presence map ...". `(RTP2h2a)` "If a `SYNC` is in progress: The incoming message must be stored in the presence map with the action set to `ABSENT`". `(RTP2h2b)` "When the `SYNC` completes, then all `ABSENT` members in the presence map must be deleted. (No leave events should be emitted other than those required by `RTP19`)".
- **What the SDK does:** `PresenceMap.Remove` always removes the entry and returns true (emit) unless the stored entry is newer or already ABSENT. A LEAVE for a member that is not in the map is therefore emitted (`Expected emitted to be false, but found True`), and a LEAVE during a SYNC deletes the member and is emitted at once instead of being stored as ABSENT (`Expected Get(map,"c2","bob") not to be <null>`; `Expected leaveResult to be false, but found True`). `Presence.OnPresence` broadcasts whenever `Remove` returns true, so the extra LEAVE events reach subscribers (the observable is public, not only internal shape).
- **Root cause:** `src/Ably.PubSub.Shared/Realtime/PresenceMap.cs`, `Remove` (no `SyncInProgress` branch storing an ABSENT copy; returns true when nothing was removed). `EndSync` already deletes ABSENT entries (RTP2h2b), but nothing ever stores one.
- **Test impact:** `PresenceMapTests.RTP2h1_LeaveNonexistentReturnsNull`, `PresenceMapTests.RTP2h2a_LeaveDuringSyncStoresAbsent`, `PresenceMapTests.RTP2_ValuesExcludesAbsent` and `PresenceSyncTests.RTP2h2a_LeaveDuringSyncAbsentCleanup`, env-gated `[DeviationFact]` with the spec-correct assertions (S-1/S-2 shape: `bool` for emit/null).
- **Status:** open bug.

### RTL2 / RTL4d / RTL5e / RTC8a1 — attach(), detach() and authorize() complete before the state change is emitted

- **Spec point:** RTL2a, RTL4d, RTL5e, RTC8a1 (tests `realtime/unit/RTL2a/state-change-events-emitted-0`, `realtime/unit/RTL2/filtered-event-subscription-0`, `realtime/unit/RTL2d/resumed-flag-propagated-2`, `realtime/unit/RTL5/detach-state-change-events-0`, `realtime/unit/RTL3a/other-states-unaffected-2`, `realtime/unit/RTL3d/init-detached-not-reattached-2`, `realtime/unit/RTL14/attached-to-failed-0`, `realtime/unit/RTC8a1/successful-reauth-update-event-0`).
- **What the features spec says:** features.md does not say whether the state-change event comes before or after the operation completes. The nearest points: `(RTL4d)` "A callback ... can be provided that is called when the channel next moves to one of `ATTACHED`, `DETACHED`, `SUSPENDED`, or `FAILED` states"; `(RTL5e)` "a callback can be provided that is called when the channel is detached successfully"; `(RTL2a)` "It emits a `ChannelState` `ChannelEvent` for every channel state change"; `(RTC8a1)` "The `Connection` should emit an `UPDATE` event per `RTN24`".
- **What the SDK does:** `RealtimeChannel.SetChannelState` runs `InternalStateChanged` (`Realtime/RealtimeChannel.cs:771`) before `RealtimeClient.NotifyExternalClients(StateChanged + Emit)` (`:774-786`). `InternalStateChanged`'s only subscriber is `ChannelAwaiter` (`Realtime/ChannelAwaiter.cs:152`), which completes `AttachAsync`/`DetachAsync` through `TaskWrapper` (`Transport/TaskWrapper.cs:10,18`, a TCS without `RunContinuationsAsynchronously`). `Connection.NotifyUpdate` likewise runs its internal handlers (`Realtime/Connection.cs:306`) before the public emit (`:308-320`). One of them is `ConnectionChangeAwaiter` (`Utils/ConnectionChangeAwaiter.cs:35`), which completes `AuthorizeAsync` (the other is RTL3d1 channel propagation, which must stay first). With no `CustomContext` and no synchronization context, as in xunit runs with parallelization disabled, the caller's continuation runs inline on the SDK workflow thread before the emit. So the test reads its recorded events too early ("Expected changes.Count >= 2, found 1", "Expected updateEvents.Count 1, found 0"), or a listener it registers afterwards still receives the late event. In parallel xunit runs `MaxConcurrencySyncContext` posts the continuation, and the tests usually pass.
- **Root cause (traced):** the UTS specs assume that once `AWAIT channel.attach()` / `detach()` / `authorize()` returns, the matching event has reached listeners registered earlier, and has not reached listeners registered afterwards. ably-js and ably-java both behave that way. In ably-java, `attach` registers a `ChannelStateCompletionListener` on the public emitter (`ChannelBase.java:262`), so it runs inside `setState`'s `emit` (`:174-177`) after listeners registered earlier, and `ConnectionWaiter` is a public `connection.on` listener (`ConnectionManager.java:532`). In ably-js, the `RealtimeChannel` emit override calls `super.emit` before `internalStateChanges.emit`. Here, a stack captured at the resume point of `await AttachAsync()` in a serial run: `RealtimeWorkflow.Consume` -> `ChannelMessageProcessor.MessageReceived:86` -> `RealtimeChannel.SetChannelState:771` -> `ChannelAwaiter.ChannelOnChannelStateChanged:172` -> `TaskWrapper.Callback:18` -> `RealtimeChannel.AttachAsync:371` -> the test. The public listener fired on the same thread once the test yielded. Moving `InternalStateChanged` after the emit fixed the 7 channel tests (5/5 serial each), but widened a race in `ChannelsSpecs.ReleaseAll_ShouldRemoveChannelWhenDetached`: the release callback now ran after the public DETACHED, so it was reverted. Full analysis and fix design: CHANNEL_AWAITER_ISSUE.md (repo root).
- **Test impact:** `ChannelStateEventsTests.RTL2a_StateChangeEventsEmitted`, `ChannelStateEventsTests.RTL2_FilteredEventSubscription`, `ChannelStateEventsTests.RTL2d_ResumedFlagPropagated`, `ChannelDetachTests.RTL5_DetachStateChangeEvents`, `ChannelConnectionStateTests.RTL3a_OtherStatesUnaffected`, `ChannelConnectionStateTests.RTL3d_InitDetachedNotReattached`, `ChannelErrorTests.RTL14_AttachedToFailed`, `RealtimeAuthorizeTests.RTC8a1_SuccessfulReauthUpdateEvent`, each `[DeviationFact("<its spec point>")]`. They fail when the unit tier runs serially and pass in the default parallel run.
- **Status:** open bug.

### RTC8b / RTC8b1 — authorize() while CONNECTING neither halts the attempt nor waits for the outcome

- **Spec point:** RTC8b, RTC8b1 (tests `realtime/unit/RTC8b/authorize-connecting-halts-attempt-0`, `realtime/unit/RTC8b1/authorize-connecting-fails-on-failed-0`).
- **What the features spec says:** `(RTC8b)` "If the connection is in the `CONNECTING` state when `auth#authorize` is called, all current connection attempts should be halted, and after obtaining a new token the library should immediately initiate a connection attempt using the new token"; `(RTC8b1)` "The `authorize` call should be indicated as completed with the new token once the connection has moved to the `CONNECTED` state, or with an error if the connection instead moves to the `FAILED`, `SUSPENDED`, or `CLOSED` states".
- **What the SDK does:** `AuthorizeAsync` returns as soon as the token is obtained: right after it the connection is still CONNECTING ("Expected ConnectionState.Connected, but found ConnectionState.Connecting"), and when the new attempt then fails with a fatal ERROR the call has already succeeded ("Expected a <AblyException> to be thrown, but no exception was thrown").
- **Root cause:** `ConnectionManager.OnAuthUpdated(token, wait: true)` starts with `if (Connection.State != ConnectionState.Connected) { ExecuteCommand(Connect()); return; }`: it neither halts the pending attempt nor waits for CONNECTED/FAILED.
- **Test impact:** `RealtimeAuthorizeTests.RTC8b_AuthorizeConnectingHaltsAttempt`, `RealtimeAuthorizeTests.RTC8b1_AuthorizeConnectingFailsOnFailed`, env-gated; also `RealtimeAuthorizeTests.RTC8c_AuthorizeDisconnectedInitiatesConnection` (`[DeviationFact("RTC8b1")]`): `authorize()` from INITIALIZED returns the token while the connection is still CONNECTING (`Expected Connected, but found Connecting`), against `(RTC8b1)` "The `authorize` call should be indicated as completed with the new token once the connection has moved to the `CONNECTED` state". Likewise `RealtimeAuthorizeTests.RTC8c_AuthorizeClosedInitiatesConnection` (`[DeviationFact("RTC8b1")]`, test `realtime/unit/RTC8c/authorize-closed-initiates-connection-2`): `authorize()` from CLOSED returns the token while the connection is still CONNECTING (`Expected ConnectionState.Connected, but found ConnectionState.Connecting`); it passed only when the mock's CONNECTED won the race against the return (3/5 parallel, 4/5 serial in `.uts-work/reports/race_experiment.md`), the same `OnAuthUpdated` early return (`ConnectionManager.cs:160-164`).
- **Status:** open bug.

### RTC8a2 / RTC8c — authorize() after a fatal connection ERROR: the connection does not stay FAILED

- **Spec point:** RTC8a2, RTC8c (tests `realtime/unit/RTC8a2/failed-reauth-connection-failed-0`, `realtime/unit/RTC8c/authorize-failed-initiates-connection-1`).
- **What the features spec says:** `(RTC8a2)` "If the authentication token change fails, then Ably will send an `ERROR` `ProtocolMessage` triggering the connection to transition to the `FAILED` state"; `(RTC8c)` "If the connection is in the ... `FAILED` ... state when `auth#authorize` is called, after obtaining a token the library should move to the `CONNECTING` state and initiate a connection attempt using the new token, and `RTC8b1` applies."
- **What the SDK does:** RTC8a2: after the AUTH is answered with a connection-level ERROR (40012) and a close, `AuthorizeAsync` completes without an error. RTC8c: once FAILED has been reached, `AuthorizeAsync` does not complete within 10 s ("timed out after 10000 ms waiting for AuthorizeAsync").
- **Root cause (probable, inferred from the outcomes, not traced in a run):** the RTN27h race already recorded (see "RTN15h1 — non-renewable token error: FAILED does not hold"): the fatal ERROR's handling runs behind the close's retry, so the connection leaves FAILED on its own and reconnects (the mock answers CONNECTED). RTC8a2's `OnAuthUpdated` wait then sees CONNECTED and returns; in RTC8c the client is CONNECTED again when `authorize()` is called, so `OnAuthUpdated` sends AUTH and waits up to `realtimeRequestTimeout` (10 s) for an answer the mock never sends.
- **Test impact:** `RealtimeAuthorizeTests.RTC8a2_FailedReauthConnectionFailed`, `RealtimeAuthorizeTests.RTC8c_AuthorizeFailedInitiatesConnection`, env-gated; the RTC8a2/RTC8b1 `AuthorizeAsync` throws are bounded by a 15 s `UtsWait.WithTimeout` (infra-driving).
- **Status:** open bug (confirm the race with `RUN_DEVIATIONS=1` and state recording).

### RTL33b / RTP11e — presence get() on a DETACHED channel throws instead of attaching

- **Spec point:** RTP11e, RTL33b, RTP5a (test `realtime/unit/RTP5a/detached-clears-presence-maps-0`).
- **What the features spec says:** `(RTP11e)` "If the channel is in any state other than `SUSPENDED` (which is handled by RTP11d), perform the *ensure-active-channel* procedure (RTL33) on the underlying `RealtimeChannel`." `(RTL33b)` "If the channel is in the `INITIALIZED`, `DETACHED`, `DETACHING`, or `ATTACHING` state, perform an implicit attach per RTL4 and wait for it to complete".
- **What the SDK does:** `channel.Presence.GetAsync(waitForSync: false)` on a DETACHED channel throws `AblyException` 90001 "channel operation failed. Invalid channel state (Detached)".
- **Root cause:** `src/Ably.PubSub.Shared/Realtime/Presence.cs`, `GetAsync(GetParams)`: the "RTP11b" guard throws for both FAILED and DETACHED (RTP11b was replaced by RTP11e; only FAILED should throw, per RTL33c), and only INITIALIZED attaches.
- **Test impact:** `RealtimePresenceChannelStateTests.RTP5a_DetachedClearsPresenceMaps`, env-gated. Note for the spec review: under RTL33b the `get` re-attaches and this mock answers with a SYNC containing alice, so the spec's `members_after.length == 0` may also need revisiting once the SDK is fixed.
- **Status:** open bug.

### RTP8c / RTP9d / RTP10c — enter, update and leave send the client's clientId

- **Spec point:** RTP8c, RTP9d, RTP10c (tests `realtime/unit/RTP8a/enter-sends-presence-enter-0`, `realtime/unit/RTP9a/update-sends-presence-update-0`, `realtime/unit/RTP10a/leave-sends-presence-leave-0`).
- **What the features spec says:** `(RTP8c)` "A `PRESENCE ProtocolMessage` with a `PresenceMessage` with the action `ENTER` is sent to the Ably service. The `clientId` attribute of the `PresenceMessage` must not be present." RTP9d and RTP10c say the same for `UPDATE` and `LEAVE`.
- **What the SDK does:** the outgoing `PresenceMessage` carries `clientId: "my-client"` (`Expected capturedPresence[0].Presence[0].ClientId to be <null>, but found "my-client"`).
- **Root cause:** `src/Ably.PubSub.Shared/Realtime/Presence.cs`: `EnterAsync`, `UpdateAsync` and `LeaveAsync` delegate to the `…ClientAsync` forms with `_clientId`, so the message is built with the client's clientId.
- **Test impact:** `RealtimePresenceEnterTests.RTP8a_EnterSendsPresenceEnter`, `RTP9a_UpdateSendsPresenceUpdate` and `RTP10a_LeaveSendsPresenceLeave`, env-gated.
- **Status:** open bug.

### RTP8j — enter() with a null clientId does not fail

- **Spec point:** RTP8j (test `realtime/unit/RTP8j/enter-null-clientid-errors-0`).
- **What the features spec says:** `(RTP8j)` "If the connection is in the `CONNECTED` state, and the value of `RealtimeClient#clientId` is ... `null` -- indicating that the client is anonymous ... then the `enter` request results in an error immediately."
- **What the SDK does:** `channel.Presence.EnterAsync()` on a CONNECTED client with no clientId sends the PRESENCE and succeeds (`Expected enterResult.IsFailure to be true, but found False`).
- **Root cause:** `Presence.EnterAsync` / `UpdatePresence` have no RTP8j check on the client's clientId (`AblyAuth.ValidateClientIds` only rejects mismatches, and treats a null library clientId as "anything goes").
- **Test impact:** `RealtimePresenceEnterTests.RTP8j_EnterNullClientidErrors`, env-gated.
- **Status:** open bug.

### RSA4a2 — non-renewable token error: FAILED is emitted twice, and the shared 40171 ErrorInfo is mutated

- **Spec point:** RSA4a2 (test `realtime/unit/RSA4a2/token-error-non-renewable-failed-0`).
- **What the features spec says:** `(RSA4a2)` "if the server responds with a token error ... then the client library should indicate an error with error code `40171`, not retry the request and, in the case of the realtime library, transition the connection to the `FAILED` state".
- **What the SDK does:** two FAILED state changes (CONNECTING -> FAILED, then DISCONNECTED -> FAILED): "Expected failedChanges ... to contain 1 item(s), but found 2." The connection leaves FAILED (the RTN27h race, see "RTN15h1 — non-renewable token error: FAILED does not hold") and fails again. The reason's message also reads "... Original: Token error (40140) Original: Token error (40140) Original: Token error (40140) Original: Token expired (40142) Original: Token expired (40142)", errors from other tests' clients.
- **Root cause:** the race as recorded for RTN15h1; and `RealtimeWorkflow` `TransformIfTokenErrorAndNotRetryable` does `var newError = ErrorInfo.NonRenewableToken; newError.Message += $" Original: ..."`, mutating the process-wide static `ErrorInfo.NonRenewableToken` instead of copying it.
- **Test impact:** `TokenExpiryNonRenewableTests.RSA4a2_TokenErrorNonRenewableFailed`, env-gated.
- **Status:** open bug (two bugs).

### RTN15h3 — a DISCONNECTED followed by the transport close starts two resume attempts

- **Spec point:** RTN15h3 (test `realtime/unit/RTN15h3/non-token-error-resume-0`, `uts/realtime/unit/connection/connection_failures_test.md`).
- **What the features spec says:** "`(RTN15h3)` If the `DISCONNECTED` message contains an error other than a token error, the library must initiate an immediate reconnect attempt, by transitioning into the `CONNECTING` state and initiating an `RTN15b` resume attempt."
- **What the SDK does:** the server's DISCONNECTED (80003/503) and the transport close that follows it each start a reconnect: 3 connection attempts instead of 2, the second and third both with `resume=key-1`, and the recorded states end `Disconnected, Connecting, Connected, Connected` (observed with a diagnostic run).
- **Root cause:** not isolated; the DISCONNECTED handling and the transport-closed handling both schedule a connect without checking that one is already under way.
- **Test impact:** `ConnectionFailuresTests.RTN15h3_NonTokenErrorResume`, env-gated `[DeviationFact("RTN15h3")]`.
- **Status:** open bug.

### RTAN4e — no warning is logged when an annotation listener is added without ANNOTATION_SUBSCRIBE

- **Spec point:** RTAN4e (test `realtime/unit/RTAN4e/subscribe-warns-no-mode-0`, `uts/realtime/unit/channels/channel_annotations.md`).
- **What the features spec says:** "`(RTAN4e)` … once the channel is in the `attached` state, the `RTL4m` channel modes are checked for the presence of the `ANNOTATION_SUBSCRIBE` mode. … If this mode is missing, the library should log a warning indicating that the user has tried to add an annotation listener without having requested the `ANNOTATION_SUBSCRIBE` channel mode in ChannelOptions".
- **What the SDK does:** with the channel ATTACHED and its granted modes `Publish` only (observed: `state=Attached modes=Publish`), `channel.Annotations.Subscribe(…)` logs no warning at all (nothing at Warning level in the captured log).
- **Root cause:** not isolated. `RealtimeAnnotations.WarnIfSubscribeModeNotGranted` exists and is called from `Subscribe`, but nothing reaches the client's log sink; candidates are the channel logger (`RealtimeChannel.Logger`) not being the client's logger, or the `_handlers.IsEmpty` guard.
- **Test impact:** `ChannelAnnotationsTests.RTAN4e_SubscribeWarnsNoMode`, env-gated `[DeviationFact("RTAN4e")]` (the test matches the SDK's spelling "AnnotationSubscribe").
- **Status:** open bug.

### RTC8a1 — authorize() can miss the CONNECTED that answers its AUTH

- **Spec point:** RTC8a, RTC8a1 (test `realtime/unit/RTC8a1/capability-downgrade-channel-failed-1`, `uts/realtime/unit/auth/realtime_authorize.md`).
- **What the features spec says:** "`(RTC8a1)` If the authentication token change is successful, then Ably will send a new `CONNECTED` `ProtocolMessage`." and "`(RTC8b1)` The `authorize` call should be indicated as completed with the new token once the connection has moved to the `CONNECTED` state".
- **What the SDK does:** `AuthorizeAsync` never completes (bounded at 10 s) when the CONNECTED reply arrives quickly; seen intermittently when the unit classes run in parallel.
- **Root cause:** `ConnectionManager.OnAuthUpdated` sends the AUTH message first and only then creates the `ConnectionChangeAwaiter` that waits for CONNECTED, so a CONNECTED processed in between is never seen; the wait then lasts `RealtimeRequestTimeout`. Every authorize-while-CONNECTED test is exposed; only this one has been seen to fail.
- **Test impact:** `RealtimeAuthorizeTests.RTC8a1_CapabilityDowngradeChannelFailed`, env-gated `[DeviationFact("RTC8a1")]`. Also `RealtimeAuthorizeTests.RTC8a3_AuthorizeCompletesAfterResponse` (test `realtime/unit/RTC8a3/authorize-completes-after-response-0`, `[DeviationFact("RTC8a1")]`; `(RTC8b1)` as quoted above): the test answers the AUTH with CONNECTED as soon as it sees it (`ConnectionManager.cs:176`), before `OnAuthUpdated` creates its awaiter (`:187`), so `AuthorizeAsync` never completes (`timed out after 10000 ms waiting for authorizeFuture`). Failed in 3/3 serial runs of the unit tier (`RunConfiguration.DisableParallelization=true`) and passed in the parallel runs. And `RealtimeAuthorizeTests.RTC8a_AuthorizeConnectedSendsAuth` (`[DeviationFact("RTC8a1")]`): the same race, `timed out after 10000 ms waiting for AuthorizeAsync`, seen once in a parallel and once in a serial run after passing in 12 earlier runs.
- **Status:** open bug (race).

### RTN14d — DISCONNECTED is left at once and errorReason is cleared by the next CONNECTING

- **Spec point:** RTN14d, RTN25, RTB1, RTN15h2, RSA4c2 (tests `realtime/unit/RTN25/error-reason-disconnected-1`, `realtime/unit/RTN25/error-reason-cleared-on-connect-4`, `realtime/unit/RTN15h2/token-error-renew-fails-1`, `realtime/unit/RTN14c/connection-timeout-0`, `realtime/unit/RSA4c2/callback-timeout-connecting-disconnected-1`).
- **What the features spec says:** `(RTN14d)` "the `Connection#state` will transition to `DISCONNECTED`, the `Connection#errorReason` will be updated, a `ConnectionStateChange` with the `reason` will be emitted, and new connection attempts will periodically be made ... The `retryIn` attribute ... should be calculated as described in RTB1"; `(RTB1)` "the time until retry is calculated as the product of the initial retry timeout (for `DISCONNECTED` connections this is the `disconnectedRetryTimeout`), the backoff coefficient ... and the jitter coefficient"; `(RTN15h2)` "If the token creation fails ... the connection must transition to the `DISCONNECTED` state and set the `Connection#errorReason`".
- **What the SDK does:** after a failed attempt it enters DISCONNECTED and immediately retries (no `disconnectedRetryTimeout` wait), and every state change overwrites `Connection.ErrorReason` with that state's error, so the next CONNECTING clears it. Observed: error-reason-disconnected-1 and cleared-on-connect-4 `Expected client.Connection.ErrorReason not to be <null>` right after the DISCONNECTED wait; token-error-renew-fails-1 `Expected ConnectionState.Disconnected, but found Connected` right after DISCONNECTED was recorded (no fake clock, default 15 s `disconnectedRetryTimeout`); RTN14c `timed out ... waiting for connection state Disconnected (current: Connecting)` after advancing past `realtimeRequestTimeout`. RSA4c2 callback-timeout: within `ADVANCE_TIME(11000)` the recorded changes are CONNECTING, DISCONNECTED (80019), CONNECTING, so the later `AWAIT_STATE disconnected` times out (current: Connecting); the 80019 itself is correct.
- **Root cause:** the instant retry in `ConnectionDisconnectedState` / `RealtimeWorkflow` (`CheckInstantRetryFlag`, cited as RTN17j, applied even when no fallback host is tried; see `realtime-mapping.md` section 13 "RTN14d retry timing"), and `RealtimeState.ConnectionData.Update` (`ErrorReason = state.Error` on every transition).
- **Test impact:** `ErrorReasonTests.RTN25_ErrorReasonDisconnected` and `RTN25_ErrorReasonClearedOnConnect` (`[DeviationFact("RTN14d")]`), `ConnectionFailuresTests.RTN15h2_TokenErrorRenewFails` (`[DeviationFact("RTN15h2")]`), `ConnectionOpenFailuresTests.RTN14c_ConnectionTimeout` (`[DeviationFact("RTN14c")]`; the DISCONNECTED it waits for is transient for the same reason), `AuthCallbackErrorsTests.RSA4c2_CallbackTimeoutConnectingDisconnected` (`[DeviationFact("RTN14d")]`; it was a MIL stub until the fake-clock advance learnt to fire a timer the SDK is parked on), and `ChannelDetachTests.RTL5l_DetachAttachedWhenDisconnected` (`[DeviationFact("RTN14d")]`: the connection is CONNECTING or CONNECTED again when `detach()` is called, so a DETACH is sent and the channel waits in DETACHING; seen when the unit classes run in parallel).
- **Status:** open bug.

### RTN13d — ping() while CONNECTING fails at once instead of being deferred

- **Spec point:** RTN13d, RTN13c (tests `realtime/unit/RTN13d/ping-deferred-connecting-0`, `realtime/unit/RTN13c/deferred-ping-timeout-1`, `uts/realtime/unit/connection/connection_ping_test.md`).
- **What the features spec says:** `(RTN13d)` "If the connection state is `CONNECTING` or `DISCONNECTED`, do the operation once the connection state is `CONNECTED`".
- **What the SDK does:** `PingAsync` returns a failed `Result` straight away with "Unable to ping service; not connected" (code 40000) whenever the state is not CONNECTED. Observed: `Expected pingResult.IsSuccess to be true ... (Unable to ping service; not connected)` and `Expected error.Message "Unable to ping service; not connected" to contain the equivalent of "timeout"`.
- **Root cause:** `RealtimeWorkflow.HandlePingCommand` fails the request with `PingRequest.DefaultError` when `Connection.State != Connected`; nothing queues it.
- **Test impact:** `ConnectionPingTests.RTN13d_PingDeferredConnecting`, `ConnectionPingTests.RTN13c_DeferredPingTimeout` and `ConnectionPingTests.RTN13d_PingDeferredDisconnected` (the last passes only when the instant reconnect wins the race with `ping()`; it failed with "Unable to ping service; not connected" in a parallel run): `[DeviationFact("RTN13d")]`.
- **Status:** open bug.

### RTN26 / RTL25 — `whenState` is missing (Connection and RealtimeChannel)

- **Spec point:** RTN26, RTL25. Tests: `WhenStateTests` (6: `realtime/unit/RTN26a/immediate-callback-current-state-0`, `RTN26b/deferred-callback-future-state-0`, `RTN26b/fires-only-once-1`, `RTN26a/multiple-whenstate-calls-1`, `RTN26a/no-fire-for-past-state-2`, `RTN26/whenstate-different-states-0`) and `ChannelWhenStateTests` (4: `RTL25a/resolves-immediately-current-0`, `RTL25b/waits-for-state-change-0`, `RTL25b/fires-once-only-1`, `RTL25a/past-state-does-not-resolve-1`).
- **What the features spec says:** "`(RTN26)` `Connection#whenState` function" — "`(RTN26a)` If the connection is already in the given state, calls the listener with a \`null\` argument"; "`(RTL25)` `RealtimeChannel#whenState` function" — "`(RTL25a)` If the channel is already in the given state, calls the listener with a \`null\` argument".
- **What the SDK does:** the API is missing: neither `Connection` nor `IRealtimeChannel` has `WhenState` (`realtime-mapping.md` §12, Missing APIs). Every test needs the method, so none can run.
- **Test impact:** 10 skipped stubs (`Skip = "Missing SDK API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** Not present in ably-java or ably-cocoa either (no `whenState` in their sources); a newer spec point no SDK here implements.
- **Status:** open: feature not implemented in this SDK (moved from Mock Infrastructure Limitations: a missing SDK API is SDK non-compliance, not a mock gap).

### RTL22 — `subscribe(MessageFilter, …)` is missing

- **Spec point:** RTL22. Tests (`ChannelSubscribeTests`, 5): `realtime/unit/RTL22a/filter-matching-name-0`, `RTL22a/filter-matching-ref-timeserial-1`, `RTL22a/filter-matching-clientid-2`, `RTL22b/filter-isref-false-0`, `RTL22c/filter-multiple-criteria-0`.
- **What the features spec says:** "`(RTL22)` Methods must be provided for attaching and removing a listener which only executes when the message matches a set of criteria."
- **What the SDK does:** the API is missing: no `Subscribe` overload takes a `MessageFilter` (`realtime-mapping.md` §12).
- **Test impact:** 5 skipped stubs (`Skip = "Missing SDK API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** Not present in ably-java or ably-cocoa either (no `MessageFilter`).
- **Status:** open: feature not implemented in this SDK (moved from Mock Infrastructure Limitations: a missing SDK API is SDK non-compliance, not a mock gap).

### RTL10b — channel history has no `untilAttach`

- **Spec point:** RTL10b. Tests (`ChannelHistoryTests`, 2): `realtime/unit/RTL10b/adds-from-serial-0`, `RTL10b/errors-when-not-attached-1`.
- **What the features spec says:** "`(RTL10b)` Additionally supports the param `untilAttach`, which if true, will only retrieve messages prior to the moment that the channel was attached …".
- **What the SDK does:** the API is missing: `IRealtimeChannel.HistoryAsync` takes no `untilAttach` (presence history has it; `realtime-mapping.md` §12).
- **Test impact:** 2 skipped stubs (`Skip = "Missing SDK API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** ably-java and ably-cocoa both support `untilAttach` on channel history.
- **Status:** open: feature not implemented in this SDK (moved from Mock Infrastructure Limitations: a missing SDK API is SDK non-compliance, not a mock gap).

### TB4 / RTL7h / RTP6e / RTAN4e1 — `attachOnSubscribe` channel option is missing

- **Spec point:** TB4, RTL7h, RTP6e, RTAN4e1. Tests (4): `realtime/unit/TB4/attach-on-subscribe-default-0` (`ChannelOptionsTests`), `RTL7h/no-attach-on-subscribe-0` (`ChannelSubscribeTests`), `RTP6e/subscribe-no-attach-option-0` (`RealtimePresenceSubscribeTests`), `RTAN4e1/no-warn-unattached-0` (`ChannelAnnotationsTests`).
- **What the features spec says:** "`(TB4)` `attachOnSubscribe` (for realtime client libraries only) a boolean which determines whether calling `subscribe` on a channel or presence object should trigger an implicit attach. Defaults to `true`."
- **What the SDK does:** the API is missing: `ChannelOptions` has no `AttachOnSubscribe`; these tests depend on the option being false on an unattached channel (`realtime-mapping.md` §12). Where the option is only setup the other tests drop it with a `// NOTE:`.
- **Test impact:** 4 skipped stubs (`Skip = "Missing SDK API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** ably-java and ably-cocoa both have `attachOnSubscribe`.
- **Status:** open: feature not implemented in this SDK (moved from Mock Infrastructure Limitations: a missing SDK API is SDK non-compliance, not a mock gap).

### RTS5 / DO2a — `Channels#getDerived` and `DeriveOptions` are missing

- **Spec point:** RTS5, RTS5a, RTS5a1, RTS5a2, DO2a. Tests (`ChannelOptionsTests`, 5): `realtime/unit/RTS5a/creates-derived-channel-0`, `RTS5a1/filter-base64-encoded-0`, `RTS5a2/derived-with-params-0`, `RTS5/get-derived-with-options-0`, `DO2a/filter-attribute-0`.
- **What the features spec says:** "`(RTS5)` `Channels#getDerived` function" — "`(RTS5a)` Takes `RealtimeChannel` name and `DeriveOptions` object as argument, to create a derived channel"; "`(DO2a)` `filter` (string) - A JMESPath string for filter expression."
- **What the SDK does:** the API is missing: no `GetDerived` on `Channels`, no `DeriveOptions` type.
- **Test impact:** 5 skipped stubs (`Skip = "Missing SDK API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** Not present in ably-java or ably-cocoa either (no `getDerived`).
- **Status:** open: feature not implemented in this SDK (moved from Mock Infrastructure Limitations: a missing SDK API is SDK non-compliance, not a mock gap).

## Adapted Tests

### RTN8a / RTN8d / RTN9a / RTN9d / RTN16g3 — connection id, key and recovery key are empty strings, not null

- **Spec point:** RTN8a, RTN9a (test `realtime/proxy/RTN14a/fatal-connect-error-0`).
- **What the spec says:** `(RTN8a)` "Is unset until connected"; `(RTN9a)` "Is unset until connected".
- **What the SDK does:** `Connection.Id` and `Connection.Key` are `""`.
- **Test impact:** `ConnectionOpenFailuresTests.RTN14a_FatalConnectError` asserts `BeEmpty()` for both, with a `// DEVIATION:` comment; the other assertions follow the spec.
- `(RTN8d)` / `(RTN9d)` "Is `Null` when the SDK is in the `CLOSED`, `CLOSING`, or `FAILED` states." and `(RTN16g3)` "It should return `Null` when the SDK is in the `CLOSED`, `CLOSING`, or `FAILED` states, or when it does not have a `connectionKey`". The SDK resets `Id` and `Key` to `string.Empty` (`RealtimeState.ConnectionData`) and `Connection.CreateRecoveryKey()` documents returning `string.Empty` "where RTN16g3 calls for null" (intentional: "This SDK returns empty strings rather than nulls for absent string values throughout"). Before the first connect `Id` and `Key` are still null (`realtime/unit/RTN8a/id-unset-until-connected-0` passes).
- Unit tests adapted to `BeEmpty()`: `ConnectionIdKeyTests.RTN8d_IdNullAfterClosed`, `RTN9d_KeyNullAfterClosed`, `RTN8d_IdKeyNullAfterFailed`, `ConnectionOpenFailuresTests.RTN14a_InvalidKeyFailed`.
- **Status:** intentional / SDK-wide (empty string instead of null; also seen in RTN16g3).

### TB2 — default ChannelOptions has non-null cipher params and empty params/modes

- **Spec point:** TB2, TB2b, TB2c, TB2d (tests `realtime/unit/TB2/channel-options-attributes-0`, `realtime/unit/RTS3c1/error-reattach-params-0`).
- **What the features spec says:** `(TB2)` "The attributes of `ChannelOptions` consist of: `(TB2b)` `cipher` ... `(TB2c)` `params` ... a `Dict<string,string>` ... `(TB2d)` `modes` ... an array of `ChannelMode`s" - the UTS spec asserts all three are null on a default `ChannelOptions`.
- **What the SDK does:** `new ChannelOptions()` has `CipherParams = Crypto.GetDefaultParams()` (an AES-256 key) with `Encrypted == false`, and `Params`/`Modes` are empty collections (their setters replace null with an empty collection) (`Expected options.CipherParams to be <null>, but found Ably.PubSub.CipherParams ...`; `Expected channel.Options.Params to be <null>, but found {empty}`).
- **Root cause:** `Http/ChannelOptions.cs` constructor and the `Params`/`Modes` setters.
- **Test impact:** `ChannelOptionsTests.TB2_ChannelOptionsAttributes` asserts `Encrypted == false`, `CipherParams` non-null and empty `Params`/`Modes`; `ChannelOptionsTests.RTS3c1_ErrorReattachParams` asserts `Params` empty; each with a `// DEVIATION:` comment.
- **Status:** intentional / SDK-wide (non-null option defaults; encryption is governed by `Encrypted`).

### RTN23a — idle-connection detection runs on a 1 s poll, so the disconnect comes up to 1 s late

- **Spec point:** RTN23a (tests `realtime/unit/RTN23a/idle-timeout-reconnect-1`, `heartbeat-resets-timer-2`, `timeout-triggers-reconnect-4`, `reconnect-uses-resume-5`, `ping-resets-timer-6`; `uts/realtime/unit/connection/heartbeat_test.md`).
- **What the features spec says:** `(RTN23a)` "If a transport does not receive any indication of activity on a transport for a period greater than the sum of the `maxIdleInterval` ... and the `realtimeRequestTimeout`, that transport should be disconnected."
- **What the SDK does:** the tests advance fake time 100 ms past `maxIdleInterval + realtimeRequestTimeout` and expect DISCONNECTED then a reconnect; the SDK is still CONNECTED with one connection attempt (`Expected connectionAttemptCount to be 2, but found 1`; recorded states only `[Connecting, Connected]`).
- **Root cause:** `RealtimeWorkflow.Start` runs the RTN23a monitor as a loop that queues a `HeartbeatMonitorCommand` every `ClientOptions.HeartbeatMonitorDelay` (default 1000 ms); `HandleHeartbeatMonitorCommand` disconnects only when `idleFor > maxIdleInterval + realtimeRequestTimeout`. The ticks fall on whole seconds, so a tick at exactly the threshold does not fire and detection comes on the next tick, up to 1 s late.
- **Test impact:** `HeartbeatTests.RTN23a_IdleTimeoutReconnect`, `RTN23a_HeartbeatResetsTimer`, `RTN23a_TimeoutTriggersReconnect`, `RTN23a_ReconnectUsesResume`, `RTN23a_PingResetsTimer`: adapted, one extra `AdvanceTime(1000)` after the spec's advance, with a `// DEVIATION:` comment; every spec assertion is kept. (The re-entry `AWAIT_STATE connected` in these tests and in `RTN23a_AnyMessageResetsTimer` is rendered as a poll of the recorded states, a translation fix.)
- **Status:** intentional: polling design with a configurable granularity (`HeartbeatMonitorDelay`).

### RTN23c1 / RTN7b — a PONG carries msgSerial 0

- **Spec point:** RTN23c1, RTN7b (test `realtime/unit/RTN23c1/ping-pong-echo-id-0`, `uts/realtime/unit/connection/heartbeat_test.md`).
- **What the features spec says:** `(RTN7b)` "Every `ProtocolMessage` that expects an `ACK` or `NACK` sent must contain a unique serially incrementing `msgSerial` integer value starting at zero." The UTS spec reads this as "A PONG does not expect an ACK, so must not have a `msgSerial`" and asserts `msgSerial IS NULL`.
- **What the SDK does:** the PONG frame on the wire carries a `msgSerial` (`Expected pongFrame["msgSerial"] == null ... but found False`).
- **Root cause:** `ProtocolMessage.MsgSerial` is a non-nullable `long` with a plain `[JsonProperty("msgSerial")]`, so every client frame serialises `msgSerial` (0 unless `RealtimeWorkflow.SendMessage` assigned one); `RealtimeWorkflow` sends the PONG as `new ProtocolMessage(Pong) { Id = message.Id }` straight to the transport.
- **Test impact:** `HeartbeatTests.RTN23c1_PingPongEchoId`: adapted, asserts `msgSerial == 0` on both PONG frames with a `// DEVIATION:` comment; the id and empty-payload assertions follow the spec.
- **Status:** SDK-wide (every non-ACK client frame, e.g. ATTACH, also carries `msgSerial: 0`); low impact.

### RTP2 (white-box) — PresenceMap.Clear does not end an in-progress sync

- **Spec point:** RTP2, RTP5a (test `realtime/unit/RTP2/clear-resets-state-3`, `presence_map.md`; white-box per realtime-mapping.md section 14).
- **What the features spec says:** `(RTP5a)` "If the channel enters the `DETACHED` or `FAILED` state then all queued presence messages will fail immediately, and the `PresenceMap` and internal PresenceMap (see RTP17) is cleared." The features spec does not define `clear()` itself; the UTS spec models it as also resetting `isSyncInProgress`.
- **What the SDK does:** `PresenceMap.Clear` empties the members and the RTP19 tracking set but leaves `SyncInProgress` true; the next `EndSync` resets it. `map.Values` is empty and `get` returns null as the spec expects.
- **Test impact:** `PresenceMapTests.RTP2_ClearResetsState` asserts `SyncInProgress` is still true (adapted, `// DEVIATION:` comment); the other assertions follow the spec.
- **Status:** intentional internal shape (white-box PresenceMap API, case 3 of "Idiomatic translation vs genuine deviations"); no public observable was found to be lost, so it is recorded here rather than gated.

### RSA4a1 — the 40171 "no means to renew" message is logged at Warning, not info

- **Spec point:** RSA4a1 (test `realtime/unit/RSA4a1/non-renewable-token-logs-warning-0`).
- **What the features spec says:** `(RSA4a1)` "At instantiation time, a message at `info` log level with error code `40171` should be logged indicating that no means has been provided to renew the supplied token, including an associated url per `TI5`".
- **What the SDK does:** logs "Library initialized with a token literal without any way to renew the token when it expires ... See https://help.ably.io/error/40171 for help" at `LogLevel.Warning` (`AblyAuth`, constructor path). The SDK has no info level (the infra maps info to Debug), so the test's Debug filter found nothing.
- **Test impact:** `TokenExpiryNonRenewableTests.RSA4a1_NonRenewableTokenLogsWarning` asserts the message (40171 text and help URL) at `LogLevel.Warning`, with a `// DEVIATION:` comment.
- **Status:** intentional / SDK-wide (the SDK's level set is Debug, Warning, Error; Warning is its choice for this message).

### RTN13c — ping timeout error says "timed out", not "timeout"

- **Spec point:** RTN13c (test `realtime/unit/RTN13c/ping-timeout-0`, `uts/realtime/unit/connection/connection_ping_test.md`).
- **What the features spec says:** `(RTN13c)` "Will fail if a `HEARTBEAT` `ProtocolMessage` is not received within `realtimeRequestTimeout`" (no message text is specified; the UTS spec asserts `error.message CONTAINS "timeout"`).
- **What the SDK does:** the ping fails on time, with the message "Unable to ping service; Request timed out".
- **Test impact:** `ConnectionPingTests.RTN13c_PingTimeout` asserts the message contains "timed out", with a `// DEVIATION:` comment.
- **Status:** intentional (message wording only; the timeout itself is spec-compliant).

### RTN16f1 — a malformed recovery key is logged at Warning, not as an error

- **Spec point:** RTN16f1 (test `realtime/unit/RTN16f1/malformed-recovery-key-0`, `uts/realtime/unit/connection/connection_recovery_test.md`).
- **What the features spec says:** `(RTN16f1)` "If the recovery key provided in the `recover` client option cannot be deserialized due to malformed data, then an error should be logged and the connection should be made like no `recover` option was provided."
- **What the SDK does:** connects without `recover` as required, but logs "Error deserializing recover - ..., setting it as null" at Warning level, so no Error-level log is emitted.
- **Root cause:** `RecoveryKeyContext.Decode` (`logger?.Warning(...)` in its catch).
- **Test impact:** `ConnectionRecoveryTests.RTN16f1_MalformedRecoveryKey` asserts a Warning-level log mentioning "recover", with a `// DEVIATION:` comment; the connection assertions are spec-correct.
- **Status:** open bug (minor: log level).

## Mock Infrastructure Limitations

### RTC5 / RTC6 / RTC9 / RTL10a / RTL28 / RTL31 — delegated REST cases helpers don't exist yet

- **Spec point:** RTC5, RTC6, RTC9, RTL10a, RTL28, RTL31. Tests (6): `realtime/unit/RTC5/stats-proxies-rest-0` (`RealtimeStatsTests`), `RTC6/time-proxies-rest-0` (`RealtimeTimeTests`), `RTC9/request-proxies-rest-0` (`RealtimeRequestTests`), `RTL10a/supports-rest-params-0` (`ChannelHistoryTests`), `RTL28/identical-to-rest-0` (`ChannelGetMessageTests`), `RTL31/identical-to-rest-0` (`ChannelMessageVersionsTests`).
- **What the features spec says:** "`(RTL28)` `RealtimeChannel#getMessage` function: same as `RestChannel#getMessage`"; "`(RTL31)` `RealtimeChannel#getMessageVersions` function: same as `RestChannel#getMessageVersions`"; "`(RTL10a)` Supports all the same params as `RestChannel#history`"; "`(RTC9)` `RealtimeClient#request` is a wrapper around `RestClient#request` (see RSC19)"; RTC5/RTC6 `RealtimeClient#stats` / `#time` proxy the REST functions.
- **Limitation:** each spec says to run the referenced `uts/rest/unit/…` cases against the realtime operation; the shared REST cases helpers (`StatsCases`, `TimeCases`, `RequestCases`, `HistoryCases`, `GetMessageCases`, `MessageVersionsCases`, `rest-mapping.md` §12) are not generated yet because the REST unit specs are not translated. Regenerate these after the REST unit tier exists.
- **Test impact:** 6 skipped stubs.
