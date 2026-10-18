#!/usr/bin/env python3
"""Corpus construct scanner for UTS pseudocode.

Usage: scan_constructs.py <spec-clone>/uts/<module>

Prints the uppercase keywords and snake_case calls used in `pseudo` fences,
outside comments and string literals, with counts, as plain text in two
sections (KEYWORDS, SNAKE_CASE CALLS). Compare its output with the construct
catalogue; also use it to list the infra symbols the corpus uses. Exit 0 on
success; 2 on a usage error. Standalone (no imports from sibling scripts), so it
can be copied into a generated skill.
"""
import collections, pathlib, re, sys


USAGE = "usage: scan_constructs.py <spec-clone>/uts/<module>"


def usage_error(message):
    print(f"{USAGE}\nscan_constructs.py: error: {message}", file=sys.stderr)
    return 2


def main(argv):
    if "-h" in argv[1:] or "--help" in argv[1:]:
        print(__doc__.strip())
        return 0
    unknown = [a for a in argv[1:] if a.startswith("-")]
    if unknown:
        return usage_error(f"unknown option {unknown[0]}")
    if len(argv) != 2:
        return usage_error(f"expected one <spec-clone>/uts/<module>, got {len(argv) - 1} arguments")
    root = pathlib.Path(argv[1]).expanduser()
    if not root.is_dir():
        return usage_error(f"not a directory: {argv[1]}")
    keywords, calls = collections.Counter(), collections.Counter()
    for path in root.rglob("*.md"):
        in_pseudo = False
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("```"):
                in_pseudo = (line.strip() == "```pseudo") if not in_pseudo else False
                continue
            if not in_pseudo or line.lstrip().startswith(("#", "//")):
                continue
            line = re.sub(r'"[^"]*"', '""', line)                 # ignore string literals
            line = re.split(r"\s(?:#|//)\s", line, maxsplit=1)[0]  # drop trailing comments
            keywords.update(re.findall(r"\b[A-Z][A-Z_]+\b", line))
            calls.update(re.findall(r"\b([a-z][a-z0-9]*(?:_[a-z0-9]+)+)\s*\(", line))
    for title, counter in (("KEYWORDS", keywords), ("SNAKE_CASE CALLS", calls)):
        print("==", title)
        for name, count in sorted(counter.items()):
            print(f"{count:6d}  {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
