---
name: uts-to-csharp
description: "Translates the UTS pseudocode test specs in an ably/specification module directory (uts/rest or uts/realtime) into runnable C# xUnit tests in the ably-pubsub-dotnet UTS test tree. Use when asked to translate, port, derive or generate tests from a UTS module, or to evaluate UTS-derived tests against the .NET SDK and record deviations. Validates the directory, resolves target directories, asks for a tier (unit, integration, proxy), the specs and translate-only or evaluate, then derives, compiles, optionally runs and audits a C# test per spec. Not for uts/objects (the SDK has no LiveObjects API), creating or upgrading this skill or its UTS infra, writing UTS specs, or native non-UTS tests."
license: "Apache-2.0"
allowed-tools: Bash Read Edit Write
metadata:
  short-description: "Translate UTS specs into C# xUnit tests"
  team: "engineering"
  version: "2.0.0"
  tags: "testing, uts, csharp, dotnet, ably-pubsub-dotnet, test-translation"
  marketplace: "false"
  aligned-to: "uts-to-swift 2.0.0 (ALIGN-1)"
---

Translate the UTS pseudocode test specs under a **module directory** into runnable C# xUnit tests in the
ably-pubsub-dotnet UTS test tree (`src/Ably.PubSub.Tests.Shared/Uts`). Every tier lands under that
directory via the resolver's per-module mapping. The resolver output (Step A) is always the single source
of truth for where a file goes and what it is named — never hand-compute the path or class name. Every
new file is registered in `Ably.PubSub.Tests.Shared.projitems` (Step 5).

**Working directory and paths.** Run every command in this skill from the **repo root**. Call this skill's
scripts by their path inside the skill directory — the folder that holds this SKILL.md — written below as
`<skill-dir>`.

**The argument** is a UTS *module* directory — a directory sitting directly under the spec repo's `uts/`,
e.g. `<cloned-ably-specification-repo-path>/uts/rest`. Take it from the text after `/uts-to-csharp`
(Codex: `$uts-to-csharp`), or from a module path named in the user's message; make it absolute (resolve a
relative path against the current directory) before Step A. Its name (`rest`, `realtime`, …) is the
**source module**. A module directory holds many spec files, organised into tiers (`unit/`,
`integration/`, and `integration/proxy/`).

The work happens in two phases:

- **Phase 1 — Selection (Steps A–E below):** a bundled resolver script validates the directory and works
  out the target test directories, the spec files, and their class names; you then pick a tier, pick
  which specs, and choose whether to also evaluate.
- **Phase 2 — Per-spec translation (Steps 1–7):** for each selected spec file, derive a C# test.

## Required reading — from the module's clone

Always read `<module-dir>/../docs/writing-derived-tests.md` first (once per run) — it is in the same spec
clone the module directory came from; don't rely on memory or the inlined summaries, the manual is updated
over time. For the proxy tier also read `<module-dir>/../docs/proxy.md` (once per run).

---

# Phase 1 — Selection

Path validation, the directory mapping, spec discovery, and class-name derivation are all mechanical, so a
bundled script does them — that keeps selection byte-for-byte deterministic instead of relying on the model
to re-eyeball regexes, join paths, and hand-convert `snake_case` → `PascalCase` each run.

> **If no module directory was given**, stop and show: `Usage: /uts-to-csharp <path-to-uts-module-directory>`
> (Codex: `$uts-to-csharp <path-to-uts-module-directory>`) — with the example
> `/uts-to-csharp <cloned-ably-specification-repo-path>/uts/rest`
> (the path to a module directory inside a local clone of
> [`ably/specification`](https://github.com/ably/specification)).

## Step A — Resolve the module

Run the resolver on the absolute directory (substitute the real path):

```bash
python3 <skill-dir>/scripts/resolve_uts.py "<module-dir>"
```

It prints one JSON object. **If `ok` is `false`, relay `error` and `message` to the user and stop** — error
codes: `NOT_A_UTS_MODULE_PATH` (not a `.../uts/<module>` directory), `DIR_NOT_FOUND`, `NO_TIER_DIRS` (no
`unit/` or `integration/`), `MAPPING_NOT_FOUND`, `BAD_MAPPING`, `NOTES_NOT_FOUND`, `BAD_DERIVED_NAME`,
`NO_PROJECT`, `REPO_NOT_FOUND`, `INTERNAL_ERROR`; with `--create` also `BAD_TARGET_NAME`,
`ENTRY_PROTECTED`, `WRITE_FAILED`. (Argument errors are argparse text on stderr, exit 2.)

On success it gives `sourceModule`, `mapped`, `testRoot`, `translationNotes`, `offerable`, `reason`,
`specClone` (`sha`), `infra`, `warnings`, and a `tiers` object with one entry per tier
(`unit` / `integration` / `proxy`), each carrying `present`, `sourceDir`, `targetDir`, `namespace`, and
`specs` (a list of `{file, className, compileInclude, registered, compileInsertAfter}`). `infra` carries
`root`, `readme`, `testsRoot` and, per tier, `sources` (the helper files to read in Step 3). **Relay every
`warnings` entry to the user.** **If `offerable` is `false` (here `objects`), relay `reason` verbatim and
stop.**

Everything downstream reads from this output — treat it as the single source of truth and don't recompute
paths or names by hand. A tier's `targetDir` comes from the module mapping (`testRoot` + the tier entry,
e.g. `src/Ably.PubSub.Tests.Shared/Uts/RestTests/Unit`). The test file is `<targetDir>/<className>.cs`,
and the run filter is `FullyQualifiedName~<namespace>.<className>.` — both composed from those fields.

`translationNotes` is the path to a per-module ably-js → .NET type/interface map when the module declares
one (its `notes` field in `uts-package-mapping.json`: `rest` → `references/rest-mapping.md`, `realtime` →
`references/realtime-mapping.md`), else `null`. When it's non-null, it is **required reading before
Phase 2** — see Step 1. (If a module's notes file only carries an "intentionally empty" placeholder
stub, tell the user the module's mapping hasn't been authored yet and treat the module as
not-yet-translatable rather than guessing a mapping.)

## Step B — Confirm or create the target mapping

The target dirs come from `uts-package-mapping.json` (alongside this skill); a spec module's name and
the SDK's internal naming for that area don't necessarily match, which is why the mapping is explicit.

- **If `mapped` is `true`**: show the resolved `targetDir` for each present tier and ask the user to confirm
  (show whatever the resolver returned verbatim).
  If they say the mapping is wrong, ask for the correct ably-pubsub-dotnet module base name and re-run with
  `--create` (below) to overwrite the entry, then re-resolve.
- **If `mapped` is `false`**: there's no mapping for `sourceModule` yet. Ask for the target module
  base name (default to `sourceModule`; suggest a rename only when the SDK uses different terminology), then
  create it deterministically and re-resolve:

  ```bash
  python3 <skill-dir>/scripts/resolve_uts.py "<module-dir>" --create <target>
  ```

  This adds `<Target>Tests/Unit`, `<Target>Tests/Integration`, and `<Target>Tests/Integration/Proxy` under
  `src/Ably.PubSub.Tests.Shared/Uts/` (PascalCase of `<target>`) and re-prints the resolved output.
  (`<target>` must be a simple module base name — a letter, then letters, digits, underscore — whose
  PascalCase form is a valid C# identifier; the script returns `BAD_TARGET_NAME` otherwise, so just ask
  again. `ENTRY_PROTECTED` means the module is `unready` or `unsupported` (such as `objects`): relay the
  message and stop. `WRITE_FAILED`: relay and stop.)

## Step C — Choose the tier

Offer the tiers whose `present` is `true`. The chosen tier fixes the `targetDir` and `specs` (from Step A)
**and** the translation flow Phase 2 uses — don't re-detect any of it per spec:

| Tier | Translation flow |
|---|---|
| **unit** | mocked transport — Steps 3–4 below |
| **integration** (direct sandbox) | real sandbox, no faults — **Direct-sandbox integration tests** section |
| **proxy** | real sandbox + fault injection — **Integration tests** section (proxy subsections) |

## Step D — Choose which specs to translate

The chosen tier's `specs` list (from Step A) is the candidate set — each entry already has its source `file`
and derived `className`. Present it and ask whether to translate **all** of them or a **selected subset**.
Then continue to Step E.

## Step E — Translate only, or also evaluate?

`writing-derived-tests.md` splits the work into **Translation** (always) and **Evaluation** (only
meaningful once the SDK implementation for this module exists). Ask the user which they want, and carry the
answer into Phase 2:

- **Translate only** — generate each test and make it **compile** (Steps 1–5 and the Step 7 review). Don't
  run the tests. Use this when the SDK feature isn't implemented yet, so there's nothing to run against.
- **Translate and evaluate** — all of the above **plus** running the tests and **fixing until they pass**
  (Step 6): work the decision tree, and where the SDK genuinely diverges, gate/adapt the assertion and
  record a deviation. Use this when the implementation exists.

If you can't tell whether the implementation exists, ask the user rather than guessing.

---

# Phase 2 — Per-spec translation

Run this for **each** spec file selected in Step D. **Step 6 only applies in "translate and evaluate" mode
(Step E)** — in "translate only" mode, stop after compiling (Step 5) and reviewing (Step 7), and skip
Step 6 entirely.

When translating several specs, do Steps 1–4 (generate the file) for every spec first, then run Step 5
(compile) once for the whole module, then per file run Step 6 (only if evaluating) and the Step 7 review —
compiling once is faster than per-file and surfaces cross-file issues together. For a single spec, just go
through the steps in order. Finish with the **Run summary**.

## Step 1 — Read the spec (and any module translation notes)

**If Step A reported a non-null `translationNotes`, read that file first (once per run).** UTS specs are
written in a language-agnostic pseudocode that mirrors the *ably-js* API; the .NET SDK differs in naming
(PascalCase, `…Async`), failure model and options, and the notes map each spec symbol to its .NET
equivalent. Skipping them yields tests that read like ably-js and won't compile. A module's notes may also
**override parts of the generic flow below** — Step 3's infrastructure reading list, the **white-box spec
list** (Step 4's internals section), Step 4's method-naming rule, and Step 6's deviations-file location;
where the notes override, they win. (The internal-access ladder itself is not overridable.)
(If the notes file is still the "intentionally empty" placeholder, stop and tell the user — see Step A.)

Then read the current spec file (the one being translated from the Step D selection). Identify:
- All test cases — each has a structured ID like `realtime/unit/RSA4c2/callback-error-connecting-disconnected-0` and a description
- The protocol used (WebSocket for Realtime, HTTP for REST)
- Any timer usage (`enable_fake_timers`, `ADVANCE_TIME`)
- Any **protocol-variant dimension** — a `PROTOCOL` (`json` / `msgpack`) matrix the spec header says to run
  "once per variant". msgpack is compiled out of this SDK build (`UseBinaryProtocol` is a no-op), so
  translate the test **once, JSON only** — never a `[Theory]` over protocols — and put the line
  `// NOTE: msgpack variant not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)`
  **above** the test's `// UTS:` tag.
- Any **structural variant**: an H3 sub-case → one test with the one tag; a `### Test Cases` table → one
  `[Theory]` with an `[InlineData]` per applicable row (or a loop in one `[Fact]`) under the one tag,
  inapplicable rows omitted with a `// NOTE:`; an ID-less section → translated with
  `// NOTE: no Test ID in the spec: <heading>` and method name `NoId_<PascalHeading>`, listed in the run
  summary for hand review; Given/When/Then prose → the prose as `//` comments, Given/When as setup/action,
  each Then an assertion; a spec filed under another module's tree is translated where it sits.
- Any **not-applicable input**: first decide not-applicable vs deviation (`writing-derived-tests.md`,
  "Check the SDK's API surface"). Inputs the build or the language can't express (msgpack-only, a
  wrong-typed argument C# rejects, a capability the platform lacks, a removed deprecated option the notes
  list) → **omit the test** and put, alone on its line where the test would be,
  `// UTS-NA: <Test ID> — not applicable: <reason>` (the spec lines may follow as `//` comments). Never a
  deviation, never a coverage gap, never silently translated as JSON. A spec with **no Test IDs** that is
  wholly not applicable (e.g. `rest/unit/encoding/msgpack_interop.md`) → generate no file; list it in the
  run summary.

---

## Step 2 — Output path

Don't derive anything here — the resolver (Step A) already produced it. For the chosen tier use its
`targetDir` and `namespace`, and for the spec being translated use its `className` from that tier's `specs`
list. Write the test to `<targetDir>/<className>.cs`.

> **Existing file.** If that file already exists, check that its `// Derived from` line names this spec
> (else stop and ask); regenerate the affected tests, **preserving** `[DeviationFact]` gates, adapted
> assertions, fail-fast placeholders and their `deviations.md` entries. Never extend a non-UTS file.

The spec's own `<sub>` grouping (e.g. `connection/`, `channels/`) is **not** reflected in the output — every
test sits directly in `targetDir` (the resolver flattens it). The chosen tier also fixes the translation
flow: **unit** → the rules in Steps 3–4 below; **integration** (direct sandbox) → the **Direct-sandbox
integration tests** section; **proxy** → the proxy subsections of the **Integration tests** section.

Each test file is an **xUnit** class whose base class matches the tier:
`public class <className> : UtsUnitTestBase` (unit), `… : UtsIntegrationTestBase` (direct sandbox), or
`… : UtsProxyTestBase` (proxy). No `[Collection]` at unit; `[Collection(UtsSandboxCollection.Name)]` at
integration and `[Collection(UtsProxyCollection.Name)]` at proxy. Every class carries
`[Trait("uts", "<tier>")]`, plus `[Trait("type", "integration")]` at integration and proxy — traits aren't
inherited, and a class without them runs in no UTS job and leaks into the native jobs.

---

## Step 3 — Read infrastructure files

### Infra reference

> **Orientation — read `infra.readme` first.** It's the human-readable guide to the infra: layout, the
> symbol maps from every helper spec, waits and defaults, the threading model, per-tier defaults, the
> integration and proxy tiers, SDK behaviour a translator meets, and **Known gaps**. A module whose
> translation notes override the reading list (Step 1) may fall outside that README — when the notes say
> so, follow them instead.

Then read **every file in `infra.tiers.<tier>.sources`** (the resolver's list for the chosen tier — never a
hard-coded list; you need exact method signatures). Use the infra smoke tests under `infra.testsRoot` as
API-usage examples only — never copy their scenarios. (`infra.tiers.<tier>.tests` is only the command that
runs the infra self-tests; this skill never runs it.) Reference tests: none yet, for every tier.

The README's "SDK behaviour a translator meets" lists known SDK deviations: translate as written; the
evaluation step gates them.

## Step 4 — Generate the C# test file

Apply the translation rules below, then write the file.

### Accessing SDK internals (`InternalsVisibleTo`)

Both test heads are friends of the SDK via `InternalsVisibleTo`, so C# `internal` members are visible to
the tests. When a spec needs an internal class/method/field, work down this list:

1. **The public API** first.
2. **An existing `internal` member or seam.** The infra README's "SDK hooks and seams" lists the seams
   already wired (fake clock, delta decoder, network state, workflow drain) — reuse them, never duplicate
   them.
3. **Anything else — don't change the SDK** (its files carry uncommitted work). Keep the spec's line as a
   comment under a `// NOTE:` saying why no assertion is emitted (see "Comments and assertion fidelity"),
   and record a **Mock Infrastructure Limitation**: a MIL stub (see "Test patterns for a diagnosed
   failure") when the whole test depends on it; otherwise record the line under *Mock Infrastructure
   Limitations* in `deviations.md` in evaluate mode, or list it in the run summary in translate-only mode.

Only white-box specs listed in the module notes reach internals; integration and proxy specs never do
unless the notes list them as white-box because the spec itself prescribes the internal write. White-box
access stays in the test body or a file-local helper; it never moves into the infra.

### Client construction

Set `ClientOptions` fields (`Key`, `AutoConnect`, `Recover`, `DisconnectedRetryTimeout`, etc.) in the
object initialiser passed to `Realtime(...)`/`Rest(...)`. Set **only** the options the spec sets, exactly
as it sets them (keep `autoConnect: false` when the spec has it, never add it when it doesn't); spec
millisecond durations become `TimeSpan` options. The unit factories seed **no** key: when the spec sets
none, use `ValidKey` (or `ValidOptions()`) and disclose it in the header
(`// Disclosures: dummy key ValidKey (spec sets no key)`). Never qualify SDK types with `Realtime.`,
`Types.`, `Shared.`, `Push.` or `Http.` (they bind to test namespaces) — add the `using` instead.

| Pseudocode | C# |
|---|---|
| `install_mock(mock_http)` / `install_mock(mock_ws)` | `InstallMock(mockHttp);` / `InstallMock(mockWs);` — **before** `Rest(...)`/`Realtime(...)` (the mock is injected at construction). `UninstallMocks()` undoes it |
| `Rest(options: ClientOptions(key: "..."))` | `var rest = Rest(new ClientOptions { Key = "..." });` |
| `Realtime(options: ClientOptions(key: "...", autoConnect: false))` | `var client = Realtime(new ClientOptions { Key = "...", AutoConnect = false });` |
| `Realtime(options: ClientOptions(autoConnect: false))` | `var client = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });` — plus the dummy-key disclosure |
| `enable_fake_timers()` | `EnableFakeTimers();` — **before** `Realtime(...)`/`Rest(...)` (idempotent; a timer already running stays on the real clock) |

With nothing installed, the factories use a strict HTTP default and a refusing WebSocket default;
unexpected requests fail at teardown.

### Mock setup — WebSocket

Use the **handler pattern**: configure the simulated server in `onConnectionAttempt`. As in the spec,
opening the socket and delivering the `CONNECTED` message are two separate calls —
`respond_with_success()` then `send_to_client(...)`.

Spec pseudocode:

```pseudo
mock_ws = MockWebSocket(
  onConnectionAttempt: (conn) => {
    conn.respond_with_success()
    conn.send_to_client(ProtocolMessage(
      action: CONNECTED,
      connectionId: "connection-1",
      connectionKey: "key-abc-123",
      connectionDetails: ConnectionDetails(connectionKey: "key-abc-123")
    ))
  }
)
install_mock(mock_ws)
client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))
client.connect()
AWAIT_STATE client.connection.state == ConnectionState.connected
ws_connection = mock_ws.events.find(e => e.type == CONNECTION_SUCCESS).connection
```

C# (`mockWs.Connections[0]` is the first opened connection — the spec's
`events.find(CONNECTION_SUCCESS).connection`; `ActiveConnection` is the latest open one):

```csharp
var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
{
    conn.RespondWithSuccess();
    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-abc-123"));
});
InstallMock(mockWs);
var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });
client.Connect();
await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
var ws = mockWs.Connections[0];
```

When attempts need different behaviour (e.g. first succeeds, reconnects refused), branch on a counter
inside the handler:

Spec pseudocode:

```pseudo
connection_attempt_count = 0
mock_ws = MockWebSocket(
  onConnectionAttempt: (conn) => {
    connection_attempt_count++
    IF connection_attempt_count == 1:
      conn.respond_with_success()
      conn.send_to_client(ProtocolMessage(
        action: CONNECTED,
        connectionId: "conn-s",
        connectionKey: "key-s",
        connectionDetails: ConnectionDetails(connectionKey: "key-s", connectionStateTtl: 2000)
      ))
    ELSE:
      conn.respond_with_refused()
  }
)
```

C# (handlers run on SDK threads: bump counters with `Interlocked.Increment(ref n)`, read them with
`Volatile.Read(ref n)`):

```csharp
var attemptCount = 0;
var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
{
    if (Interlocked.Increment(ref attemptCount) == 1)
    {
        conn.RespondWithSuccess();
        conn.SendToClient(ProtocolMessages.ConnectedMessage("conn-s", "key-s", connectionStateTtl: 2000));
    }
    else
    {
        conn.RespondWithRefused();
    }
});
```

A handler that uses its own mock must be assigned after construction (`mockWs.OnConnectionAttempt = …`).
Handlers run on SDK threads and must not block or await. The infra also offers an await style
(`AwaitConnectionAttempt`, `AwaitNextMessageFromClient`, `AwaitClientMessage(action)`, `AwaitClientClose`,
`AwaitProtocolMessages`) and the shorthand `OnConnect`/`OnMessage(action, …)`/`OnClientMessage`; don't mix
the handler and await styles on one mock except as the README's mixing rules allow.

**The mock doesn't answer CLOSE.** When a spec closes a client and then awaits CLOSED, add the
infra-driving answer `mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());` (or
deliver `ProtocolMessages.ClosedMessage()` explicitly) with
`// Disclosures: the mock answers CLOSE with CLOSED (infra-driving)` — not a deviation.

### Mock setup — HTTP

Spec pseudocode:

```pseudo
captured_requests = []
mock_http = MockHttpClient(
  onConnectionAttempt: (conn) => conn.respond_with_success(),
  onRequest: (req) => {
    captured_requests.push(req)
    req.respond_with(200, [1704067200000])
  }
)
install_mock(mock_http)
client = Rest(options: ClientOptions(key: "app.key:secret"))
```

C#:

```csharp
var capturedRequests = new CaptureList<PendingHttpRequest>();
var mockHttp = new MockHttpClient(
    onConnectionAttempt: conn => conn.RespondWithSuccess(),
    onRequest: req =>
    {
        capturedRequests.Add(req);
        req.RespondWith(200, new[] { 1704067200000L });
    });
InstallMock(mockHttp);
var rest = Rest(new ClientOptions { Key = "app.key:secret" });
```

`MockHttpClient` also offers `CapturedRequests`, the await style `AwaitConnectionAttempt()`/`AwaitRequest()`,
and the legacy queue (`QueueResponse*`, `QueueTimeout`) for specs that use `queue_response`.

### Variable declarations

Take spec variable names (adapted to camelCase), for new ones, if needed don't use "noname" names (like
"fields"), make the name concrete. Spec-built names: `"history-RTL10d-" + random_id()` →
`"history-RTL10d-" + RandomId()` at integration/proxy, `+ Guid.NewGuid().ToString("N")` at unit; a name
that is a C# keyword gets `@` (`@base`).

### Control flow and data helpers

| Pseudocode | C# |
|---|---|
| `IF/ELSE IF/ELSE`, `WHILE`, `PASS`, `END` | native; `PASS` → `// PASS` |
| `AND`/`OR`/`NOT` | `&&`/`\|\|`/`!` with the spec's grouping |
| `LOG "…"` | `Output.WriteLine(…)` |
| `FOR i IN 0..n-1` | inclusive: `for (var i = 0; i <= n - 1; i++)` |
| `FIND … WHERE`, `FILTER`, `.map/.any` | `FirstOrDefault`, `Where(...).ToList()`, `Select`, `Any` (`using System.Linq;`) |
| spec-local `function` | a private method before the first test |
| `"${…}"`, `str(x)` | `$"…{x}…"`, `x.ToString()` |
| `RETURN x IF cond ELSE null` / `BREAK` / `RETURN` | `return cond ? x : null;` / `break;` / `return …;` |
| `parse_json` / `json_encode` | `JObject.Parse(req.BodyText)` / `JArray.Parse`; `JsonConvert.SerializeObject(x)` (`using Newtonsoft.Json;`) |
| base64 / `encode_uri_component` / `encode_utf8` | `Convert.ToBase64String`/`FromBase64String`; `Uri.EscapeDataString(x)`; `Encoding.UTF8.GetBytes(s)` (`using System.Text;`) |
| `byte_array`, `random_bytes(n)` / `DateTime(…UTC)` | `new byte[] { … }`, `new Random().NextBytes(buf)` / `new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)` |
| `parse_query_string(s)` | `new RecordedUrl(new Uri("http://localhost/?" + s)).QueryParams` |
| `extract_between`, `COUNT_UNIQUE`, `EXTRACT_FALLBACK_ID` | a file-local helper; `xs.Distinct().Count()`; a `Regex` |

### Capturing connection attempts / requests

Mock handlers and SDK callbacks run on SDK threads, so a plain `List<T>` captured into them is a data
race. Use the UTS infra's thread-safe `CaptureList<T>` instead:

Spec pseudocode:

```pseudo
captured_connection_attempts = []
mock_ws = MockWebSocket(
  onConnectionAttempt: (conn) => {
    captured_connection_attempts.append(conn)
    conn.respond_with_success()
    conn.send_to_client(ProtocolMessage(
      action: CONNECTED,
      connectionId: "c",
      connectionKey: "k",
      connectionDetails: ConnectionDetails(connectionKey: "k")
    ))
  }
)
# ... after AWAIT_STATE client.connection.state == ConnectionState.connected:
ASSERT captured_connection_attempts[0].url.query_params["recover"] == "..."
```

C#:

```csharp
var capturedConnectionAttempts = new CaptureList<PendingWebSocketConnection>();
var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
{
    capturedConnectionAttempts.Add(conn);
    conn.RespondWithSuccess();
    conn.SendToClient(ProtocolMessages.ConnectedMessage("c", "k"));
});
// ... after await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected):
capturedConnectionAttempts[0].QueryParams["recover"].Should().Be("...");
```

`CaptureList<T>` exposes `Add`, `Count`, an indexer and `Snapshot()` (a copy; use `.First()` on it). Use
`Count` to change outcome for different connection attempts. The mock also records them itself:
`mockWs.ConnectionAttempts`, `mockWs.LastConnectUrl`. A `SET()` filled from callbacks →
`ConcurrentDictionary<string, byte>` (`using System.Collections.Concurrent;`).

### Inspecting outgoing frames

The spec inspects client-sent frames through the mock's event timeline
(`mock_ws.events.filter(e => e.type == "ws_frame" AND e.direction == "client_to_server")`); the .NET UTS
infra exposes the decoded equivalent as `mockWs.MessagesFromClient` / `MessagesFromClientOfAction(action)`.
Capture after the channel/connection state confirms the send happened:

Spec pseudocode:

```pseudo
ws_connection = mock_ws.events.find(e => e.type == CONNECTION_SUCCESS).connection
channel.attach()
ws_connection.send_to_client(ProtocolMessage(action: ATTACHED, channel: "channel-one", channelSerial: "serial-1"))
AWAIT_STATE channel.state == ChannelState.attached
sent_frames = mock_ws.events.filter(e => e.type == "ws_frame" AND e.direction == "client_to_server")
attach_frame = sent_frames.find(f => f.message.action == ATTACH AND f.message.channel == "channel-one")
ASSERT attach_frame.message.channelSerial == "serial-1"
```

C#:

```csharp
var ws = mockWs.Connections[0];
_ = channelOne.AttachAsync();
ws.SendToClient(ProtocolMessages.AttachedMessage("channel-one", new Dictionary<string, JToken> { ["channelSerial"] = "serial-1" }));
await UtsWait.AwaitChannelState(channelOne, ChannelState.Attached);
var attachFrames = mockWs.MessagesFromClientOfAction(ProtocolMessages.Attach).Where(m => m.Channel == "channel-one").ToList();
attachFrames.Should().HaveCount(1);
attachFrames[0].ChannelSerial.Should().Be("serial-1");
```

`MessagesFromClient` spans all the mock's connections, and `MockEvent` has no connection field; for one
connection's frames, slice `mockWs.Events` between the `MockEventType.ConnectionSuccess` events (the
`MessageFromClient` events after the Nth success and before the next; `e.Message`). Await style:
`await mockWs.AwaitClientMessage(ProtocolMessages.Attach)`.

### Mock method reference

`conn` is the object the relevant handler hands you — for WebSocket it's the `PendingWebSocketConnection`
passed to `onConnectionAttempt` (and `mockWs.Connections[n]` / `ActiveConnection` are the `MockConnection`s
once open); for HTTP it's the `PendingHttpConnection` passed to the `MockHttpClient` `onConnectionAttempt`.
`req` is the `PendingHttpRequest` passed to `onRequest`.

| Pseudocode | C# |
|---|---|
| `conn.respond_with_success()` | `conn.RespondWithSuccess()` |
| `conn.respond_with_success(message)` | `conn.RespondWithSuccess(message)` — accept + deliver in one call; `conn.RespondWithConnected()` for the default CONNECTED |
| `conn.respond_with_refused()` | `conn.RespondWithRefused()` |
| `conn.respond_with_timeout()` / `respond_with_dns_error()` (WS) | `conn.RespondWithTimeout()` (partial: the SDK's connect timer decides; drive it with `EnableFakeTimers` + `AdvanceTime`) / `conn.RespondWithDnsError()` |
| `conn.respond_with_error(msg)` | `conn.RespondWithError(msg, thenClose: true)` |
| `mock_ws.send_to_client(msg)` / `_and_close` / `simulate_disconnect()` | `SendToClient(msg)` / `SendToClientAndClose(msg)` / `SimulateDisconnect()` |
| close from server / raw frame / ping frame | `CloseFromServer()` / `SendToClientRaw(json)` / `SendPingFrame()` |
| `await_connection_attempt()` / `await_next_message_from_client()` / `await_client_message(action)` / `await_client_close()` | `await mockWs.AwaitConnectionAttempt()` / `AwaitNextMessageFromClient()` / `AwaitClientMessage(action)` / `AwaitClientClose()`; HTTP `await mockHttp.AwaitConnectionAttempt()` / `AwaitRequest()` |
| `req.respond_with(200, {...})` | `req.RespondWith(200, body, headers)` |
| `req.respond_with_delay(delay, status, body)` | `req.RespondWithDelay(ms or TimeSpan, status, body)` (milliseconds; fake clock when enabled) |
| `req.respond_with_timeout()` | `req.RespondWithTimeout()` |
| `conn.respond_with_refused/timeout/dns_error()` (HTTP) | `conn.RespondWithRefused()` / `RespondWithTimeout()` / `RespondWithDnsError()` |
| `queue_response(…)` | `mockHttp.QueueResponse(status, body, headers)` and friends |
| `mock_ws.events…` / `connect_attempts` / `last_connect_url` / `reset()` | `mockWs.Events` / `EventsOfType(MockEventType.X)`; `ConnectionAttempts`; `LastConnectUrl`; `Reset()` (also `mockHttp.Reset()`) |

Remaining gaps are in the README's "Known gaps"; never silently substitute a similar outcome. The skill
**never changes the UTS infra**: a missing capability is a MIL stub (see "Test patterns for a diagnosed
failure") in evaluate mode, or a run-summary item in translate-only mode; infra changes are a separate task.

### Protocol messages and types

`ProtocolMessages` (namespace `Ably.PubSub.Tests.Uts.Infra.Unit`) builds server-to-client frames as
`JObject`s of wire names; `ProtocolMessage` (with `.Flag`, `.MessageAction`) is in `Ably.PubSub.Types`.
Use the builders; a hand-built `new JObject { ["action"] = ProtocolMessages.Detach, ["channel"] = "c" }`
only where no builder exists.

| Pseudocode | C# |
|---|---|
| `ProtocolMessage(action: CONNECTED, connectionId, connectionKey, …)` / `CONNECTED_MESSAGE` | `ProtocolMessages.ConnectedMessage(connectionId, connectionKey, …)` / `ProtocolMessages.ConnectedMessage()` |
| `ProtocolMessage(action: ATTACHED, channel, channelSerial)` | `ProtocolMessages.AttachedMessage("c", new Dictionary<string, JToken> { ["channelSerial"] = "serial-1" })` |
| `ProtocolMessage(action: ERROR, error: ErrorInfo(code, statusCode, message))` | `ProtocolMessages.ErrorMessage(code, message, statusCode)` — **argument order differs**; pass the spec's `statusCode` (default `code/100`) |
| ACK / NACK / CLOSED / DETACHED / DISCONNECTED / MESSAGE / PRESENCE / SYNC / AUTH / PING / HEARTBEAT | `AckMessage(msgSerial, count)`, `NackMessage(…)`, `ClosedMessage()`, `DetachedMessage`, `ServerDetachedMessage`, `DisconnectedMessage`, `MessageProtocolMessage`, `PresenceProtocolMessage`, `SyncMessage`, `AuthMessage`, `PingMessage`, `HeartbeatMessage` |
| protocol actions as values / flags | `ProtocolMessages.Attach`, `ProtocolMessages.Message`, …; `(int)ProtocolMessage.Flag.HasPresence`, `ProtocolMessages.HasPresenceFlag` |
| mock event types | `MockEventType.ConnectionAttempt`, `ConnectionSuccess`, `ConnectionFailure`, `MessageFromClient`, … |
| `connectionStateTtl: 2000` (wire ms) in a template | stays `2000`; `ClientOptions` durations are `TimeSpan` |
| `DEFAULT_REST_HOST` / `SANDBOX_ENDPOINT` | `private const string DefaultRestHost = "main.realtime.ably.net";` / the integration base's `SandboxEndpoint` |
| `ConnectionState.connected` / `ChannelState.attached` | `ConnectionState.Connected` / `ChannelState.Attached` |
| the two `MessageAction` enums | `Ably.PubSub.MessageAction` (message) vs `ProtocolMessage.MessageAction` (wire) |

### Awaiting state

`AWAIT_STATE x.state == ConnectionState.X` → `await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.X)`
(default 10 s, or `timeout:`; latch-based; fails fast on FAILED; **completes at once if the state already
matches**). Channels: `await UtsWait.AwaitChannelState(channel, ChannelState.Attached)`. For other
conditions (e.g. "a frame was sent") use `await UtsWait.PollUntil(() => cond, description: "…")`. Don't use
poll unless you can't find alternative or spec says to do so. The details:

| Pseudocode | C# |
|---|---|
| **re-entry**: already in S, the stimulus must first leave S (e.g. `simulate_disconnect()` then `AWAIT_STATE … == connected`) | record before the stimulus — `var states = UtsRecord.ConnectionStates(client.Connection);` — then `await UtsWait.PollUntil(() => states.Count(s => s == ConnectionState.S) >= 1 && <progress condition>, timeout: …, description: "re-entered S");` with `// NOTE: AWAIT_STATE would return at once (already S)`; in evaluate mode list it as a UTS-spec-error candidate |
| `AWAIT_STATE x.state IN [a, b]` / `!= S` / transient states | record before the stimulus, then poll the record (`states.Any(s => s == a \|\| s == b)`); transient states → `UtsAssert.ContainsInOrder(states.Snapshot(), …)`; never an intermediate `AWAIT_STATE`, and never `AwaitConnectionState(…, Disconnected)` when FAILED is acceptable. Record-and-verify applies at **every** tier |
| `x.on(LISTEN state_changes.append)` | `var stateChanges = UtsRecord.ConnectionStateChanges(client.Connection);` (also `ChannelStates`, `ChannelStateChanges`) |
| `… WITH timeout: N seconds` | the `timeout:` argument; on a bare `AWAIT`: `await UtsWait.WithTimeout(task, TimeSpan.FromSeconds(N), "what")`. Deadlines are wall-clock, immune to the fake clock |
| `AWAIT UNTIL` / `WAIT UNTIL` / `WAIT_FOR <cond>` / `poll_until(cond, …)` | `await UtsWait.PollUntil(() => cond, timeout, interval);` counters bumped on SDK threads read with `Volatile.Read(ref n)` |
| `x = poll_until(…)` | `var x = await UtsWait.PollUntil(() => …);` — assert on `x`, never re-read; `T` must be a reference type, so wrap a value type: `var box = await UtsWait.PollUntil(() => count == 3 ? Tuple.Create(count) : null, …); box.Item1` |
| `poll_until_success(…)` | `await UtsWait.PollUntilSuccess(() => op(), timeout, interval)` |
| `process_pending_events()` | `await ProcessPendingEvents();` — at unit, prefer it to `PollUntil` when the condition only needs queued work drained |
| `error = AWAIT_ERROR future` / `AWAIT_ALL futures` | `var r = await future; r.IsSuccess.Should().BeFalse(); var error = r.Error;` / `var results = await Task.WhenAll(futures);` then assert each |
| unit `WAIT(n)` | by case: (a) fake timers enabled → `await AdvanceTime(n);`; (b) a delivery wait → `await ProcessPendingEvents();` + `// NOTE:`; (c) a negative check with no fake clock → `await Task.Delay(n); // NOTE: spec real-time WAIT kept: a drain would weaken the negative check` |
| integration `WAIT 500ms` | `await Task.Delay(500); // spec WAIT kept: wall-clock` |

A connection **already FAILED** when a state wait starts is latched, not failed. Integration and proxy
tiers use the base wrappers and an explicit poll timeout **and** interval (see **Integration tests**).

### Timer control

Spec pseudocode:

```pseudo
client = Realtime(...)
# ...
enable_fake_timers()
# ...
ADVANCE_TIME(1500)                     # fires due timers
```

C#:

```csharp
EnableFakeTimers();                    // BEFORE Realtime(...)/Rest(...)
var client = Realtime(new ClientOptions { ... });
// ...
await AdvanceTime(1500);               // fires due timers and runs to quiescence
```

For multi-retry scenarios use the spec's loop (don't compute exact jumps):

Spec pseudocode:

```pseudo
LOOP up to 10 times:
  ADVANCE_TIME(1500)
  IF client.connection.state == ConnectionState.suspended:
    BREAK
AWAIT_STATE client.connection.state == ConnectionState.suspended
```

C# (one statement per line — StyleCop SA1107):

```csharp
for (var i = 0; i < 10; i++)
{
    await AdvanceTime(1500);
    if (client.Connection.State == ConnectionState.Suspended)
    {
        break;
    }
}
await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Suspended);
```

`TestClock()` / `WITH_CLOCK` → `EnableFakeTimers(start?)`, `await AdvanceTime(ms)`, `Clock.Now` /
`Clock.NowMillis`; disclose in the header. `SCHEDULE_AFTER(100ms):` in a handler →
`Clock.ScheduleAfter(TimeSpan.FromMilliseconds(100), () => …)`. `NOW()` → `Clock.NowMillis` at unit,
`DateTimeOffset.UtcNow` at integration. The unit tier never uses real timers except case (c) above.

### Asserting on logs

To assert "an error is logged", inject a `CapturingLogSink` and check it:

```csharp
var log = new CapturingLogSink();
var client = Realtime(new ClientOptions { Key = "...", LogHandler = log, LogLevel = LogLevel.Debug });
// ...
log.Contains(LogLevel.Error, "recovery key").Should().BeTrue();
```

Levels are Debug/Warning/Error/None; spec `info`/`verbose` → `Debug`, and an info-only assertion is a
deviation candidate (README "Known gaps").

### Comments and assertion fidelity

Carry over **every** comment from the spec pseudocode verbatim, as a `//` comment at the matching step —
these explain *why* each step exists and must not be dropped or paraphrased.

Translate every spec `ASSERT`/`AWAIT` into a C# assertion at the same place. If an assertion genuinely
has no C# equivalent (e.g. it checks a language-specific construct, or the SDK exposes no observable
hook for it), **do not silently omit it** — keep the spec's comment/line and add a `//` note explaining why
no assertion is emitted. Never delete the spec line.

```csharp
// The recovery key should round-trip the connection id        ← spec comment, copied verbatim
recovered.ConnectionId.Should().Be("connection-1");

// NOTE: no assertion: Connection exposes no ErrorReason in this state — see deviations.md
// ASSERT connection.errorReason IS null
```

A dropped or weakened assertion that is *not* annotated this way is a bug — Step 7 re-checks for it.

When the spec is ambiguous, pick an interpretation, write it as a `// NOTE:`, continue, and list it in the
run summary. An SDK name or shape difference is idiomatic translation — record nothing; adapting a
white-box check's *shape* is fine, *dropping* the check is not. Keep the spec's test section headings as
comments: `// Setup`, `// Test Steps`, `// Assertions`, `// Test Steps and Assertions`, `// Cleanup`.

### Assertions (FluentAssertions)

Use FluentAssertions (`using FluentAssertions;`), **not** xUnit `Assert`:

| Pseudocode | C# |
|---|---|
| `ASSERT a == b` (`!=`) | scalars `a.Should().Be(b)` / `.NotBe(b)`; ordered collections `.Should().Equal(1L, 2L)`; unordered or object graphs `.BeEquivalentTo(…)`; status codes `((int?)error.StatusCode).Should().Be(401)` (`ErrorInfo.StatusCode` is `HttpStatusCode?`); numeric literals typed to the SDK's type (`1234567890000L`); JSON tokens cast first: `((string)frame["name"]).Should().Be("x")` |
| `IS NOT null` / `IS null` | `.Should().NotBeNull()` / `.BeNull()`; nullable value types `.Should().HaveValue()` / `NotHaveValue()` |
| `IS null OR … IS NOT SET` | decoded object: `.Should().BeNull()`; raw JSON: `(frame["k"] == null \|\| frame["k"].Type == JTokenType.Null).Should().BeTrue();` |
| `ASSERT x IS <Type>` | reference types: `x.Should().BeAssignableTo<…>();`. Where the declared type already guarantees it — **always for value types and sealed types** — keep the spec line as a comment noting the compile-time guarantee |
| `IS SAME AS` / `IS NOT SAME AS` | `.Should().BeSameAs(y)` / `.NotBeSameAs(y)` |
| `"k" IN map` / `NOT IN`; element `IN`; `x IN [a, b]` | `.Should().ContainKey("k")` / `NotContainKey`; `.Contain(x)` / `NotContain`; `x.Should().BeOneOf(a, b)` |
| `CONTAINS` / `STARTS WITH` / `ENDS WITH` | `s.Should().Contain("x")` / `ContainEquivalentOf("x")`; collections `.Should().Contain(e => pred)`; `.StartWith(…)` / `.EndWith(…)` |
| `x matches pattern "..."` | `x.Should().MatchRegex("...");` |
| `ASSERT list CONTAINS_IN_ORDER [a, b, c]` | `UtsAssert.ContainsInOrder(list, new[] { a, b, c });` — a subsequence check; never `.Equal` on a filtered list |
| `ALL x IN xs: pred` / `ANY` | `xs.Should().NotContain(x => !(pred));` (vacuous on empty) / `xs.Should().Contain(x => pred);`; not `OnlyContain`; no `?.` in expression predicates |
| `ARE all unique` / `IS EMPTY` / `IS NOT complete` | `.OnlyHaveUniqueItems()` / `.BeEmpty()`, `.NotBeEmpty()` / `await ProcessPendingEvents(); future.IsCompleted.Should().BeFalse();` |
| `ASSERT list.length == N` / `>=` | `.Should().HaveCount(N)` / `xs.Count.Should().BeGreaterThanOrEqualTo(N)` |
| `ASSERT x == y (with message)` / `FAIL("msg")` | `x.Should().Be(y, "message")` / `UtsAssert.Fail("msg")` |
| `AWAIT op FAILS WITH error` (throwing API) | `Func<Task> act = () => client.TimeAsync(); var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo; error.Code.Should().Be(…);` |
| `AWAIT op FAILS WITH error` (`Result` API) | `var result = await channel.AttachAsync(); result.IsFailure.Should().BeTrue(); var error = result.Error;` |
| sync `THROWS` / `EXPECT THROW creating …` | `Action create = () => Rest(options); var error = create.Should().Throw<AblyException>().Which.ErrorInfo;` |
| `error.cause.code` | `error.Cause.Should().NotBeNull(); error.Cause.Code.Should().Be(90000);` |
| `THROW ErrorInfo(…)` in a test callback | `AuthCallback = tokenParams => throw new AblyException(new ErrorInfo("…", 50000, HttpStatusCode.InternalServerError))` (`using System.Net;`) |
| `TRY: … CATCH` | throwing API `try { … } catch (AblyException e) { error = e.ErrorInfo; }`; `Result` API: bind the failed `Result` with a comment |

Assert `ErrorInfo` fields, never exception types or `.Message`; normalise numeric types.
FluentAssertions assertions are hard; use
`using (new AssertionScope())` only where the spec says every check must be evaluated.

Every .NET SDK op is natively async. A `Result`-returning op: at integration/proxy
`AssertSucceeded(await channel.AttachAsync(), "channel.attach()");` (a `UtsIntegrationTestBase` static); at
unit `var r = await channel.AttachAsync(); r.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", r.Error?.Message);`.
A held future: `var attachTask = channel.AttachAsync(); … var r = await attachTask;`. A dropped `IsSuccess`
check turns a failing `AWAIT` green. Sync SDK calls stay sync. `AWAIT x WITH timeout:` →
`UtsWait.WithTimeout` (see above).

### Test naming and annotation

- `// UTS: <spec-id>` comment alone on its line immediately above the test's attributes. Copy the spec's
  **full** structured id verbatim (e.g. `realtime/unit/RTN16g/recovery-key-structure-0`). Don't hand-build
  the prefix — use what the spec file declares. (A hand-built prefix silently breaks the Step 7 audit's
  ID matching.) Only a `// DEVIATION: …` line may sit between the tag and the first attribute.
- Method name: `<Point>_<PascalSlug>` — `<Point>` is the ID's spec-point segment and `<PascalSlug>` the
  PascalCase of the ID's slug without the trailing `-<n>` (e.g.
  `rest/unit/RSA8d/callback-invoked-for-auth-0` → `RSA8d_CallbackInvokedForAuth`). An ID that doesn't have
  that shape is a suspected spec error: report it. The method is `public async Task`, with
  `[Trait("spec", "<Point>")]`.
- **Hang guard (unit tier):** every async unit test uses `[UtsFact]` / `[UtsTheory]` (namespace
  `Ably.PubSub.Tests.Uts.Infra`), never a bare `[Fact]`: it bounds the test at
  `UtsTestTimeouts.DefaultTestTimeoutMs` (30 s), so an SDK operation that never completes (its fallback
  timer sits on the fake clock) fails the test instead of stalling the run. A gated unit test is
  `[DeviationFact("<point>", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]`. xUnit 2.4 honours
  `Timeout` on async tests only, so a synchronous test keeps `[Fact]`. Await SDK operations directly;
  don't wrap them in `UtsWait.WithTimeout` unless the spec states a timeout.

```csharp
// UTS: realtime/unit/RTN16g/recovery-key-structure-0
[UtsFact]
[Trait("spec", "RTN16g")]
public async Task RTN16g_RecoveryKeyStructure()
{
    ...
}
```

### File template (unit tier)

This scaffold is for the **unit** tier — it wires the mocked transport (`MockWebSocket`, `InstallMock`,
`Realtime`). For the **integration** (direct sandbox) and **proxy** tiers, start from the file templates
in the **Integration tests** section instead, not from this one.

The header comes first, in this order: `// <Feature> (<spec points>)` (the spec file's H1 and its spec
points); `// Derived from uts/<path>`; `// at ably/specification@<specClone.sha>` (the resolver's full
40-character SHA); `// Tier: <tier>`; `// Unit spec: <path(s)>` (integration and proxy, when the spec names
one); `// Disclosures: <stand-ins, or none>`. Stand-ins go in `// Disclosures:`, never in `deviations.md`.

`using`s go outside the namespace, System first, then alphabetical; unused ones are fine. Test namespaces
nest under `Ably.PubSub`, so `Ably.PubSub` types need no `using`.

| Tier | `using` block |
|---|---|
| unit | `System`, `System.Collections.Generic`, `System.Linq`, `System.Net`, `System.Threading`, `System.Threading.Tasks`, `Ably.PubSub.Realtime`, `Ably.PubSub.Tests.Uts.Infra`, `Ably.PubSub.Tests.Uts.Infra.Unit`, `Ably.PubSub.Types`, `FluentAssertions`, `Newtonsoft.Json.Linq`, `Xunit`, `Xunit.Abstractions` |
| integration | as unit, minus `…Infra.Unit`, plus `Ably.PubSub.Tests.Uts.Infra.Integration` |
| proxy | as integration, plus `Ably.PubSub.Tests.Uts.Infra.Integration.Proxy` and `Ably.PubSub.Tests.Uts.Infra.Unit` (`ProtocolMessages`) |

Add `System.Collections.Concurrent`, `System.Text`, `Newtonsoft.Json` only where used.

```csharp
// Connection recovery (RTN16)
// Derived from uts/realtime/unit/connection/connection_recovery_test.md
// at ably/specification@<full 40-character sha>
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

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class ConnectionRecoveryTests : UtsUnitTestBase
    {
        public ConnectionRecoveryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: <spec-id>
        [UtsFact]
        [Trait("spec", "<Point>")]
        public async Task <Point>_<Description>()
        {
            // Setup
            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-1", "key-1"));
            });
            InstallMock(mockWs);
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });   // exactly the spec's options

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Assertions
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }
    }
}
```

The namespace is the resolver's tier `namespace`.

Helper methods' return types should be ready for use in the test code without additional casting (if you
need a cast in the test, move the conversion to the helper instead). Don't return nullables from these
methods — assert not `null` within the method itself, unless the test expects a nullable. Don't create
additional helper types for parsing JSON — use `JObject`/`JToken`, accessing fields by indexer. Spec-local
functions and helpers for the class go before the first test. Keep the spec's original tests order in the
generated test file. Keep test segmentation by adding comments like "// Setup", "// Test Steps",
"// Assertions".

**Setup and teardown.** Build clients only through the tier base's factories, so teardown closes them even
when the body throws. **Always render the spec's `CLOSE_CLIENT`** as `await CloseClient(client);`; a spec
`### Cleanup` step that awaits a state is translated; only `session.close()` and un-awaited closes become
comments (teardown does them). Extra cleanup → `OnTeardown(async () => …)`; `AFTER EACH TEST` that closes
clients → a comment; `BEFORE ALL TESTS` → the collection fixture. Never hand-roll `try`/`finally`.

**Lint.** C# 8 only (no records, `init`, file-scoped namespaces); net462-safe APIs (the net462 head
compiles the same sources); StyleCop SA1025 (single spaces, no alignment), SA1028 and SX1101 are errors;
one statement per line (lay loops and multi-statement lambdas over several lines); LF and a final newline.

---

## Step 5 — Compile

1. **Register** each new file: insert the spec's `compileInclude` line after `compileInsertAfter` in
   `src/Ably.PubSub.Tests.Shared/Ably.PubSub.Tests.Shared.projitems`, unless `registered` is `true` (an
   unregistered file is silently absent from the build; standing approval to add these lines).
2. **Build** (inner loop):

   ```bash
   dotnet build src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release
   ```

3. **Gate** — the net6 build, which also leaves the net6.0 test assembly in place for Step 6:

   ```bash
   ./build.sh --target=Build.NetStandard
   ```

   It first deletes every `bin/`/`obj/` under `src/`, and StyleCop runs as errors, as in CI. The net462
   head compiles the same shared sources in CI, so keep to C# 8 and net462-safe APIs; it isn't gated here.

Fix any compilation errors and recompile until clean. Common issues:
- The projitems line missing.
- A missing `using` (use the block above), or an SDK type qualified with `Realtime.`/`Types.`.
- Mock method names differing from what you read in Step 3's files — use the exact signatures.
- `PollUntil<T>` with a value type — wrap it.
- StyleCop SA1025/SA1028/SX1101, or C# above 8 / an API net462 lacks.

---

## Step 6 — Run tests *(evaluate mode only)*

Skip this whole step in "translate only" mode. In "translate and evaluate" mode, run the test and resolve
every failure via the decision tree below. Each test must end in exactly one of these states — never an
**unexplained** red:
- the spec-correct assertion **passes**; or
- a documented **SDK deviation** — env-gated or adapted, stays green (SDK ≠ spec; fix belongs in the SDK); or
- a documented **UTS spec error** — **fails fast** (the spec is wrong; fix belongs in the spec). This is the
  one acceptable red; or
- a **MIL stub** — a skipped test whose whole body needs a mock capability the infra lacks (recorded as a
  Mock Infrastructure Limitation, not an SDK deviation); or
- a **not-applicable** ID — no test at all, just a `// UTS-NA:` line (Step 1).

Run the suite with the namespace-qualified filter composed in Step A (`dotnet test` builds incrementally
first — never add `--no-build`, so stale binaries are never run):

```bash
# a spec's class (the filter is FullyQualifiedName~<namespace>.<className>.)
dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --filter "FullyQualifiedName~<namespace>.<className>."
# a single test (exact match, not a substring)
dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --filter "FullyQualifiedName=<namespace>.<className>.<method>"
# a whole tier, generated + infra tests
./build.sh --target=Test.NetStandard.Uts --framework=net6.0 --uts-tier=<tier>
```

(Derived class names can equal native test class names, so always qualify with the namespace. There is no
env-var gating of the integration and proxy tiers: they run directly, they just need network access; the
proxy tier on Windows needs `UTS_PROXY_LOCAL_PATH`. Run net6.0 only.)

Handle test failures using this decision tree (the **Required reading** doc you read up front has the full detail):

```
Test fails
  |
  +-- Does UTS spec match features spec?  (read <module-dir>/../../specifications/features.md from the same clone)
  |     NO  → UTS SPEC ERROR — fix the spec at source, not the test. Record in deviations.md
  |           (UTS Spec Errors) + emit a fail-fast test (below) so it's fixed early.
  |     YES
  |       +-- Does test accurately translate the UTS spec?
  |             NO  → fix the test (no deviation entry needed)
  |             YES → SDK deviation — env-gated skip or adapted assertion (below); record in the deviations file
```

### Test patterns for a diagnosed failure

Two patterns are for an **SDK deviation** (both write the spec-correct assertions); the third,
**spec-error fail-fast**, is for a **UTS spec error** and is not a deviation. The **MIL stub** and the
**not-applicable omission** complete the list.

**Env-gated skip** *(preferred for a deviation you expect to be fixed)* — the test carries the
**spec-correct** assertions, but is disabled by default via `[DeviationFact]`, so it only runs when
`RUN_DEVIATIONS` is set. Use this when the SDK diverges from the spec **today** but is expected to
converge — the skipped test becomes the executable record of what "correct" will look like:

```csharp
// UTS: realtime/unit/RSA4c2/callback-error-connecting-disconnected-0
// DEVIATION: see deviations.md
[DeviationFact("RSA4c2")]
[Trait("spec", "RSA4c2")]
public async Task RSA4c2_CallbackErrorConnectingDisconnected()
{
    // ... spec-correct setup and assertions ...
}
```

At integration/proxy use `[DeviationFact("<point>", Timeout = TestTimeoutMs)]`; at unit
`[DeviationFact("<point>", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]` (the hang guard). Record it under *Failing
Tests*. Reproduce with
`RUN_DEVIATIONS=1 dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --filter "FullyQualifiedName=<namespace>.<className>.<method>"`.

**Adapted assertion** — assert the SDK's actual behaviour to prevent regressions. **Prefer this over an
env-gated skip when the divergence is permanent or intentional** (a documented, deliberate departure from
the spec that the SDK is *not* going to change): a running test guards against regressions, whereas a
permanently-skipped spec assertion verifies nothing. Record it under *Adapted Tests*:

```csharp
// DEVIATION: spec requires error code 40106, SDK returns 40160 — see deviations.md
error.Code.Should().Be(40160);
```

**Spec-error fail-fast** *(UTS spec error — NOT an SDK deviation)* — when the decision tree's first branch
is **NO** (the UTS spec contradicts the features spec, or is internally inconsistent), the spec is the
fixable source of truth, so the test must **fail loudly** pointing at the deviation entry, rather than be
quietly adapted to green. This is the opposite of the SDK-deviation patterns above (which stay green /
assert actual behaviour) — failing fast is the forcing function that gets the spec fixed early. The body is
exactly this — no translated body (when the spec is fixed, regenerate from it; never "resolve" by matching
the broken spec); a plain `[UtsFact]` at unit (`[Fact(Timeout = TestTimeoutMs)]` at integration/proxy),
never gated or skipped:

```csharp
// UTS: realtime/unit/RTN15c4/resume-token-error-0
[UtsFact]
[Trait("spec", "RTN15c4")]
public async Task RTN15c4_ResumeWithTokenErrorReattaches()
{
    // SPEC ERROR RTN15c4: the replayed CONNECTED message contradicts the harness's resume rules.
    // Fix the UTS spec first — see deviations.md (UTS Spec Errors).
    UtsAssert.Fail("UTS spec error RTN15c4 — fix the spec first; see deviations.md");
}
```

List each in the run summary with the features-spec quote.

**MIL stub** *(a missing mock capability, SDK API, or delegated helper blocks the whole test)* — a skipped stub whose body is a
`// NOTE:` plus the spec lines as comments; recorded under *Mock Infrastructure Limitations* in evaluate
mode (a skipped stub, not an SDK deviation), listed in the run summary in translate-only mode:

```csharp
// UTS: realtime/unit/RTN22a/server-initiated-auth-0
[UtsFact(Skip = "Mock infrastructure limitation: <missing capability, API or helper>; see deviations.md")]
[Trait("spec", "RTN22a")]
public async Task RTN22a_ServerInitiatedAuth()
{
    // NOTE: <what the infra lacks>
    // <the spec lines, as comments>
}
```

**Not applicable** — no test: `// UTS-NA: <Test ID> — not applicable: <reason>` alone on its line where the
test would be (Step 1); never in `deviations.md`; listed in the run summary.

**Never use the accommodate-both pattern** (accept either spec or SDK behaviour). Every test must assert
either spec behaviour or the SDK's actual behaviour — never both at once.

Note: an *infra-driving* difference (e.g. using fake timers where the spec uses real ones, a
queue-ordering workaround, or the mock answering CLOSE) is **not** an SDK deviation — explain it in a code
comment or the header `// Disclosures:`, not `deviations.md`. `deviations.md` is only for SDK
non-compliance and for mock-capability gaps that leave a case **unable to run** (a skipped stub, per
`writing-derived-tests.md`); a sanctioned stand-in whose ports all run is neither.

### Deviations file

Record in the respective section of the shared deviations file
`src/Ably.PubSub.Tests.Shared/Uts/deviations.md` (not registered in the projitems), unless a module's notes
declare another location (Step 1), using the manual's **Recording deviations** entry format and sections
(the `writing-derived-tests.md` you read up front — not a home-grown format). Create it in evaluate mode
when absent: a title line; one paragraph saying env-gated tests run with `RUN_DEVIATIONS=1`; then the four
headings **in this order** — `## UTS Spec Errors`, `## Failing Tests`, `## Adapted Tests`,
`## Mock Infrastructure Limitations` — each `*(none)*` when empty. Entries are `### <point> — <title>`
with the manual's fields, and new entries are inserted into their category's section, never appended at
the file end. A **UTS Spec Error** (test fails fast — fix in the spec) goes under *UTS Spec Errors*; an
**SDK deviation** (env-gated/adapted — fix in the SDK) goes under *Failing Tests* / *Adapted Tests*; a
mock-capability gap goes under *Mock Infrastructure Limitations*. A module's own shape vocabulary stays in
its notes and is cited from the entry.

---

## Step 7 — Review generated output against the spec

The translation isn't done when it compiles — it's done when **every line of the source spec is faithfully
represented** in the C#: each test case, each setup step, each operation, and each assertion. Missing a
single `ASSERT` produces a test that compiles, passes, and silently checks less than the spec demands. This
review runs in **both** modes (it's static — it doesn't need the tests to have run).

### Deterministic faithfulness audit — run the script first

Eyeballing two files for "did I translate every line?" is exactly the kind of mechanical comparison the
model does inconsistently, so a bundled script extracts the ledger for you — same inputs, same report every
time. Run it for each translated spec — don't hand-build paths; use the resolver's output (Step A): the
**source spec file** is that spec's `specs[].file` (already an absolute path) and the **generated C#
file** is the chosen tier's `targetDir` + `className` (Step 2); the working directory is the repo root:

```bash
python3 <skill-dir>/scripts/audit_translation.py \
    "<tier.specs[].file>" \
    "<tier.targetDir>/<className>.cs"
```

It prints one JSON report and exits non-zero (2) when there are missing, orphan or duplicate Test IDs
(`64` when it couldn't run). It does **no** semantic judgement — it only extracts, deterministically, what
you must then reconcile by hand:

- **`idCoverage`** — the spec's `**Test ID**` set vs the C# file's `// UTS:` tag set.
  - `missingInCSharp` **must be empty.** Each entry is a spec test case with no test method — implement it.
  - `orphanInCSharp` **must be empty** (or every entry explained) — a `// UTS:` tag that no longer matches
    any spec Test ID means a stale or hand-edited tag.
  - `duplicateInCSharp` **must be empty** — a tag carried by more than one test means the audit only
    examined the last method with that tag; deduplicate the tags first.
  - `notApplicableInCSharp` lists the IDs omitted with a `// UTS-NA:` marker (reported, not a gap).
- **`notVerifiable`** — `true` when the spec has no Test IDs (`specCount` is `0`): verify that file by
  hand. A wholly not-applicable ID-less spec has no file (Step 1).
- **`perTest[]`** — for each spec test case, every non-comment code line inside the spec's `pseudo` blocks,
  **grouped by section** (`Setup` / `Test Steps` / `Assertions` / …) in `sections[]` and each line tagged
  `assert` (an `ASSERT*` outcome), `await` (an `AWAIT*` / `EXPECT` wait), or `step` (setup, mock/client
  construction, an operation). `specAsserts` / `specAwaits` are flat convenience views of the first two.
  The matching C# method's calls come alongside, counted **separately** so a surplus of waits can't hide a
  dropped assertion: assertions (`.Should()`, `.And.<X>(`, `Assert.<X>`, `UtsAssert.ContainsInOrder` →
  `csharpAssertionCount`) and waits (`AwaitConnectionState`, `AwaitChannelState`, `PollUntil`,
  `PollUntilSuccess`, `AssertSucceeded`, `UtsWait.WithTimeout`, `Task.WhenAll`, the mock `Await*` calls and
  the proxy session calls `TriggerActionAsync`/`GetLogAsync`/`AddRulesAsync` → `csharpAwaitCount`).
  `UtsAssert.Fail`, `ProcessPendingEvents`, `AdvanceTime`, `Task.Delay` and a plain `await` of an SDK op
  count as neither. `assertionShortfall` and `awaitShortfall` are the differences.
- **`summary`** — `testsWithShortfall` and `testsWithAwaitShortfall`.

The audit is a review aid, not a gate — it never crashes, it degrades. If `idCoverage.specCount` is `0`
for a file you know is a real spec, the extractor couldn't find any `**Test ID**` markers (an unusual spec
format) — treat that as "couldn't verify deterministically" and fall back to a manual side-by-side read
for that file. The known case is a **fixture-driven** spec (e.g. `rest/unit/encoding/msgpack_interop.md` —
one parameterised test looping over external fixture data instead of enumerated test cases): there the
audit will also report your `// UTS:` tag as an *orphan* — expected, not a defect; explain it in the
review and verify that file by hand.

### Coverage check — every test case is present

From the audit's `idCoverage`:
- [ ] `missingInCSharp` is empty (every spec Test ID has a test with that ID in its `// UTS:` comment, or a
      `// UTS-NA:` omission reported under `notApplicableInCSharp`); a marker never hides a spec ID that
      has no marker
- [ ] `orphanInCSharp` is empty, or each orphan is a deliberate, explained rename
- [ ] `duplicateInCSharp` is empty (no `// UTS:` tag is shared by two test methods)
- [ ] Each method name contains the spec point and a meaningful description

### Line-by-line completeness — every spec line is translated

Walk the audit's `perTest[].sections` and reconcile **each** extracted spec line against the C# method.
This is the guarantee that no setup step, operation, or assertion was dropped — the whole point of this step:
- [ ] Every `assert` line maps to a concrete C# assertion (`.Should()…`) — not a comment, not a weaker check
- [ ] Every `await` line (state waits **and** awaited operations like `AWAIT channel.attach()` /
      `setup_synced_channel(...)`) is performed via `UtsWait.AwaitConnectionState` / `AwaitChannelState` /
      `PollUntil` (unit tier) or the base's `AwaitConnectionState` / `AwaitChannelState` / `UtsWait.PollUntil`
      (integration tiers) or the corresponding SDK call; a poll counts for an `AWAIT` inside its condition
- [ ] A value-assigned `poll_until` (`x = poll_until(…)`) maps to the value-returning `PollUntil<T>` with
      later assertions on the returned value — **no re-read after the poll** (the rule lives in
      `writing-test-specs.md` in the clone's `docs/`)
- [ ] Every `step` line — client/mock construction, `ClientOptions`, installed mocks, channel ops — is
      reflected in the test setup or body. Multi-line spec constructs split across several `step` lines;
      reconcile them as a group.
- [ ] The pseudocode's inline `#` comments are carried over as `//` comments at the matching steps verbatim
- [ ] A spec line with no C# equivalent is annotated per "Comments and assertion fidelity" (spec line
      kept + `//` note), never silently dropped
- [ ] `assertionShortfall > 0` for a test (`summary.testsWithShortfall`) is a **tripwire** — fewer C#
      assertions than spec `ASSERT`s strongly suggests a dropped assertion; open that test and account for
      each one. (A negative shortfall is fine — the SDK mapping often needs *more* C# lines per spec
      `ASSERT`; an annotated omission — a MIL stub, a `// NOTE:` omission, a helper-hidden assertion or a
      value-type `IS <Type>` comment — is a valid account.)
- [ ] `awaitShortfall > 0` (`summary.testsWithAwaitShortfall`) is the **softer secondary signal** — fewer
      infra wait calls than spec `AWAIT`s. Every .NET SDK op is natively async, so a spec `AWAIT op`
      correctly becomes a plain `await` expression the counter doesn't see — expect a benign shortfall on
      nearly every test. Account for those rather than treating this as a gate.

### Setup fidelity — preconditions match the spec

For each test case, verify:
- [ ] Client options (`AutoConnect`, `Recover`, timeouts, etc.) match the spec's `ClientOptions`
- [ ] Mock responses (success / refused / timeout / DNS error / custom messages) match the spec's prescribed network behaviour
- [ ] Timer setup (`EnableFakeTimers`, `AdvanceTime`) matches every `enable_fake_timers` / `ADVANCE_TIME` in the spec
- [ ] Channel operations (attach, detach, publish) are performed in the order the spec requires

### Deviation honesty *(evaluate mode)*

Deviations are discovered by running, so this check applies in evaluate mode. For any place where the
generated test diverges from the spec pseudocode (adapted assertion, env-gated skip, or omitted step):
- [ ] A `// DEVIATION:` comment explains why
- [ ] The deviation is recorded in `src/Ably.PubSub.Tests.Shared/Uts/deviations.md`

If you find gaps during this review, fix them, then **re-run the audit script** until `missingInCSharp` /
`orphanInCSharp` / `duplicateInCSharp` are empty and every `perTest` entry reconciles, and re-run Step 5
(compile) — and, in evaluate mode, Step 6 — before finishing.

---

## Integration tests (direct sandbox + proxy)

Some specs are **integration tests** — they run against the **real Ably sandbox** instead of a mocked transport. Two tiers share one foundation and differ only in *transport*:

- **Direct sandbox** — the client connects straight to the sandbox. Happy-path interop (connect, publish, subscribe, presence); no faults.
- **Proxy** — the client is routed through the [`ably/uts-proxy`](https://github.com/ably/uts-proxy), a programmable HTTP/WebSocket proxy that forwards traffic transparently but can inject faults (dropped connections, modified/injected/delayed frames, error responses) via rules.

**Shared foundation (both tiers, covered once):** the tier is the one chosen in Step C (the resolver's
path-based tiers) — don't re-detect it from the spec's content. A sandbox app is provisioned **once per
collection** (`SandboxAppFixture`) rather than per test — disclose that in the header. Clients and proxy
sessions are built **only** through the base factories and torn down automatically; **never hand-roll
teardown**, and `BEFORE ALL TESTS` is the collection fixture. Real (unmocked) clients, and the async
waits — never a fixed sleep, since real network is involved. Every integration and proxy test uses a
unique channel name: the spec's own naming with `RandomId()`, else `UniqueChannelName("<prefix>")`.

**One timeout and interval rule.** State waits → the base wrappers `AwaitConnectionState` /
`AwaitChannelState` (default 15 s), passing the spec's timeout when it states one — never `UtsWait.Await*`
(10 s) at these tiers. Polls → **always pass both** `timeout:` (the spec's value, else
`TimeSpan.FromSeconds(15)`) **and** `interval:` (the spec's value, else
`TimeSpan.FromMilliseconds(500)`, the spec's own `poll_until` default) to `UtsWait.PollUntil`. The 15 s default is this skill's integration rule and needs no `// NOTE:`; a
spec-stated value is always used verbatim. The `poll_until` semantics (value-assigned vs bare condition)
are in `writing-test-specs.md` in the clone's `docs/`. Waits throw on failure and stop the test (teardown
still runs), so there is nothing to guard.

**Required reading for the proxy tier:** before translating any proxy spec, read
`<module-dir>/../docs/proxy.md` (once per run) — it defines the proxy session/control API, the rule and
action vocabulary, and the protocol action-number table the subsections below rely on.

### Which integration tier?

| Test type | When the spec uses it |
|---|---|
| **Unit test** (mock HTTP/WS — the rest of this skill) | Client-side logic, state machines, request formation, error parsing. Fast, deterministic. |
| **Direct sandbox integration** | Happy-path behaviour (connect, publish, subscribe, presence). No fault injection. |
| **Proxy integration test** | Fault behaviour against the real backend: connection failures, resume, heartbeat starvation, token renewal under network errors, channel error injection. |

### Direct-sandbox integration tests

A **direct-sandbox** spec (no `create_proxy_session`, no rules — just happy-path interop against
`nonprod:sandbox`) uses the same `SandboxApp` provisioning as a proxy test but **drops all proxy wiring**:
no `ProxyManager`, no `ProxySession`, no `ProxyRealtime`. The client connects straight to the sandbox.
There is no reference example yet; for API usage see `SandboxSmokeTests.cs` under `infra.testsRoot` (a
wiring example only — never copy its scenarios). Don't add env-var gating to generated tests —
integration suites run ungated (they just need network).

Classes subclass `UtsIntegrationTestBase` and join `UtsSandboxCollection`. Its `Realtime(options)` /
`Rest(options)` factories build real clients and always close them at teardown (waiting for CLOSED).

**Client wiring** — the factory fills `Endpoint` (`SandboxEndpoint`) and `Key` (`ApiKey`) unless the spec
sets them or sets auth. A literal spec `endpoint: "nonprod:sandbox"` → drop it (the factory honours
`ABLY_ENDPOINT`). TLS stays on, so the plain sandbox key works. `nonprod:sandbox` is a non-production
routing policy ID, which gives the REC2c3 fallback domains; still don't set `FallbackHosts`. Set only the
options the spec sets (e.g. `AutoConnect = false`).

**Class docstring** — use a direct-sandbox variant (no proxy/fault-injection wording):

```csharp
/// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
```

**Protocol variants (`json` / `msgpack`)** — JSON only (see Step 1): translate once, with the `// NOTE:`
line above the `// UTS:` tag. The proxy tier is always JSON as well.

**Awaiting real server outcomes** — integration specs assert on real backend state, so never sleep; await
or poll:

| Pseudocode | C# |
|---|---|
| `AWAIT channel.attach()` / `publish(name, data)` | `AssertSucceeded(await channel.AttachAsync(), "channel.attach()");` / `AssertSucceeded(await channel.PublishAsync(name, data), "channel.publish()");` |
| `poll_until(() => AWAIT channel.history().items.length == N, …)` | `var page = await UtsWait.PollUntil<PaginatedResult<Message>>(async () => { var p = await channel.HistoryAsync(); return p.Items.Count == N ? p : null; }, timeout: …, interval: …, description: "history has N items");` (lambda laid over several lines), then assert on `page` |
| `AWAIT op WITH timeout: N seconds` | `await UtsWait.WithTimeout(op, TimeSpan.FromSeconds(N), "op")` |

Use generous timeouts (10–30 s) — real network is involved. Everything else is the shared foundation
described at the top of this section; a direct-sandbox test just skips the proxy-only subsections
(`ProxySession`, rule factories, the event log). Helpers shared by several of a module's suites go in
sibling files next to the suites, never under `Uts/Infra/`; suite-specific helpers stay in the class.

**File template — direct sandbox** (`integration/` dir of the module; usings per the table in Step 4):

```csharp
/// <summary>Direct-sandbox integration test against the Ably Sandbox (nonprod:sandbox via SandboxEndpoint) — no proxy, no fault injection.</summary>
[Collection(UtsSandboxCollection.Name)]
[Trait("uts", "integration")]
[Trait("type", "integration")]
public class ChannelHistoryTests : UtsIntegrationTestBase
{
    private const int TestTimeoutMs = 60000;   // raised to the spec's stated waits + 12 s

    public ChannelHistoryTests(SandboxAppFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    // UTS: <spec-id>
    [Fact(Timeout = TestTimeoutMs)]
    [Trait("spec", "<Point>")]
    public async Task <Point>_<Description>()
    {
        // Setup
        var channelName = "history-RTL10d-" + RandomId();   // the spec's own naming
        var client = Realtime(new ClientOptions { AutoConnect = false });   // only if the spec sets it
        client.Connect();
        await AwaitConnectionState(client.Connection, ConnectionState.Connected);
        // … spec steps — waits throw, the base handles all teardown …
    }
}
```

`[Fact(Timeout = …)]` is honoured by xUnit 2.4.1 only on async tests; `[DeviationFact("<point>", Timeout = TestTimeoutMs)]`
keeps the timeout on gated tests. Header: `// Unit spec: …` when the spec names one;
`// Disclosures: infra stand-in: one sandbox app per collection, not per spec file (SandboxAppFixture)`.

### Infrastructure

The helpers live under `infra.root` (`Integration/`, `Integration/Proxy/`). **Read the ones your tier uses
before translating an integration spec** (`infra.tiers.<tier>.sources`) — they hold the exact method
signatures. `SandboxApp` and `UtsIntegrationTestBase` serve **both** tiers; `ProxyManager`, `ProxySession`,
`ProxyRules`, `ProxyEvent`/`ProxyLog` and `UtsProxyTestBase` are **proxy-only**.

- **`SandboxApp`** — provisions/deletes a sandbox test app from the embedded `test-app-setup.json`.
  `SandboxApp.CreateAsync()`; `DeleteAsync()`; members `KeyStr`, `Keys`, `AppId`. The endpoint is
  `SandboxEndpoint` (`nonprod:sandbox`, or `ABLY_ENDPOINT` when set).
- **`SandboxAppFixture`** — one app per collection.
- **`UtsIntegrationTestBase`** — the direct-sandbox base class: `App`, `ApiKey`, `SandboxEndpoint`,
  `Realtime`, `Rest`, `AwaitConnectionState`, `AwaitChannelState`, `AssertSucceeded`, `RandomId`,
  `UniqueChannelName`, `GenerateJwt`, `ExtractKeyName`/`ExtractKeySecret`, `RequestTokenFromSandbox`,
  `SandboxTokenCallback`, `OnTeardown`.
- **`ProxyManager`** — syncs (downloads, verifies, caches) and starts the shared `uts-proxy` process;
  `EnsureStartedAsync()` is idempotent and called by the base (control port 9100, or
  `UTS_PROXY_CONTROL_PORT`). Windows has no pinned binary: set `UTS_PROXY_LOCAL_PATH`.
- **`ProxySession`**, **`ProxyRules`**, **`ProxyEvent`** / **`ProxyLog`** — one programmable session
  wrapping the proxy control API, the rule factories, and the typed log.
- **`UtsProxyTestBase`** — the proxy base class (extends the integration base): `CreateProxySession`,
  `ProxyRealtime`, `ProxyRest`.

**Async** (call with `await`): `SandboxApp.CreateAsync`/`DeleteAsync`, `RequestTokenFromSandbox`,
`AwaitConnectionState`/`AwaitChannelState`, `CreateProxySession`, `ProxySession.AddRulesAsync` /
`TriggerActionAsync` / `GetLogAsync` / `ExistsAsync` / `CloseAsync`, `ProxyManager.EnsureStartedAsync`,
`UtsWait.*`. Everything else listed (factories, `RandomId`, `UniqueChannelName`, `GenerateJwt`,
`ExtractKey*`, `AssertSucceeded`, `SandboxTokenCallback`, `OnTeardown`, `ProxyRules.*`, the `ProxyLog`
filters) is synchronous.

### Proxy test class docstring

Give every proxy integration test class this XML doc comment:

```csharp
/// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
```

### Session lifecycle

`create_proxy_session(endpoint: "nonprod:sandbox", rules: [...])` → the base class's
`CreateProxySession(rules: …)` — it ensures the proxy is running, creates the session against the
collection's sandbox app, and the session is **always** closed after the clients at teardown (the sandbox
is already the session's default target and honours `ABLY_ENDPOINT`, so never pass the literal endpoint).
Never hand-roll the teardown. `rules: []` → `CreateProxySession()` (`new[] { }` doesn't compile).

| Pseudocode | C# |
|---|---|
| `create_proxy_session(endpoint: "nonprod:sandbox", rules: [...])` | `var session = await CreateProxySession(rules: new[] { … });` |
| `session.add_rules(rules, position: "prepend")` | `await session.AddRulesAsync(rules, ProxyRules.Prepend);` |
| `session.trigger_action({ type: "disconnect" })` | `await session.TriggerActionAsync(ProxyRules.Disconnect());` — no open WebSocket → 409, which **fails the test** (the log still gains an `action` event); trigger only after CONNECTED |
| `session.get_log()` | `await session.GetLogAsync()` (`List<ProxyEvent>`) |
| `session.close()` | handled at teardown — don't call it yourself |
| `session.proxy_port` / `session.proxy_host` | `session.ProxyPort` / `session.ProxyHost` |

### Connecting through the proxy

`ProxyRealtime(session, options)` / `ProxyRest(session, options)` wire the SDK through the proxy:

| Proxy-def option | What `ProxyRealtime` sets |
|---|---|
| `endpoint: "localhost"` | `Endpoint = session.ProxyHost` (`"localhost"`) |
| `port: proxy_port` | `Port` / `TlsPort` = `session.ProxyPort` |
| `tls: false` | `Tls = false` |
| auth | unless the spec sets token auth, `AuthCallback = SandboxTokenCallback(<spec key or ApiKey>)` and `Key` is cleared (the key is used only for the direct sandbox token request) |

Proxy tests are **always JSON** — never parameterise the protocol here. The factory does **not** decide
`AutoConnect` — set it per the spec. `Endpoint = "localhost"` is an explicit hostname (REC1b2), so there are
no fallback domains (REC2c2); don't set `FallbackHosts`.

### Auth in proxy tests

The proxy serves plain ws (`Tls = false`) and basic (key) auth is TLS-only, so a proxied client can't just
use the sandbox key; the base wires `SandboxTokenCallback(apiKey)`, which fetches a real `TokenDetails`
over TLS. Disclose it in the header: `// Disclosures: token auth via SandboxTokenCallback (basic auth needs TLS; the proxy hop is plain)`.

**A spec callback that returns a JWT** (`generate_jwt(…)`, `callback(null, jwt)`): the SDK reads a
**string** callback result as `TokenRequest` JSON (error 80019), so return a `TokenDetails`:

```csharp
AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails(GenerateJwt(ExtractKeyName(ApiKey), ExtractKeySecret(ApiKey))))
```

with `// NOTE: a string return is read as a TokenRequest (RSA8d); wrapped in TokenDetails`. Keep the string
form only in a test whose subject is RSA8d.

**Counting callbacks:** when the spec's callback counts **and returns its own token**, hand-write it over
several lines (`Interlocked.Increment(ref authCalls); return Task.FromResult<object>(new TokenDetails(GenerateJwt(…)));`);
use `SandboxTokenCallback(onInvoked: _ => Interlocked.Increment(ref authCalls))` only when the spec's
callback returns a sandbox token. Read the counter with `Volatile.Read`. The callback runs before the AUTH
frame is sent, so after polling a client-side count, poll the proxy log for the frame
(`l.FramesToServer(ProtocolMessages.Auth).Count >= n`) with `// NOTE: timing adaptation` (not a deviation).

### Rule factory helpers

Build rules with the `ProxyRules` factories rather than raw dictionary literals. Rules are evaluated in
order, first match wins, unmatched traffic passes through, and `times` auto-removes a rule after N
firings: `ProxyRules.Rule(match, action, times: n, comment: "…")`.

| Match condition | C# |
|---|---|
| `{ "type": "ws_connect", "count": 2 }` | `ProxyRules.WsConnect(count: 2)` |
| `{ "type": "ws_connect", "queryContains": { "resume": "*" } }` | `ProxyRules.WsConnect(queryContains: new Dictionary<string, string> { ["resume"] = "*" })` |
| `{ "type": "ws_frame_to_client", "action": "ATTACHED", "channel": "c" }` | `ProxyRules.WsFrameToClient(ProtocolMessages.Attached, "c")` |
| `{ "type": "ws_frame_to_server", "action": "ATTACH", "channel": "c" }` | `ProxyRules.WsFrameToServer(ProtocolMessages.Attach, "c")` |
| `{ "type": "http_request", "method": "POST", "pathContains": "/keys/" }` | `ProxyRules.HttpRequest("POST", "/keys/")` |
| delay after the WebSocket connects | `ProxyRules.DelayAfterWsConnect(ms)` |

Actions: `RefuseConnection()`, `AcceptAndClose(code)`, `Disconnect()`, `Close(code)`, `Suppress()`,
`SuppressOnwards()`, `Delay(ms)`, `InjectToClient(JObject)`, `InjectToClientAndClose(…)`, `Replace(JObject)`,
`Passthrough()`, `HttpRespond(status, body, headers)`, `HttpDelay(ms)`, `HttpDrop()`.
`"__PASSTHROUGH__"` as a field value in a `replace` message is an ordinary string. The action is the
protocol action **number** (`ProtocolMessages.X`, e.g. 4 CONNECTED, 6 DISCONNECTED, 9 ERROR, 10 ATTACH,
11 ATTACHED) — see the action-number table in `proxy.md`. `http_replace_response` has no builder (README
"Known gaps": a MIL stub).

### Verifying the event log

`GetLogAsync()` returns a typed `List<ProxyEvent>`. Fields: `Type`, `Direction`, `QueryParams`, `Status`,
`CloseCode`, `MessageAction` (`int?`), `Message` (`JObject`). The `ProxyLog` filters: `OfType(type)`,
`WsConnects()`, `WsDisconnects()`, `Frames(direction, action?)`, `FramesToClient(action?)`,
`FramesToServer(action?)`, `HttpRequests(pathContains?)`, `HttpResponses()`, `Actions()`,
`MatchedBy(comment)`. Actions are `int?`: pass `ProtocolMessages.X` or `(int)ProtocolMessage.MessageAction.X`.

```csharp
var log = await session.GetLogAsync();
var wsConnects = log.WsConnects();
wsConnects.Count.Should().BeGreaterThanOrEqualTo(2);
wsConnects[1].QueryParams.Should().ContainKey("resume");
```

Filter by type; **never assert the log's exact length** (it also holds `action` events). **Never print
`Raw`, `Url`, `Path` or `QueryParams`** (unredacted; may carry an `accessToken`) — print the event
(`ToString()` is redacted) or use `ProxyRedaction.RedactText`.

### Conventions

1. Each test references the spec point and (where it exists) the corresponding unit test (`// Unit spec:`).
2. Setup/teardown (proxy launch, sandbox app, session, client close) is the **base class's** job — build
   everything through its factories; never hand-roll it.
3. **Waits throw** — a timeout fails the test and stops it; there is nothing to guard.
4. Use `AwaitConnectionState` / `AwaitChannelState` for state assertions; verify via SDK state **and** the
   proxy log where useful.
5. Timeouts and intervals per the one rule at the top of this section.
6. Don't set `FallbackHosts`; explicit hosts already disable fallbacks.
7. Each proxy test creates its own session.
8. Unique channel names.
9. No fixed sleeps except a spec `WAIT`, commented.

Step 5 (compile) still applies; Step 6 (run) applies only in evaluate mode (Step E). Note that proxy tests
hit the live sandbox and download the proxy binary on first run, so they are slower and require network
access.

**File template — proxy** (`integration/proxy/` dir of the module):

```csharp
/// <summary>Proxy integration test against the Ably Sandbox endpoint. Uses the programmable uts-proxy to inject transport-level faults while the SDK communicates with the real Ably backend. See uts/docs/proxy.md for proxy infrastructure details.</summary>
[Collection(UtsProxyCollection.Name)]
[Trait("uts", "proxy")]
[Trait("type", "integration")]
public class AuthReauthTests : UtsProxyTestBase
{
    private const int TestTimeoutMs = 60000;   // at least realtimeRequestTimeout + 12 s

    public AuthReauthTests(SandboxAppFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    // UTS: <spec-id>
    [Fact(Timeout = TestTimeoutMs)]
    [Trait("spec", "<Point>")]
    public async Task <Point>_<Description>()
    {
        // Setup
        var session = await CreateProxySession();   // the spec's rules: [] (or rules: new[] { … })
        // token auth: basic auth can't cross the plain proxy hop
        var client = ProxyRealtime(session, new ClientOptions { AutoConnect = false });

        // Test Steps
        client.Connect();
        await AwaitConnectionState(client.Connection, ConnectionState.Connected);
        // Late imperative fault injection, then poll — never sleep — and verify via the log:
        await session.TriggerActionAsync(ProxyRules.InjectToClient(new JObject { ["action"] = ProtocolMessages.Auth }));
        // … poll an observable state, then assert on the log …
    }
}
```

Header: `// Unit spec: …`, and the token-auth disclosure from "Auth in proxy tests" when a proxied spec
authenticates with `key:`.

---

## Run summary

End every run with this short summary, filled in:

```
UTS translation: <module>/<tier> @ ably/specification@<specClone.sha>  (mode: translate-only | evaluate)
| Spec file | Test file | IDs (spec/test/NA) | Audit | Compile | Run | Deviations added |
Not applicable (UTS-NA), MIL stubs, ID-less sections for hand review: …
Ambiguity NOTEs for review; suspected UTS spec errors (features-spec quote, draft fix): …
Changed files (incl. projitems lines added): …
```
