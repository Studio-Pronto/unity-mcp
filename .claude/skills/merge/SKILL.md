---
name: merge
description: Merge upstream changes into the fork. Checks for clean working tree, deeply analyzes incoming commits and conflicts, resolves them one-by-one with rerere awareness, and applies fork fixups.
---

# Merge Upstream into Fork

You are merging upstream (CoplayDev/unity-mcp) changes into the Studio-Pronto fork.

The bar for this skill: **understand every incoming change deeply enough to explain its behavioral effect on fork-specific code, and review every single conflict resolution explicitly — never bulk-apply rules without reading both sides first.**

## allowed-tools

Bash, Read, Edit, Glob, Grep

## Instructions

### 1. Pre-flight checks

Run these checks and **stop with a clear message** if any fail:

```bash
git status --porcelain          # must be empty (clean working tree)
git branch --show-current       # must be "main"
git config --get rerere.enabled # should be "true" — see step 2 if not
```

- If working tree is dirty: "You have uncommitted changes. Commit or stash them first, then re-run `/merge`."
- If not on main: "You're on branch `X`. Switch to `main` first."

### 2. Confirm rerere is on (and surface what it remembers)

This fork relies on **git rerere** ("reuse recorded resolution") to replay conflict fixes from prior upstream merges. The cache lives in `.git/rr-cache/` and resolutions are applied automatically the next time the same conflict hunk appears.

```bash
git config --get rerere.enabled            # expect "true"
git config --get rerere.autoupdate         # expect "true" (auto-stages replayed resolutions)
ls .git/rr-cache 2>/dev/null | wc -l       # how many resolutions are remembered
```

If rerere is not enabled, **stop and tell the user**:
> "rerere is disabled. Enable it before merging so conflict resolutions from prior merges replay automatically:
> ```
> git config rerere.enabled true
> git config rerere.autoupdate true
> ```
> Then re-run `/merge`."

**Important:** rerere replays resolutions blindly based on conflict hunk hash. A cached resolution can become *stale* if either side's surrounding code has evolved — the replayed fix may compile but silently drop new behavior. In step 5 you must re-read every file rerere touched, not trust the cache.

### 3. Check if there's anything to merge

```bash
git fetch upstream
git log --oneline HEAD..upstream/main
```

If empty, report "Already up to date with upstream/main" and stop.

### 4. Deep pre-merge analysis

Don't summarize from commit messages alone — commit messages lie or under-describe. **Read the actual diffs** for every non-trivial incoming commit and trace the impact through the fork.

#### 4a. Catalogue every incoming commit

```bash
git log --oneline --no-merges HEAD..upstream/main
git diff --stat HEAD..upstream/main
```

For each non-trivial commit (skip pure version bumps, lockfile churn, CI tweaks unless they affect fork CI), do this analysis:

```bash
git show <sha> --stat                     # what files
git show <sha>                            # the full diff
```

Write a one-line behavioral summary of what changed and **why it matters for this fork specifically**. Group findings by domain (tools, transport, clients, tests, etc.) when presenting.

#### 4b. Identify conflict surface (file-level)

Find the last upstream merge and intersect changed-file sets:

```bash
LAST_MERGE=$(git log --format=%H --merges --grep="upstream" -1)
FORK_FILES=$(git diff --name-only $LAST_MERGE..HEAD | sort)
UPSTREAM_FILES=$(git diff --name-only HEAD..upstream/main | sort)
comm -12 <(echo "$FORK_FILES") <(echo "$UPSTREAM_FILES")
```

For every overlapping file, **read both sides**:

```bash
git diff $LAST_MERGE..HEAD -- <path>            # what the fork did
git diff HEAD..upstream/main -- <path>          # what upstream did
```

Describe how the two sets of changes interact. Pay special attention to:
- Same function modified on both sides
- Fork added a parameter or branch that upstream is now reworking
- Upstream removed/renamed something the fork still references

#### 4c. Identify silent-breakage surface (file-level — no conflict markers expected)

These won't show up as merge conflicts but can break the fork after the merge:

- **Renamed/removed APIs:** scan upstream's diff for deleted/renamed methods, parameters, tool actions, MCP tool names, attribute names. Then grep fork-only files for any references that no longer exist.
- **Removed configurators:** if upstream removed a configurator the fork's policy script also strips, no-op. If upstream removed one the fork explicitly kept (e.g. `OpenClawConfigurator`), flag it loudly.
- **Test fixture changes:** upstream tests that touch fork-removed configurators or fork-modified tool surfaces will fail post-fixup. Read new/modified files under `*/Tests/` and `Server/tests/`.
- **Dependency drift:** diff `pyproject.toml`, `MCPForUnity/package.json`, `manifest.json` for added/removed/upgraded packages.
- **Schema/contract drift:** look for changes to `models.py`, `command_registry`, `HandleCommand` signatures, attribute fields like `Group`, `AutoRegister`, etc.

#### 4d. Identify redundancy / convergence

Where upstream now ships functionality the fork already implemented:

```bash
git diff --name-only --diff-filter=A HEAD..upstream/main   # net-new upstream files
```

For each net-new file or tool, check whether the fork already solves the same problem. If yes:
- Briefly describe both implementations (upstream vs fork) by reading both
- Compare scope, test coverage, robustness, and integration surface
- **Default recommendation: drop the fork's version and adopt upstream's** to minimize divergence — overturn this only if the fork's solution is materially more complete or solves a meaningfully different shape of the problem
- Flag each overlap with a recommendation for the user

#### 4e. New configurators (fork policy)

```bash
git diff --name-status HEAD..upstream/main -- MCPForUnity/Editor/Clients/Configurators/
```

List any added (A) or modified (M) configurators that aren't `ClaudeCodeConfigurator*`. The fixup script will strip these — flag them in case the user wants to keep one.

#### 4f. Present and wait for approval

Lay out the findings as:

```
## Incoming commits (N)
<one-line behavioral summary per non-trivial commit, grouped by domain>

## Conflict surface (overlap files)
<file>: <how fork edits and upstream edits interact, and the likely resolution shape>

## Silent-breakage risks
<bullets per finding — renames, removed APIs, test fixtures, deps>

## Redundancy with fork features
<bullets with recommendation: adopt upstream / keep fork / merge concepts>

## Configurators to be stripped
<list>

## Plan
<short plan describing the order of resolution and any decisions the user needs to make>
```

**Stop and wait for the user to approve before proceeding.** The user may want to discuss specific changes, defer the merge, or make decisions on the redundancy questions.

### 5. Run the merge (no auto-commit)

```bash
git merge upstream/main --no-commit --no-ff
```

Capture the upstream version for the commit message:
```bash
UPSTREAM_VERSION=$(git show upstream/main:MCPForUnity/package.json | python3 -c "import sys,json; print(json.load(sys.stdin)['version'])")
```

### 6. Identify what rerere already resolved

Before touching anything else, see what rerere replayed automatically:

```bash
git rerere status      # files with conflicts (still open OR resolved by rerere)
git rerere diff        # the resolutions rerere applied (or is applying)
git diff --name-only --diff-filter=U   # files STILL conflicted (rerere didn't have a match)
```

A file appearing in `git rerere status` but **not** in `--diff-filter=U` means rerere fully resolved it and (with `rerere.autoupdate=true`) staged it.

**Treat every rerere auto-resolution as suspect until reviewed.** Read each rerere-resolved file:

```bash
git diff HEAD -- <path>      # what's now staged
git diff MERGE_HEAD -- <path>  # what upstream wanted
```

For each one, confirm the replayed resolution still makes sense given the current state of both sides. If a cached resolution is stale (e.g. the fork has evolved since the original conflict was resolved), unstage it and resolve fresh:

```bash
git checkout --conflict=merge -- <path>   # restore conflict markers
# then resolve by hand
```

### 7. Resolve remaining conflicts — one file at a time

For each file still in `git diff --name-only --diff-filter=U`:

1. **Read the file in full** (including unconflicted regions, for context).
2. **Read both sides of every conflict hunk:** what was on the fork side, what was on the upstream side, what was the merge-base. Use `git diff --cc <path>` or `git log -p --merge -- <path>` if the intent is unclear.
3. **Decide deliberately, document the decision in your output.** Default policies, but never apply blindly without reading:
   - **Configurator files** (`MCPForUnity/Editor/Clients/Configurators/`): if not `ClaudeCodeConfigurator*`, `git rm` (fork policy). Confirm the file truly is the upstream-only configurator pattern before deleting.
   - **Version/lock files** (`MCPForUnity/package.json`, `Server/pyproject.toml`, `manifest.json`, `Server/uv.lock`): accept upstream — the fixup step will reapply the fork version suffix.
   - **Code conflicts:** synthesize a resolution that keeps both fork intent and upstream intent. If they're genuinely incompatible, choose with reasoning and flag it for the human review in step 8.
4. **Re-read the full file after resolving** to verify the hunks compose correctly (e.g. no orphaned helper, no stale import, no half-stitched function).
5. `git add <path>` for that single file before moving to the next.

Do not bulk `git add -A` until every conflicted file has been individually reviewed.

### 8. Behavioral re-verification before committing

After all conflicts are resolved and staged, re-run the silent-breakage scans from step 4c against the merged tree — the resolution may have re-introduced or papered over an issue:

```bash
git diff --cached --stat
```

For each fork-specific feature touched by the merge (animation tools, read-only preflight gating, OpenClaw configurator, fork-only tests), open the file and confirm the fork's behavior still works as written. Specifically:

- Read fork-modified source files end-to-end where upstream also edited them. Re-trace the call path and confirm parameters, return shapes, and side effects still line up.
- Re-run any quick syntactic check available (`python3 -c "import ast; ast.parse(open('<path>').read())"` for Python; for C#, eyeball the brace/namespace closure).
- If the merge dropped a fork-only test or assertion as part of resolving a conflict, surface it as an intentional decision in the report.

Write up the findings: what each conflict was, how it was resolved (and why), what rerere did, what behavioral checks passed. Include unified diffs of any subtle code resolutions so the user can audit them.

### 9. Review with the user before committing

```bash
git diff --cached --stat       # summary
git diff --cached              # full diff (offer per-file on request if large)
```

Present:
- Every conflict that was resolved, what each side wanted, and the final resolution
- Every file rerere auto-resolved, with a confirmation that the cached resolution was reviewed and is still correct
- Every file deleted (configurators, etc.) with the policy reason
- Every fork-specific code path the merge touched, and the behavioral verification you did
- Any decisions you flagged for the user in step 7

**Wait for the user to approve before committing.** If they want changes, make them and re-present the relevant section.

Once approved:
```bash
git commit -m "Merge upstream CoplayDev/unity-mcp v${UPSTREAM_VERSION} into fork"
```

rerere will record any *new* resolutions you produced this merge into `.git/rr-cache/` automatically, so future merges replay them.

### 10. Apply fork fixups

```bash
./tools/update_fork.sh --fixup-only
```

This will:
- Remove non-ClaudeCode configurator files
- Set the fork version suffix (e.g., `9.6.0-fork.1`)
- Regenerate `uv.lock`
- Commit the fixups

### 11. Verify

```bash
git log --oneline -5                            # recent history
ls MCPForUnity/Editor/Clients/Configurators/    # ClaudeCodeConfigurator + OpenClawConfigurator (fork-kept)
```

### 12. Report results

Summarize:
- How many upstream commits were merged and the top behavioral changes
- Every conflict that was resolved (file + the call you made)
- rerere replay summary: files auto-resolved, files where the cache was stale and you re-resolved
- Silent-breakage findings (renames, removed APIs) and how they were handled
- The new fork version
- Remind: "Run tests before pushing. Python: `cd Server && uv run pytest tests/ -v`. Unity tests require opening TestProjects/UnityMCPTests in Unity Editor."
