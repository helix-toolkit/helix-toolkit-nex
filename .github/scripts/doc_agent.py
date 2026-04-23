#!/usr/bin/env python3
from __future__ import annotations

"""
Documentation Agent for HelixToolkit-Nex.

Automatically checks latest commits, updates README.md for changed C# packages,
and writes up full documentation for packages with empty READMEs.

Usage:
  - Triggered via GitHub Actions on push to main/develop
  - Manually via: python .github/scripts/doc_agent.py [--force-all] [--regenerate-all-readmes] [--since-sha <sha>]

Environment variables:
  GITHUB_TOKEN   - Required. Used to authenticate with GitHub Models API.
  OPENAI_API_KEY - Optional. If set, uses OpenAI API instead of GitHub Models.
  FORCE_ALL      - If "true", processes all package docs regardless of change detection.
  FORCE_REGENERATE_ALL_READMES - If "true", regenerates all package README.md files from source.
  BEFORE_SHA     - The commit SHA before the push (set by GitHub Actions push event).
  SINCE_SHA      - Optional override for the git base commit used in change detection.
  DOC_AGENT_MODEL - AI model name (default: gpt-4o).
"""

import os
import sys
import subprocess
import argparse
import json
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from openai import OpenAI

# ---- Configuration ----

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
PROJECTS_DIR = REPO_ROOT / "Source" / "HelixToolkit-Nex"
SAMPLES_DIR = PROJECTS_DIR / "Samples"

# Maximum characters sent per source file to the AI
MAX_FILE_CHARS = 8_000
# Maximum total characters of source context per AI request
MAX_FULL_DOC_CHARS = 60_000
MAX_UPDATE_DOC_CHARS = 30_000

MODEL = os.environ.get("DOC_AGENT_MODEL", "gpt-4o")
GITHUB_TOKEN = os.environ.get("GITHUB_TOKEN", "")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY", "")
FORCE_ALL = os.environ.get("FORCE_ALL", "false").lower() == "true"
FORCE_REGENERATE_ALL_READMES = os.environ.get("FORCE_REGENERATE_ALL_READMES", "false").lower() == "true"
DEFAULT_SINCE_SHA = "HEAD~1"

# Git uses 40 zero-chars as the "null" SHA to indicate a non-existent ref
# (e.g. github.event.before on the very first push to a branch).
GIT_NULL_SHA = "0" * 40

# ---- AI Client ----

SYSTEM_PROMPT = """\
You are a technical documentation writer for HelixToolkit-Nex, a 3D Graphics engine \
implemented in C# targeting the Vulkan API.

Engine background (important for accuracy):
- All C# matrices are row-major; all GLSL matrices are column-major.
- Uses Reverse-Z for projection matrices.
- Uses Forward Plus light culling.
- Uses GPU-based instance culling.
- Uses Entity Component System (ECS) based on the Arch ECS library.
- Uses a Render Graph to manage render node execution order.
- Renders Entity Information (Entity Id, Entity Version, Instancing Index) onto an \
entity texture (RG_F32) during the depth pre-pass for screen-space mesh picking.

When writing documentation:
- Be accurate and technical.
- Focus on the public API surface (public classes, interfaces, enums, methods, properties).
- Include practical C# usage examples where helpful.
- Explain design patterns and architectural decisions.
- Use proper Markdown: headers, fenced code blocks, bullet lists.
- Do not expose internal implementation details that may change.
- Do not fabricate API members that do not exist in the source code provided.
"""


def get_openai_client() -> "OpenAI":
    try:
        from openai import OpenAI
    except ImportError as exc:
        print(
            "ERROR: openai package is required. Install with: pip install openai>=1.0.0",
            file=sys.stderr,
        )
        raise SystemExit(1) from exc

    if OPENAI_API_KEY:
        return OpenAI(api_key=OPENAI_API_KEY)
    if GITHUB_TOKEN:
        return OpenAI(
            base_url="https://models.inference.ai.azure.com",
            api_key=GITHUB_TOKEN,
        )
    print("ERROR: Neither GITHUB_TOKEN nor OPENAI_API_KEY is set.", file=sys.stderr)
    sys.exit(1)


# ---- Project Discovery ----


def is_test_project(project_dir: Path) -> bool:
    return "Tests" in project_dir.name or project_dir.name.endswith(".Test")


def is_sample_project(project_dir: Path) -> bool:
    try:
        project_dir.relative_to(SAMPLES_DIR)
        return True
    except ValueError:
        return False


def has_csproj(project_dir: Path) -> bool:
    return any(f.suffix == ".csproj" for f in project_dir.iterdir() if f.is_file())


def get_all_packages() -> list[Path]:
    packages: list[Path] = []
    for d in PROJECTS_DIR.iterdir():
        if not d.is_dir():
            continue
        if d.name == "Samples":
            continue
        if is_test_project(d) or is_sample_project(d):
            continue
        if not has_csproj(d):
            continue
        packages.append(d)
    return sorted(packages)


def get_project_name(project_dir: Path) -> str:
    csproj_files = list(project_dir.glob("*.csproj"))
    if csproj_files:
        return csproj_files[0].stem
    return project_dir.name


# ---- README State ----


def is_readme_empty(readme_path: Path) -> bool:
    """A README is considered empty when it has no meaningful content beyond a bare title."""
    if not readme_path.exists():
        return True
    # utf-8-sig transparently strips the BOM if present
    raw = readme_path.read_text(encoding="utf-8-sig")
    non_empty = [ln.strip() for ln in raw.splitlines() if ln.strip()]
    if not non_empty:
        return True
    # Only a single heading line (e.g. "# PackageName") counts as empty
    if len(non_empty) == 1 and non_empty[0].startswith("#"):
        return True
    return False


# ---- Source Code Collection ----


def get_cs_files(project_dir: Path) -> list[Path]:
    files: list[Path] = []
    for f in project_dir.rglob("*.cs"):
        # Skip build artifacts and auto-generated files
        if "obj" in f.parts or "bin" in f.parts:
            continue
        if f.name.endswith(".g.cs") or f.name.endswith(".Designer.cs"):
            continue
        files.append(f)
    return sorted(files)


def get_shader_files(project_dir: Path) -> list[Path]:
    files: list[Path] = []
    for pattern in ("*.glsl", "*.vert", "*.frag", "*.comp", "*.glh"):
        for f in project_dir.rglob(pattern):
            if "obj" not in f.parts and "bin" not in f.parts:
                files.append(f)
    return sorted(files)


def read_file_limited(filepath: Path, max_chars: int = MAX_FILE_CHARS) -> str:
    try:
        content = filepath.read_text(encoding="utf-8-sig", errors="replace")
        if len(content) > max_chars:
            return content[:max_chars] + f"\n... [truncated — {len(content) - max_chars} additional chars omitted]"
        return content
    except OSError as exc:
        return f"[Could not read file: {exc}]"


def build_source_context(project_dir: Path, max_total: int = MAX_FULL_DOC_CHARS) -> str:
    """Collect source files into a single context string, respecting the total char limit."""
    all_files = get_cs_files(project_dir) + get_shader_files(project_dir)
    parts: list[str] = []
    total = 0
    for f in all_files:
        if total >= max_total:
            parts.append("\n[Additional source files omitted — context limit reached]")
            break
        content = read_file_limited(f, MAX_FILE_CHARS)
        rel = f.relative_to(project_dir)
        lang = "glsl" if f.suffix in {".glsl", ".vert", ".frag", ".comp", ".glh"} else "csharp"
        block = f"### {rel}\n```{lang}\n{content}\n```\n"
        total += len(block)
        parts.append(block)
    return "\n".join(parts)


# ---- Git Helpers ----


def run_git(*args: str) -> str:
    result = subprocess.run(
        ["git", "--no-pager", *args],
        capture_output=True,
        text=True,
        cwd=REPO_ROOT,
    )
    return result.stdout


def run_git_optional(*args: str) -> str | None:
    result = subprocess.run(
        ["git", "--no-pager", *args],
        capture_output=True,
        text=True,
        cwd=REPO_ROOT,
    )
    if result.returncode != 0:
        stderr = (result.stderr or "").strip()
        if stderr:
            print(f"WARN: git {' '.join(args)} failed: {stderr}", file=sys.stderr)
        return None
    return result.stdout.strip()


def get_pr_base_sha_from_event_payload() -> str | None:
    event_path = os.environ.get("GITHUB_EVENT_PATH", "").strip()
    if not event_path:
        return None
    try:
        payload = json.loads(Path(event_path).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    base_sha = payload.get("pull_request", {}).get("base", {}).get("sha")
    if not isinstance(base_sha, str):
        return None
    base_sha = base_sha.strip()
    return base_sha or None


def resolve_since_sha(explicit_since_sha: str = "") -> tuple[str, str]:
    explicit_since_sha = explicit_since_sha.strip()
    if explicit_since_sha:
        return explicit_since_sha, "explicit --since-sha / SINCE_SHA override"

    before_sha = os.environ.get("BEFORE_SHA", "").strip()
    if before_sha and before_sha != GIT_NULL_SHA:
        return before_sha, "BEFORE_SHA from push context"

    base_ref = os.environ.get("GITHUB_BASE_REF", "").strip()
    if base_ref:
        merge_base = run_git_optional("merge-base", "HEAD", f"origin/{base_ref}")
        if merge_base:
            return merge_base, f"merge-base of HEAD and origin/{base_ref} (PR context)"

    event_name = os.environ.get("GITHUB_EVENT_NAME", "").strip()
    if event_name.startswith("pull_request"):
        pr_base_sha = get_pr_base_sha_from_event_payload()
        if pr_base_sha:
            return pr_base_sha, "pull_request.base.sha from GitHub event payload"

    return DEFAULT_SINCE_SHA, f"fallback default ({DEFAULT_SINCE_SHA})"


def get_changed_packages(since_sha: str) -> list[Path]:
    """Return packages that have source file changes since since_sha."""
    changed_files_raw = run_git("diff", "--name-only", since_sha, "HEAD")
    changed_files = [REPO_ROOT / ln for ln in changed_files_raw.strip().splitlines() if ln]

    all_pkgs = get_all_packages()
    changed: set[Path] = set()
    for filepath in changed_files:
        for pkg in all_pkgs:
            try:
                filepath.relative_to(pkg)
                changed.add(pkg)
                break
            except ValueError:
                continue
    return sorted(changed)


def get_diff_for_package(project_dir: Path, since_sha: str, max_chars: int = 15_000) -> str:
    diff = run_git("diff", since_sha, "HEAD", "--", str(project_dir))
    if len(diff) > max_chars:
        diff = diff[:max_chars] + "\n... [diff truncated]"
    return diff


# ---- AI Documentation Generation ----


def call_ai(client: OpenAI, prompt: str) -> str:
    response = client.chat.completions.create(
        model=MODEL,
        messages=[
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": prompt},
        ],
        max_tokens=4096,
        temperature=0.2,
    )
    return response.choices[0].message.content.strip()


_FULL_README_PROMPT = """\
Generate a comprehensive `README.md` for the C# package `{project_name}`.

The README must include:
1. **Package title** (`# {project_name}`) and a one-paragraph description of its purpose.
2. **Overview** — key concepts, responsibilities, and how it fits into the HelixToolkit.Nex engine.
3. **Key types** — a table or list of important public classes, interfaces, and enums with brief descriptions.
4. **Usage examples** — practical C# code snippets demonstrating the most common scenarios.
5. **Architecture notes** — relevant design patterns, dependencies on other HelixToolkit.Nex packages.

Source code (use this as the authoritative source — do not invent API members):

{source_context}

Return ONLY the Markdown content of the README.md, with no preamble or trailing commentary.\
"""

_UPDATE_README_PROMPT = """\
Update the existing `README.md` for the C# package `{project_name}` to reflect the recent \
code changes shown in the diff below.

**Current README.md:**
```markdown
{current_readme}
```

**Recent code changes (git diff):**
```diff
{diff_context}
```

**Relevant source files for additional context:**
{source_context}

Instructions:
- Update sections that are affected by the code changes (new types, changed API, removed features).
- Add documentation for new public classes, methods, or properties.
- Remove or correct documentation for deleted or renamed items.
- Keep all existing documentation that remains accurate.
- Improve any sections that are clearly incomplete or missing important detail.

Return ONLY the updated Markdown content of the README.md, with no preamble or trailing commentary.\
"""


def generate_full_readme(client: OpenAI, project_name: str, source_context: str) -> str:
    prompt = _FULL_README_PROMPT.format(
        project_name=project_name,
        source_context=source_context,
    )
    return call_ai(client, prompt)


def update_readme(
    client: "OpenAI",
    project_name: str,
    current_readme: str,
    diff_context: str,
    source_context: str,
) -> str:
    prompt = _UPDATE_README_PROMPT.format(
        project_name=project_name,
        current_readme=current_readme,
        diff_context=diff_context,
        source_context=source_context,
    )
    return call_ai(client, prompt)


# ---- Per-Package Processing ----


def process_package(
    client: "OpenAI",
    project_dir: Path,
    since_sha: str,
    force_regenerate_readme: bool = False,
) -> bool:
    """Process a package and update its README.md. Returns True if the file was changed."""
    project_name = get_project_name(project_dir)
    readme_path = project_dir / "README.md"

    print(f"\n── {project_name}")

    if force_regenerate_readme or is_readme_empty(readme_path):
        if force_regenerate_readme:
            print("   Force-regenerate mode — generating full documentation...")
        else:
            print("   README is empty — generating full documentation...")
        source_context = build_source_context(project_dir, max_total=MAX_FULL_DOC_CHARS)
        if not source_context.strip():
            print("   No source files found — skipping.")
            return False
        new_content = generate_full_readme(client, project_name, source_context)
    else:
        diff_context = get_diff_for_package(project_dir, since_sha)
        if not diff_context.strip():
            print("   No source changes detected — skipping.")
            return False
        print("   Updating README based on recent changes...")
        current_readme = readme_path.read_text(encoding="utf-8-sig")
        source_context = build_source_context(project_dir, max_total=MAX_UPDATE_DOC_CHARS)
        new_content = update_readme(client, project_name, current_readme, diff_context, source_context)

    readme_path.write_text(new_content + "\n", encoding="utf-8")
    print(f"   ✓ README.md updated")
    return True


# ---- Main Entry Point ----


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Update package README.md files using AI. "
            "By default, only changed packages and packages with empty README files are processed."
        )
    )
    parser.add_argument(
        "--force-all",
        action="store_true",
        default=FORCE_ALL,
        help=(
            "Process all packages (same as FORCE_ALL=true). "
            "README update behavior remains unchanged unless --regenerate-all-readmes is also set."
        ),
    )
    parser.add_argument(
        "--regenerate-all-readmes",
        action="store_true",
        default=FORCE_REGENERATE_ALL_READMES,
        help=(
            "Regenerate every package README.md from source regardless of existing README content "
            "(same as FORCE_REGENERATE_ALL_READMES=true). Implies processing all packages."
        ),
    )
    parser.add_argument(
        "--since-sha",
        default=os.environ.get("SINCE_SHA", ""),
        help=(
            "Override the git base commit used for change detection "
            "(same as SINCE_SHA environment variable)."
        ),
    )
    args = parser.parse_args()

    force_all = args.force_all or args.regenerate_all_readmes
    force_regenerate_all_readmes = args.regenerate_all_readmes
    since_sha, since_sha_reason = resolve_since_sha(args.since_sha)

    client = get_openai_client()
    all_packages = get_all_packages()

    if force_all:
        print("Force mode: processing all packages.")
        packages_to_process = all_packages
    else:
        changed_packages = get_changed_packages(since_sha)
        empty_packages = [p for p in all_packages if is_readme_empty(p / "README.md")]

        # Union of changed packages and packages with empty READMEs
        seen: set[Path] = set()
        packages_to_process = []
        for p in changed_packages + empty_packages:
            if p not in seen:
                seen.add(p)
                packages_to_process.append(p)
        packages_to_process.sort()

    print(f"Packages to process ({len(packages_to_process)}): "
          f"{[p.name for p in packages_to_process]}")
    print(f"Since SHA: {since_sha} ({since_sha_reason})")
    if force_regenerate_all_readmes:
        print("README mode: force-regenerate all README.md files.")

    changed: list[str] = []
    for pkg in packages_to_process:
        try:
            if process_package(client, pkg, since_sha, force_regenerate_readme=force_regenerate_all_readmes):
                changed.append(get_project_name(pkg))
        except Exception as exc:
            print(f"   ERROR processing {pkg.name}: {exc}", file=sys.stderr)

    changed_summary = "\n".join(f"- `{p}`" for p in changed) if changed else "_No packages changed._"
    print(f"\nSummary:\n{changed_summary}")

    # Export variables for the GitHub Actions workflow
    github_env = os.environ.get("GITHUB_ENV", "")
    if github_env:
        with open(github_env, "a", encoding="utf-8") as fh:
            fh.write(f"CHANGED_PACKAGES<<EOF\n{changed_summary}\nEOF\n")
            fh.write(f"HAS_CHANGES={'true' if changed else 'false'}\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())
