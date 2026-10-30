#!/usr/bin/env python3
"""Resolve a UTS spec module directory to its Ably .NET test targets.

Deterministic helper for the uts-to-csharp skill. Given a UTS spec *module*
directory (a directory directly under .../specification/uts/), it:

  - validates the path and the module's tier structure,
  - reads uts-package-mapping.json (in the skill directory, above this script),
  - resolves, per tier, the target output directory, the C# namespace and the
    .projitems registration line, and
  - lists the candidate spec files with their derived C# class names.

Doing this in code (rather than asking the model to eyeball regexes, join
paths and hand-convert snake_case to PascalCase every run) keeps the skill's
selection phase deterministic.

Run it from the repository root:
  resolve_uts.py <module-dir>                 # validate + resolve + list specs
  resolve_uts.py <module-dir> --create NAME   # add a mapping entry for this module
                                              # (test folder <Pascal(NAME)>Tests),
                                              # then resolve

Always prints a single JSON object to stdout and exits 0. On failure it prints
{"ok": false, "error": <CODE>, "message": ...} and exits 1. Command-line usage
errors are the one exception: plain text on stderr, exit 2.

Error codes: NOT_A_UTS_MODULE_PATH, DIR_NOT_FOUND, NO_TIER_DIRS,
MAPPING_NOT_FOUND, BAD_MAPPING, NOTES_NOT_FOUND, BAD_DERIVED_NAME, NO_PROJECT,
REPO_NOT_FOUND, INTERNAL_ERROR; with --create also BAD_TARGET_NAME,
ENTRY_PROTECTED, WRITE_FAILED.

Output schema (success). Paths other than the spec paths are repo-relative:
  ok                      true
  sourceModule            the module directory name, e.g. "rest"
  mapped                  true when the mapping has an entry for the module
  testRoot                the UTS test root (parent of infra.root, or the mapping's testRoot)
  translationNotes        absolute path of the module's notes file, or null when none is declared
  offerable               false when the module is marked unready or unsupported in the mapping
  reason                  that mapping text, verbatim, or null
  specClone               {sha}: HEAD of the spec clone (null sha when it is not a git clone)
  infra                   {root, readme, testsRoot, tiers.<tier> {sources [paths], tests (informational)}}
  tiers.<tier>            for unit, integration, proxy:
    present               the module has this tier directory (always false when not offerable)
    sourceDir             absolute path of the spec tier directory
    targetDir             repo-relative output directory, or null (unmapped, unready or no entry for the tier)
    namespace             the tier's C# namespace, or null when targetDir is null
    specs[]               {file (absolute), className, compileInclude, registered, compileInsertAfter}
  warnings                list of strings

Layout is flat: the test file is <targetDir>/<className>.cs. compileInclude is the
<Compile Include=...> line a shared project (.projitems) needs for that file
(null when the nearest project is an SDK .csproj, which globs, and when there is no
targetDir); registered says whether the line is already there; compileInsertAfter is
the existing line it goes after (or null).

Naming: className = PascalCase(file stem minus a trailing _test) + "Tests"; the
namespace is the mapping's namespaceRoot plus the PascalCase path below the nearest
project file. Python 3.8+, standard library only.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path, PurePosixPath

SKILL_DIR = Path(__file__).resolve().parent.parent
MAPPING = SKILL_DIR / "uts-package-mapping.json"
TIERS = ("unit", "integration", "proxy")
DEFAULT_TIER_LAYOUT = {"unit": "Unit", "integration": "Integration", "proxy": "Integration/Proxy"}
SPEC_REPO_RE = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
IDENT_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
CSHARP_KEYWORDS = set("""abstract as base bool break byte case catch char checked class const continue decimal default
delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int
interface internal is lock long namespace new null object operator out override params private protected public
readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint
ulong unchecked unsafe ushort using virtual void volatile while""".split())


class ResolveError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


def fail(code, message):
    print(json.dumps({"ok": False, "error": code, "message": message}, indent=2, ensure_ascii=False))
    sys.exit(1)


def pascal(s):
    return "".join(p[:1].upper() + p[1:] for p in re.split(r"[^A-Za-z0-9]+", s) if p)


def valid_ident(s):
    return bool(IDENT_RE.match(s)) and s not in CSHARP_KEYWORDS


def posix(p):
    return PurePosixPath(Path(p).as_posix()).as_posix()


def class_name(md_path):
    """objects_batch_test.md -> ObjectsBatchTests; auth_callback.md -> AuthCallbackTests."""
    stem = md_path.stem
    if stem.endswith("_test"):
        stem = stem[: -len("_test")]
    name = pascal(stem) + "Tests"
    if not valid_ident(name):
        raise ResolveError("BAD_DERIVED_NAME", f"{md_path.name}: derived class name '{name}' is not a C# identifier")
    return name


def is_nonspec_doc(name):
    """README/PLAN/*_SUMMARY markdown are docs, not test specs."""
    if re.fullmatch(r"(README|PLAN)\.md", name, re.IGNORECASE):
        return True
    return name.upper().endswith("_SUMMARY.MD")


def list_specs(base, exclude_proxy=False):
    """List spec .md files under `base`, deterministically.

    All exclusions are checked against the path relative to base, so they can't
    be tripped by an ancestor directory in the checkout path.
    """
    if not base.is_dir():
        return []
    specs = []
    for p in sorted(base.rglob("*.md")):
        rel_parts = p.relative_to(base).parts
        if "helpers" in rel_parts:
            continue
        if exclude_proxy and "proxy" in rel_parts:
            continue
        if is_nonspec_doc(p.name):
            continue
        specs.append(p)
    return specs


# --------------------------------------------------------------------------- mapping


def rel_path(raw, what):
    """A mapping path: non-empty, relative, no '..'. Returned with forward slashes."""
    if not isinstance(raw, str) or not raw.strip():
        raise ResolveError("BAD_MAPPING", f"{what} must be a non-empty string path, got {raw!r}")
    s = raw.strip().replace("\\", "/")
    if s.startswith(("/", "~")) or re.match(r"^[A-Za-z]:", s):
        raise ResolveError("BAD_MAPPING", f"{what} must be a relative path, not absolute: {raw}")
    parts = [x for x in s.split("/") if x not in ("", ".")]
    if ".." in parts or not parts:
        raise ResolveError("BAD_MAPPING", f"{what} must be a path without '..': {raw!r}")
    return "/".join(parts)


def load_mapping():
    if not MAPPING.is_file():
        raise ResolveError("MAPPING_NOT_FOUND", f"mapping file not found at {MAPPING}")
    try:
        return json.loads(MAPPING.read_text(encoding="utf-8"))
    except ValueError as e:
        raise ResolveError("BAD_MAPPING", f"{MAPPING} is not valid JSON: {e}")


def validate_mapping(data):
    """Check the mapping's shape; returns the normalised pieces the resolver uses."""
    if not isinstance(data, dict):
        raise ResolveError("BAD_MAPPING", "the mapping must be a JSON object")
    spec_repo = data.get("specRepo")
    if spec_repo is not None and not (isinstance(spec_repo, str) and SPEC_REPO_RE.match(spec_repo)):
        raise ResolveError("BAD_MAPPING", f"specRepo must look like '<owner>/<name>', got {spec_repo!r}")
    ns_root = data.get("namespaceRoot")
    if (not isinstance(ns_root, str) or not ns_root
            or not all(valid_ident(seg) for seg in ns_root.split("."))):
        raise ResolveError("BAD_MAPPING", f"namespaceRoot must be a dotted C# identifier, got {ns_root!r}")
    infra = data.get("infra")
    if not isinstance(infra, dict):
        raise ResolveError("BAD_MAPPING", "infra must be an object with root, readme, testsRoot and testCommand")
    clean_infra = {}
    for key in ("root", "readme", "testsRoot"):
        clean_infra[key] = rel_path(infra.get(key), f"infra.{key}")
    cmd = infra.get("testCommand")
    if not isinstance(cmd, str) or "{tier}" not in cmd:
        raise ResolveError("BAD_MAPPING", "infra.testCommand must be a string containing {tier}")
    clean_infra["testCommand"] = cmd
    test_root = data.get("testRoot")
    if test_root is not None:
        test_root = rel_path(test_root, "testRoot")
    packages = data.get("packages")
    if not isinstance(packages, dict):
        raise ResolveError("BAD_MAPPING", "packages must be an object")
    entries = {}
    for name, entry in packages.items():
        if name.startswith("_"):
            continue
        if not isinstance(entry, dict):
            raise ResolveError("BAD_MAPPING", f"packages.{name} must be an object")
        e = {"tiers": {}, "notes": None, "unready": None, "unsupported": None}
        for t in TIERS:
            if t in entry:
                e["tiers"][t] = rel_path(entry[t], f"packages.{name}.{t}")
        for key in ("notes", "unready", "unsupported"):
            if key in entry:
                if not isinstance(entry[key], str) or not entry[key].strip():
                    raise ResolveError("BAD_MAPPING", f"packages.{name}.{key} must be a non-empty string")
                e[key] = entry[key]
        entries[name] = e
    return {"specRepo": spec_repo, "namespaceRoot": ns_root, "infra": clean_infra, "testRoot": test_root,
            "packages": entries}


def find_repo_root(infra_root):
    """The repo root: four levels above this script, else the git top level of the cwd; each is
    accepted only when it contains the mapping's infra.root."""
    candidates = [Path(__file__).resolve().parents[4]]
    out = git_out(os.getcwd(), "rev-parse", "--show-toplevel")
    if out:
        candidates.append(Path(out))
    for c in candidates:
        if (c / infra_root).is_dir():
            return c
    raise ResolveError("REPO_NOT_FOUND",
                       f"no repository root containing {infra_root} found (run from the repo root)")


# --------------------------------------------------------------------------- spec clone


def git_out(clone, *args):
    """Read-only git: stdout stripped, or None on any failure."""
    try:
        out = subprocess.run(["git", "--no-optional-locks", "-C", str(clone), *args], capture_output=True,
                             timeout=30, env=dict(os.environ, GIT_OPTIONAL_LOCKS="0"))
    except (OSError, subprocess.SubprocessError):
        return None
    if out.returncode != 0:
        return None
    return out.stdout.decode("utf-8", errors="replace").strip() or None


def clone_facts(clone, spec_repo, warnings):
    sha = git_out(clone, "rev-parse", "HEAD")
    origin = git_out(clone, "remote", "get-url", "origin")
    if origin and spec_repo and not re.search(r"[/:]" + re.escape(spec_repo) + r"(?:\.git)?/?$", origin,
                                              re.IGNORECASE):
        warnings.append(f"the spec clone's origin ({origin}) is not {spec_repo} (mapping specRepo)")
    return {"sha": sha}


# --------------------------------------------------------------------------- projects


class Projects:
    """Project files of the repo: the nearest project of a target, and the Compile lines a .projitems has."""

    def __init__(self, repo):
        self.repo = repo
        self._text = {}

    def text(self, path):
        if path not in self._text:
            self._text[path] = path.read_text(encoding="utf-8", errors="replace")
        return self._text[path]

    def nearest(self, target_rel):
        """The nearest ancestor directory (inclusive) holding a .projitems or .csproj, within the repo."""
        d = self.repo / target_rel
        while True:
            if d.is_dir():
                items = sorted(d.glob("*.projitems")) or sorted(d.glob("*.csproj"))
                if items:
                    return items[0]
            if d == self.repo or self.repo not in d.parents:
                return None
            d = d.parent

    def compile_lines(self, project):
        return re.findall(r"<Compile\s+Include=\"([^\"]+)\"", self.text(project))


def namespace_for(projects, project, target_rel, ns_root):
    below = (projects.repo / target_rel).relative_to(project.parent)
    segs = [pascal(s) for s in below.parts]
    for s in segs:
        if not valid_ident(s):
            raise ResolveError("BAD_DERIVED_NAME", f"{target_rel}: segment '{s}' is not a C# identifier")
    return ".".join([ns_root] + segs)


def registration(projects, project, test_file_rel):
    """(compileInclude line, registered, insertAfter); a .csproj (SDK globbing) needs no line."""
    if project.suffix != ".projitems":
        return None, True, None
    rel = (projects.repo / test_file_rel).relative_to(project.parent)
    inc = "$(MSBuildThisFileDirectory)" + "\\".join(rel.parts)
    line = f'<Compile Include="{inc}" />'
    existing = projects.compile_lines(project)
    registered = any(x.lower() == inc.lower() for x in existing)
    after = None
    if not registered:
        prefix = "$(MSBuildThisFileDirectory)" + rel.parts[0] + "\\"
        peers = sorted((x for x in existing if x.lower().startswith(prefix.lower())), key=str.lower)
        for x in peers:
            if x.lower() < inc.lower():
                after = f'<Compile Include="{x}" />'
    return line, registered, after


# --------------------------------------------------------------------------- infra


def child_ci(parent, name):
    """The child directory of parent whose name equals name, ignoring case."""
    if parent is None or not parent.is_dir():
        return None
    for c in sorted(parent.iterdir()):
        if c.is_dir() and c.name.lower() == name.lower():
            return c
    return None


def infra_output(norm, repo, warnings):
    infra = norm["infra"]
    root = repo / infra["root"]
    tests_root, readme = infra["testsRoot"], infra["readme"]
    if not (repo / readme).is_file():
        warnings.append(f"infra: readme {readme} does not exist")

    def rel(p):
        return posix(p.relative_to(repo))

    def excluded(p):
        r = rel(p)
        return r == readme or r == tests_root or r.startswith(tests_root + "/")

    unit_d = child_ci(root, "unit")
    integ_d = child_ci(root, "integration")
    proxy_d = child_ci(integ_d, "proxy")
    by_tier = unit_d is not None or integ_d is not None
    shared = []
    if by_tier:
        for c in sorted(root.iterdir(), key=lambda p: p.name.lower()):
            if not excluded(c) and c not in (unit_d, integ_d):
                shared.append(rel(c))
    tiers = {}
    for t in TIERS:
        if not by_tier:
            sources = [infra["root"]]
        else:
            sources = list(shared)
            if t == "unit":
                if unit_d is not None:
                    sources.append(rel(unit_d))
            elif integ_d is not None:
                for c in sorted(integ_d.iterdir(), key=lambda p: p.name.lower()):
                    if c != proxy_d and not excluded(c):
                        sources.append(rel(c))
                if t == "proxy" and proxy_d is not None:
                    sources.append(rel(proxy_d))
        tiers[t] = {"sources": sources, "tests": infra["testCommand"].replace("{tier}", t)}
    return {"root": infra["root"], "readme": readme, "testsRoot": tests_root, "tiers": tiers}


# --------------------------------------------------------------------------- resolve


def full_target(norm, rel):
    return f"{norm['testRoot']}/{rel}" if norm["testRoot"] else rel


def tier_source(module_dir, tier):
    if tier == "proxy":
        return module_dir / "integration" / "proxy"
    return module_dir / tier


def resolve(module_dir, norm, repo):
    name = module_dir.name
    warnings = []
    entry = norm["packages"].get(name)
    mapped = entry is not None
    spec_clone = clone_facts(module_dir.parent.parent, norm["specRepo"], warnings)

    notes = None
    if mapped and entry["notes"]:
        p = (SKILL_DIR / entry["notes"]).resolve()
        if not p.is_file():
            raise ResolveError("NOTES_NOT_FOUND",
                               f"packages.{name}.notes declares {entry['notes']}, which does not exist in the skill")
        notes = str(p)

    reason = (entry["unready"] or entry["unsupported"]) if mapped else None
    offerable = reason is None

    projects = Projects(repo)
    tiers = {}
    for t in TIERS:
        src = tier_source(module_dir, t)
        target_rel = full_target(norm, entry["tiers"][t]) if (mapped and offerable and t in entry["tiers"]) else None
        project = projects.nearest(target_rel) if target_rel else None
        if target_rel and project is None:
            raise ResolveError("NO_PROJECT", f"no .projitems or .csproj above {target_rel}")
        ns = namespace_for(projects, project, target_rel, norm["namespaceRoot"]) if project else None
        specs = []
        if offerable:
            for p in list_specs(src, exclude_proxy=(t == "integration")):
                spec = {"file": str(p), "className": class_name(p)}
                if target_rel:
                    inc, registered, after = registration(projects, project, f"{target_rel}/{spec['className']}.cs")
                    spec.update({"compileInclude": inc, "registered": registered, "compileInsertAfter": after})
                else:
                    spec.update({"compileInclude": None, "registered": None, "compileInsertAfter": None})
                specs.append(spec)
        tiers[t] = {"present": src.is_dir() and offerable, "sourceDir": str(src), "targetDir": target_rel,
                    "namespace": ns, "specs": specs}

    test_root = norm["testRoot"] or posix(PurePosixPath(norm["infra"]["root"]).parent)
    return {
        "ok": True,
        "sourceModule": name,
        "mapped": mapped,
        "testRoot": test_root,
        "translationNotes": notes,
        "offerable": offerable,
        "reason": reason,
        "specClone": spec_clone,
        "infra": infra_output(norm, repo, warnings),
        "tiers": tiers,
        "warnings": warnings,
    }


# --------------------------------------------------------------------------- --create


def create_entry(raw, norm, module, target_name):
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", target_name):
        raise ResolveError("BAD_TARGET_NAME", f"--create target {target_name!r} must be a simple name (letters, "
                                              "digits, underscore, starting with a letter)")
    folder = pascal(target_name) + "Tests"
    if not valid_ident(folder):
        raise ResolveError("BAD_TARGET_NAME", f"'{target_name}' gives the folder '{folder}', not a C# identifier")
    old = norm["packages"].get(module)
    if old and (old["unready"] or old["unsupported"]):
        raise ResolveError("ENTRY_PROTECTED", f"packages.{module} is marked unready or unsupported "
                                              f"({old['unready'] or old['unsupported']}); --create never replaces "
                                              "it: edit the mapping by hand once the scope decision changes")
    parent = PurePosixPath(norm["infra"]["root"]).parent
    if norm["testRoot"]:
        tr = PurePosixPath(norm["testRoot"])
        if parent == tr or tr in parent.parents:
            parent = parent.relative_to(tr)
    base = posix(parent / folder)
    entry = {t: f"{base}/{DEFAULT_TIER_LAYOUT[t]}" for t in TIERS}
    raw_old = (raw.get("packages") or {}).get(module)
    if isinstance(raw_old, dict):
        for k, v in raw_old.items():
            if k == "notes" or k.startswith("_"):
                entry[k] = v
    new_raw = dict(raw)
    new_raw["packages"] = dict(raw["packages"])
    new_raw["packages"][module] = entry
    return new_raw


def write_mapping(data):
    """Atomic write, UTF-8, LF."""
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    fd, tmp = tempfile.mkstemp(prefix=".uts-mapping-", suffix=".json", dir=str(MAPPING.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        os.replace(tmp, MAPPING)
    except OSError as e:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise ResolveError("WRITE_FAILED", f"could not write {MAPPING}: {e}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("module_dir")
    ap.add_argument("--create", metavar="NAME",
                    help="add a mapping entry for this module, with test folder <Pascal(NAME)>Tests, then resolve")
    args = ap.parse_args()

    raw_arg = args.module_dir
    module_dir = Path(raw_arg).expanduser().resolve()
    if len(module_dir.parts) < 2 or module_dir.parts[-2] != "uts":
        fail("NOT_A_UTS_MODULE_PATH",
             f"{raw_arg!r} is not a module directory directly under uts/ (expected .../uts/<module>).")
    if not module_dir.is_dir():
        fail("DIR_NOT_FOUND", f"{raw_arg!r} does not exist or is not a directory.")
    if not (module_dir / "unit").is_dir() and not (module_dir / "integration").is_dir():
        fail("NO_TIER_DIRS", f"{raw_arg!r} has no unit/ or integration/ sub-directory; not a valid UTS module.")

    raw = load_mapping()
    norm = validate_mapping(raw)
    repo = find_repo_root(norm["infra"]["root"])

    if args.create is not None:
        new_raw = create_entry(raw, norm, module_dir.name, args.create)
        result = resolve(module_dir, validate_mapping(new_raw), repo)  # BAD_DERIVED_NAME / NO_PROJECT before any write
        write_mapping(new_raw)
    else:
        result = resolve(module_dir, norm, repo)
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except ResolveError as e:
        fail(e.code, e.message)
    except Exception as e:  # SystemExit is not an Exception: usage errors and fail() pass through
        fail("INTERNAL_ERROR", f"{type(e).__name__}: {e}")
