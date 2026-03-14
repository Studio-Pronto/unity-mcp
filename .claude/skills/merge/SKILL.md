---
name: merge
description: Merge upstream changes into the fork. Checks for clean working tree, merges upstream/main, resolves conflicts, and applies fork fixups.
---

# Merge Upstream into Fork

You are merging upstream (CoplayDev/unity-mcp) changes into the Studio-Pronto fork.

## allowed-tools

Bash, Read, Edit, Glob, Grep

## Instructions

### 1. Pre-flight checks

Run these checks and **stop with a clear message** if any fail:

```bash
git status --porcelain          # must be empty (clean working tree)
git branch --show-current       # must be "main"
```

- If working tree is dirty: "You have uncommitted changes. Commit or stash them first, then re-run `/merge`."
- If not on main: "You're on branch `X`. Switch to `main` first."

### 2. Check if there's anything to merge

```bash
git fetch upstream
git log --oneline HEAD..upstream/main
```

If empty, report "Already up to date with upstream/main" and stop.

### 3. Review what's incoming and get approval

Before merging, present a full summary to the user:

**Upstream changes:**
- Number of new commits
- `git log --oneline --no-merges HEAD..upstream/main` (list of changes)
- `git diff --stat HEAD..upstream/main` (files changed)

**New configurators (fork policy check):**
Check if upstream added any new configurator files that the fork doesn't have:
```bash
git diff --name-status HEAD..upstream/main -- MCPForUnity/Editor/Clients/Configurators/
```
List any new (A) or modified (M) configurators that aren't `ClaudeCodeConfigurator`. These will be removed by the fixup script — flag them to the user in case any are worth keeping.

**Redundant fork features:**
Check if upstream added functionality that overlaps with fork-specific features. Compare:
```bash
# New files/tools upstream is adding
git diff --name-only --diff-filter=A HEAD..upstream/main
# Our fork-only changes since last upstream merge
LAST_MERGE=$(git log --format=%H --merges --grep="upstream" -1)
git diff --name-only $LAST_MERGE..HEAD
```
Look for overlapping domains — e.g., upstream adds a new tool/command/handler that does something similar to what the fork already implemented. For each overlap found:
- Briefly describe both implementations (upstream vs fork)
- Compare scope and quality (which is more complete, better tested, more robust?)
- **Recommend keeping upstream's version by default** to reduce fork divergence — unless the fork's solution is considerably more feature-rich or better designed
- Flag these to the user for a decision before proceeding

**Potential conflicts:**
Find the last upstream merge commit and identify files changed in both the fork and upstream:
```bash
LAST_MERGE=$(git log --format=%H --merges --grep="upstream" -1)
# Files we changed since last merge
FORK_FILES=$(git diff --name-only $LAST_MERGE..HEAD)
# Files upstream changed
UPSTREAM_FILES=$(git diff --name-only HEAD..upstream/main)
```
List any overlapping files — these are likely to conflict.

**Lower-priority checks (report if found, don't block on these):**

- *Breaking API changes:* Did upstream rename/remove methods, parameters, or tool actions that fork-specific code calls? These won't show as merge conflicts (different files) but will silently break the fork. Scan fork-only files for references to anything upstream deleted or renamed.
- *Test assumptions:* Did upstream add or modify tests that reference configurators or features the fork removes? Those tests will fail after fixups. Check for new test files touching `Configurators/` or fork-removed functionality.
- *Dependency changes:* Did upstream add, upgrade, or remove packages in `pyproject.toml` or `package.json`? Note any new or changed dependencies briefly.

**Stop and wait for the user to approve before proceeding.** The user may want to discuss specific changes, defer the merge, or handle certain conflicts a particular way.

### 4. Run the merge

```bash
git merge upstream/main -m "Merge upstream CoplayDev/unity-mcp <version> into fork"
```

Try to determine the upstream version from their `MCPForUnity/package.json` for the commit message:
```bash
git show upstream/main:MCPForUnity/package.json | python3 -c "import sys,json; print(json.load(sys.stdin)['version'])"
```

### 5. Handle merge conflicts

If the merge produces conflicts:

1. List all conflicted files: `git diff --name-only --diff-filter=U`
2. For each conflicted file, read it and resolve the conflict:
   - **Configurator files** (in `MCPForUnity/Editor/Clients/Configurators/`): if it's not `ClaudeCodeConfigurator`, resolve by deleting the file (`git rm`)
   - **Version files** (`package.json`, `pyproject.toml`, `manifest.json`, `uv.lock`): accept upstream's version — the fixup step will add the fork suffix
   - **Code conflicts**: resolve intelligently by understanding both sides. Prefer keeping fork-specific enhancements while incorporating upstream changes
3. After resolving all conflicts: `git add -A && git commit --no-edit`

### 6. Apply fork fixups

Run the post-merge fixup script:

```bash
./tools/update_fork.sh --fixup-only
```

This will:
- Remove non-ClaudeCode configurator files
- Set the fork version suffix (e.g., `9.6.0-fork.1`)
- Regenerate `uv.lock`
- Commit the fixups

### 7. Verify

After the merge and fixups:

```bash
git log --oneline -5                    # show recent history
ls MCPForUnity/Editor/Clients/Configurators/  # should only have ClaudeCodeConfigurator*
```

### 8. Report results

Summarize:
- How many upstream commits were merged
- Whether there were conflicts and how they were resolved
- The new fork version
- Remind: "Run tests before pushing. Python: `cd Server && uv run pytest tests/ -v`. Unity tests require opening TestProjects/UnityMCPTests in Unity Editor."
