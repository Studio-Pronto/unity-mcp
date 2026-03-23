#!/usr/bin/env python3
"""Generate Unity .meta files for assets under MCPForUnity/."""
import argparse
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
UNITY_PKG = REPO_ROOT / "MCPForUnity"

# Templates verified against existing .meta files in the repo.
# Only {guid} varies per file.

TEMPLATES = {
    ".cs": """\
fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    ".asmdef": """\
fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    ".uxml": """\
fileFormatVersion: 2
guid: {guid}
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 2
  userData:
  assetBundleName:
  assetBundleVariant:
  script: {{fileID: 13804, guid: 0000000000000000e000000000000000, type: 0}}
""",
    ".uss": """\
fileFormatVersion: 2
guid: {guid}
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 2
  userData:
  assetBundleName:
  assetBundleVariant:
  script: {{fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}}
  disableValidation: 0
""",
    ".md": """\
fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "package.json": """\
fileFormatVersion: 2
guid: {guid}
PackageManifestImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "_folder": """\
fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "_default": """\
fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
}


def get_template(path: Path, is_dir: bool) -> str:
    if is_dir:
        return TEMPLATES["_folder"]
    if path.name == "package.json":
        return TEMPLATES["package.json"]
    return TEMPLATES.get(path.suffix, TEMPLATES["_default"])


def generate_meta(asset_path: Path, is_dir: bool) -> Path:
    """Write a .meta file for the given asset. Returns the .meta path."""
    meta_path = asset_path.parent / (asset_path.name + ".meta") if not is_dir else asset_path.with_name(asset_path.name + ".meta")
    content = get_template(asset_path, is_dir).format(guid=uuid.uuid4().hex)
    meta_path.write_text(content)
    return meta_path


def ensure_parent_metas(path: Path) -> list[Path]:
    """Create folder .meta files for any new directories between path and UNITY_PKG root."""
    created = []
    current = path.parent
    while current != UNITY_PKG and current.is_relative_to(UNITY_PKG):
        meta_path = current.with_name(current.name + ".meta")
        if not meta_path.exists():
            generate_meta(current, is_dir=True)
            created.append(meta_path)
        current = current.parent
    return created


def is_under_unity_pkg(path: Path) -> bool:
    try:
        path.relative_to(UNITY_PKG)
        return True
    except ValueError:
        return False


def mode_hook():
    """Claude Code PostToolUse hook: read JSON from stdin, generate .meta if needed."""
    try:
        data = json.load(sys.stdin)
    except (json.JSONDecodeError, EOFError):
        return

    file_path_str = data.get("tool_input", {}).get("file_path", "")
    if not file_path_str:
        return

    file_path = Path(file_path_str)

    # Recursion guard + scope check
    if file_path.name.endswith(".meta"):
        return
    if not is_under_unity_pkg(file_path):
        return

    meta_path = file_path.with_name(file_path.name + ".meta")
    if meta_path.exists():
        return

    created = ensure_parent_metas(file_path)
    generate_meta(file_path, is_dir=False)
    created.append(meta_path)

    for p in created:
        print(f"ensure_meta: generated {p.relative_to(REPO_ROOT)}")


def mode_staged():
    """Pre-commit hook: generate .meta for staged MCPForUnity/ files missing them."""
    # Get all staged files (additions + modifications) under MCPForUnity/
    result = subprocess.run(
        ["git", "diff", "--cached", "--name-only", "--diff-filter=AM", "--", "MCPForUnity/"],
        capture_output=True, text=True, cwd=REPO_ROOT,
    )
    staged_files = [f for f in result.stdout.strip().splitlines() if f]

    created = []
    for rel in staged_files:
        if rel.endswith(".meta"):
            continue
        asset_path = REPO_ROOT / rel
        meta_path = asset_path.with_name(asset_path.name + ".meta")
        if not meta_path.exists():
            # Generate parent folder .metas first
            parent_metas = ensure_parent_metas(asset_path)
            for pm in parent_metas:
                created.append(pm)
            generate_meta(asset_path, is_dir=False)
            created.append(meta_path)

    # Auto-stage generated .metas
    if created:
        rel_paths = [str(p.relative_to(REPO_ROOT)) for p in created]
        subprocess.run(["git", "add", "--"] + rel_paths, cwd=REPO_ROOT)
        for p in rel_paths:
            print(f"ensure_meta: generated and staged {p}")

    # Check for orphaned .metas (staged deletions without .meta deletion)
    result = subprocess.run(
        ["git", "diff", "--cached", "--name-only", "--diff-filter=D", "--", "MCPForUnity/"],
        capture_output=True, text=True, cwd=REPO_ROOT,
    )
    deleted_files = set(f for f in result.stdout.strip().splitlines() if f)

    for rel in deleted_files:
        if rel.endswith(".meta"):
            continue
        meta_rel = rel + ".meta"
        meta_path = REPO_ROOT / meta_rel
        if meta_path.exists() and meta_rel not in deleted_files:
            print(f"ensure_meta: WARNING — {meta_rel} may be orphaned ({rel} is being deleted)")


def mode_all(dry_run: bool):
    """Full scan of MCPForUnity/ for missing or orphaned .meta files."""
    missing = []
    orphaned = []

    for root, dirs, files in os.walk(UNITY_PKG):
        root_path = Path(root)

        # Check subdirectories for missing folder .metas
        for d in dirs:
            dir_path = root_path / d
            meta_path = dir_path.with_name(d + ".meta")
            if not meta_path.exists():
                missing.append((dir_path, True))

        # Check files
        for f in files:
            file_path = root_path / f
            if f.endswith(".meta"):
                # Check if this .meta is orphaned
                asset_name = f[:-5]  # strip .meta
                asset_path = root_path / asset_name
                if not asset_path.exists() and not asset_path.is_dir():
                    # Also check if it's a folder .meta
                    if not (root_path / asset_name).is_dir():
                        orphaned.append(file_path)
            else:
                meta_path = file_path.with_name(f + ".meta")
                if not meta_path.exists():
                    missing.append((file_path, False))

    if missing or orphaned:
        for path, is_dir in missing:
            rel = path.relative_to(REPO_ROOT)
            kind = "folder" if is_dir else "file"
            print(f"MISSING: {rel}.meta ({kind})")
        for path in orphaned:
            rel = path.relative_to(REPO_ROOT)
            print(f"ORPHANED: {rel}")

        if dry_run:
            print(f"\n{len(missing)} missing, {len(orphaned)} orphaned")
            sys.exit(1)
        else:
            for path, is_dir in missing:
                meta = generate_meta(path, is_dir)
                print(f"  -> generated {meta.relative_to(REPO_ROOT)}")
            if orphaned:
                print(f"\n{len(orphaned)} orphaned .meta file(s) — delete manually")
    else:
        print("All .meta files are properly paired.")


def main():
    parser = argparse.ArgumentParser(description="Ensure Unity .meta files exist for MCPForUnity/ assets.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--hook", action="store_true", help="Claude Code PostToolUse mode (reads JSON from stdin)")
    group.add_argument("--staged", action="store_true", help="Pre-commit mode (check staged files)")
    group.add_argument("--all", action="store_true", help="Full scan of MCPForUnity/")
    parser.add_argument("--dry-run", action="store_true", help="With --all: report only, exit 1 if issues found")
    args = parser.parse_args()

    if args.hook:
        mode_hook()
    elif args.staged:
        mode_staged()
    elif args.all:
        mode_all(args.dry_run)


if __name__ == "__main__":
    main()
