# Construct catalogue: evidence and accounting (working record)

The catalogue `references/construct-catalogue.md` was folded into SKILL.md and the notes and deleted on
2026-10-18 (ALIGN-1); the row IDs below are historical. This evidence was moved out of the catalogue when it was
trimmed. Not read at translation time. Built from the
committed guide section 6 and `python3 scripts/scan_constructs.py <clone>/uts/rest` and `…/uts/realtime` at
ably/specification@0e52e656.

## Usage citations for undocumented constructs

| Row | Inferred from |
|---|---|
| W-05 | `realtime/unit/connection/fallback_hosts_test.md:399`, `realtime/unit/auth/connection_auth_test.md:158`, `realtime/unit/channels/channel_server_initiated_detach.md:532` |
| W-18 | `realtime/integration/proxy/connection_resume.md:93`, RTN15a |
| W-07 | `realtime/unit/auth/auth_callback_errors_test.md:268`, `realtime/integration/delta_decoding_test.md:133`, `realtime/unit/connection/connection_open_failures_test.md:562` |
| W-08 | `realtime/unit/connection/connection_ping_test.md:303` |
| W-09 | `realtime/integration/presence_lifecycle_test.md:107` |
| W-15 | `realtime/unit/auth/auth_callback_errors_test.md:138` |
| W-17 | `realtime/integration/auth.md:69` |
| T-04 | `rest/unit/auth/token_details.md:281` |
| T-05 | `realtime/unit/connection/connection_ping_test.md:500` |
| AS-03 | `realtime/unit/channels/channel_attach.md:689` |
| AS-05 | `realtime/unit/channels/channels_collection.md:176` |
| AS-07 | `realtime/integration/proxy/rest_faults.md:47` |
| AS-12 | `rest/unit/logging.md:57`, `rest/integration/history.md:255` |
| AS-13 | `rest/integration/publish.md:108` |
| AS-15 | `realtime/unit/presence/realtime_presence_get.md:62` |
| AS-16 | `rest/unit/fallback.md:243` |
| E-03 | `rest/unit/auth/client_id.md:434`, `rest/unit/channel/publish.md:282` |
| E-04 | `realtime/integration/auth.md:251` |
| E-05 | `realtime/unit/auth/auth_callback_errors_test.md:48` |
| C-03 | `realtime/integration/delta_decoding_test.md:233` |
| C-06 | `realtime/unit/channels/channel_delta_decoding.md:632`, `realtime/unit/connection/fallback_hosts_test.md:349` |
| C-08 | `realtime/integration/auth.md:69`, `realtime/integration/presence_lifecycle_test.md:95` |
| C-12 | `rest/integration/history.md:66` |
| C-14 | `rest/unit/batch_publish.md:31`, `rest/unit/presence/rest_presence.md` |
| MH-02 | `realtime/unit/connection/fallback_hosts_test.md:290` |
| MH-10 | `rest/unit/batch_presence.md:74`, `realtime/unit/channels/channel_annotations.md:32` |
| MW-06 | `realtime/unit/connection/connection_recovery_test.md:34`, `realtime/unit/channels/channel_annotations.md:73` |
| MW-09 | `realtime/unit/client/realtime_client.md:352` |
| DH-11 | `realtime/unit/connection/fallback_hosts_test.md:559`, `:676`, whose comment defines it |
| K-11 | `rest/unit/request_endpoint.md:43` |

## Row evidence

### W-14: unit `WAIT(n)` uses by case (decision log #44; @0e52e656, `realtime/unit`)

- (a) fake timers enabled → `AdvanceTime(n)`: `connection/when_state_test.md:235,254`.
- (b) delivery wait → `ProcessPendingEvents()` + NOTE: `connection/update_events_test.md:90,189,278,375`, `connection/fallback_hosts_test.md:651`, `connection/when_state_test.md:68,144,328,456,470`.
- (c) negative check, no fake clock → `Task.Delay(n)` + NOTE: `connection/auto_connect_test.md:116`, `connection/fallback_hosts_test.md:403`, `connection/when_state_test.md:395`, `channels/channel_when_state_test.md:241,318`.
- Both `when_state` files are pending stubs (RTN26/RTL25 `whenState` missing; realtime notes section 10). Re-check each case at the first translation.
- Why: guide 5.6 forbids real timers in unit tests, 5.2 wants waits to match the spec; four of the five files use no fake clock, and some uses are negative checks ("confirm no connection attempt is made").

### WB-03: `CLEAR channel._lastPayload.messageId` (`realtime/integration/delta_decoding_test.md:380`)

RTL20 reads `channel.LastSuccessfulMessageIds.LastMessageId` (`src/Ably.PubSub.Shared/Realtime/ChannelMessageProcessor.cs:110,205`), not `MessageDecodingContext.PreviousPayload`. The setter is internal "to simulate mismatching LastMessageIds" (`Realtime/LastMessageIds.cs:10-12`). Before any message the property is the shared `LastMessageIds.Empty` (`LastMessageIds.cs:8`, `Realtime/RealtimeChannel.cs:43`), so write it only after the spec's `WAIT UNTIL` (messages arrived; `ChannelMessageProcessor.cs:136` then holds a per-channel instance). Assigning `Empty` would also drop the channel serial the recovery attach sends (`RealtimeChannel.cs:349-351`).

### DH-12: `load_fixtures("encoding.json")` (`rest/unit/encoding/message_encoding.md:546`)

The vendored ably-common (`common` @ 133665b, P-19) has no `encoding.json`. `messages-encoding.json` is embedded in both heads, but the spec reads `input_data`, `expected_wire_data`, `expected_encoding`, `use_binary_protocol`, while the file has `messages[]` with `data`, `encoding`, `expectedType`, `expectedValue`; content equivalence is unverified. Bumping the submodule is a dependency change.

### CM-09: `http_replace_response` (`rest/integration/proxy/rest_fallback.md:509`)

A string inside a rule literal, so the scanner can't see it. No `ProxyRules` builder, not in the committed `proxy.md`; uts-proxy v0.3.0 support unknown. `HttpRespond` would not forward the publish upstream.

### C-14: Given/When/Then prose

148 lines in 30 tests: 28 in `rest/unit/batch_publish.md` (e.g. `:31`), 2 in `rest/unit/presence/rest_presence.md`
(RSP1a, RSP1b). They are the source of the scanner's `HTTP`, `REST`, `JSON`, `ID`, `TM` and four `POST` hits. Test IDs:
generation/prose-tests.md.

### Constructs the scanner can't see (found by reading uses)

`WAIT UNTIL` (7 uses), `AWAIT_STATE … IN [..]` / `!= x` (3), `AWAIT_STATE … WITH condition:` (1), `CONNECTED()` /
`ATTACHED(...)` / `ACK(...)` / `NACK(...)` constructors, `events.filter(type: X)`, `respond_with(…, contentType:)`,
`Rest(…, httpClient:)` / `Realtime(…, webSocketClient:)` (41 uses), `RETURN x IF c ELSE null`, `str()` / `length()`,
`IS NOT SET`, Given/When/Then prose, the `http_replace_response` proxy action.

## Scanner accounting (@0e52e656)

Every token `scan_constructs.py` prints for `uts/rest` and `uts/realtime`, with the row that covers it. `spec` counts
exclude `helpers/` (helper-spec internals implemented in the infra, never translated). Prose words come from
Given/When/Then lines (C-14) or descriptive text inside fences.

| Token | Kind | Total | Spec (non-helper) | Modules | First use | Row |
|---|---|---|---|---|---|---|
| `add_rules` | call | 1 | 1 | realtime | `realtime/integration/proxy/channel_faults.md:317` | CM-10 |
| `await_client_close` | call | 2 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:46 (helper)` | MW-08 |
| `await_client_message` | call | 1 | 1 | realtime | `realtime/unit/auth/realtime_authorize.md:496` | MW-03 |
| `await_connection_attempt` | call | 17 | 12 | realtime,rest | `realtime/unit/channels/channel_attach.md:570` | MW-08, MH-07 |
| `await_next_message_from_client` | call | 4 | 3 | realtime | `realtime/unit/connection/heartbeat_test.md:1269` | MW-08 |
| `await_request` | call | 3 | 1 | rest | `rest/unit/rest_client.md:438` | MH-07 |
| `base64_decode` | call | 6 | 6 | rest | `rest/integration/presence.md:467` | DH-03 |
| `base64_encode` | call | 4 | 4 | realtime,rest | `rest/unit/encoding/message_encoding.md:349` | DH-03 |
| `base64url_decode` | call | 2 | 0 | realtime | `realtime/unit/helpers/mock_vcdiff.md:58 (helper)` | DH-04 (helper-internal) |
| `base64url_encode` | call | 2 | 0 | realtime | `realtime/unit/helpers/mock_vcdiff.md:28 (helper)` | DH-04 (helper-internal) |
| `byte_array` | call | 1 | 1 | realtime | `realtime/integration/channels/channel_publish_test.md:222` | DH-07 |
| `close_from_server` | call | 1 | 1 | realtime | `realtime/unit/connection/connection_ping_test.md:607` | MW-07 |
| `create_mock_connection` | call | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:367 (helper)` | helper-internal (`mock_websocket.md` pseudocode; implemented by MockWebSocket/MockTransport) |
| `create_mock_websocket` | call | 21 | 21 | realtime | `realtime/unit/client/realtime_client.md:91` | MW-01 |
| `create_proxy_session` | call | 38 | 38 | realtime,rest | `rest/integration/proxy/rest_fallback.md:101` | CM-07 |
| `create_realtime_client` | call | 2 | 2 | realtime | `realtime/unit/channels/channel_detach.md:434` | MW-02 |
| `current_fake_time` | call | 1 | 1 | realtime | `realtime/unit/connection/connection_open_failures_test.md:556` | T-06 |
| `current_time` | call | 2 | 2 | realtime | `realtime/unit/auth/connection_auth_test.md:50` | T-06 |
| `decode_uri_component` | call | 2 | 0 | realtime | `realtime/unit/helpers/mock_vcdiff.md:43 (helper)` | DH-04 (helper-internal) |
| `enable_fake_timers` | call | 62 | 61 | realtime,rest | `rest/unit/fallback.md:500` | T-01 |
| `encode_recovery_key` | call | 2 | 2 | realtime | `realtime/unit/client/realtime_client.md:373` | WB-02 |
| `encode_uri_component` | call | 22 | 20 | realtime,rest | `rest/unit/channel/annotations.md:85` | DH-05 |
| `encode_utf8` | call | 2 | 2 | rest | `rest/unit/encoding/message_encoding.md:755` | DH-06 |
| `extract_between` | call | 1 | 1 | realtime | `realtime/unit/channels/channel_options.md:444` | DH-10 |
| `extract_key_name` | call | 12 | 12 | realtime,rest | `rest/integration/auth.md:81` | CM-06 |
| `extract_key_secret` | call | 12 | 12 | realtime,rest | `rest/integration/auth.md:82` | CM-06 |
| `generate_jwt` | call | 31 | 31 | realtime,rest | `rest/integration/auth.md:80` | CM-06 |
| `get_backoff_coefficient` | call | 1 | 1 | realtime | `realtime/unit/connection/backoff_jitter_test.md:52` | WB-01 |
| `get_jitter_coefficient` | call | 1 | 1 | realtime | `realtime/unit/connection/backoff_jitter_test.md:94` | WB-01 |
| `get_key_parts` | call | 1 | 1 | realtime | `realtime/integration/proxy/auth_reauth.md:64` | CM-06 |
| `get_log` | call | 29 | 29 | realtime,rest | `rest/integration/proxy/rest_fallback.md:139` | CM-10 |
| `install_mock` | call | 704 | 698 | realtime,rest | `rest/unit/auth/auth_callback.md:50` | MH-01, MW-01, MW-15, MW-16 |
| `json_decode` | call | 2 | 2 | rest | `rest/unit/request.md:196` | DH-01 |
| `json_encode` | call | 1 | 1 | rest | `rest/unit/request.md:462` | DH-02 |
| `load_fixtures` | call | 1 | 1 | rest | `rest/unit/encoding/message_encoding.md:546` | DH-12 |
| `load_json` | call | 1 | 1 | rest | `rest/unit/encoding/msgpack_interop.md:31` | DH-13 |
| `msgpack_decode` | call | 3 | 3 | rest | `rest/unit/encoding/message_encoding.md:192` | DH-13, ID-07 |
| `msgpack_deserialize` | call | 3 | 3 | rest | `rest/unit/encoding/msgpack_interop.md:39` | DH-13, ID-07 |
| `msgpack_encode` | call | 10 | 10 | rest | `rest/unit/auth/token_renewal.md:555` | DH-13, ID-07 |
| `msgpack_serialize` | call | 1 | 1 | rest | `rest/unit/encoding/msgpack_interop.md:101` | DH-13, ID-07 |
| `now_millis` | call | 2 | 2 | rest | `rest/integration/presence.md:278` | T-06 |
| `on_client_message` | call | 7 | 7 | realtime | `realtime/unit/auth/realtime_authorize.md:81` | MW-03 |
| `on_connect` | call | 7 | 7 | realtime | `realtime/unit/client/realtime_client.md:289` | MW-03 |
| `on_message` | call | 1 | 1 | realtime | `realtime/unit/client/realtime_client.md:592` | MW-03 |
| `parse_form_urlencoded` | call | 1 | 1 | rest | `rest/unit/auth/client_id.md:258` | DH-09 |
| `parse_json` | call | 85 | 85 | realtime,rest | `rest/integration/auth.md:27` | DH-01 |
| `parse_query_string` | call | 1 | 1 | realtime | `realtime/unit/channels/channel_options.md:452` | DH-09 |
| `poll_until` | call | 50 | 50 | realtime,rest | `rest/integration/history.md:63` | W-11 |
| `poll_until_success` | call | 8 | 8 | realtime,rest | `rest/integration/mutable_messages.md:136` | W-12 |
| `process_pending_events` | call | 2 | 2 | realtime | `realtime/unit/channels/channel_detach.md:721` | W-13 |
| `queue_delayed_response` | call | 1 | 1 | rest | `rest/unit/request.md:846` | MH-05 |
| `queue_response` | call | 71 | 71 | rest | `rest/unit/fallback.md:30` | MH-05 |
| `queue_response_for_host` | call | 6 | 6 | rest | `rest/unit/fallback.md:453` | MH-05 |
| `queue_response_for_url` | call | 5 | 5 | rest | `rest/unit/fallback.md:1428` | MH-05 |
| `queue_responses` | call | 1 | 1 | rest | `rest/unit/fallback.md:60` | MH-05 |
| `queue_timeout` | call | 1 | 1 | rest | `rest/unit/fallback.md:162` | MH-05 |
| `random_bytes` | call | 1 | 1 | realtime | `realtime/integration/delta_decoding_test.md:192` | DH-07 |
| `random_id` | call | 107 | 107 | realtime,rest | `rest/integration/auth.md:48` | CM-05 |
| `random_string` | call | 4 | 4 | realtime,rest | `rest/integration/proxy/rest_fallback.md:528` | CM-05 |
| `record_event` | call | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:347 (helper)` | helper-internal (`mock_websocket.md` pseudocode; MockWebSocket.Record) |
| `request_token_from_sandbox` | call | 3 | 3 | realtime | `realtime/integration/proxy/connection_open_failures.md:174` | CM-06, CM-04 |
| `respond_with` | call | 370 | 362 | realtime,rest | `rest/unit/auth/auth_callback.md:47` | MH-02 |
| `respond_with_connected` | call | 1 | 1 | realtime | `realtime/unit/channels/channel_detach.md:430` | MW-04 |
| `respond_with_delay` | call | 2 | 1 | rest | `rest/unit/rest_client.md:439` | MH-03 |
| `respond_with_dns_error` | call | 3 | 1 | realtime,rest | `rest/unit/fallback.md:259` | MH-04, MW-04 |
| `respond_with_error` | call | 7 | 5 | realtime | `realtime/unit/channels/channel_publish.md:714` | MW-04 |
| `respond_with_refused` | call | 33 | 29 | realtime,rest | `rest/unit/fallback.md:227` | MH-04, MW-04 |
| `respond_with_success` | call | 695 | 684 | realtime,rest | `rest/unit/auth/auth_callback.md:44` | MH-04, MW-04 |
| `respond_with_timeout` | call | 6 | 3 | realtime,rest | `rest/unit/fallback.md:290` | MH-04, MW-04 |
| `schedule_microtask` | call | 2 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:348 (helper)` | helper-internal (`mock_websocket.md` pseudocode; MockTransport's ordered async chain) |
| `send_ping_frame` | call | 6 | 4 | realtime | `realtime/unit/connection/heartbeat_test.md:777` | MW-07 |
| `send_to_client` | call | 580 | 578 | realtime | `realtime/unit/auth/auth_callback_errors_test.md:58` | MW-07 |
| `send_to_client_and_close` | call | 28 | 26 | realtime | `realtime/unit/auth/realtime_authorize.md:409` | MW-07 |
| `send_to_client_raw` | call | 3 | 3 | realtime | `realtime/unit/connection/forwards_compatibility_test.md:83` | MW-07 |
| `simulate_disconnect` | call | 43 | 41 | realtime | `realtime/unit/channels/channel_attach.md:310` | MW-07 |
| `simulate_network_available` | call | 2 | 2 | realtime | `realtime/unit/connection/network_change_test.md:323` | MW-15 |
| `simulate_network_lost` | call | 2 | 2 | realtime | `realtime/unit/connection/network_change_test.md:109` | MW-15 |
| `time_now` | call | 8 | 8 | realtime | `realtime/unit/connection/connection_failures_test.md:112` | T-06 |
| `to_json` | call | 1 | 1 | rest | `rest/unit/encoding/message_encoding.md:754` | DH-02 |
| `token_auth_callback` | call | 9 | 9 | rest | `rest/integration/proxy/rest_fallback.md:57` | spec-local function (`rest/integration/proxy/rest_fallback.md:57`): C-07 |
| `trigger_action` | call | 8 | 8 | realtime | `realtime/integration/proxy/channel_faults.md:413` | CM-10 |
| `uninstall_mock` | call | 2 | 0 | realtime,rest | `rest/unit/helpers/mock_http.md:204 (helper)` | MH-12 (helper-internal in the corpus) |
| `unique_channel_name` | call | 2 | 2 | realtime | `realtime/integration/proxy/presence_reentry.md:66` | CM-05 |
| `utf8_decode` | call | 1 | 0 | realtime | `realtime/unit/helpers/mock_vcdiff.md:52 (helper)` | DH-04 (helper-internal) |
| `utf8_encode` | call | 2 | 1 | realtime | `realtime/unit/channels/channel_delta_decoding.md:837` | DH-06 (spec use); DH-04 (helper use) |
| `ABSENT` | keyword | 3 | 3 | realtime | `realtime/unit/presence/presence_map.md:339` | K-06 (enum) |
| `ACK` | keyword | 42 | 42 | realtime | `realtime/unit/channels/channel_annotations.md:73` | K-01 (wire), MW-06 (`ACK(...)`) |
| `ADVANCE_TIME` | keyword | 87 | 84 | realtime,rest | `rest/unit/fallback.md:519` | T-02 |
| `AFTER` | keyword | 44 | 42 | realtime,rest | `rest/integration/auth.md:31` | CM-02 |
| `ALL` | keyword | 70 | 70 | realtime,rest | `rest/integration/auth.md:23` | CM-02 (`BEFORE/AFTER ALL TESTS`), AS-12 (`ASSERT ALL`) |
| `AND` | keyword | 84 | 84 | realtime,rest | `rest/integration/auth.md:64` | C-02 |
| `ANNOTATION` | keyword | 13 | 13 | realtime | `realtime/unit/channels/channel_annotations.md:72` | K-01 (wire) |
| `ANNOTATION_CREATE` | keyword | 8 | 8 | realtime,rest | `rest/integration/mutable_messages.md:404` | K-08 (enum) |
| `ANNOTATION_DELETE` | keyword | 4 | 4 | realtime,rest | `rest/unit/types/mutable_message_types.md:311` | K-08 (enum) |
| `ANNOTATION_PUBLISH` | keyword | 13 | 13 | realtime | `realtime/integration/mutable_messages_test.md:613` | K-03, K-04 (flag / mode) |
| `ANNOTATION_SUBSCRIBE` | keyword | 10 | 10 | realtime | `realtime/integration/mutable_messages_test.md:613` | K-03, K-04 (flag / mode) |
| `ANY` | keyword | 5 | 5 | rest | `rest/integration/history.md:255` | AS-12 |
| `ARE` | keyword | 1 | 1 | rest | `rest/integration/publish.md:108` | AS-13 |
| `AS` | keyword | 9 | 9 | realtime,rest | `rest/unit/channels_collection.md:159` | AS-05 (`IS SAME AS`) |
| `ASSERT` | keyword | 3831 | 3822 | realtime,rest | `rest/integration/auth.md:64` | section 3 (AS-*), E-03 |
| `ATTACH` | keyword | 240 | 240 | realtime | `realtime/unit/auth/realtime_authorize.md:282` | K-01 (wire) |
| `ATTACHED` | keyword | 238 | 238 | realtime | `realtime/integration/channels/channel_attach_test.md:73` | K-01 (wire), K-05 (state), MW-06 (`ATTACHED(...)`) |
| `AUTH` | keyword | 13 | 13 | realtime | `realtime/unit/auth/auth_callback_errors_test.md:264` | K-01 (wire) |
| `AWAIT` | keyword | 1261 | 1253 | realtime,rest | `rest/integration/auth.md:58` | W-01, W-02 |
| `AWAIT_ALL` | keyword | 1 | 1 | realtime | `realtime/integration/presence_lifecycle_test.md:107` | W-09 |
| `AWAIT_ERROR` | keyword | 8 | 8 | realtime | `realtime/unit/connection/connection_ping_test.md:303` | W-08 |
| `AWAIT_STATE` | keyword | 739 | 735 | realtime,rest | `rest/integration/batch_presence.md:107` | W-03, W-04, W-05 |
| `BEFORE` | keyword | 34 | 32 | realtime,rest | `rest/integration/auth.md:23` | CM-02 |
| `BREAK` | keyword | 12 | 12 | realtime,rest | `rest/integration/pagination.md:228` | C-13, T-03 |
| `CATCH` | keyword | 2 | 2 | realtime | `realtime/unit/channels/channel_annotations.md:277` | E-06 |
| `CLEAR` | keyword | 1 | 1 | realtime | `realtime/integration/delta_decoding_test.md:380` | WB-03 |
| `CLIENT_CLOSE` | keyword | 12 | 9 | realtime | `realtime/unit/connection/heartbeat_test.md:184` | K-09 (mock event), MW-09 |
| `CLOSE` | keyword | 1 | 1 | realtime | `realtime/unit/client/realtime_client.md:592` | K-01 (wire) |
| `CLOSED` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:214 (helper)` | K-01 (wire; helper-internal use only) |
| `CLOSED_MESSAGE` | keyword | 2 | 1 | realtime | `realtime/unit/client/realtime_client.md:592` | MW-05 |
| `CLOSE_CLIENT` | keyword | 463 | 463 | realtime,rest | `rest/unit/fallback.md:1453` | CM-01 |
| `CONDITION` | keyword | 1 | 1 | realtime | `realtime/integration/proxy/auth_reauth.md:113` | W-11 (`AWAIT pollUntil(CONDITION: ...)`) |
| `CONNECTED` | keyword | 246 | 245 | realtime | `realtime/integration/auth/token_renewal_test.md:84` | K-05 (state), K-01 (wire), MW-05 (`CONNECTED()`) |
| `CONNECTED_MESSAGE` | keyword | 210 | 207 | realtime | `realtime/unit/channels/channel_additional_attached.md:35` | MW-05 |
| `CONNECTING` | keyword | 1 | 1 | realtime | `realtime/integration/connection_lifecycle_test.md:233` | K-05 (state) |
| `CONNECTION_ATTEMPT` | keyword | 2 | 1 | realtime | `realtime/unit/client/realtime_client.md:352` | K-09 (mock event), MW-09 |
| `CONNECTION_FAILURE` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:54 (helper)` | K-09 (mock event; helper-internal use only) |
| `CONNECTION_SUCCESS` | keyword | 20 | 19 | realtime | `realtime/unit/connection/backoff_jitter_test.md:186` | K-09 (mock event), MW-09 |
| `CONTAINS` | keyword | 87 | 87 | realtime,rest | `rest/integration/proxy/rest_fallback.md:140` | AS-08 |
| `CONTAINS_IN_ORDER` | keyword | 37 | 36 | realtime | `realtime/integration/proxy/channel_faults.md:136` | AS-11 |
| `COUNT_UNIQUE` | keyword | 1 | 1 | realtime | `realtime/unit/connection/fallback_hosts_test.md:559` | DH-11 |
| `DEFAULT_CONNECTION_STATE_TTL` | keyword | 4 | 4 | realtime | `realtime/unit/connection/connection_open_failures_test.md:464` | T-07, K-13 (spec-local constant) |
| `DEFAULT_REST_HOST` | keyword | 5 | 5 | rest | `rest/unit/request_endpoint.md:43` | K-11 (named constant) |
| `DELETE` | keyword | 32 | 32 | realtime,rest | `rest/integration/auth.md:32` | CM-03 (sandbox teardown) |
| `DETACH` | keyword | 28 | 28 | realtime | `realtime/unit/channels/channel_attach.md:151` | K-01 (wire) |
| `DETACHED` | keyword | 35 | 35 | realtime | `realtime/integration/channels/channel_attach_test.md:119` | K-01 (wire), K-05 (state) |
| `DISCONNECTED` | keyword | 7 | 6 | realtime | `realtime/unit/connection/connection_failures_test.md:61` | K-01 (wire), K-05 (state) |
| `DISCONNECTED_MESSAGE` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:217 (helper)` | MW-05 (helper-internal use only) |
| `EACH` | keyword | 43 | 39 | realtime,rest | `rest/integration/proxy/rest_fallback.md:49` | C-04 (`FOR EACH`), CM-02 (`BEFORE/AFTER EACH TEST`) |
| `ELSE` | keyword | 212 | 209 | realtime,rest | `rest/integration/auth.md:277` | C-01, C-12 |
| `EMPTY` | keyword | 2 | 2 | realtime,rest | `rest/unit/logging.md:137` | AS-14 |
| `END` | keyword | 5 | 5 | realtime,rest | `rest/unit/encoding/msgpack_interop.md:50` | C-01 |
| `ENDS` | keyword | 2 | 2 | realtime | `realtime/unit/channels/channel_options.md:374` | AS-09 |
| `ENDS_WITH` | keyword | 2 | 2 | rest | `rest/unit/channel/rest_channel_attributes.md:167` | AS-09 |
| `ENTER` | keyword | 106 | 106 | realtime,rest | `rest/unit/types/presence_message_types.md:53` | K-06 (enum) |
| `ERROR` | keyword | 51 | 49 | realtime | `realtime/unit/auth/realtime_authorize.md:318` | K-01 (wire) |
| `ERROR_MESSAGE` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:222 (helper)` | MW-05 (helper-internal use only) |
| `EXPECT` | keyword | 1 | 1 | realtime | `realtime/integration/auth.md:251` | E-04 |
| `EXTRACT_FALLBACK_ID` | keyword | 2 | 2 | realtime | `realtime/unit/connection/fallback_hosts_test.md:676` | DH-11 |
| `FAIL` | keyword | 3 | 3 | realtime | `realtime/integration/delta_decoding_test.md:123` | AS-18 |
| `FAILED` | keyword | 5 | 5 | realtime | `realtime/integration/connection/connection_failures_test.md:58` | K-05 (state) |
| `FAILS` | keyword | 141 | 141 | realtime,rest | `rest/integration/auth.md:344` | E-01 |
| `FILTER` | keyword | 3 | 3 | realtime | `realtime/unit/connection/fallback_hosts_test.md:349` | C-06 |
| `FIND` | keyword | 5 | 5 | realtime,rest | `rest/integration/presence.md:131` | C-06 |
| `FOR` | keyword | 116 | 116 | realtime,rest | `rest/integration/batch_presence.md:140` | C-04, C-05 |
| `FUNCTION` | keyword | 109 | 109 | realtime,rest | `rest/integration/auth.md:165` | C-07 |
| `HAS` | keyword | 1 | 1 | realtime | `realtime/integration/proxy/rest_faults.md:47` | AS-07 |
| `HAS_BACKLOG` | keyword | 1 | 1 | realtime | `realtime/unit/channels/channel_state_events.md:438` | K-03 (flag) |
| `HAS_PRESENCE` | keyword | 17 | 17 | realtime | `realtime/unit/presence/realtime_presence_channel_state.md:36` | K-03 (flag) |
| `HEARTBEAT` | keyword | 21 | 20 | realtime | `realtime/unit/connection/connection_ping_test.md:41` | K-01 (wire) |
| `HEARTBEAT_MESSAGE` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:227 (helper)` | MW-05 (helper-internal use only) |
| `HTTP` | keyword | 33 | 33 | rest | `rest/unit/batch_publish.md:31` | prose in Given/When/Then lines: C-14 |
| `ID` | keyword | 1 | 1 | rest | `rest/unit/batch_publish.md:182` | prose (`rest/unit/batch_publish.md:182`): C-14 |
| `IF` | keyword | 559 | 549 | realtime,rest | `rest/integration/auth.md:270` | C-01, C-12 |
| `IN` | keyword | 265 | 265 | realtime,rest | `rest/integration/batch_presence.md:140` | AS-06, C-05, C-06 |
| `INITIALIZED` | keyword | 5 | 5 | realtime | `realtime/integration/auth.md:124` | K-05 (state) |
| `IS` | keyword | 732 | 730 | realtime,rest | `rest/integration/auth.md:140` | AS-02..AS-05, AS-14..AS-16 |
| `JSON` | keyword | 3 | 3 | rest | `rest/unit/batch_publish.md:135` | prose (`rest/unit/batch_publish.md:135`): C-14 |
| `JSON_DECODE` | keyword | 1 | 1 | realtime | `realtime/unit/channels/channel_publish.md:146` | DH-01, MW-14 |
| `JSON_PARSE` | keyword | 7 | 7 | rest | `rest/unit/auth/revoke_tokens.md:125` | DH-01 |
| `LEAVE` | keyword | 41 | 41 | realtime | `realtime/integration/presence_lifecycle_test.md:251` | K-06 (enum) |
| `LISTEN` | keyword | 1 | 1 | realtime | `realtime/integration/auth.md:69` | W-17, C-08 |
| `LOG` | keyword | 1 | 1 | realtime | `realtime/integration/delta_decoding_test.md:233` | C-03 |
| `LOG_INFO` | keyword | 2 | 2 | realtime | `realtime/unit/auth/token_expiry_non_renewable_test.md:64` | L-01, L-03, K-10 |
| `LOOP` | keyword | 12 | 12 | realtime | `realtime/unit/channels/channel_attach.md:312` | T-03 |
| `MATCHES` | keyword | 1 | 1 | realtime | `realtime/unit/connection/fallback_hosts_test.md:482` | AS-10 |
| `MESSAGE` | keyword | 127 | 126 | realtime | `realtime/unit/channels/channel_delta_decoding.md:86` | K-01 (wire) |
| `MESSAGE_ACTION_INT` | keyword | 2 | 2 | realtime | `realtime/unit/channels/channel_publish.md:147` | K-02 (wire constant) |
| `MESSAGE_APPEND` | keyword | 5 | 5 | realtime,rest | `rest/unit/types/mutable_message_types.md:25` | K-07 (enum) |
| `MESSAGE_CREATE` | keyword | 6 | 6 | realtime,rest | `rest/integration/mutable_messages.md:145` | K-07 (enum) |
| `MESSAGE_DELETE` | keyword | 6 | 6 | realtime,rest | `rest/integration/mutable_messages.md:247` | K-07 (enum) |
| `MESSAGE_FROM_CLIENT` | keyword | 6 | 5 | realtime | `realtime/unit/connection/connection_ping_test.md:73` | K-09 (mock event), MW-09 |
| `MESSAGE_SUMMARY` | keyword | 1 | 1 | rest | `rest/unit/types/mutable_message_types.md:24` | K-07 (enum) |
| `MESSAGE_TO_CLIENT` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:56 (helper)` | K-09 (mock event; helper-internal use only) |
| `MESSAGE_UPDATE` | keyword | 11 | 11 | realtime,rest | `rest/integration/mutable_messages.md:194` | K-07 (enum) |
| `META` | keyword | 1 | 1 | rest | `rest/unit/types/mutable_message_types.md:23` | K-07 (enum) |
| `MSGPACK_DECODE` | keyword | 1 | 1 | realtime | `realtime/unit/channels/channel_publish.md:223` | DH-13, MW-14, ID-07 |
| `NACK` | keyword | 5 | 5 | realtime | `realtime/unit/channels/channel_annotations.md:359` | K-01 (wire), MW-06 (`NACK(...)`) |
| `NEVER_RESOLVING_FUTURE` | keyword | 1 | 1 | realtime | `realtime/unit/auth/auth_callback_errors_test.md:138` | W-15 |
| `NOT` | keyword | 409 | 407 | realtime,rest | `rest/integration/history.md:87` | C-02, AS-02, AS-06, AS-14, AS-15 |
| `NOW` | keyword | 11 | 11 | realtime | `realtime/unit/presence/presence_sync.md:155` | T-06 |
| `NULL` | keyword | 19 | 19 | realtime | `realtime/integration/auth/token_request_test.md:75` | AS-02 |
| `OR` | keyword | 80 | 80 | realtime,rest | `rest/integration/auth.md:202` | C-02, AS-02, AS-03 |
| `PASS` | keyword | 2 | 2 | realtime | `realtime/unit/client/realtime_timeouts.md:58` | C-01 |
| `PING` | keyword | 5 | 4 | realtime | `realtime/unit/connection/heartbeat_test.md:1266` | K-01 (wire) |
| `PING_FRAME` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:57 (helper)` | K-09 (mock event; helper-internal use only) |
| `PING_MESSAGE` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:233 (helper)` | MW-05 (helper-internal use only) |
| `POLL_UNTIL` | keyword | 2 | 2 | realtime | `realtime/integration/proxy/presence_reentry.md:128` | W-11 |
| `PONG` | keyword | 3 | 3 | realtime | `realtime/unit/connection/heartbeat_test.md:1281` | K-01 (wire) |
| `POST` | keyword | 37 | 37 | realtime,rest | `rest/integration/auth.md:24` | CM-03 (33 sandbox provisioning lines), CM-04 (`/keys/{key_name}/requestToken`), prose (4, `rest/unit/batch_publish.md`): C-14 |
| `PRESENCE` | keyword | 53 | 53 | realtime,rest | `rest/unit/types/presence_message_types.md:119` | K-01 (wire) |
| `PRESENT` | keyword | 56 | 56 | realtime,rest | `rest/unit/batch_presence.md:292` | K-06 (enum) |
| `PROTOCOL` | keyword | 49 | 49 | realtime,rest | `rest/integration/batch_presence.md:99` | C-10, ID-06 |
| `PUBLISH` | keyword | 23 | 23 | realtime | `realtime/integration/mutable_messages_test.md:613` | K-03, K-04 (flag / mode) |
| `REST` | keyword | 28 | 28 | rest | `rest/unit/batch_publish.md:31` | prose in Given/When/Then lines: C-14 |
| `RESUMED` | keyword | 4 | 4 | realtime | `realtime/unit/channels/channel_additional_attached.md:130` | K-03 (flag) |
| `RETURN` | keyword | 137 | 135 | realtime,rest | `rest/integration/auth.md:166` | C-07, C-12, C-13 |
| `SAME` | keyword | 9 | 9 | realtime,rest | `rest/unit/channels_collection.md:159` | AS-05 |
| `SANDBOX_ENDPOINT` | keyword | 4 | 4 | realtime,rest | `rest/integration/proxy/rest_fallback.md:63` | K-12 (named constant), CM-11 |
| `SCHEDULE_AFTER` | keyword | 3 | 3 | realtime | `realtime/unit/connection/connection_ping_test.md:500` | T-05 |
| `SERVER_DISCONNECT` | keyword | 1 | 0 | realtime | `realtime/unit/helpers/mock_websocket.md:58 (helper)` | K-09 (mock event; helper-internal use only) |
| `SET` | keyword | 2 | 2 | realtime | `realtime/integration/presence_lifecycle_test.md:95` | C-08 (`SET()`), AS-03 (`IS NOT SET`) |
| `STARTS` | keyword | 7 | 7 | realtime,rest | `rest/integration/history.md:229` | AS-09 |
| `STARTS_WITH` | keyword | 4 | 4 | rest | `rest/unit/batch_presence.md:517` | AS-09 |
| `SUBSCRIBE` | keyword | 4 | 4 | realtime | `realtime/integration/mutable_messages_test.md:613` | K-04 (mode) |
| `SYNC` | keyword | 17 | 17 | realtime | `realtime/unit/presence/realtime_presence_channel_state.md:40` | K-01 (wire) |
| `TEST` | keyword | 14 | 10 | realtime,rest | `rest/integration/proxy/rest_fallback.md:49` | CM-02 |
| `TESTS` | keyword | 64 | 64 | realtime,rest | `rest/integration/auth.md:23` | CM-02 |
| `THEN` | keyword | 1 | 1 | realtime | `realtime/unit/connection/connection_id_key_test.md:364` | C-01 |
| `THROW` | keyword | 20 | 15 | realtime,rest | `rest/unit/auth/auth_callback.md:507` | E-05 |
| `THROWS` | keyword | 2 | 2 | rest | `rest/unit/auth/client_id.md:434` | E-03 |
| `TM` | keyword | 1 | 1 | rest | `rest/unit/batch_publish.md:246` | prose (`rest/unit/batch_publish.md:246`): C-14 |
| `TRY` | keyword | 2 | 2 | realtime | `realtime/unit/channels/channel_annotations.md:275` | E-06 |
| `UNTIL` | keyword | 12 | 12 | realtime | `realtime/integration/delta_decoding_test.md:133` | W-07 |
| `UPDATE` | keyword | 19 | 19 | realtime | `realtime/integration/presence_lifecycle_test.md:246` | K-06 (enum) |
| `URL` | keyword | 2 | 0 | realtime,rest | `rest/unit/helpers/mock_http.md:47 (helper)` | helper-internal (a field type in `mock_http.md` / `mock_websocket.md` interfaces) |
| `UTC` | keyword | 7 | 7 | rest | `rest/unit/presence/rest_presence.md:636` | DH-08 |
| `WAIT` | keyword | 26 | 25 | realtime,rest | `rest/integration/history.md:211` | W-14 (integration), W-07 (`WAIT UNTIL`), W-14 (unit `WAIT(n)`) |
| `WAIT_FOR` | keyword | 1 | 1 | realtime | `realtime/unit/connection/connection_open_failures_test.md:562` | W-07 |
| `WARN` | keyword | 2 | 2 | realtime | `realtime/unit/channels/channel_annotations.md:745` | L-03, K-10 |
| `WHERE` | keyword | 9 | 9 | realtime,rest | `rest/integration/presence.md:131` | C-06, AS-08 |
| `WHILE` | keyword | 2 | 2 | rest | `rest/integration/pagination.md:224` | C-01 |
| `WITH` | keyword | 403 | 403 | realtime,rest | `rest/integration/auth.md:25` | W-06 (`WITH timeout`), E-03 (`THROWS ... WITH:`), CM-03 (`POST ... WITH body`) |
| `WITH_CLOCK` | keyword | 2 | 2 | rest | `rest/unit/auth/token_details.md:281` | T-04 |

Totals: 144 keywords and 85 snake_case calls (union of the two module scans), every one mapped to a row above.
