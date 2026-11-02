#!/usr/bin/env python3
"""audit_translation.py: deterministic faithfulness audit of a UTS spec against its translated C# test, so the
per-spec review reconciles an extracted ledger instead of eyeballing two files.

Usage (run from the repository root):
    python3 audit_translation.py <spec-file.md> <GeneratedTests.cs>
    python3 audit_translation.py --help          # this text, exit 0

It does zero semantic judgement: only mechanical extraction with regular expressions, so the same inputs always
give the same report.

1. Test-ID coverage. Every `**Test ID**: <id>` in the spec against every `// UTS: <id>` comment in the C# file.
   Tags are read from the C# line-comment text, so a tag inside a block comment or a string is not a tag.
   - missingInCSharp: a spec Test ID with no tag (and no not-applicable marker): a whole test is absent.
   - orphanInCSharp: a tag with no matching spec Test ID: a stale or hand-edited tag.
   - duplicateInCSharp: a tag carried by more than one test; only the last block with that tag is examined.
   - notApplicableInCSharp: spec IDs that have no test but carry a `// UTS-NA: <Test ID> - not applicable:
     <reason>` marker; they are listed here instead of in missingInCSharp. Nothing else is checked about them.

2. Per-test ledger. Within each spec test block (from one Test ID to the next Test ID, or to a heading at or
   above the heading level the Test ID sits under; a same-level sub-section heading such as Setup, Test Steps,
   Assertions, Cleanup or Notes does not end it) every non-blank, non-comment line inside a pseudo fence is
   extracted, grouped by section, each line tagged "assert", "await" or "step". Spec assert keywords are
   matched first, longest first (AWAIT_ERROR, EXPECT THROW, FAILS WITH, THROWS, ASSERT_*, ASSERT), then await
   keywords (poll_until_success, AWAIT_STATE, AWAIT UNTIL, POLL_UNTIL, WAIT UNTIL, poll_until, AWAIT_ALL,
   WAIT_FOR, AWAIT); string literals and trailing comments are ignored. The C# test's assertion calls and
   wait calls are counted separately, on code only (comments and string contents are removed), from one tag to
   the next tag; a spec ASSERT is not hidden by surplus waits. assertionShortfall > 0 is a strong signal that an
   assertion was dropped; awaitShortfall is the softer signal (a native await of an SDK call is not counted).

C# pattern table (the single authority; G is the optional generic argument, nesting allowed:
(?:<(?:[^<>()]|<[^<>()]*>)*>)? ). A matched span is not matched twice; longest first.
  Tag             //\s*UTS:\s*(\S+)                     on line-comment text
  Not applicable  //\s*UTS-NA:\s*(\S+)\s*(?:-|em dash)?\s*(.*)   on line-comment text
  Assertion       UtsAssert.ContainsInOrder(   .Should()   .And.<Upper>(   Assert.<Upper>(
  Wait            PollUntilSuccess G(  PollUntil G(  AwaitConnectionState(  AwaitChannelState(
                  UtsWait.WithTimeout G(  AssertSucceeded(  Task.WhenAll G(  AwaitConnectionAttempt(
                  AwaitRequest(  AwaitClientMessage(  AwaitNextMessageFromClient(  AwaitClientClose(
                  AwaitProtocolMessages(  TriggerActionAsync(  GetLogAsync(  AddRulesAsync(
  Neither         UtsAssert.Fail, ProcessPendingEvents, AdvanceTime, Task.Delay, a plain await of an SDK op
A spec AWAIT inside a poll_until(...) condition is covered by the C# poll that renders it.

Output (one JSON object on stdout):
  ok, spec, csharp, notVerifiable (true exactly when the spec has no Test IDs),
  idCoverage {specCount, csharpCount, missingInCSharp [], orphanInCSharp [], duplicateInCSharp [],
              notApplicableInCSharp []},
  perTest [{testId, title, sections [{heading, lines [{text, kind}]}], specCodeLineTotal, specAsserts [],
            specAwaits [], specAssertCount, specAwaitCount, csharpPresent, csharpAssertionCalls [],
            csharpAssertionCount, csharpAwaitCalls [], csharpAwaitCount, assertionShortfall, awaitShortfall}],
  idlessSections [{heading, level, afterTestId, sections, lines [{text, kind}], assertCount, awaitCount}]
              (sections after the first Test ID that have pseudo code but no Test ID of their own; reported
              separately and never counted toward the preceding test or the summary totals),
  summary {specAssertTotal, specAwaitTotal, csharpAssertionTotal, csharpAwaitTotal,
           testsWithShortfall [], testsWithAwaitShortfall []}.
On failure: {"ok": false, "error": "USAGE" | "FILE_NOT_FOUND" | "INTERNAL_ERROR", "message": ...}.

Robustness: the tool never crashes on a spec/test pair; it prints one parseable JSON object, never a traceback.

Exit status: 0 clean; 2 when missingInCSharp, orphanInCSharp or duplicateInCSharp is non-empty; 64 could not
run (bad usage, unreadable file, internal error). Python 3.8+, standard library only; reads UTF-8.
"""
import json
import re
import sys
from pathlib import Path

# Spec markers -------------------------------------------------------------
TEST_ID_RE = re.compile(r"\*\*Test ID\*\*:\s*`([^`]+)`")
HEADING_RE = re.compile(r"^(#{1,4})(?:\s+(.*?))?\s*#*\s*$")
# Any opening or closing fence (``` or ~~~, three or more); PSEUDO_FENCE_RE says whether an opener is a pseudo block.
FENCE_RE = re.compile(r"^\s*(`{3,}|~{3,})\s*([^`\s]*)")
PSEUDO_FENCE_RE = re.compile(r"^\s*(?:```+|~~~+)\s*pseudo\b", re.I)

# Headings that name a part of one test (not a new test or group): a same-level heading with one of these titles
# continues the current test region instead of ending it (e.g. a `### Setup` under a `### RSP3a - ...` test).
SUBSECTION_TITLE_RE = re.compile(
    r"^(?:common\s+)?(?:setup|test\s+steps?|assertions?|test\s+steps\s+and\s+assertions?|cleanup|teardown|"
    r"notes?|steps|expected(?:\s+\w+)?|verification|preconditions?|arrange|act)\b", re.I)

SPEC_ASSERT_KEYWORDS = [
    r"\bAWAIT_ERROR\b", r"\bEXPECT\s+THROW\b", r"\bFAILS\s+WITH\b", r"\bTHROWS\b", r"\bASSERT_[A-Z0-9_]+\b",
    r"\bASSERT\b",
]
SPEC_AWAIT_KEYWORDS = [
    r"\bpoll_until_success\b", r"\bAWAIT_STATE\b", r"\bAWAIT\s+UNTIL\b", r"\bPOLL_UNTIL\b", r"\bWAIT\s+UNTIL\b",
    r"\bpoll_until\b", r"\bAWAIT_ALL\b", r"\bWAIT_FOR\b", r"\bAWAIT\b",
]
SPEC_ASSERTS = [re.compile(x) for x in SPEC_ASSERT_KEYWORDS]
SPEC_AWAITS = [re.compile(x) for x in SPEC_AWAIT_KEYWORDS]

# C# markers (matched on line-comment text) --------------------------------
UTS_TAG_RE = re.compile(r"//\s*UTS:\s*(\S+)")
UTS_NA_RE = re.compile(r"//\s*UTS-NA:\s*(\S+)\s*(?:—|-)?\s*(.*)")

G = r"(?:<(?:[^<>()]|<[^<>()]*>)*>)?"  # a generic argument list, nested generics allowed
TEST_ASSERTION_PATTERNS = [
    ("UtsAssert.ContainsInOrder", r"\bUtsAssert\s*\.\s*ContainsInOrder\b"),
    (".Should()", r"\.\s*Should\s*\(\s*\)"),
    (".And.<assertion>()", r"\.\s*And\s*\.\s*(?!Should\b)[A-Z]\w*\s*" + G + r"\s*\("),
    ("Assert.<method>", r"(?<![\w.])(?:Xunit\s*\.\s*)?Assert\s*\.\s*[A-Z]\w*"),
]
TEST_WAIT_PATTERNS = [
    ("PollUntilSuccess", r"\bPollUntilSuccess\s*" + G + r"\s*\("),
    ("PollUntil", r"\bPollUntil\s*" + G + r"\s*\("),
    ("AwaitConnectionState", r"\bAwaitConnectionState\s*\("),
    ("AwaitChannelState", r"\bAwaitChannelState\s*\("),
    ("UtsWait.WithTimeout", r"\bUtsWait\s*\.\s*WithTimeout\s*" + G + r"\s*\("),
    ("AssertSucceeded", r"\bAssertSucceeded\s*\("),
    ("Task.WhenAll", r"\bTask\s*\.\s*WhenAll\s*" + G + r"\s*\("),
    ("AwaitNextMessageFromClient", r"\.\s*AwaitNextMessageFromClient\s*\("),
    ("AwaitConnectionAttempt", r"\.\s*AwaitConnectionAttempt\s*\("),
    ("AwaitProtocolMessages", r"\.\s*AwaitProtocolMessages\s*\("),
    ("AwaitClientMessage", r"\.\s*AwaitClientMessage\s*\("),
    ("AwaitClientClose", r"\.\s*AwaitClientClose\s*\("),
    ("AwaitRequest", r"\.\s*AwaitRequest\s*\("),
    ("TriggerActionAsync", r"\bTriggerActionAsync\s*\("),
    ("GetLogAsync", r"\bGetLogAsync\s*\("),
    ("AddRulesAsync", r"\bAddRulesAsync\s*\("),
]
TEST_ASSERTIONS = [(label, re.compile(rx)) for label, rx in TEST_ASSERTION_PATTERNS]
TEST_WAITS = [(label, re.compile(rx)) for label, rx in TEST_WAIT_PATTERNS]


def fail(code, message, status=64, **extra):
    """Emit a structured error and exit; 64 is kept distinct from the audit's own 0 and 2 outcomes."""
    print(json.dumps({"ok": False, "error": code, "message": message, **extra}, indent=2, ensure_ascii=False))
    sys.exit(status)


def read_text(path):
    """Tolerant read: undecodable bytes are replaced, a BOM is dropped."""
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    return text[1:] if text.startswith("﻿") else text


def count_forms(code, table):
    """Matches of each form in code, longest first, never matching one span twice; returns the labels."""
    hits, taken = [], []
    for label, rx in table:
        for m in rx.finditer(code):
            if any(m.start() < e and s < m.end() for s, e in taken):
                continue
            taken.append((m.start(), m.end()))
            hits.append(label)
    return hits


# --------------------------------------------------------------------------- spec side


def spec_code(line):
    """A pseudo line without string literals and trailing comment, or None for a blank/comment line."""
    s = line.strip()
    if not s or s.startswith("#") or s.startswith("//"):
        return None
    s = re.sub(r'"(?:[^"\\]|\\.)*"', '""', s)
    return re.split(r"\s(?:#|//)(?:\s|$)", s, maxsplit=1)[0]


def line_kind(code):
    """assert (first match, assert keywords first), await, or step."""
    for rx in SPEC_ASSERTS:
        if rx.search(code):
            return "assert"
    for rx in SPEC_AWAITS:
        if rx.search(code):
            return "await"
    return "step"


def is_subsection(title):
    return bool(SUBSECTION_TITLE_RE.match(title))


def parse_spec(path):
    """Return (tests, idless). tests: ordered list of {testId, title, sections [{heading, lines [{text, kind}]}],
    asserts, awaits, codeLineTotal}. A test block runs from its Test ID to the next Test ID or to a heading that
    is not part of the test: one above its level, or at its level unless it is a sub-section heading such as
    Setup / Test Steps / Assertions (see SUBSECTION_TITLE_RE); deeper headings stay inside the test.
    idless: sections after the first Test ID that open with such a boundary heading but have no Test ID of their
    own, [{heading, level, afterTestId, sections, lines, assertCount, awaitCount}]; only those holding pseudo
    code are kept, and their lines are never counted toward a test."""
    tests, idless = [], []
    cur = None     # open test block
    loose = None   # open ID-less block (only after the first test)
    last_heading = (0, "")
    fence = None  # (marker char, length, is_pseudo) while inside a fence

    def close_loose():
        nonlocal loose
        if loose is not None:
            loose["sections"] = [s for s in loose["sections"] if s["lines"]]
            if loose["sections"]:
                loose["lines"] = [ln for s in loose["sections"] for ln in s["lines"]]
                loose["assertCount"] = sum(1 for ln in loose["lines"] if ln["kind"] == "assert")
                loose["awaitCount"] = sum(1 for ln in loose["lines"] if ln["kind"] == "await")
                idless.append(loose)
            loose = None

    for line in read_text(path).splitlines():
        fm = FENCE_RE.match(line)
        if fm:
            marker = fm.group(1)
            if fence is None:
                fence = (marker[0], len(marker), bool(PSEUDO_FENCE_RE.match(line)))
            elif marker[0] == fence[0] and len(marker) >= fence[1] and not fm.group(2):
                fence = None
            continue
        if fence is not None:
            target = cur if cur is not None else loose
            if target is not None and fence[2]:
                code = spec_code(line)
                if code is not None:
                    target["sections"][-1]["lines"].append({"text": line.strip(), "kind": line_kind(code)})
            continue
        hm = HEADING_RE.match(line)
        if hm:
            level, title = len(hm.group(1)), (hm.group(2) or "").strip()
            last_heading = (level, title)
            if cur is not None:
                if level > cur["level"] or (level == cur["level"] and is_subsection(title)):
                    cur["sections"].append({"heading": title, "lines": []})
                    continue
                tests.append(cur)
                cur = None
                loose = {"heading": title, "level": level, "afterTestId": tests[-1]["testId"],
                         "sections": [{"heading": title, "lines": []}]}
            elif loose is not None:
                if level > loose["level"] or (level == loose["level"] and is_subsection(title)):
                    loose["sections"].append({"heading": title, "lines": []})
                else:
                    close_loose()
                    loose = {"heading": title, "level": level, "afterTestId": tests[-1]["testId"] if tests else "",
                             "sections": [{"heading": title, "lines": []}]}
            elif tests:
                loose = {"heading": title, "level": level, "afterTestId": tests[-1]["testId"],
                         "sections": [{"heading": title, "lines": []}]}
            continue
        tm = TEST_ID_RE.search(line)
        if tm:
            if cur is not None:
                tests.append(cur)
            close_loose()
            level, title = last_heading
            cur = {"testId": tm.group(1), "title": title, "level": level,
                   "sections": [{"heading": title, "lines": []}]}
    if cur is not None:
        tests.append(cur)
    close_loose()
    for t in tests:
        t["sections"] = [s for s in t["sections"] if s["lines"]]
        t["asserts"] = [ln["text"] for s in t["sections"] for ln in s["lines"] if ln["kind"] == "assert"]
        t["awaits"] = [ln["text"] for s in t["sections"] for ln in s["lines"] if ln["kind"] == "await"]
        t["codeLineTotal"] = sum(len(s["lines"]) for s in t["sections"])
        del t["level"]
    return tests, idless


# --------------------------------------------------------------------------- C# side


def lex_csharp(text):
    """Per line: (code with comments removed and string/char literal contents blanked, line-comment text or '').
    Handles // and /* */ comments, "..." with escapes, @"..." verbatim (""), $"..." and $@"..." interpolated
    strings (holes are blanked too), and char literals."""
    code_lines, comment_lines = [], []
    code, comment = [], []
    mode = "code"
    stack = []  # interpolated-string frames: [string mode, hole brace depth]
    i, n = 0, len(text)

    def newline():
        code_lines.append("".join(code))
        comment_lines.append("".join(comment))
        code.clear()
        comment.clear()

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "\r":
            i += 1
            continue
        if c == "\n":
            if mode == "line":
                mode = "code"
            elif mode in ("str", "chr", "istr"):
                mode = "code"  # unterminated literal: recover at the line end
                stack.clear()
            newline()
            i += 1
            continue
        in_hole = bool(stack) and mode == "code"
        if mode == "code":
            if c == "/" and nxt == "/":
                mode = "line"
                comment.append("//")
                i += 2
                continue
            if c == "/" and nxt == "*":
                mode = "block"
                i += 2
                continue
            if c in "$@" and nxt in "$@" and nxt != c and i + 2 < n and text[i + 2] == '"':
                mode = "ivstr"
                if not in_hole:
                    code.append('"')
                i += 3
                continue
            if c == "$" and nxt == '"':
                mode = "istr"
                if not in_hole:
                    code.append('"')
                i += 2
                continue
            if c == "@" and nxt == '"':
                mode = "vstr"
                if not in_hole:
                    code.append('"')
                i += 2
                continue
            if c == '"':
                mode = "str"
                if not in_hole:
                    code.append('"')
                i += 1
                continue
            if c == "'":
                mode = "chr"
                if not in_hole:
                    code.append("'")
                i += 1
                continue
            if in_hole:
                if c == "{":
                    stack[-1][1] += 1
                elif c == "}":
                    if stack[-1][1] == 0:
                        mode = stack.pop()[0]
                    else:
                        stack[-1][1] -= 1
                i += 1
                continue
            code.append(c)
            i += 1
            continue
        if mode == "line":
            comment.append(c)
            i += 1
            continue
        if mode == "block":
            if c == "*" and nxt == "/":
                mode = "code"
                i += 2
            else:
                i += 1
            continue
        if mode in ("str", "istr"):
            if c == "\\":
                i += 2
                continue
            if mode == "istr" and c == "{":
                if nxt == "{":
                    i += 2
                    continue
                stack.append([mode, 0])
                mode = "code"
                i += 1
                continue
            if c == '"':
                mode = "code"
                if not stack:
                    code.append('"')
            i += 1
            continue
        if mode in ("vstr", "ivstr"):
            if c == '"' and nxt == '"':
                i += 2
                continue
            if mode == "ivstr" and c == "{":
                if nxt == "{":
                    i += 2
                    continue
                stack.append([mode, 0])
                mode = "code"
                i += 1
                continue
            if c == '"':
                mode = "code"
                if not stack:
                    code.append('"')
            i += 1
            continue
        if mode == "chr":
            if c == "\\":
                i += 2
                continue
            if c == "'":
                mode = "code"
                if not stack:
                    code.append("'")
            i += 1
            continue
        i += 1
    newline()
    return code_lines, comment_lines




def parse_csharp(path):
    """Return (blocks, duplicates, not_applicable_ids). `blocks` maps a Test ID to
    {assertionCalls, assertionCount, awaitCalls, awaitCount}; a block runs from its tag line to the next tag
    line (or EOF), counting lexed code only. A duplicated tag keeps its LAST block and is listed in `duplicates`."""
    code_lines, comment_lines = lex_csharp(read_text(path))
    tags, na_ids = [], []
    for i, comment in enumerate(comment_lines):
        if not comment:
            continue
        m = UTS_TAG_RE.search(comment)
        if m:
            tags.append((i, m.group(1)))
        n = UTS_NA_RE.search(comment)
        if n:
            na_ids.append(n.group(1))
    counts = {}
    for _, tag in tags:
        counts[tag] = counts.get(tag, 0) + 1
    duplicates = [tag for tag, n in counts.items() if n > 1]
    blocks = {}
    for idx, (start, tag) in enumerate(tags):
        end = tags[idx + 1][0] if idx + 1 < len(tags) else len(code_lines)
        asserts, awaits = [], []
        for code in code_lines[start:end]:
            asserts += count_forms(code, TEST_ASSERTIONS)
            awaits += count_forms(code, TEST_WAITS)
        blocks[tag] = {"assertionCalls": asserts, "assertionCount": len(asserts),
                       "awaitCalls": awaits, "awaitCount": len(awaits)}
    return blocks, duplicates, na_ids


def build_report(spec_path, test_path, spec_tests, blocks, duplicates, na_ids, idless=()):
    spec_ids = [t["testId"] for t in spec_tests]
    spec_set, test_set, na_set = set(spec_ids), set(blocks), set(na_ids)
    missing = [i for i in spec_ids if i not in test_set and i not in na_set]
    not_applicable = [i for i in dict.fromkeys(spec_ids) if i not in test_set and i in na_set]
    orphan = [i for i in blocks if i not in spec_set]
    per_test = []
    for t in spec_tests:
        b = blocks.get(t["testId"])
        per_test.append({
            "testId": t["testId"],
            "title": t["title"],
            "sections": t["sections"],
            "specCodeLineTotal": t["codeLineTotal"],
            "specAsserts": t["asserts"],
            "specAwaits": t["awaits"],
            "specAssertCount": len(t["asserts"]),
            "specAwaitCount": len(t["awaits"]),
            "csharpPresent": b is not None,
            "csharpAssertionCalls": b["assertionCalls"] if b else [],
            "csharpAssertionCount": b["assertionCount"] if b else 0,
            "csharpAwaitCalls": b["awaitCalls"] if b else [],
            "csharpAwaitCount": b["awaitCount"] if b else 0,
            "assertionShortfall": (len(t["asserts"]) - b["assertionCount"]) if b else len(t["asserts"]),
            "awaitShortfall": (len(t["awaits"]) - b["awaitCount"]) if b else len(t["awaits"]),
        })
    return {
        "ok": True,
        "spec": spec_path,
        "csharp": test_path,
        "notVerifiable": len(spec_ids) == 0,
        "idCoverage": {
            "specCount": len(spec_ids),
            "csharpCount": len(blocks),
            "missingInCSharp": missing,
            "orphanInCSharp": orphan,
            "duplicateInCSharp": duplicates,
            "notApplicableInCSharp": not_applicable,
        },
        "perTest": per_test,
        "idlessSections": list(idless),
        "summary": {
            "specAssertTotal": sum(len(t["asserts"]) for t in spec_tests),
            "specAwaitTotal": sum(len(t["awaits"]) for t in spec_tests),
            "csharpAssertionTotal": sum(b["assertionCount"] for b in blocks.values()),
            "csharpAwaitTotal": sum(b["awaitCount"] for b in blocks.values()),
            "testsWithShortfall": [p["testId"] for p in per_test if p["assertionShortfall"] > 0],
            "testsWithAwaitShortfall": [p["testId"] for p in per_test if p["awaitShortfall"] > 0],
        },
    }


def main():
    if len(sys.argv) == 2 and sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        sys.exit(0)
    if len(sys.argv) != 3:
        fail("USAGE", "Usage: audit_translation.py <spec-file.md> <GeneratedTests.cs>  (or --help)")
    spec_path, test_path = sys.argv[1], sys.argv[2]
    for p in (spec_path, test_path):
        if not Path(p).is_file():
            fail("FILE_NOT_FOUND", f"{p!r} not found.")
    try:
        spec_tests, idless = parse_spec(spec_path)
        blocks, duplicates, na_ids = parse_csharp(test_path)
        report = build_report(spec_path, test_path, spec_tests, blocks, duplicates, na_ids, idless)
    except Exception as exc:  # noqa: BLE001 - deliberate catch-all; this tool must not crash
        fail("INTERNAL_ERROR", f"{type(exc).__name__}: {exc}", spec=spec_path, csharp=test_path)
    print(json.dumps(report, indent=2, ensure_ascii=False))
    cov = report["idCoverage"]
    sys.exit(2 if (cov["missingInCSharp"] or cov["orphanInCSharp"] or cov["duplicateInCSharp"]) else 0)


if __name__ == "__main__":
    main()
