# UTS deviations

Findings from evaluating the UTS-derived tests against the .NET SDK. Tests marked as env-gated (`[DeviationFact]`) are skipped by default and run with `RUN_DEVIATIONS=1`, for example `RUN_DEVIATIONS=1 dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --filter "FullyQualifiedName=<namespace>.<class>.<method>"`.

## UTS Spec Errors

A spec error normally leads to one of two outcomes, and each entry's **Test impact** says which (a few entries say why they use another form, e.g. RTP11c1 is env-gated and RTB1 is a skipped stub):

- **The defect is in the test's subject** (the behaviour the test exists to check): the test is a skipped placeholder (`Skip = "UTS spec error <point>: …; see deviations.md (UTS Spec Errors)"`) rather than a fail-fast placeholder, so CI stays green while the spec is fixed upstream. This departs from the manual's fail-fast convention by decision of the SDK team. Once the spec is fixed, regenerate the test from it and remove the skip.
- **The defect is incidental** (an over-specification the features spec permits otherwise, or an inconsistent fixture, stub or member that isn't what the test checks): the test is corrected inline to what the features spec allows, with a `// NOTE:` naming the spec point at each corrected line, and recorded as "adapted, not skipped" (for example RSA3b, RSC1b, the `/time` stub under RSC16 / RSA10k, RSL1k1 / RSL1k3, RSL4d3, TP3h, RSC7d1, RSC18, RSC17, RTP8). Every other assertion stays verbatim, so the test keeps guarding the SDK.

Either way, the entry's draft fix says what the spec should change.

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

### RTL3e — disconnected-attached-noop test reads ATTACHED after a disconnect the client immediately recovers from

- **Spec point:** RTL3e (test `realtime/unit/RTL3e/disconnected-attached-noop-0`, `uts/realtime/unit/channels/channel_connection_state.md`).
- **What the features spec says:** `(RTL3e)` "If the connection state enters the `DISCONNECTED` state, it will have no effect on the channel states."; `(RTN15a)` "If a transport is disconnected unexpectedly ... it should respond as if it had received a non-token `DISCONNECTED` (following `RTN15h3`)."; `(RTN15h3)` "the library must initiate an immediate reconnect attempt, by transitioning into the `CONNECTING` state"; `(RTL3d)` "If the connection state enters the `CONNECTED` state, any channels in the `ATTACHING`, `ATTACHED`, or `SUSPENDED` states should be transitioned to `ATTACHING` ...".
- **What the UTS spec gets wrong:** its mock answers every connection attempt with CONNECTED, so after the disconnect the client reconnects at once (RTN15a/RTN15h3) and RTL3d moves the ATTACHED channel to ATTACHING. The test asserts `ATTACHED` right after `AWAIT_STATE disconnected`, which holds only if the read wins the race against the reconnect. The SDK follows the spec. Same class as the RTL13c entry.
- **Observed:** `Expected the enum to be ChannelState.Attached {value: 2}, but found ChannelState.Attaching {value: 1}` in CI (unit, net6.0, ubuntu); passed on net7.0 in the same run and in local runs.
- **Draft fix:** make the mock refuse every connection attempt after the first (`respond_with_refused()`), so the connection stays DISCONNECTED while the channel state is asserted.
- **Test impact:** `ChannelConnectionStateTests.RTL3e_DisconnectedAttachedNoop` is a skipped placeholder (`Skip = "UTS spec error <point>: …"`; skipped so CI stays green until the spec is fixed — then regenerate the test from the fixed spec and remove the skip).

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

### RSA3b — tests assert the plain token in `Authorization: Bearer`, but the features spec makes Base64 optional

- **Spec point:** RSA3b (REST unit tests in `uts/rest/unit/auth/auth_scheme.md`, `auth_callback.md`, `authorize.md`, `token_renewal.md`).
- **What the features spec says:** `(RSA3b)` "For REST requests, the token string is optionally Base64-encoded and used in the `Authorization: Bearer` header".
- **What the UTS spec gets wrong:** the tests assert `Authorization == "Bearer <plain token>"` (and `RSC10`/`RSA4b` mocks match on the plain header to decide which token to reject), which over-specifies a choice the features spec leaves to the SDK. This SDK Base64-encodes the token (`AblyAuth.AddAuthHeader`: `"Bearer " + token.ToBase64()`).
- **Observed:** `Expected request.Headers["Authorization"] to be "Bearer callback-token" ... but "Bearer Y2FsbGJhY2stdG9rZW4=" ...`.
- **Draft fix:** compare against the SDK-chosen encoding (accept either form through a `mock_http.md` helper), or assert only the decoded token.
- **Test impact:** adapted to the features-permitted behaviour, not skipped (each adapted point asserts `"Bearer " + Base64(token)`, mock conditions match that form, and a `// NOTE:` names RSA3b; every other assertion is verbatim): `AuthSchemeTests.RSA4_UseTokenAuthForced`, `RSA4_AuthCallbackTriggersToken`, `RSA4_AuthUrlTriggersToken`, `RSA1_TokenAuthTakesPrecedence`, `RSC18_TokenAuthOverNonTls`; `AuthCallbackTests.RSA8d_CallbackInvokedForAuth`, `RSA8d_CallbackReturnsJwt`, `RSA8d_CallbackReturnsTokenRequest`, `RSA8c_AuthUrlInvokedForAuth`, `RSA8c_AuthUrlReturnsJwt`; `AuthorizeTests.RSA10a_AuthorizeDefaultParams`; `TokenRenewalTests.RSA4b_RenewalOn40142`, `RSA4b_RenewalViaAuthurl`, `RSC10_RequestRetriedAfterRenewal`. Skipped placeholders (the test's subject is the header value itself): `AuthSchemeTests.RSA3_TokenAuthExplicitToken`, `AuthSchemeTests.RSA3_TokenAuthTokenDetails`.

### RSC1b — the no-auth test expects the 40106 on the first request, the features spec doesn't say when

- **Spec point:** RSC1b (test `rest/unit/RSC1b/no-auth-method-error-0`, `uts/rest/unit/auth/auth_scheme.md`).
- **What the features spec says:** `(RSC1b)` "If invalid arguments are provided such as no API key, no token and no means to create a token, then this will result in an error with error code 40106 and an informative message."
- **What the UTS spec gets wrong:** it constructs `Rest(options: ClientOptions())` and expects the error from `client.request(...)`; RSC1b fixes neither the moment nor the call. The SDK raises 40106 when the client is built (`AblyAuth.CheckAndGetAuthMethod`).
- **Draft fix:** accept a construction-time error (wrap both the construction and the request in the error expectation).
- **Test impact:** adapted to the features-permitted behaviour, not skipped: `AuthSchemeTests.RSC1b_NoAuthMethodError` asserts the construction throws 40106 and that no HTTP request was made, with a `// NOTE:`.

### RSA4a2 — the expired-static-token test expects a local 40171 with no HTTP request

- **Spec point:** RSA4a2, RSA4b1 (test `rest/unit/RSA4a2/expired-token-no-renewal-0`, `uts/rest/unit/auth/auth_scheme.md`).
- **What the features spec says:** `(RSA4a2)` "if the server responds with a token error (401 HTTP status code and an Ably error value `40140 <= code < 40150`), then the client library should indicate an error with error code `40171`, not retry the request and, in the case of the realtime library, transition the connection to the `FAILED` state"; `(RSA4b1)` local expiry detection is optional and only "when all of the following applies: ... the library has previously queried the time from the Ably service and persisted the local clock offset according to RSA10k".
- **What the UTS spec gets wrong:** it expects a request with a locally expired static token to fail with 40171 without any HTTP request, with no server time known. Per the features spec that failure follows a server token error (so one request is made); local detection is optional (the manual lists this as an example of a spec error).
- **Observed:** no error is thrown for the request (the SDK, like RSA4b1 permits, sends it).
- **Draft fix:** have the mock answer 401/40142 and assert 40171 and a single request.
- **Test impact:** `AuthSchemeTests.RSA4a2_ExpiredTokenNoRenewal` is a skipped placeholder.

### RSA4b1 — pre-emptive-renewal tests rely on local expiry detection without a known server time

- **Spec point:** RSA4b1 (tests `rest/unit/RSA4b1/preemptive-renewal-0` in `token_renewal.md` and `rest/unit/RSA16c/updated-after-expiry-renewal-2` in `token_details.md`).
- **What the features spec says:** `(RSA4b1)` "Client libraries can optionally save a round-trip request to the Ably service for expired tokens by detecting when a token has expired when all of the following applies: the current token is a `TokenDetails` object with an `expires` attribute; the library has previously queried the time from the Ably service and persisted the local clock offset according to RSA10k; the `expires` time has passed based on the Ably service time and not the local clock".
- **What the UTS spec gets wrong:** both tests expect a pre-emptive renewal from the local clock alone (no `queryTime`, no `/time`).
- **Observed:** the token is reused (callback count 1, `token-v1` still current).
- **Draft fix:** query the server time first (`authorize(authOptions: {queryTime: true})` with a `/time` stub answering `[ms]`) so the optional detection applies, or assert the renewal on the server's 40142.
- **Test impact:** `TokenRenewalTests.RSA4b1_PreemptiveRenewal`, `TokenDetailsTests.RSA16c_UpdatedAfterExpiryRenewal` are skipped placeholders.

### RSC16 / RSA10k — `/time` stubbed as an object

- **Spec point:** RSC16, RSA10k (`uts/rest/unit/auth/authorize.md`, `fallback.md`, `rest_client.md`, `request_endpoint.md`, `request.md`, and `uts/rest/unit/helpers/mock_http.md`, whose examples carry the same object form).
- **What the features spec says:** `(RSC16)` "`RestClient#time` function sends a get request to the `/time` path of the REST endpoint as specified in RSC25 and returns the server time in milliseconds since epoch or as a Date/Time object where suitable"; `(RSA10k)` "If the `AuthOption` argument's `queryTime` attribute is true, it will obtain the server time once and persist the offset from the local clock."
- **What the UTS spec gets wrong:** the mocks answer `/time` with `{"time": 1234567890000}`, whereas the endpoint returns a JSON array of milliseconds (as `uts/rest/unit/time.md` itself stubs it).
- **Observed:** `Cannot deserialize the current JSON object ... into type 'List<long>'`.
- **Draft fix:** answer `/time` with `[1234567890000]` everywhere, including the `mock_http.md` examples.
- **Test impact:**
  - **(a) Stub corrected, not skipped** (each carries `// NOTE: /time stub corrected to the array form RSC16/time.md use`; `/time` is incidental to the test's subject): `FallbackTests.REC1a_DefaultPrimaryDomain`, `REC1b2_ExplicitHostnameWithPeriod`, `REC1b2_EndpointLocalhost`, `REC1b2_EndpointIpv6Address`, `REC1b3_NonprodRoutingPolicy`, `REC1b4_ProductionRoutingPolicy`, `REC2a2_CustomFallbackHosts`, `REC2c1_DefaultFallbackDomains`, `REC2c3_NonprodFallbackDomains`, `REC2c4_ProductionEndpointFallbackDomains`, `RSC15l_QualifyingErrorsTriggerFallback`, `RSC15l_ConnectionRefusedFallback`, `RSC15l_DnsErrorFallback`, `RSC15l_ConnectionTimeoutFallback`, `RSC15l_RequestTimeoutFallback`, `RSC15l_Http5xxTriggersFallback`, `RSC15l4_CloudfrontErrorTriggersFallback` (gated, see "RSC15l4"), `RSC15j_HostHeaderMatchesRequest`; `RequestEndpointTests.RSC25_DefaultPrimaryDomain`, `RSC25_CustomEndpointDomain`, `RSC25_MultipleRequestsPrimaryDomain`, `RSC25_PrimaryTriedBeforeFallback`; `RestClientTests.RSC7e_AblyVersionHeader`, `RSC7d_AblyAgentHeaderFormat` (also adapted, see "RSC7d1"), `RSC7c_RequestIdIncluded`, `RSC7c_RequestIdPreservedFallback` (gated, see "RSC7c"), `RSC18_TlsControlsProtocolScheme`, `RSC18_BasicAuthOverHttpRejected` (adapted and gated, see "RSC18"). Of these, 24 pass (RSC7d and RSC18 tls included); `RSC15l4`, `RSC7c` x2 and `RSC18_BasicAuthOverHttpRejected` are gated for the independent reasons named.
  - **(b) Placeholder (the test's subject is the time value or its parsing):** `AuthorizeTests.RSA10k_AuthorizeQueryTime` (skipped).
  - The two `fallback.md` RSC15f tests carry the same stub and are placeholders for another reason, see "RSC15f".

### RSA10a — the switch-to-basic test expects authorize() to leave token auth

- **Spec point:** RSA10a (test `rest/unit/RSA16d/null-after-switch-to-basic-3`, `uts/rest/unit/auth/token_details.md`).
- **What the features spec says:** `(RSA10a)` "Instructs the library to create a token immediately and ensures Token Auth is used for all future requests."
- **What the UTS spec gets wrong:** it calls `authorize(authOptions: AuthOptions(key: ..., useTokenAuth: false))` and expects the client to be on Basic auth with `tokenDetails == null`; `authorize()` always creates a token and keeps token auth.
- **Draft fix:** drop the step, or build a second client with `useTokenAuth: false` to cover RSA16d for Basic auth (already covered by `null-with-basic-auth-0`).
- **Test impact:** `TokenDetailsTests.RSA16d_NullAfterSwitchToBasic` is a skipped placeholder.

### RSA15a / RSA15c — REST unit tests expect a construction-time throw with code 40102

- **Spec point:** RSA15a, RSA15c (tests `rest/unit/RSA15a/token-clientid-must-match-0`, `rest/unit/RSA15c/incompatible-clientid-error-0`, `uts/rest/unit/auth/client_id.md`).
- **What the features spec says:** `(RSA15a)` "Any `clientId` provided in `ClientOptions` must match any non wildcard (`'*'`) `clientId` value in `TokenDetails` or `connectionDetails` of the `CONNECTED` `ProtocolMessage`, where applicable"; `(RSA15c)` "Following an auth request which uses a `TokenDetails` or `TokenRequest` object that contains an incompatible `clientId`, the library should in the case of Realtime transition the connection state to `FAILED`, and in the case of REST result in an appropriate error response".
- **What the UTS spec gets wrong:** the same timing problem as the realtime "RSA7" entry above: the tests expect `Rest(...)` itself to throw, with code 40102 (a code and a construction-time throw that features.md does not name). RSA15c's REST case is "an appropriate error response" following an auth request; the RSA15c test has no setup, steps or assertions of its own.
- **Draft fix:** assert the error on the first request (as `rest/unit/RSA7/clientid-mismatch-error-1` does, see the "RSA15 — no clientId mismatch error" Failing Tests entry).
- **Test impact:** `ClientIdTests.RSA15a_TokenClientidMustMatch`, `ClientIdTests.RSA15c_IncompatibleClientidError` are skipped placeholders.

### RSL1k1 / RSL1k3 — idempotent id expectations in the publish tests

- **Spec point:** RSL1k1, RSL1k3, RSAN1c4 (tests `rest/unit/RSL1k/mixed-ids-in-batch-1`, `rest/unit/RSL1k2/message-id-format-0`, `rest/unit/RSAN1c4/idempotent-id-generated-0`, `rest/unit/RSL1e/null-name-and-data-0`; `uts/rest/unit/channel/idempotency.md`, `annotations.md`, `publish.md`).
- **What the features spec says:** `(RSL1k1)` "Idempotent publishing via library-generated `Message` `id` s is supported if `idempotentRestPublishing` (see TO3n) is enabled and one or more `Message` instances are passed to `publish()` and all `Message` s have an empty `id` attribute. The library generates a base `id` string by base64-encoding a sequence of at least 9 bytes obtained from a source of randomness."; `(RSL1k3)` "If more than one `Message` is passed to `publish()` and one or more of those messages contains a non-empty `id` attribute, then all message ids (present or absent) are preserved on sending the batch of messages."; `(RSAN1c4)` "... base64-encoding a sequence of at least 9 bytes ...".
- **What the UTS spec gets wrong:**
  - `mixed-ids-in-batch-1` expects a library-generated id for the message without an id when others in the batch carry ids; RSL1k3 says absent ids stay absent.
  - `message-id-format-0` and `idempotent-id-generated-0` require the base part to match `[A-Za-z0-9_-]+` (url-safe base64); features says only "base64-encoding", and the SDK uses standard base64 (`+`, `/`, `=` possible; the unit run produced `tokM1+weTvMo`).
  - `null-name-and-data-0` compares the whole body with `[{"data": "hello"}]` etc., but `idempotentRestPublishing` defaults to true (TO3n), so RSL1k1 adds an `id` to each message.
- **Draft fix:** mixed ids: expect `id` absent on the middle message (or assert RSL1k3 preservation). Pattern: `[A-Za-z0-9+/_-]+=*`. RSL1e: set `idempotentRestPublishing: false` in the test, or compare the body ignoring `id`.
- **Test impact:** `IdempotencyTests.RSL1k_MixedIdsInBatch` is a skipped placeholder (its subject is the over-specified behaviour). `IdempotencyTests.RSL1k2_MessageIdFormat` and `AnnotationsTests.RSAN1c4_IdempotentIdGenerated` are adapted to the base64 alphabet; `PublishTests.RSL1e_NullNameAndData` removes the generated `id` before comparing (each with a `// NOTE:`), not skipped.

### RSL4d3 — a JSON object payload is a string on the wire

- **Spec point:** RSL4d3 (test `rest/unit/RSL1a/publish-message-array-1`, `uts/rest/unit/channel/publish.md`).
- **What the features spec says:** `(RSL4d3)` "a JSON Message payload is stringified either as a JSON Object or Array and represented as a JSON string and the `encoding` attribute is set to \"json\"".
- **What the UTS spec gets wrong:** it asserts `body[1]["data"] == { "key": "value" }` (a raw JSON object in the request body); the payload is a string containing the JSON, with `encoding: "json"`.
- **Draft fix:** assert `parse_json(body[1]["data"]) == { "key": "value" }` (and `body[1]["encoding"] == "json"`).
- **Test impact:** `PublishTests.RSL1a_PublishMessageArray` parses the string before comparing (`// NOTE:`), not skipped.

### RSL4 — the encoding-fixtures test loads an `encoding.json` that does not exist in ably-common

- **Spec point:** RSL4, RSL6a1 (test `rest/unit/RSL4/encoding-fixtures-ably-common-0`, `uts/rest/unit/encoding/message_encoding.md`).
- **What the features spec says:** `(RSL6a1)` "A set of tests must exist to ensure that the client library provides data encoding & decoding interoperability with other client libraries. The tests must use the [set of predefined interoperability message fixtures](https://github.com/ably/ably-common/blob/main/test-resources/messages-encoding.json) ...".
- **What the UTS spec gets wrong:** it calls `load_fixtures("encoding.json")` and reads `input_data`, `expected_wire_data`, `expected_encoding` and `use_binary_protocol`. No `encoding.json` exists in ably-common (neither in the vendored `common` @ 133665b nor upstream main); the test-resources folder holds `messages-encoding.json` and `presence-messages-encoding.json`, whose entries have `data`, `encoding`, `expectedType` and `expectedValue`.
- **Draft fix:** point the spec at `messages-encoding.json` and use its field names (or add the fixture to ably-common).
- **Test impact:** `MessageEncodingTests.RSL4_EncodingFixturesAblyCommon` is a skipped placeholder.

### TM2s1 / TM2u — the defaults tests apply the received-message defaults to a locally deserialised JSON object

- **Spec point:** TM2s, TM2s1, TM2s2, TM2u (tests `rest/unit/TM2s1/version-defaults-from-message-0`, `rest/unit/TM2u/annotations-defaults-empty-0`, `uts/rest/unit/types/mutable_message_types.md`).
- **What the features spec says:** `(TM2s)` "`version` - an object containing information about the latest version of a message. If a message received from Ably (whether over realtime or REST) does not contain a `version` object the SDK must initialize a `version` object and set a subset of fields as specified below."; `(TM2u)` "`annotations` - an object of type `MessageAnnotations`. If not set on the wire, the SDK must set it to an empty `MessageAnnotations` object."
- **What the UTS spec gets wrong:** both tests call `Message.fromJson({...})` on a local object and expect the defaults; `fromJson` is not a features-spec operation and a locally deserialised object is not "a message received from Ably". The SDK applies the defaults on its receive paths (REST history, realtime delivery; `MessageHandler.ApplyTm2Defaults`), not in `JsonHelper` deserialisation, so `Version` and `Annotations` are null here (`Expected msg.Version not to be <null>`).
- **Draft fix:** drive the test through a received path (a REST history or get-message response) instead of `fromJson`.
- **Test impact:** `MutableMessageTypesTests.TM2s1_VersionDefaultsFromMessage`, `TM2u_AnnotationsDefaultsEmpty` are skipped placeholders (the test's subject is the over-specified detail, so the RULE's placeholder form applies).

### TP3h — member-key test asserts the order "connectionId:clientId"

- **Spec point:** TP3h (test `rest/unit/TP3h/member-key-combines-ids-0`, `uts/rest/unit/types/presence_message_types.md`).
- **What the features spec says:** `(TP3h)` "`memberKey` string function that combines the `connectionId` and `clientId` ensuring multiple connected clients with the same clientId are uniquely identifiable".
- **What the UTS spec gets wrong:** it asserts `"conn-1:user-1"`; TP3h fixes no order. The SDK builds `"{ClientId}:{ConnectionId}"` (`user-1:conn-1`).
- **Draft fix:** assert only that the key differs for different connectionIds and is the same for equal ids (or fix an order in features.md).
- **Test impact:** adapted to the features-permitted behaviour, not skipped: `PresenceMessageTypesTests.TP3h_MemberKeyCombinesIds` asserts `user-1:conn-1` / `user-1:conn-2` with a `// NOTE:`.

### TP3 — the presence toJson tests expect the action as the string "enter"

- **Spec point:** TP3, TP3b (tests `rest/unit/TP3/presence-to-json-2`, `rest/unit/TP3/null-attributes-omitted-3`, `uts/rest/unit/types/presence_message_types.md`).
- **What the features spec says:** `protocol.md` (Presence Message) "Action state := The presence action (`ABSENT`, `PRESENT`, `ENTER`, `LEAVE`, or `UPDATE`) encoded as the ordinal in the [PresenceMessage Action](/api/realtime-sdk/types#presence-action) enum."; `(TP3b)` "`action` enum".
- **What the UTS spec gets wrong:** both tests assert `json_data["action"] == "enter"` on the serialised wire form, but the wire form is the ordinal (the same corpus uses `"action": 1` in `rest/unit/TG/pagination-presence-results-7`). The SDK serialises `2`.
- **Draft fix:** assert the numeric ordinal (`2` for ENTER).
- **Test impact:** `PresenceMessageTypesTests.TP3_PresenceToJson`, `TP3_NullAttributesOmitted` are skipped placeholders.

### RSP4 — the presence-history pagination test expects LEAVE for wire action 4

- **Spec point:** RSP4, TP2 (test `rest/unit/RSP4/history-pagination-1`, `uts/rest/unit/presence/rest_presence.md`).
- **What the features spec says:** `(TP2)` "`PresenceMessage` `Action` enum has the following values in order from zero: `ABSENT`, `PRESENT`, `ENTER`, `LEAVE`, `UPDATE`", so LEAVE is 3 and UPDATE is 4.
- **What the UTS spec gets wrong:** the second page is `{ "action": 4, ... }` and the test asserts `page2.items[0].action == PresenceAction.leave`. The spec's own `RSP4a/history-returns-paginated-1` and `RSP5/presence-action-mapping-8` use 3 for leave and 4 for update (the trailing note "some SDKs use 4" is not in features).
- **Draft fix:** change the second page's action to 3 (or assert `PresenceAction.update`).
- **Test impact:** `RestPresenceTests.RSP4_HistoryPagination` is a skipped spec-error placeholder.

### RSP5 — the cipher test's encrypted fixture cannot be decrypted

- **Spec point:** RSP5, RSL6a (test `rest/unit/RSP5/decode-cipher-channel-7`, `uts/rest/unit/presence/rest_presence.md`).
- **What the features spec says:** `(RSL6a)` "All messages received will be decoded automatically based on the `encoding` field (by inverting the encoding rules of RSL4 and applying the decryption rules of RSL5 ...)".
- **What the UTS spec gets wrong:** `encrypted_data = "HO4cYSP8LybPYBPZPHQOtuD53yrD3YV3NBoTEYBh4U0="` (commented "Encrypted data for {"secret":"data"}") is 32 bytes, an IV plus one block. With the key `WUP6u0K7MXI5Zeo0VppPwg==` and AES-128-CBC it decrypts to the truncated text `{"example":{"jso` (no valid PKCS7 padding), not `{"secret":"data"}`. The SDK leaves the message undecoded (`encoding` stays `json/utf-8/cipher+aes-128-cbc`, per RSL6b), so no compliant SDK can pass the `data IS Object` assertion.
- **Draft fix:** use a ciphertext that really encrypts `{"secret":"data"}` (IV + two blocks), for example generated with the same key, or a fixture from ably-common `crypto-data-128.json`.
- **Test impact:** `RestPresenceTests.RSP5_DecodeCipherChannel` is a skipped spec-error placeholder.

### RSC15l — tests stub one response for a status or timeout that triggers a fallback retry

- **Spec point:** RSC15l, RSC19d (tests `rest/unit/RSC19d/response-status-code-0`, `rest/unit/RSC19d/response-success-indicator-1`, `uts/rest/unit/request.md`; `rest/unit/RSC13/request-timeout-enforced-0` and `rest/unit/RSC8e/unsupported-content-type-0`, `uts/rest/unit/rest_client.md`).
- **What the features spec says:** `(RSC15l)` "Errors that necessitate use of an alternative host include any of the following conditions", including `(RSC15l2)` "request timeout" and the 500-504 statuses; `(RSC15a)` "In the case of an error necessitating use of an alternative host ... try `fallback domains` in random order, continuing to try further domains if qualifying errors occur".
- **What the UTS spec gets wrong:** the tests queue a single 500 response (RSC19d: status 500; RSC8e Case 1) or a request that times out (RSC13) on a client with the default fallback hosts, then expect that response or timeout to surface. The SDK correctly retries on a fallback host, where the mock has nothing queued, so the mock fails the retry ("nothing answered the request GET https://main.x.fallback.ably-realtime.com/..."). Observed.
- **Draft fix:** set `fallbackHosts: []` in those tests (as `rest/unit/RSC19e/network-error-propagated-0` does), or queue a response for every host. With `fallbackHosts: []` the two RSC19d tests pass and RSC13 gets the `HttpClient.Timeout` error ("... configured HttpClient.Timeout ... elapsing", code 50000; the `timeout` match is case-insensitive).
- **Test impact:** `RequestTests.RSC19d_ResponseStatusCode`, `RSC19d_ResponseSuccessIndicator`, `RestClientTests.RSC13_RequestTimeoutEnforced`, `RSC8e_UnsupportedContentType` are skipped placeholders. Behind the RSC8e stub there is an SDK deviation, observed with `fallbackHosts: []`: Case 2 (200 `text/html`) throws `ArgumentNullException` (`JsonHelper.DeserializeObject`, parsing a null text body) instead of the `statusCode` 400 / code 40013 error that `(RSC8e2)` requires ("In the case of a response with a `statusCode` less than 400, an error must be propagated back to the caller with a `statusCode` of 400, a `code` of `40013`"); Case 1 gives the 500 as `(RSC8e1)` requires. Open bug once the spec is fixed. The two `RequestTests.RSC19d_*` placeholders are inherited by `RealtimeTests.Unit.RealtimeRequestTests` (RTC9) and skipped the same way.

### RSC15f — the cached-fallback tests assume the first fallback host is `main.a`

- **Spec point:** RSC15f, RSC15a (tests `rest/unit/RSC15f/successful-fallback-cached-0`, `rest/unit/RSC15f/cached-fallback-expires-1`, `uts/rest/unit/fallback.md`).
- **What the features spec says:** `(RSC15a)` "try `fallback domains` in random order"; `(RSC15f)` "Once/if a given fallback host succeeds, the client should store that successful fallback host for `ClientOptions.fallbackRetryTimeout`".
- **What the UTS spec gets wrong:** the mock queues the fallback success only for `main.a.fallback.ably-realtime.com` (`queue_response_for_host`), but the first fallback host is any of `main.[a-e].fallback...`, so most runs send the fallback request to a host nothing answers. `rest/unit/RSC15f/expired-not-resurrected-2` avoids the assumption and passes.
- **Draft fix:** answer by role instead of host (a handler: the primary fails, any other host answers `[ms]`), as `expired-not-resurrected-2` does, and read the cached host from the first fallback request.
- **Test impact:** `FallbackTests.RSC15f_SuccessfulFallbackCached`, `RSC15f_CachedFallbackExpires` are skipped placeholders.

### RSC7d1 — the Ably-Agent test restricts the library name to `ably-[a-z]+`

- **Spec point:** RSC7d1 (test `rest/unit/RSC7d/ably-agent-header-format-0`, `uts/rest/unit/rest_client.md`).
- **What the features spec says:** `(RSC7d1)` "This must include at least the `<library name>/<library version>` entry plus, optionally, the `name[/version]` entry for underlying library layer or platform layers. An example would be `ably-flutter/1.2.0 ably-java/1.2.1 android/24`."
- **What the UTS spec gets wrong:** it matches `ably-[a-z]+/[0-9]+\.[0-9]+\.[0-9]+`, which rejects a library name with a hyphen; the SDK sends `ably-pubsub-dotnet/2.0.0 dotnet6/6.0.29 dotnet-macOS`.
- **Draft fix:** match `ably-[a-z-]+/[0-9]+\.[0-9]+\.[0-9]+`.
- **Test impact:** adapted to the features-permitted behaviour, not skipped: `RestClientTests.RSC7d_AblyAgentHeaderFormat` uses `ably-[a-z-]+/...` with a `// NOTE:`.

### RSC18 — the basic-auth-over-http test expects a construction-time error

- **Spec point:** RSC18 (test `rest/unit/RSC18/basic-auth-over-http-rejected-1`).
- **What the features spec says:** `(RSC18)` "Basic Auth over HTTP will result in an error as private keys cannot be submitted over an insecure connection". It doesn't say when.
- **What the UTS spec gets wrong:** it expects `Rest(...)` itself to throw. The SDK raises the error on the first authenticated request.
- **Draft fix:** assert the error on an authenticated request (for example `stats()`; `time()` sends no credentials, RSC16).
- **Test impact:** adapted to the features-permitted behaviour, not skipped: the test constructs the client and asserts the error on `StatsAsync()` with a `// NOTE:`. The error content is a separate SDK deviation, see "RSC18" under Failing Tests.

### RSC6a — the no-parameters stats test asserts the request omits every query parameter

- **Spec point:** RSC6a, RSC6b2-4 (test `rest/unit/RSC6a/no-params-clean-request-2`, `uts/rest/unit/stats.md`).
- **What the features spec says:** the IDL marks `direction`, `limit` and `unit` `api-default`: "`Type api-default value` denotes that the Ably server API uses those defaults and therefore it is unnecessary for the client library to send these default values to the API".
- **What the UTS spec gets wrong:** it asserts `request.query_params IS empty` after `stats()` with no arguments. Sending the defaults (the SDK sends `direction=backwards`, `limit=100`, `by=minute`) is permitted.
- **Draft fix:** assert each parameter "absent OR the default", as `direction-defaults-backwards-1`, `limit-defaults-to-100-1`, `unit-defaults-to-minute-1` and `rest_presence.md` do.
- **Test impact:** `StatsTests.RSC6a_NoParamsCleanRequest` is a skipped placeholder (the test's subject is the omission). The test is inherited by `RealtimeTests.Unit.RealtimeStatsTests` (RTC5), where it is skipped the same way.

### RTP8 — lifecycle test reads presence events by position, but a re-sync can legitimately add a PRESENT event

- **Spec point:** RTP8, RTP9, RTP10, RTP2a, RTP2b2, RTP2d1 (test `realtime/integration/RTP8/enter-update-leave-lifecycle-0`, `uts/realtime/integration/presence_lifecycle_test.md`).
- **What the features spec says:** `(RTP2a)` an incoming presence message is discarded only "if there is an existing message but it is `RTP2b`-newer than the incoming message"; `(RTP2b2)` ids `connid:msgSerial:index` compare by `msgSerial` then `index`, "larger being newer"; `(RTP2d1)` an `ENTER`, `UPDATE` or `PRESENT` that passes the newness check "must be emitted to `RealtimePresence` subscribers".
- **What the UTS spec gets wrong:** it waits with `poll_until(all_events.length >= N)` and asserts `all_events[0] == ENTER`, `all_events[1] == UPDATE`, `all_events[2] == LEAVE`. When client B receives a presence SYNC after the live ENTER (a late or server-initiated re-sync), the SYNC copy of the member carries the same id, is not older, and is emitted as `PRESENT` (`PresenceMap.Put`). The extra event shifts the indexes, and "2 events" no longer means the UPDATE arrived (the following `get()` check races the UPDATE). The sibling test `RTP4/bulk-enter-observed-0` already allows for this by counting `ENTER` and `PRESENT`.
- **Observed:** intermittent CI failure on the integration job, `Expected the enum to be PresenceAction.Update {value: 4}, but found PresenceAction.Present {value: 1}` at the `update_event.action` assertion (two first attempts in a row; both re-runs passed).
- **Draft fix:** poll for the specific action (`ANY e IN all_events: e.action == UPDATE`), and assert the order on the non-`PRESENT` events (`CONTAINS_IN_ORDER [ENTER, UPDATE, LEAVE]`, or filter `PRESENT` before indexing).
- **Test impact:** `RealtimeTests.Integration.PresenceLifecycleTests.RTP8_EnterUpdateLeaveLifecycle` adapted, not skipped: each wait polls for its action, and `PRESENT` events are filtered out before the positional assertions, with a `// NOTE:` naming RTP8 at each point; every assertion is otherwise verbatim.

### RSC17 — tests assert `RestClient#clientId`, which the features spec doesn't define

- **Spec point:** RSC17 (tests `rest/unit/RSC17/client-id-from-options-0`, `rest/unit/RSC17/client-id-matches-auth-1`, `uts/rest/unit/rest_client.md`).
- **What the features spec says:** `(RSC17)` "When instantiating a `RestClient`, if a `clientId` attribute is set in `ClientOptions`, then the `Auth#clientId` attribute will contain the provided `clientId`". The IDL's `class RestClient` has no `clientId`; only `RealtimeClient` has one (`clientId: String? // RTC17`).
- **What the UTS spec gets wrong:** both tests assert `client.clientId == "explicit-client-id"` and `client.clientId == client.auth.clientId` on a REST client, a member the features spec doesn't define. The requirement is on `Auth#clientId`.
- **Draft fix:** assert `client.auth.clientId == "explicit-client-id"` and drop the `client.clientId == client.auth.clientId` line (or move it to a realtime RTC17 test).
- **Test impact:** adapted, not skipped: `RestClientTests.RSC17_ClientIdFromOptions`, `RSC17_ClientIdMatchesAuth` assert `client.Auth.ClientId`, and the `client.clientId == client.auth.clientId` line is kept as a comment with a `// NOTE:`.

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
- **Test impact:** `AuthCallbackErrorsTests.RSA4e_RestCallbackError40170`, env-gated; and `AuthCallbackTests.RSA8d_CallbackErrorPropagated` (`rest/unit/RSA8d/callback-error-propagated-4`, spec-correct: `error.message` contains the callback's message; the wrapper's message is "Error calling AuthCallback, token request failed..." and the original lives in `Cause`), env-gated.
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

- **Spec point:** RTL2a, RTL4d, RTL5e, RTC8a1, RTL14 (tests `realtime/proxy/RTL14/channel-error-goes-failed-1`, `realtime/unit/RTL2a/state-change-events-emitted-0`, `realtime/unit/RTL2/filtered-event-subscription-0`, `realtime/unit/RTL2d/resumed-flag-propagated-2`, `realtime/unit/RTL5/detach-state-change-events-0`, `realtime/unit/RTL3a/other-states-unaffected-2`, `realtime/unit/RTL3d/init-detached-not-reattached-2`, `realtime/unit/RTL14/attached-to-failed-0`, `realtime/unit/RTC8a1/successful-reauth-update-event-0`).
- **What the features spec says:** features.md does not say whether the state-change event comes before or after the operation completes. The nearest points: `(RTL4d)` "A callback ... can be provided that is called when the channel next moves to one of `ATTACHED`, `DETACHED`, `SUSPENDED`, or `FAILED` states"; `(RTL5e)` "a callback can be provided that is called when the channel is detached successfully"; `(RTL2a)` "It emits a `ChannelState` `ChannelEvent` for every channel state change"; `(RTC8a1)` "The `Connection` should emit an `UPDATE` event per `RTN24`".
- **What the SDK does:** `RealtimeChannel.SetChannelState` runs `InternalStateChanged` (`Realtime/RealtimeChannel.cs:771`) before `RealtimeClient.NotifyExternalClients(StateChanged + Emit)` (`:774-786`). `InternalStateChanged`'s only subscriber is `ChannelAwaiter` (`Realtime/ChannelAwaiter.cs:152`), which completes `AttachAsync`/`DetachAsync` through `TaskWrapper` (`Transport/TaskWrapper.cs:10,18`, a TCS without `RunContinuationsAsynchronously`). `Connection.NotifyUpdate` likewise runs its internal handlers (`Realtime/Connection.cs:306`) before the public emit (`:308-320`). One of them is `ConnectionChangeAwaiter` (`Utils/ConnectionChangeAwaiter.cs:35`), which completes `AuthorizeAsync` (the other is RTL3d1 channel propagation, which must stay first). With no `CustomContext` and no synchronization context, as in xunit runs with parallelization disabled, the caller's continuation runs inline on the SDK workflow thread before the emit. So the test reads its recorded events too early ("Expected changes.Count >= 2, found 1", "Expected updateEvents.Count 1, found 0"), or a listener it registers afterwards still receives the late event. In parallel xunit runs `MaxConcurrencySyncContext` posts the continuation, and the tests usually pass.
- **Root cause (traced):** the UTS specs assume that once `AWAIT channel.attach()` / `detach()` / `authorize()` returns, the matching event has reached listeners registered earlier, and has not reached listeners registered afterwards. ably-js and ably-java both behave that way. In ably-java, `attach` registers a `ChannelStateCompletionListener` on the public emitter (`ChannelBase.java:262`), so it runs inside `setState`'s `emit` (`:174-177`) after listeners registered earlier, and `ConnectionWaiter` is a public `connection.on` listener (`ConnectionManager.java:532`). In ably-js, the `RealtimeChannel` emit override calls `super.emit` before `internalStateChanges.emit`. Here, a stack captured at the resume point of `await AttachAsync()` in a serial run: `RealtimeWorkflow.Consume` -> `ChannelMessageProcessor.MessageReceived:86` -> `RealtimeChannel.SetChannelState:771` -> `ChannelAwaiter.ChannelOnChannelStateChanged:172` -> `TaskWrapper.Callback:18` -> `RealtimeChannel.AttachAsync:371` -> the test. The public listener fired on the same thread once the test yielded. Moving `InternalStateChanged` after the emit fixed the 7 channel tests (5/5 serial each), but widened a race in `ChannelsSpecs.ReleaseAll_ShouldRemoveChannelWhenDetached`: the release callback now ran after the public DETACHED, so it was reverted. Full analysis and fix design: CHANNEL_AWAITER_ISSUE.md (repo root).
- **Test impact:** `ChannelStateEventsTests.RTL2a_StateChangeEventsEmitted`, `ChannelStateEventsTests.RTL2_FilteredEventSubscription`, `ChannelStateEventsTests.RTL2d_ResumedFlagPropagated`, `ChannelDetachTests.RTL5_DetachStateChangeEvents`, `ChannelConnectionStateTests.RTL3a_OtherStatesUnaffected`, `ChannelConnectionStateTests.RTL3d_InitDetachedNotReattached`, `ChannelErrorTests.RTL14_AttachedToFailed`, `RealtimeAuthorizeTests.RTC8a1_SuccessfulReauthUpdateEvent`, each `[DeviationFact("<its spec point>")]`. They fail when the unit tier runs serially and pass in the default parallel run.
  Proxy tier, same cause: `ChannelFaultsTests.RTL14_ChannelErrorGoesFailed` (`[DeviationFact("RTL14", Timeout = TestTimeoutMs)]`). It starts recording channel states right after `await channel.AttachAsync()`; when the continuation resumes before the public ATTACHED emit, the recorder also catches that ATTACHED. Observed in CI (proxy, ubuntu): `Expected channelStateChanges.Count to be 1, but found 2`; passed locally.
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

### RSC10 — request() is not retried with a renewed token after a token error

- **Spec point:** RSC10, RSA4b (test `rest/integration/RSC10/token-renewal-expired-jwt-0`, `uts/rest/integration/auth.md`).
- **What the spec says:** `(RSC10)` "If a REST request responds with a token error (401 HTTP status code and an Ably error value `40140 <= code < 40150`), then the Auth class is responsible for reissuing a token and the request should be reattempted"; `(RSA4b)` "the client should automatically make a single attempt to reissue the token and resend the request using the new token".
- **What the SDK does:** `client.Request("GET", "/channels/…")` with an `authCallback` whose first token is an expired JWT returns the server's 401 / 40142 "Token expired" response; the callback is called once, not twice, and the request is not retried (`Expected ((int)result.StatusCode) to be less than 300, but found 401`).
- **Root cause:** `PubSubHttpClient.HttpPaginatedRequestInternal` (the `Request` / `RequestV2` path) sets `request.NoExceptionOnHttpError = true`, so the 401 comes back as a response; the reauthorise-and-retry lives only in `PubSubHttpClient.ExecuteRequest`'s `catch (AblyException ex)` branch (`IsUnAuthorizedError && IsTokenError`), which is never entered. Other REST operations (which throw on HTTP errors) do renew.
- **Test impact:** `RestTests.Integration.AuthTests.RSC10_TokenRenewalExpiredJwt`, env-gated `[DeviationFact("RSC10")]` with the spec-correct assertions (request succeeds, callback invoked twice). The spec's `generate_jwt(expires_at: now() - 5_seconds)` is `GenerateJwt(…, expiresAt: DateTimeOffset.UtcNow.AddSeconds(-5))`.
- **Status:** open bug.

### RSH1b1 — deviceRegistrations.get() sends the local device's credentials for any deviceId

- **Spec point:** RSH1b1, RSH6 (tests `rest/integration/RSH1b3/save-and-get-device-0`, `RSH1b3/update-device-registration-1`, `RSH1b1/get-unknown-device-error-0`, `RSH1b4/remove-device-0`; `uts/rest/integration/push_admin.md`).
- **What the spec says:** `(RSH1b1)` "`#get(deviceId)` performs a request to /push/deviceRegistrations/:deviceId and returns a `DeviceDetails` object if the `deviceId` is found or results in a not found error if the device cannot be found. If the client has been activated as a push target device, and the specified `deviceId` is that of the present client, then this request must include push device authentication."
- **What the SDK does:** every `DeviceRegistrations.GetAsync(id)` carries the client's own local-device authentication (here `X-Ably-DeviceSecret`, as the device is not activated), whatever `id` is. The sandbox rejects it: 401 / 40100 "Incompatible deviceSecret specified for device access", so a get of a just-saved device throws instead of returning it, and a get of an unknown or removed device gives 401 instead of 404.
- **Root cause:** `PushAdmin` `IDeviceRegistrations.GetAsync` calls `AddDeviceAuthenticationToRequest(request, _restClient.Device)` unconditionally (contrast `IPushChannelSubscriptions.SaveAsync`, which adds it only when `subscription.DeviceId == _restClient.Device?.Id`). The integration factory gives each client a `LocalDevice` with a `DeviceSecret` (as the Device door does on a real device); a server-door client with no mobile device has no local device and is not affected.
- **Test impact:** `RestTests.Integration.PushAdminTests.RSH1b3_SaveAndGetDevice`, `RSH1b3_UpdateDeviceRegistration`, `RSH1b1_GetUnknownDeviceError`, `RSH1b4_RemoveDevice`, env-gated `[DeviationFact("RSH1b1")]` with the spec-correct assertions. The save, list, remove and removeWhere tests pass.
- **Status:** open bug.

### RSC15l4 — a `Server: CloudFront` 4xx response does not trigger fallback

- **Spec point:** RSC15l, RSC15l4 (test `rest/proxy/RSC15l4/cloudfront-header-fallback-0`, `uts/rest/integration/proxy/rest_fallback.md`).
- **What the spec says:** `(RSC15l)` "Errors that necessitate use of an alternative host include any of the following conditions"; `(RSC15l4)` "a response with a `Server: CloudFront` header and an HTTP status code `>= 400`".
- **What the SDK does:** with `fallbackHosts: ["localhost"]` and the proxy answering the first `/time` with 403 + `Server: CloudFront`, `client.time()` throws the parsed 403 / 40300 "Forbidden" at once; no fallback request is made.
- **Root cause:** `AblyHttpRequester.IsRetryableResponse` returns `ErrorInfo.IsRetryableStatusCode(response.StatusCode)` only (500–504); the `Server` header is never inspected.
- **Test impact:** `RestTests.Integration.Proxy.RestFallbackTests.RSC15l4_CloudfrontHeaderFallback`, env-gated `[DeviationFact("RSC15l4")]` with the spec-correct assertions.
- **Also affected:** `FallbackTests.RSC15l4_CloudfrontErrorTriggersFallback` (`rest/unit/RSC15l4/cloudfront-error-triggers-fallback-0`), env-gated `[DeviationFact("RSC15l4")]`: `time()` throws the 403 at once.
- **Status:** open bug.

### RSA5 / RSA6 — createTokenRequest() defaults ttl and capability client-side

- **Spec point:** RSA5, RSA6 (tests `rest/unit/RSA5/ttl-null-when-unspecified-0`, `rest/unit/RSA6/capability-null-when-unspecified-0`).
- **What the features spec says:** `(RSA5)` "If the user-provided `tokenParams` does not specify a TTL, the TTL field should be omitted from the `tokenRequest`, and Ably will supply a token with a TTL of 60 minutes"; `(RSA6)` "If the user-provided `tokenParams` does not specify capabilities, the `capability` field should be omitted from the `tokenRequest`".
- **What the SDK does:** the request carries `ttl` 1 h and `capability` `{"*":["*"]}` (`Did not expect a value, but found 1h.`, `Expected tokenRequest.Capability to be <null>, but found {"*":["*"]}`).
- **Root cause:** `AblyAuth.CreateTokenRequestAsync` falls back to `TokenParams.WithDefaultsApplied()` and `TokenRequest.Populate` applies `Defaults.DefaultTokenTtl` and `Capability.AllowAll`.
- **Test impact:** `TokenRequestParamsTests.RSA5_TtlNullWhenUnspecified`, `RSA6_CapabilityNullWhenUnspecified`, env-gated `[DeviationFact]`.
- **Status:** open bug.

### RSA15 — no clientId mismatch error for a token whose clientId differs from ClientOptions

- **Spec point:** RSA15a, RSA15c (test `rest/unit/RSA7/clientid-mismatch-error-1`, `uts/rest/unit/auth/client_id.md`).
- **What the features spec says:** `(RSA15a)` "Any `clientId` provided in `ClientOptions` must match any non wildcard (`'*'`) `clientId` value in `TokenDetails` ..."; `(RSA15c)` "... in the case of REST result in an appropriate error response".
- **What the SDK does:** with `clientId: "client-a"` and a `TokenDetails` for `client-b`, `channel.status()` succeeds (`Expected a <AblyException> to be thrown, but no exception was thrown`); no mismatch check exists in `AblyAuth`.
- **Test impact:** `ClientIdTests.RSA7_ClientidMismatchError`, env-gated `[DeviationTheory("RSA15a")]` with all 5 table rows; only row 2 fails, but the passing rows are gated too because a theory cannot be split.
- **Status:** open bug.

### RSA15b / RSA7b — a wildcard token overrides the ClientOptions clientId in Auth.clientId

- **Spec point:** RSA15b (test `rest/unit/RSA15b/wildcard-token-permits-any-0`).
- **What the features spec says:** `(RSA15b)` "If the clientId from `TokenDetails` or `connectionDetails` contains only a wildcard string '\*', then the client is permitted to be either unidentified ... or identified by providing a `clientId` when communicating with Ably"; `(RSA7b1)` Auth#clientId is not null when "A `clientId` is provided in the `ClientOptions`".
- **What the SDK does:** with `clientId: "any-client"` and a `"*"` token, `Auth.ClientId` is `"*"` (`Expected client.Auth.ClientId to be "any-client" ... but "*"`).
- **Root cause:** `AblyAuth.ClientId` prefers `CurrentToken.ClientId` over `Options.GetClientId()`.
- **Test impact:** `ClientIdTests.RSA15b_WildcardTokenPermitsAny`, env-gated.
- **Status:** open bug.

### RSA16d — Auth#tokenDetails stays set after a failed renewal

- **Spec point:** RSA16d (test `rest/unit/RSA16d/null-after-invalidation-2`).
- **What the features spec says:** `(RSA16d)` "Is `null` if there is no current token, including after a previous token has been determined to be invalid or expired, or if the library is using basic auth".
- **What the SDK does:** after the server rejects the token (40142) and the renewal fails, `AblyAuth.CurrentToken` still holds the first token (the test reads it as described under the RSA16 Adapted Tests entry).
- **Root cause:** `AblyAuth` never clears `CurrentToken` when a token error or a failed renewal occurs.
- **Test impact:** `TokenDetailsTests.RSA16d_NullAfterInvalidation`, env-gated.
- **Status:** open bug.

### RSA8d / RSA4f — a token string returned by authCallback is parsed as a TokenRequest

- **Spec point:** RSA8d, RSA4f (tests `rest/unit/RSA8d/callback-returns-jwt-1`, `rest/unit/RSA16b/token-string-from-callback-1`).
- **What the features spec says:** `(RSA8d)` "... expects either a token string, a `TokenDetails` object or a `TokenRequest` object to be returned"; `(RSA4f)` the object is invalid only if "neither a `String`, `JsonObject`, `TokenRequest` object, nor `TokenDetails` object".
- **What the SDK does:** the request fails with 80019 whose cause is a `JsonReaderException` ("AuthCallback returned a string which can't be converted to TokenRequest").
- **Root cause:** `AblyAuth.RequestTokenAsync` sends a returned `string` through `GetTokenRequest`, which deserialises it as `TokenRequest` JSON, so a raw token or JWT never works (`rest-mapping.md` section 9).
- **Test impact:** `AuthCallbackTests.RSA8d_CallbackReturnsJwt`, `TokenDetailsTests.RSA16b_TokenStringFromCallback`, env-gated.
- **Status:** open bug.

### RSA8c — an authUrl JSON TokenDetails without `issued` is treated as a TokenRequest

- **Spec point:** RSA8c (tests `rest/unit/RSA4/authurl-triggers-token-3`, `rest/unit/RSA8c/authurl-invoked-for-auth-0`, `rest/unit/RSA4b/renewal-via-authurl-2`).
- **What the features spec says:** `(RSA8c)` the token is "taken to be a `TokenRequest` or `TokenDetails` object if the response has `Content-Type` `"application/json"`".
- **What the SDK does:** an authUrl response `{"token": ..., "expires": ...}` is treated as a `TokenRequest` and exchanged at `/keys/<keyName>/requestToken` (the next captured request has no `Authorization` header; in the renewal test the exchange response fails to deserialise).
- **Root cause:** `AblyAuth.RequestTokenAsync` uses `TokenDetails.IsToken(json)`, which requires an `issued` member.
- **Test impact:** `AuthSchemeTests.RSA4_AuthUrlTriggersToken`, `AuthCallbackTests.RSA8c_AuthUrlInvokedForAuth`, `TokenRenewalTests.RSA4b_RenewalViaAuthurl`, env-gated.
- **Status:** open bug.

### RSA8c — a failing authUrl request is retried

- **Spec point:** RSA8c (test `rest/unit/RSA8c/authurl-error-propagated-5`).
- **What the features spec says:** `(RSA8c)` only defines how the authUrl is queried; it does not say whether a failed query is retried. The UTS spec asserts a single authUrl request after its 500.
- **What the SDK does:** the authUrl is requested 4 times (the request goes through the same retrying HTTP requester as Ably requests, `HttpMaxRetryCount`), then fails.
- **Test impact:** `AuthCallbackTests.RSA8c_AuthUrlErrorPropagated`, env-gated.
- **Status:** open bug (to be confirmed with the SDK team): the SDK applies its Ably-host retry policy (`HttpMaxRetryCount`) to a custom authUrl, and features.md defines no retry for authUrl.

### RSL8 — `status()` does not URI-encode the channel name in the path

- **Spec point:** RSL8 (test `rest/unit/RSL8/status-special-chars-encoded-1`, `uts/rest/unit/channel/rest_channel_attributes.md`).
- **What the spec says:** the UTS test asserts the path ends with `"/channels/" + encode_uri_component("namespace:my channel")`, i.e. `/channels/namespace%3Amy%20channel`. `(RSL8)` itself says only "makes a http get request to `<restHost>/channels/<channelId>`" and does not state an encoding rule (classification to be confirmed: SDK deviation versus UTS over-specification, since `:` is legal in a path segment).
- **What the SDK does:** `HttpChannel.StatusAsync` builds `"/channels/" + Name` without `EncodeUriPart` (every other channel endpoint encodes); the path sent is `/channels/namespace:my%20channel` (the space is escaped by `System.Uri`, the colon is not).
- **Test impact:** `RestChannelAttributesTests.RSL8_StatusSpecialCharsEncoded`, env-gated `[DeviationFact]` with the spec-correct assertion.
- **Status:** open bug (inconsistent with the other channel endpoints), pending confirmation.

### TI — `ErrorInfo` has no value equality

- **Spec point:** TI (test `rest/unit/TI/error-equality-5`).
- **What the spec says:** the UTS test requires two `ErrorInfo` objects with the same code, statusCode and message to be equal and one with a different code to differ. The features spec (TI1-TI5) is silent on equality, so the UTS is the requirement.
- **What the SDK does:** `ErrorInfo` overrides neither `Equals` nor `GetHashCode` (reference equality): `Expected error1 to be [ErrorInfo ...] because same content, but found [ErrorInfo ...]`.
- **Root cause:** `Types/ErrorInfo.cs`.
- **Test impact:** `ErrorTypesTests.TI_ErrorEquality` is env-gated (`[DeviationFact("TI")]`).
- **Status:** open bug (low priority).

### TG — a `Link` header URL without a query string crashes the parser

- **Spec point:** TG (test `rest/unit/TG/link-header-parsing-1`).
- **What the spec says:** a Link header such as `</path>; rel="first"`, alone or after `</path?cursor=abc>; rel="next"`, is parsed to the right pagination state (`hasNext` false or true). `(TG2)` "Instantiating this type should not result in an error if paging headers are not returned from the REST API"; features is silent on the link URL's form, so the UTS is the requirement.
- **What the SDK does:** `PaginatedRequestParams.GetLinkQuery` does `url.Split('?')[1]`, so a link without `?` throws `IndexOutOfRangeException` while the `PaginatedResult` is built.
- **Root cause:** `PaginatedRequestParams.cs` (`GetLinkQuery`).
- **Test impact:** `PaginatedResultTests.TG_LinkHeaderParsing` is env-gated (`[DeviationFact("TG")]`); it fails on its second case. `PaginatedResultTests.TG4_FirstReturnsFirstPage` (`rest/unit/TG4/first-returns-first-page-0`) is gated the same way: its mock's first response already carries the query-less `</channels/test/messages>; rel="first"` link, so the `IndexOutOfRangeException` is thrown while page 1 is built (the initial `channel.history()`), before `next()` or the `first` re-query is reached (see the TG4 / TG5 entry under Adapted Tests).
- **Status:** open bug.

### TP3 — `PresenceMessage` JSON deserialisation does not decode `data` / `encoding`

- **Spec point:** TP3 (test `rest/unit/TP3/presence-encoded-data-from-json-1`).
- **What the spec says:** "Deserialization must decode data based on the encoding field" (`json` gives an object, `base64` gives bytes, and the encoding is consumed). The features spec does not define a `fromJson`; the decoding operation it defines for presence messages is `fromEncoded` (TP4; not public in this SDK, and the TP4 test reaches it through the internal `MessageHandler.FromEncoded`, see Adapted Tests), so the UTS is the requirement for this API.
- **What the SDK does:** `JsonHelper.DeserializeObject<PresenceMessage>` copies `data` and `encoding` as received (the `json` case leaves the string, so `JToken.DeepEquals` is false).
- **Test impact:** `PresenceMessageTypesTests.TP3_PresenceEncodedDataFromJson` is env-gated (`[DeviationFact("TP3")]`).
- **Status:** open (resolved by adding `PresenceMessage.FromEncoded`, TP4).

### RSL6a / TM3 — a `json` decode after `base64` fails

- **Spec point:** RSL6a, TM3 (tests `rest/unit/RSL6a/decode-chained-encodings-2`, `rest/unit/TM3/from-encoded-decodes-encoding-1`).
- **What the features spec says:** `(RSL6a)` "All messages received will be decoded automatically based on the `encoding` field (by inverting the encoding rules of RSL4 ...) and the payloads will be converted into the format they were originally sent using i.e. binary, string, or JSON-encodable object or array"; `(TM3)` the same decoding "as specified in `RSL6`".
- **What the SDK does:** `JsonEncoder.Decode` only handles a `string` payload. After the `base64` decoder the payload is `byte[]`, so `json/base64` leaves `Data` as bytes with `Encoding == "json"` (history), and `Message.FromEncoded` throws `Invalid Json data: 'System.Byte[]'` (50000). `json/utf-8/base64` works because the `utf-8` decoder produces the string first.
- **Root cause:** `MessageEncoders/JsonEncoder.cs` (no byte[] to UTF-8 string conversion).
- **Test impact:** `MessageEncodingTests.RSL6a_DecodeChainedEncodings` and `MessageTypesTests.TM3_FromEncodedDecodesEncoding` are env-gated (`[DeviationFact("RSL6a")]`, `[DeviationFact("TM3")]`); also `RestPresenceTests.RSP5_DecodeChainedEncoding` (`rest/unit/RSP5/decode-chained-encoding-5`, presence `json/base64`: `Data` stays `byte[]`) is env-gated (`[DeviationFact("RSL6a")]`).
- **Status:** open bug.

### RSH1a — publish() accepts an empty recipient or empty data

- **Spec point:** RSH1a (tests `rest/unit/RSH1a/rejects-empty-recipient-3`, `rest/unit/RSH1a/rejects-empty-data-4`, `uts/rest/unit/push/push_admin_publish.md`).
- **What the spec says:** `(RSH1a)` "Empty values for `recipient` or `data` should be immediately rejected." The tests expect error 40000 and no HTTP request for `{}` as recipient and for `{}` as data.
- **What the SDK does:** `PushAdmin.PublishAsync` rejects only `null` (the null-recipient test passes); an empty `JObject` is sent as a request.
- **Root cause:** `PushAdmin.PublishAsync` `ValidateRequest` checks `is null`, not emptiness.
- **Test impact:** `PushAdminPublishTests.RSH1a_RejectsEmptyRecipient` and `RSH1a_RejectsEmptyData` are env-gated (`[DeviationFact("RSH1a")]`).
- **Status:** open bug.

### RSH6a — push device authentication sends `X-Ably-DeviceIdentityToken`, not `X-Ably-DeviceToken`

- **Spec point:** RSH6a, RSH7a3, RSH7c3 (tests `rest/unit/RSH7a2/subscribe-device-post-0`, `rest/unit/RSH7c2/unsubscribe-device-delete-0`, `uts/rest/unit/push/push_channels.md`).
- **What the spec says:** `(RSH6a)` "push device authentication is performed for a request by adding an `X-Ably-DeviceToken` request header whose value is the `deviceIdentityToken`. This header has always been `X-Ably-DeviceToken`, but has previously been mistakenly documented as `X-Ably-DeviceIdentityToken` ... It was never renamed."
- **What the SDK does:** the request carries `X-Ably-DeviceIdentityToken` (`Defaults.DeviceIdentityTokenHeader`), so `request.headers["X-Ably-DeviceToken"]` is absent (`KeyNotFoundException`).
- **Root cause:** `PushAdmin.AddDeviceAuthenticationToRequest` / `Defaults.DeviceIdentityTokenHeader`.
- **Test impact:** `PushChannelsTests.RSH7a2_SubscribeDevicePost` and `RSH7c2_UnsubscribeDeviceDelete` are env-gated (`[DeviationFact("RSH6a")]`); the other assertions in them (method, path, body, query) pass.
- **Status:** open bug.

### RSH1b1 — deviceRegistrations.get() does not URI-encode the deviceId in the path

- **Spec point:** RSH1b1 (test `rest/unit/RSH1b1/get-url-encodes-deviceid-2`).
- **What the spec says:** the UTS test asserts the path is `"/push/deviceRegistrations/" + encode_uri_component("device/with special:chars")`, i.e. `/push/deviceRegistrations/device%2Fwith%20special%3Achars`. `(RSH1b1)` says only "performs a request to /push/deviceRegistrations/:deviceId"; features is silent on encoding, so the UTS is the requirement (same ruling as RSL8 above).
- **What the SDK does:** the path sent is `/push/deviceRegistrations/device/with%20special:chars`: the id is concatenated unencoded, so a `/` in an id adds a path segment.
- **Root cause:** `PushAdmin` `IDeviceRegistrations.GetAsync` (and `RemoveAsync`, `SaveAsync`) build `"/push/deviceRegistrations/" + deviceId` without `EncodeUriPart`.
- **Test impact:** `PushDeviceRegistrationsTests.RSH1b1_GetUrlEncodesDeviceid` is env-gated (`[DeviationFact("RSH1b1")]`).
- **Status:** open bug: the deviceId is not URI-encoded in the path, unlike other SDK paths (same principle as RSL8).

### RSC7c — `addRequestIds` sends the request id as a header, not the `request_id` query parameter

- **Spec point:** RSC7c (test `rest/unit/RSP3/get-request-id-enabled-6`).
- **What the spec says:** `(RSC7c)` "If the `addRequestIds` client option is enabled, every REST request to Ably should include in a `request_id` query string parameter a random string ...".
- **What the SDK does:** no `request_id` appears in the query (`AblyRequest.AddHeaders` sends it as a request header).
- **Test impact:** `RestPresenceTests.RSP3_GetRequestIdEnabled` is env-gated (`[DeviationFact("RSC7c")]`).
- **Also affected:** `RestClientTests.RSC7c_RequestIdIncluded`, `RSC7c_RequestIdPreservedFallback` (`rest/unit/RSC7c/request-id-included-0`, `request-id-preserved-fallback-1`), env-gated `[DeviationFact("RSC7c")]`: `request_id` is absent from the query in both.
- **Status:** open bug.

### RSC15a — when every host fails with 5xx, the error has no statusCode

- **Spec point:** RSC15a, TI1 (test `rest/unit/RSP3/get-server-error-3`; classification pending).
- **What the spec says:** the UTS test stubs every request with 500 / `{"error": {"code": 50000, "statusCode": 500, ...}}` and expects the thrown error to have `code` 50000 and `statusCode` 500. `(RSC15a)` says fallback domains are tried "failing when all have been tried"; features is silent on the resulting error, so the UTS is the requirement (the same ruling as the other silent cases above). `(TI1)` an `ErrorInfo` has `statusCode` (analogous to HTTP status code).
- **What the SDK does:** a 5xx is retried on the fallback hosts; when they all fail the SDK throws a generic `ErrorInfo` ("Error executing request, exceeded max no. of retries", code 50000) with `statusCode` null, discarding the server's error.
- **Root cause:** `AblyHttpRequester.Execute`, the final `throw` after the retry loop.
- **Test impact:** `RestPresenceTests.RSP3_GetServerError` and `TimeTests.RSC16_ErrorPropagated` (the mock answers every request with a 500) are env-gated (`[DeviationFact("RSC15a")]`). A 401 and a 404 (not retryable) propagate correctly in the sibling tests. `RSC16_ErrorPropagated` is inherited by `RealtimeTests.Unit.RealtimeTimeTests` (RTC6) and fails the same way there under `RUN_DEVIATIONS=1`.
- **Status:** open bug: after every host returns 5xx the surfaced error has statusCode null, although each failure was a 500 response (features RSC15a defines only the fallback order and failure after all hosts are tried; the UTS is the requirement for the error content).

### RSC19f — a `request()` path without a leading slash produces an invalid URI

- **Spec point:** RSC19f (test `rest/unit/RSC19f/path-leading-slash-handling-4`, `uts/rest/unit/request.md`).
- **What the spec says:** `(RSC19f)` "`path` is the path component of the URL such as `"/channels"`". The features spec says nothing about a path without the leading slash, so the UTS is the requirement: `channels/test` must be sent as `/channels/test`.
- **What the SDK does:** `request(GET, "channels/test")` fails with `Invalid URI: Invalid port specified` (code 50000); the path is appended to the host unchanged.
- **Root cause:** `AblyHttpRequester` builds the URI as `{protocol}{host}{port}{request.Url}` without normalising the path. The case with a leading slash passes.
- **Test impact:** `RequestTests.RSC19f_PathLeadingSlashHandling`, env-gated `[DeviationFact("RSC19f")]` with the spec-correct assertions. Inherited by `RealtimeTests.Unit.RealtimeRequestTests` (RTC9, through `client.HttpClient.Request`), same outcome.
- **Status:** open bug.

### HP7 — `errorMessage` is populated from `X-Ably-Errorcode`, not `X-Ably-Errormessage`

- **Spec point:** HP7 (test `rest/unit/RSC19d/response-error-message-header-3`, `uts/rest/unit/request.md`).
- **What the spec says:** `(HP7)` "`errorMessage` is a string attribute populated with the error code if the `X-Ably-Errormessage` HTTP header is sent in the response". The wording says "the error code", which reads as a typo; the UTS reads it as the header's value ("Token expired").
- **What the SDK does:** with `X-Ably-Errorcode: 40101` and `X-Ably-Errormessage: Token expired`, `HttpPaginatedResponse.ErrorMessage` is `"40101"`.
- **Root cause:** the `HttpPaginatedResponse` constructor reads the `X-Ably-Errorcode` header for both `ErrorCode` and `ErrorMessage`.
- **Test impact:** `RequestTests.RSC19d_ResponseErrorMessageHeader`, env-gated `[DeviationFact("HP7")]`. Inherited by `RealtimeTests.Unit.RealtimeRequestTests` (RTC9), same outcome.
- **Status:** open bug (classification note: the features wording "populated with the error code" is itself ambiguous, so the spec text may need a fix first).

### HP8 — `HttpPaginatedResponse.Headers` holds only the HTTP response headers, not the content headers

- **Spec point:** HP8 (test `rest/unit/RSC19d/response-headers-accessible-4`, `uts/rest/unit/request.md`).
- **What the spec says:** `(HP8)` "`headers` is an Array of key value pairs for each response header".
- **What the SDK does:** `Headers` is the `HttpResponseMessage.Headers` collection; `Content-Type` (a content header) is not in it, and `Headers.GetValues("Content-Type")` throws `InvalidOperationException: Misused header name, 'Content-Type'`.
- **Root cause:** `AblyHttpRequester.GetAblyResponse` copies `response.Headers` only, never `response.Content.Headers`.
- **Test impact:** `RequestTests.RSC19d_ResponseHeadersAccessible`, env-gated `[DeviationFact("HP8")]`. The other two headers in the test were not reached. Inherited by `RealtimeTests.Unit.RealtimeRequestTests` (RTC9), same outcome.
- **Status:** open bug.

### RSC6b4 — `stats()` sends the unit as `by`, not `unit`

- **Spec point:** RSC6b4 (tests `rest/unit/RSC6b4/unit-param-values-0`, `rest/unit/RSC6b/all-params-combined-0`, `uts/rest/unit/stats.md`).
- **What the spec says:** `(RSC6b4)` "`unit` is the period for which the stats will be aggregated by, values supported are `minute`, `hour`, `day` or `month`; if omitted the unit defaults to the REST API default (`minute`)".
- **What the SDK does:** `StatsRequestParams` sends the unit as the query parameter `by`, not `unit`.
- **Root cause:** `StatsRequestParams.GetParameters` adds `by`.
- **Test impact:** `StatsTests.RSC6b4_UnitParamValues`, `RSC6b_AllParamsCombined` (key `unit` absent), env-gated `[DeviationFact("RSC6b4")]`. The "absent or default" tests (`direction-defaults-backwards-1`, `limit-defaults-to-100-1`, `unit-defaults-to-minute-1`) pass. Both are inherited by `RealtimeTests.Unit.RealtimeStatsTests` (RTC5), same outcome.
- **Status:** open bug.

### TS12c — `Stats.IntervalGranularity` (the TS12c `unit`) reads `intervalGranularity`, not the `unit` property

- **Spec point:** TS12c (test `rest/unit/RSC6a/returns-paginated-stats-0`, `uts/rest/unit/stats.md`).
- **What the spec says:** `(TS12c)` "`unit` (property present in the JSON) - a `String` or (if idiomatic) a value of the enumerable type `StatsIntervalGranularity` ... This must be from the `unit` property of the JSON, not calculated from the `intervalId`".
- **What the SDK does:** `Stats.IntervalGranularity` is the TS12c member: TS12c allows the `StatsIntervalGranularity` enum, so the different name is idiomatic. But it is deserialised from the pre-2.1 `intervalGranularity` field, not from `unit`, so for `{"intervalId": "2024-01-01:00:00", "unit": "hour", ...}` it is `Minute` (the enum default).
- **Test impact:** `StatsTests.RSC6a_ReturnsPaginatedStats`, env-gated `[DeviationFact("TS12c")]`, asserts `IntervalGranularity == Hour` (with a `// NOTE:`). Inherited by `RealtimeTests.Unit.RealtimeStatsTests` (RTC5), same outcome.
- **Status:** open bug: `IntervalGranularity` reads the wrong JSON field.

### TO3b / TO3c2 — no debug log message mentions "HTTP request"

- **Spec point:** TO3b, TO3c2 (tests `rest/unit/TO3b/log-level-changeable-0`, `rest/unit/TO3c2/context-contains-expected-keys-0`, `uts/rest/unit/logging.md`).
- **What the spec says:** `(TO3b)` "`logLevel` - controls the level of verbosity of log messages from the library. The implementation of this is likely to vary by platform"; `(TO3c)` "`logHandler` ... The implementation of this is likely to vary by platform". The features spec doesn't define log message texts, so the UTS is the requirement: a debug log containing "HTTP request" for a `time()` call.
- **What the SDK does:** at `LogLevel.Debug` the log has `Sending GET request to /time` and `Executing request. Host: main.realtime.ably.net. Request: /time.`, nothing containing "HTTP request".
- **Test impact:** `LoggingTests.TO3b_LogLevelChangeable` (the Debug-level logs exist; the message match fails) and `TO3c2_ContextContainsExpectedKeys`, env-gated `[DeviationFact("TO3b")]` / `[DeviationFact("TO3c2")]`. The SDK has no Info or Verbose level, so "info" and "verbose" are read as `Debug` (`rest-mapping.md` section 3), and the `context` argument assertions (TO3c, TO3c2) are commented under `// NOTE:` (README Known gaps).
- **Status:** open (message text): decide whether the SDK should log "HTTP request" or the spec should drop the match.

### RSC18 — Basic auth over HTTP raises code 500 "Current action cannot be performed over http", not 40103 / "insecure"

- **Spec point:** RSC18 (test `rest/unit/RSC18/basic-auth-over-http-rejected-1`, `uts/rest/unit/rest_client.md`).
- **What the spec says:** `(RSC18)` "If false, all communication is over HTTP however [Basic Auth] over HTTP will result in an error as private keys cannot be submitted over an insecure connection". The features spec names no error code; the UTS expects `error.code == 40103` or a message containing "insecure" or "TLS", and the UTS is the requirement.
- **What the SDK does:** the first authenticated request throws `AblyInsecureRequestException` with code 500 and the message "Current action cannot be performed over http" (neither 40103 nor "insecure"/"TLS"). The timing (first authenticated request, not construction) is adapted inline, see "RSC18 — the basic-auth-over-http test expects a construction-time error" (UTS Spec Errors).
- **Root cause:** `AblyAuth.EnsureSecureConnection` throws `new AblyInsecureRequestException()`, whose default message and code are generic.
- **Test impact:** `RestClientTests.RSC18_BasicAuthOverHttpRejected`, env-gated `[DeviationFact("RSC18")]`; the "token auth over HTTP is allowed" part of the test is therefore only reached when the gate is on.
- **Status:** open bug.

## Missing SDK Public API / Features

### CHM2g / CHM2h — `ChannelMetrics#objectPublishers` and `ChannelMetrics#objectSubscribers` are missing

- **Spec point:** CHM2g, CHM2h (tests `rest/unit/CHM2/parses-all-metrics-fields-0`, `rest/unit/CHM2/zero-and-missing-metrics-1`, `uts/rest/unit/channel/rest_channel_attributes.md`).
- **What the features spec says:** `ChannelMetrics` has `objectPublishers` (CHM2g) and `objectSubscribers` (CHM2h).
- **What the SDK does:** `ChannelMetrics` has only `Connections`, `Publishers`, `Subscribers`, `PresenceConnections`, `PresenceMembers`, `PresenceSubscribers`.
- **Test impact:** the two tests run; the object* assertions (2 and 2) are commented out under the `// Missing SDK public API: ChannelMetrics#objectPublishers (CHM2g)` skip-reason comment (`#objectSubscribers`, CHM2h, for the other). The second test's "missing fields default to 0" check for CHM2g/h is therefore not exercised.
- **Status:** not implemented.

### REC3b — `ClientOptions#connectivityCheckUrl` is missing

- **Spec point:** REC3b (test `rest/unit/REC3b/custom-connectivity-check-url-0`, `uts/rest/unit/fallback.md`).
- **What the features spec says:** `(REC3b)` "The `connectivity check URL` may be specified explicitly by the `connectivityCheckUrl` option".
- **What the SDK does:** `ClientOptions` has no such option; the internet check always uses the constant `Defaults.InternetCheckUrl` (`PubSubHttpClient.CanConnectToAbly`), with no internal setting to override it, so there is no equivalent.
- **Test impact:** `FallbackTests.REC3b_CustomConnectivityCheckUrl` is a skipped stub carrying the spec lines as comments.
- **Status:** not implemented.

### RSA17 — `Auth#revokeTokens` is missing

- **Spec point:** RSA17, RSA17b–RSA17g, TRS2, TRF2. Tests (4): `rest/integration/RSA17g/revoke-token-prevents-use-0`, `RSA17d/token-auth-revoke-rejected-0`, `RSA17e/issued-before-reauth-margin-0`, `RSA17c/mixed-success-failure-0` (`RestTests.Integration.RevokeTokensTests`).
- **What the features spec says:** `(RSA17g)` "Takes a `TokenRevocationTargetSpecifier` or an array of `TokenRevocationTargetSpecifier`s and sends them in a POST request to /keys/{API_KEY_NAME}/revokeTokens".
- **What the SDK does:** `IAblyAuth` has no `revokeTokens` and there are no `TokenRevocationTargetSpecifier` / `TokenRevocationSuccessResult` / `TokenRevocationFailureResult` types (`rest-mapping.md` section 8).
- **Test impact:** 4 skipped stubs carrying the spec lines as comments; plus the 17 unit stubs in `RestTests.Unit.RevokeTokensTests` (`rest/unit/RSA17g`, `RSA17b` x2, `RSA17c` x3, `TRS2`, `TRF2`, `RSA17d` x2, `RSA17e` x2, `RSA17f` x3, `RSA17/server-error-propagated-0`, `RSA17/request-uses-basic-auth-0`).
- **Status:** not implemented.

### RSC22 — `RestClient#batchPublish` is missing

- **Spec point:** RSC22, RSC22c, RSC22d, BSP2, BPR2, BPF2 (28 unit tests in `uts/rest/unit/batch_publish.md`, `RestTests.Unit.BatchPublishTests`).
- **What the features spec says:** `(RSC22c)` "Takes a `BatchPublishSpec` or an array of `BatchPublishSpec`s and sends then in a POST request to `/messages`."
- **What the SDK does:** `PubSubHttpClient` has no `batchPublish` and no `BatchPublishSpec` / `BatchResult` / `BatchPublishSuccessResult` / `BatchPublishFailureResult` types, so every test depends on it.
- **Test impact:** 28 skipped stubs carrying the spec lines as comments.
- **Status:** not implemented.

### RSC24 — `RestClient#batchPresence` is missing

- **Spec point:** RSC24, BGR2, BGF2. Tests (3): `rest/integration/RSC24/batch-presence-multiple-channels-0`, `RSC24/restricted-key-channel-failure-1`, `RSC24/empty-channel-presence-2` (`RestTests.Integration.BatchPresenceTests`).
- **What the features spec says:** `(RSC24)` "`RestClient#batchPresence` function takes an array of channel name strings and sends them as a comma separated string in the `channels` query parameter in a GET request to `/presence`, returning a `BatchResult<BatchPresenceSuccessResult | BatchPresenceFailureResult>` object."
- **What the SDK does:** `PubSubHttpClient` has no `batchPresence` and there are no `BatchResult` / `BatchPresenceSuccessResult` / `BatchPresenceFailureResult` types (`rest-mapping.md` section 8), so every test depends on it.
- **Test impact:** 3 skipped stubs carrying the spec lines as comments; plus the 13 unit stubs in `RestTests.Unit.BatchPresenceTests` (`rest/unit/RSC24` x7: `get-presence-channels-param-0`, `single-channel-param-0`, `special-chars-comma-joined-0`, `mixed-success-failure-results-0`, `server-error-propagated-0`, `auth-error-propagated-0`, `uses-configured-auth-0`; `BAR2` x3; `BGR2` x2; `BGF2` x1).
- **Status:** not implemented.

### RSL1i — `ClientOptions#maxMessageSize` and the 40009 size check are missing

- **Spec point:** RSL1i, TO3l8 (test `rest/unit/RSL1i/message-size-limit-0`, `uts/rest/unit/channel/publish.md`).
- **What the features spec says:** messages larger than `maxMessageSize` are rejected before transmission with error code 40009.
- **What the SDK does:** `ClientOptions` has no `MaxMessageSize` (only `ConnectionDetails.MaxMessageSize`, a server-sent value) and `PublishAsync` performs no size check.
- **Test impact:** `PublishTests.RSL1i_MessageSizeLimit` is a skipped stub carrying the spec lines as comments. `OptionsTypesTests.TO3_ClientOptionsAttributes` runs, with its `options.maxMessageSize == 65536` assertion (TO3l8) commented out under the `// Missing SDK public API: ClientOptions#maxMessageSize (TO3l8)` skip-reason comment.
- **Searched for an equivalent:** `ConnectionDetails.MaxMessageSize` is the server-sent realtime limit, not a client option, and there is no size calculation (TM6) or 40009 check on the REST publish path (`ErrorCodes.MaxMessageLengthExceeded` is defined but never used on it).
- **Status:** not implemented.

### RSL1l1 — the `params` argument of `RestChannel#publish` is missing

- **Spec point:** RSL1l, RSL1l1. Test: `rest/integration/RSL1l1/publish-params-force-nack-0` (`RestTests.Integration.PublishTests`).
- **What the features spec says:** `(RSL1l)` "The `publish(Message)` and `publish(Message[])` forms of the method should take an extra `Dict<String, Stringifiable>` argument. These parameters should be encoded using normal querystring-encoding and sent as part of the query string of the REST publish."; `(RSL1l1)` "Publish params can be tested by publishing with a `_forceNack=true` parameter, which will result in the publish being rejected with a `40099` error code".
- **What the SDK does:** `IHttpChannel.PublishAsync` has no overload taking publish params, so `_forceNack` can't be sent.
- **Test impact:** 1 skipped stub carrying the spec lines as comments; plus the unit stub `RestTests.Unit.PublishTests.RSL1l_ParamsAsQuerystring` (`rest/unit/RSL1l/params-as-querystring-0`).
- **Status:** not implemented.

### RTL22 — `RealtimeChannel#subscribe(MessageFilter, …)` is missing

- **Spec point:** RTL22. Tests (`ChannelSubscribeTests`, 4): `realtime/unit/RTL22a/filter-matching-ref-timeserial-1`, `RTL22a/filter-matching-clientid-2`, `RTL22b/filter-isref-false-0`, `RTL22c/filter-multiple-criteria-0`. (`RTL22a/filter-matching-name-0` runs through `Subscribe(name, …)`; see the RTL22a entry under Adapted Tests.)
- **What the features spec says:** "`(RTL22)` Methods must be provided for attaching and removing a listener which only executes when the message matches a set of criteria."
- **What the SDK does:** no `Subscribe` overload takes a `MessageFilter` (`realtime-mapping.md` §12), and nothing filters on `extras.ref.timeserial`, `extras.ref.type`, `isRef` or `clientId`, so these 4 tests have no equivalent.
- **Test impact:** 4 skipped stubs (`Skip = "Missing SDK public API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** Not present in ably-java or ably-cocoa either (no `MessageFilter`).
- **Status:** not implemented.

### RTN26 / RTL25 — `Connection#whenState` and `RealtimeChannel#whenState` are missing

- **Spec point:** RTN26, RTL25. Tests: `WhenStateTests` (6: `realtime/unit/RTN26a/immediate-callback-current-state-0`, `RTN26b/deferred-callback-future-state-0`, `RTN26b/fires-only-once-1`, `RTN26a/multiple-whenstate-calls-1`, `RTN26a/no-fire-for-past-state-2`, `RTN26/whenstate-different-states-0`) and `ChannelWhenStateTests` (4: `RTL25a/resolves-immediately-current-0`, `RTL25b/waits-for-state-change-0`, `RTL25b/fires-once-only-1`, `RTL25a/past-state-does-not-resolve-1`).
- **What the features spec says:** "`(RTN26)` `Connection#whenState` function" — "`(RTN26a)` If the connection is already in the given state, calls the listener with a \`null\` argument"; "`(RTL25)` `RealtimeChannel#whenState` function" — "`(RTL25a)` If the channel is already in the given state, calls the listener with a \`null\` argument".
- **What the SDK does:** neither `Connection` nor `IRealtimeChannel` has `WhenState` (`realtime-mapping.md` §12, Missing APIs). Every test needs the method, so none can run.
- **Test impact:** 10 skipped stubs (`Skip = "Missing SDK public API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** Not present in ably-java or ably-cocoa either (no `whenState` in their sources); a newer spec point no SDK here implements.
- **Status:** not implemented.

### RTS5 / DO2a — `Channels#getDerived` and `DeriveOptions` are missing

- **Spec point:** RTS5, RTS5a, RTS5a1, RTS5a2, DO2a. Tests (`ChannelOptionsTests`, 5): `realtime/unit/RTS5a/creates-derived-channel-0`, `RTS5a1/filter-base64-encoded-0`, `RTS5a2/derived-with-params-0`, `RTS5/get-derived-with-options-0`, `DO2a/filter-attribute-0`.
- **What the features spec says:** "`(RTS5)` `Channels#getDerived` function" — "`(RTS5a)` Takes `RealtimeChannel` name and `DeriveOptions` object as argument, to create a derived channel"; "`(DO2a)` `filter` (string) - A JMESPath string for filter expression."
- **What the SDK does:** no `GetDerived` on `Channels`, no `DeriveOptions` type.
- **Test impact:** 5 skipped stubs (`Skip = "Missing SDK public API: …"`, spec lines kept as comments); they can't be written until the API exists.
- **Other SDKs:** Not present in ably-java or ably-cocoa either (no `getDerived`).
- **Status:** not implemented.

### TB4 / RTL7h / RTP6e / RTAN4e1 — `ChannelOptions#attachOnSubscribe` is missing

- **Spec point:** TB4, RTL7h, RTP6e, RTAN4e1. Tests (4): `realtime/unit/TB4/attach-on-subscribe-default-0` (`ChannelOptionsTests`), `RTL7h/no-attach-on-subscribe-0` (`ChannelSubscribeTests`), `RTP6e/subscribe-no-attach-option-0` (`RealtimePresenceSubscribeTests`), `RTAN4e1/no-warn-unattached-0` (`ChannelAnnotationsTests`).
- **What the features spec says:** "`(TB4)` `attachOnSubscribe` (for realtime client libraries only) a boolean which determines whether calling `subscribe` on a channel or presence object should trigger an implicit attach. Defaults to `true`."
- **What the SDK does:** `ChannelOptions` has no `AttachOnSubscribe`; these tests depend on the option being false on an unattached channel (`realtime-mapping.md` §12). Where the option is only setup the other tests drop it with a `// NOTE:`.
- **Test impact:** 4 skipped stubs (`Skip = "Missing SDK public API: …"`, spec lines kept as comments); they can't be written until the API exists. Three more tests run with only their `attachOnSubscribe` assertion commented out under the `// Missing SDK public API: ChannelOptions#attachOnSubscribe (TB4)` skip-reason comment: `ChannelOptionsTests.TB2_ChannelOptionsAttributes`, `RTS3c_OptionsUpdatedExisting`, `RTL16_SetOptionsUpdates`.
- **Other SDKs:** ably-java and ably-cocoa both have `attachOnSubscribe`.
- **Status:** not implemented.

### TP3i — `PresenceMessage#extras` is missing

- **Spec point:** TP3i (tests `rest/unit/TP3a/presence-message-attributes-0`, `rest/unit/TP3/presence-from-json-0`).
- **What the features spec says:** `(TP3i)` "`extras` JSON-encodable object, used to contain any arbitrary key value pairs ... should treat it opaquely, encoding it and passing it to realtime unaltered."
- **What the SDK does:** `PresenceMessage` has no `Extras` member (only `Message` has), and nothing else holds or produces the value, so there is no equivalent.
- **Test impact:** `PresenceMessageTypesTests.TP3a_PresenceMessageAttributes` and `TP3_PresenceFromJson` run; their extras assertions (1 each) are commented out under the `// Missing SDK public API: PresenceMessage#extras (TP3i)` skip-reason comment.
- **Status:** not implemented.

### TP5 — `PresenceMessage#size` is missing

- **Spec point:** TP5 (test `rest/unit/TP5/presence-message-size-0`).
- **What the features spec says:** `(TP5)` "The size of the `PresenceMessage` for TO3l8 is calculated in the same way as for `Message`; see TM6".
- **What the SDK does:** `PresenceMessage` has no size attribute, and there is no internal size calculation either: `Message` has none (TM6) and nothing in the message handler computes one. The `maxMessageSize` option is missing too, see RSL1i. So there is no equivalent.
- **Test impact:** `PresenceMessageTypesTests.TP5_PresenceMessageSize` is a skipped stub.
- **Status:** not implemented.

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

### RSA16 — `Auth#tokenDetails` is not public: tests read the internal `AblyAuth.CurrentToken`

- **Spec point:** RSA16, RSA16a–RSA16d (`uts/rest/unit/auth/token_details.md` and the RSA10g / RSA10j reads in `authorize.md`).
- **What the spec says:** `(RSA16)` "`Auth#tokenDetails`: ... `(RSA16a)` Holds a `TokenDetails` representing the token currently in use by the library, if any".
- **What the SDK does:** the public `Auth#tokenDetails` accessor the spec names is absent (`IAblyAuth` has no `TokenDetails`); the same value lives in the internal `AblyAuth.CurrentToken` (`rest-mapping.md` section 8, Missing APIs). `TokenDetails.Expires` / `Issued` are non-nullable `DateTimeOffset`, so "null" is asserted as `DateTimeOffset.MinValue`.
- **Test impact:** the tests read `((AblyAuth)client.Auth).CurrentToken` (a private helper in `TokenDetailsTests`), marked `// DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken` at each test's first read: `TokenDetailsTests.RSA16a_TokenFromCallback`, `RSA16a_TokenFromRequestToken`, `RSA16b_TokenStringInOptions`, `RSA16c_SetOnInstantiation`, `RSA16c_UpdatedAfterAuthorize`, `RSA16c_UpdatedAfter40142Renewal`, `RSA16d_NullWithBasicAuth`, `RSA16d_NullBeforeTokenObtained`, `RSA16a_PreservedAcrossRequests`, `RSA16a_ReflectsCapability` (plus the gated `RSA16b_TokenStringFromCallback` and `RSA16d_NullAfterInvalidation`), and `AuthorizeTests.RSA10g_AuthorizeUpdatesTokenDetails`, `RSA10j_AuthorizeReplacesExistingToken`.
- **Status:** open: public `Auth#tokenDetails` accessor not implemented.

### RTL10b — `untilAttach` is not a public option of `RealtimeChannel#history`: tests call the internal `RealtimeChannel.AddUntilAttachParameter`

- **Spec point:** RTL10b. Tests (`ChannelHistoryTests`, 2): `realtime/unit/RTL10b/adds-from-serial-0`, `RTL10b/errors-when-not-attached-1`.
- **What the spec says:** `(RTL10b)` "Additionally supports the param `untilAttach`, which if true, will only retrieve messages prior to the moment that the channel was attached ... passing the querystring param `fromSerial` with the `RealtimeChannel#properties.attachSerial` ... If the `untilAttach` param is specified when the channel is not attached, it results in an error."
- **What the SDK does:** the public `untilAttach` param of `IRealtimeChannel.HistoryAsync` is absent (presence history has it). The internal `RealtimeChannel.AddUntilAttachParameter(query)` adds `fromSerial` = the attach serial and throws an `AblyException` when the channel is not ATTACHED, which is the RTL10b behaviour. A public `PaginatedRequestParams.ExtraParameters["fromSerial"]` would bypass the attach-serial lookup and the not-attached error, so it is not used.
- **Test impact:** both tests call `((RealtimeChannel)channel).AddUntilAttachParameter(query)` and then `HistoryAsync(query)`, marked `// DEVIATION: RealtimeChannel#history(untilAttach) absent; using internal RealtimeChannel.AddUntilAttachParameter + HistoryAsync(query)`. The `RealtimeChannelOptions(attachOnSubscribe: false)` setup is dropped (see the TB4 entry under Missing SDK Public API / Features).
- **Other SDKs:** ably-java and ably-cocoa both support `untilAttach` on channel history.
- **Status:** open: public `untilAttach` param of `RealtimeChannel#history` not implemented.

### MOP2 — `MessageOperation#toJson` is not public: tests assert the operation in the request the SDK sends

- **Spec point:** MOP2a-c (test `rest/unit/MOP2a/message-operation-fields-0`).
- **What the features spec says:** the IDL defines `MessageOperation` with `clientId`, `description` and `metadata` (MOP2a-c); it defines no serialisation of its own. `(RSL15b7)` the update request's `version` is "set to the `MessageOperation` object if provided".
- **What the SDK does:** the public `MessageOperation#toJson` the UTS calls is absent, and `JsonHelper.Serialize(op)` gives PascalCase keys. The public `UpdateMessageAsync(message, op)` sends the operation as the `version` of the PATCH body, serialised as `clientId`, `description` and `metadata` with null members omitted, which is the JSON the spec asserts.
- **Test impact:** `MutableMessageTypesTests.MOP2a_MessageOperationFields` sends each operation with `channel.UpdateMessageAsync(new Message { Serial = ... }, op)` against a capturing `MockHttpClient` and asserts the request body's `version` object: the filled operation (3 assertions; `metadata` is checked through its `reason` entry) and the empty one (3 absent keys), each marked `// DEVIATION: MessageOperation#toJson absent; using the "version" object of the UpdateMessageAsync request body`. Every spec assertion runs through this public route, so no internal member is used.
- **Status:** open: public `MessageOperation#toJson` not implemented.

### TP4 — `PresenceMessage#fromEncoded` and `#fromEncodedArray` are not public: tests call the internal `MessageHandler.FromEncoded`

- **Spec point:** TP4 (test `rest/unit/TP4/from-encoded-presence-0`).
- **What the features spec says:** `(TP4)` "`fromEncoded` and `fromEncodedArray` are alternative constructors that take an (already deserialized) `PresenceMessage`-like object (or array of such objects), and optionally a `channelOptions`, and return a `PresenceMessage` (or array of such `PresenceMessages`) that's decoded and decrypted as specified in `RSL6` ...".
- **What the SDK does:** the public `PresenceMessage.FromEncoded` / `FromEncodedArray` are absent (only `Message` has them). The internal `MessageHandler.FromEncoded<T>` / `FromEncodedArray<T>` accept any `IMessage`, so they decode a `PresenceMessage` the way RSL6 describes.
- **Test impact:** `PresenceMessageTypesTests.TP4_FromEncodedPresence` deserialises the raw JSON with `JsonHelper` and passes it to `MessageHandler.FromEncoded` / `FromEncodedArray`, each call marked `// DEVIATION: PresenceMessage#fromEncoded absent; using internal MessageHandler.FromEncoded<PresenceMessage>` (and the array form). The `encoding IS null` check is asserted as `BeEmpty()` with a `// DEVIATION:` comment (see the RSL6 / TM3 entry). See also the TP3 Failing Tests entry (presence JSON deserialisation does not decode).
- **Status:** open: public `PresenceMessage#fromEncoded` / `#fromEncodedArray` not implemented.

### TG4 / TG5 — `PaginatedResult#first` is not public: tests re-query `FirstQueryParams`

- **Spec point:** TG5 in the features spec (the UTS tests are labelled TG4: `rest/unit/TG4/first-returns-first-page-0`, `rest/integration/TG4/first-retrieves-page-0`).
- **What the features spec says:** `(TG5)` "`PaginatedResult#first` function returns a new `PaginatedResult` for the first page of results" (IDL: `first() => io PaginatedResult<T> // TG5`).
- **What the SDK does:** the public `PaginatedResult#first` is absent (`PaginatedResult<T>` has `NextAsync`, `HasNext` and `IsLast`; only the `HttpPaginatedResponse` returned by `request()` has a `FirstAsync`). The public `FirstQueryParams` holds the first-page query, and passing it to the channel's `HistoryAsync(query)` re-runs it, which is what `first()` does.
- **Test impact:** `PaginationTests.TG4_FirstRetrievesPage` (integration) and `PaginatedResultTests.TG4_FirstReturnsFirstPage` (unit) call `channel.HistoryAsync(page2.FirstQueryParams)`, marked `// DEVIATION: PaginatedResult#first absent; using channel.HistoryAsync(page2.FirstQueryParams)`. The integration test runs. The unit test is env-gated (`[DeviationFact("TG")]`) because its mock's query-less `first` link hits the TG link-parser crash (see that entry under Failing Tests).
- **Status:** open: public `PaginatedResult#first` not implemented.

### RSAN3b — `RestAnnotations#get` takes no arbitrary `params`: the querystring test passes `AnnotationsRequestParams { Limit }`

- **Spec point:** RSAN3b (the ID-less section "RSAN3b — get passes params as querystring", `uts/rest/unit/channel/annotations.md`).
- **What the features spec says:** `(RSAN3b)` "Any `params` are sent in the querystring"; the IDL types them `params?: Dict<String, Stringifiable>`.
- **What the SDK does:** the public `params` dictionary the spec passes is absent: `RestAnnotations.GetAsync` takes an `AnnotationsRequestParams`, which exposes only `Limit` (`Http/AnnotationsRequestParams.cs:15`). The spec's `{ "limit": "50" }` maps onto `Limit = 50`, so the test still checks that the value reaches the querystring; any other parameter can't be sent.
- **Test impact:** `AnnotationsTests.NoId_Rsan3bGetPassesParamsAsQuerystring` passes `new AnnotationsRequestParams { Limit = 50 }`, marked `// DEVIATION: RestAnnotations#get(params) absent; using AnnotationsRequestParams { Limit }`, and asserts `limit=50` as the spec does.
- **Status:** open: arbitrary annotation query `params` not implemented (`AnnotationsRequestParams` supports only `Limit`).

### RTL22a — `RealtimeChannel#subscribe(MessageFilter, …)` is absent: the name-filter test subscribes by event name

- **Spec point:** RTL22, RTL22a, MFI2d (test `realtime/unit/RTL22a/filter-matching-name-0`, `uts/realtime/unit/channels/channel_subscribe.md`).
- **What the features spec says:** `(RTL22)` "Methods must be provided for attaching and removing a listener which only executes when the message matches a set of criteria"; `(RTL22a)` the filter matches "one or more of: extras.ref.timeserial, extras.ref.type or name".
- **What the SDK does:** the public `RealtimeChannel#subscribe(MessageFilter, …)` is absent (see the RTL22 entry under Missing SDK Public API / Features). For a filter on `name` alone, the public event-name subscribe `IRealtimeChannel.Subscribe(string eventName, Action<Message>)` (RTL7b) delivers exactly the messages whose name matches, and none without a name, which is what the test observes.
- **Test impact:** `ChannelSubscribeTests.RTL22a_FilterMatchingName` subscribes with `channel.Subscribe("target-event", listener)` in place of `subscribe(MessageFilter(name: "target-event"), listener)`, marked `// DEVIATION: RealtimeChannel#subscribe(MessageFilter) absent; using channel.Subscribe("target-event", listener)`; all 5 assertions are verbatim. The `attachOnSubscribe: false` setup is dropped as elsewhere (the test attaches first).
- **Status:** open: public `RealtimeChannel#subscribe(MessageFilter, …)` not implemented.

### TO3k6 — `ClientOptions#fallbackHosts` has no public getter: the test reads the internal `GetFallbackHosts()`

- **Spec point:** TO3k6 (test `rest/unit/TO3/client-options-custom-hosts-1`, `uts/rest/unit/types/options_types.md`).
- **What the features spec says:** the IDL lists `fallbackHosts` as a `ClientOptions` attribute (TO3k6), which the test reads back.
- **What the SDK does:** the public `ClientOptions#fallbackHosts` getter is absent (`ClientOptions.FallbackHosts` is set-only). The internal `ClientOptions.GetFallbackHosts()` returns a copy of the array that was set (or the endpoint's defaults when none was), which is the value the spec asserts.
- **Test impact:** `OptionsTypesTests.TO3_ClientOptionsCustomHosts` asserts `options.GetFallbackHosts()` equals `["fallback1.example.com", "fallback2.example.com"]`, marked `// DEVIATION: ClientOptions#fallbackHosts absent; using internal ClientOptions.GetFallbackHosts()` (unit tier only).
- **Status:** open: public `ClientOptions#fallbackHosts` getter not implemented.

### RSL6 / TM3 — a fully decoded message has `encoding == ""`, not null

- **Spec point:** RSL6a, RSL6 (tests `rest/unit/RSL6a/decode-base64-to-binary-0`, `rest/unit/RSL6a/decode-json-to-object-1`, `rest/unit/RSL6/decode-utf8-base64-data-2`, `rest/unit/RSL6/complex-chained-encoding-3`, and the `base64`, `json` cases of `rest/unit/TM3/from-encoded-decodes-encoding-1`).
- **What the spec says:** `ASSERT message.encoding IS null  # Encoding consumed after decode`. The features spec says (RSL6b) the residual transforms are left in `encoding` but does not say what an empty residue is.
- **What the SDK does:** once every encoding is consumed `Encoding` is `""` (the same SDK-wide empty-string-instead-of-null as the RTN8a entry above).
- **Test impact:** the tests assert `BeEmpty()` with a `// DEVIATION:` comment (the TM3 test is also env-gated for the RSL6a / TM3 entry above, and keeps the null check for its plain-text case, whose encoding stays null). Also `RestPresenceTests.RSP5_DecodeJsonData` and `RSP5_DecodeBase64Binary` (`rest/unit/RSP5/decode-json-data-1`, `decode-base64-binary-2`, presence messages): `Encoding.Should().BeEmpty()` with a `// DEVIATION:` comment, and `PresenceMessageTypesTests.TP4_FromEncodedPresence` (`rest/unit/TP4/from-encoded-presence-0`).
- **Status:** intentional / SDK-wide.

### TO3j10 / AO2g — `ClientOptions.QueryTime` defaults to null, not false

- **Spec point:** TO3j10, AO2g (test `rest/unit/TO3/client-options-attributes-0`).
- **What the features spec says:** IDL `queryTime: Bool default false // RSA9d, TO3j10, AO2g`.
- **What the SDK does:** `AuthOptions.QueryTime` is a `bool?` that is null until set; null behaves as false.
- **Test impact:** `OptionsTypesTests.TO3_ClientOptionsAttributes` asserts `QueryTime.Should().BeNull()` with a `// DEVIATION:` comment; every other attribute follows the spec.
- **Status:** intentional (nullable option).

### RSC19f1 — `request()` has no `version` argument: the version is sent as a request-level header

- **Spec point:** RSC19f, RSC19f1 (test `rest/unit/RSC19f1/version-param-sets-header-0`, `uts/rest/unit/request.md`; the same adaptation is in every `request()` call of `RequestTests`, each marked `// DEVIATION: RestClient#request(version) absent; using a request-level X-Ably-Version header`).
- **What the spec says:** `(RSC19f1)` "The `version` parameter specifies the protocol version that the Ably service should use when interpreting the request. The library must include an `X-Ably-Version` header in the request that it sends, with value given by this argument."
- **What the SDK does:** the public `version` argument the spec names is absent (`PubSubHttpClient.Request` has no `version` parameter); the SDK always sends its own `X-Ably-Version` (6), and a request-level header reaches the same behaviour.
- **Test impact:** `RequestTests.RSC19f1_VersionParamSetsHeader` passes `["X-Ably-Version"] = "2"` / `"3"` in the `headers` argument and asserts the captured header equals it (so it guards that a request-level header takes precedence over the client default, RSC7e).
- **Status:** open: public `request` `version` argument not implemented.

## Mock Infrastructure Limitations

*(none)*
