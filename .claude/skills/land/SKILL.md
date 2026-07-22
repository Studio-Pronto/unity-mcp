---
name: land
description: Land work on main via a single PR — bump the fork version, run local tests, create/adopt the PR, squash-merge (or --merge for upstream syncs), close issues, clean up. The only path to main.
---

# Land — the only path to main

Everything reaches `main` by merging **one GitHub PR**, and this skill is the machinery that does it. Direct pushes to `main` are blocked by branch protection (`enforce_admins=true`, a PR is required), so `/land` — not `git push` — is how work lands.

- **`/land`** (no arg) — land the **current feature branch**: bump, gate, push, open (or adopt) its PR, squash-merge, clean up.
- **`/land <PR#>`** — land an **existing same-repo PR**: verify its tree in this checkout, merge, close its issues.

## allowed-tools

Bash, Read, Grep, Glob, Edit

## Scope: the fork, always — pin every `gh` call

This checkout has two remotes (`origin` = `Studio-Pronto/unity-mcp`, `upstream` = `CoplayDev/unity-mcp`) and **no default `gh` repo**, so a bare `gh` command silently targets **upstream**. Pass **`-R Studio-Pronto/unity-mcp`** on *every* `gh` call in this skill.

## The gate is local — never CI

This fork has **no Unity license in CI** and runs tests locally, so `/land` **never waits on or requires GitHub Actions**. The authoritative gate is run here, on the exact tree that ships: `pytest` for Python changes, `tools/local_harness.py` for C# changes. A green local gate is the only gate.

## Model (read this — it explains the guarantees)

- **Invoking `/land` IS the authorization** for the version bump, the push, the PR, the merge, and evidence-bearing closes of issues the landed PR/commits explicitly reference (the `/issues` skill defers its close to `/land`).
- **`main` only advances to a tree that passed the local gate in the exact state that lands.** The gate runs on the branch in this checkout; the merge pins the gated commit with `--match-head-commit` and rechecks base freshness immediately before merging. The lone exception is a **no-op landing** — a diff with no compiled/runtime effect (comments, docs, whitespace) — where there is nothing to test.
- **Every mutating command is self-guarding.** Never force-push a shared ref; **never `git reset --hard`** (use `git reset --keep`); never hand-merge Unity YAML; fast-forward-only pulls of `main`.
- **Merge method depends on the PR:** **squash** for a feature branch (one commit per landing, subject `<title> (#N)`); **`--merge`** for an upstream-sync branch (`sync/upstream-*`) so the merge commit — and the merge-base with `upstream/main` — is preserved. Squashing an upstream sync would destroy the merge-base and make every future `/merge` re-conflict.

## Workflow

### Preflight (both modes, no mutation)

1. `git fetch origin`.
2. **Local-main integrity:** `git rev-list --count origin/main..main` must be `0` → else **STOP** ("local `main` has commits that never landed via PR — move them to a branch first"). Under branch protection nothing should ever be committed straight to local `main`.
3. **Tree cleanliness:** `git status --porcelain`. Untracked-only (`??`) → note and proceed. Tracked (non-`??`) changes:
   - **Mode A** — if they're this session's own work, stage and commit them onto the **feature branch** with a drafted message before landing (say what you committed). If they look foreign (you didn't author them), **STOP** and ask.
   - **Mode B** — any tracked change → **STOP** (never commit into someone's PR tree).

### Mode A — land the current branch

1. `BR=$(git branch --show-current)` — empty (detached), or `main`/`master`/`beta` → **STOP.**
2. `git rev-list --count origin/main..$BR` — `0` → **STOP** ("nothing to land").
3. **Version bump** (below), then the **GATE** (below, `HEAD_REF=$BR`), then **LAND**.

### Mode B — land an existing PR

1. `gh pr view N -R Studio-Pronto/unity-mcp --json state,isDraft,mergeable,baseRefName,headRefName,headRefOid,title,body,author,url,isCrossRepository` — **STOP** if not found, not `OPEN`, or draft.
2. **Stacked guard:** `baseRefName != main` → **STOP** ("PR #N is stacked on `<base>` — land the parent first").
3. **Fork guard:** `isCrossRepository: true` → **STOP** (can't push a sync/bump to an external fork's branch — ask the author).
4. **Materialize the PR's tree** (the gate verifies files on disk):
   ```bash
   ORIG=$(git branch --show-current)   # may be main; restored on every Mode B exit
   git fetch origin "$head"
   git checkout --detach "origin/$head"
   ```
   Detached on purpose (a plain checkout fails if the head branch is checked out elsewhere; a detached HEAD leaves no local branch to trip `--delete-branch`). Untracked-file collision aborts safely → **STOP** and list; never `-f`.
5. **Version bump** (below), then the **GATE** (`HEAD_REF=$head`), then **LAND**.

### Version bump (owned by `/land`, idempotent)

The fork version is `X.Y.Z-fork.N` in `MCPForUnity/package.json`. `/land` ensures the branch is exactly one bump ahead of `origin/main` — no double-bumps, no matter how the branch got here.

1. `BASE_V` = `origin/main`'s version: `git show origin/main:MCPForUnity/package.json | python3 -c "import sys,json;print(json.load(sys.stdin)['version'])"`. `HEAD_V` = the same from the working tree.
2. **`HEAD_V == BASE_V`** → bump: increment `N` (or start at `-fork.1` if `BASE_V` has no `-fork.` suffix), then `python3 tools/update_versions.py --version <new>` and commit `Bump version to <new>` on the branch. `update_versions.py` self-skips `pyproject.toml` for `-fork.` versions (PEP 440), so **no manual pyproject edit and no `uv lock`** are needed.
3. **`HEAD_V` already ahead** (e.g. an upstream-sync branch that `update_fork.sh` already stamped `<upstream>-fork.1`) → leave it.

### GATE (shared — the exact tree that ships, local only)

1. **Base freshness:** `git fetch origin main`, then `git merge-base --is-ancestor origin/main HEAD && echo CURRENT || echo STALE`.
2. **STALE → sync main into the tree:** `git merge origin/main`. Resolve conflicts (`git diff --name-only --diff-filter=U`) **by type**:
   - **Code / text** (`.cs .py .md .json .uss .uxml .shader .asmdef .txt .yml`): resolve with the **Edit** tool, reading both sides.
   - **Unity YAML / binaries** under `TestProjects/**` (`.unity .prefab .asset .controller .mat .meta`): **never hand-merge** — pick a side wholesale (`git checkout --ours/--theirs <file>`), **the asset and its `.meta` the same side**. `--ours` = this branch, `--theirs` = main. Ask in plain language only when a pick would drop real work.
   - Then `git add -A && git commit --no-edit`.
3. **No-op gate-skip:** if everything the branch introduces over `origin/main` (`git diff origin/main...HEAD`) is provably free of compiled/runtime effect — only comments, docs/Markdown, or whitespace — skip the test legs (there is nothing to catch). **Void the skip** if a base-sync in step 2 resolved any code/serialized file. When in doubt, gate.
4. **Python gate** — run if the diff touches `Server/**` or `tools/**`:
   ```bash
   cd Server && uv run pytest tests/ -q
   ```
   Must pass. (Return to the repo root afterward — later `git`/`tools/` calls assume it.)
5. **C#/Unity gate** — run if the diff touches `MCPForUnity/**` or `TestProjects/UnityMCPTests/**`:
   ```bash
   python tools/local_harness.py --legs editmode      # add ,playmode for runtime-affecting changes
   ```
   Needs a local Hub-licensed Editor. If none is available (exit code `4`/`5`), **STOP and ask the user to run the Unity tests locally** — do not silently skip. The lighter fallback is `tools/check-unity-versions.sh` (compile-only across the CI matrix); if you fall back, report it plainly as "compiled, EditMode/PlayMode not run."
6. `GATED_SHA=$(git rev-parse HEAD)`.
7. **Red gate → hook-safe rollback, STOP:** mid-conflict → `git merge --abort`; committed sync then failed → `git reset --keep ORIG_HEAD` (never `--hard`); the branch's own code failed (no sync) → nothing to unwind. If a PR already exists, `gh pr comment N -R Studio-Pronto/unity-mcp -b "Land gate failed at <sha>: <detail>"`. **Mode B: restore the original branch (`git checkout "$ORIG"`) before stopping.** Report what failed.

### LAND (after a green gate)

1. **Push:** Mode A `git push -u origin "$BR"`; Mode B `git push origin HEAD:"$head"`. Non-fast-forward rejection = the head moved → **STOP, never force.** (Push fails on LFS/auth/network → STOP; nothing landed.)
2. **Adopt or create the PR** (Mode A): `gh pr list -R Studio-Pronto/unity-mcp --head "$BR" --state open --json number` — adopt if one exists, else:
   ```bash
   gh pr create -R Studio-Pronto/unity-mcp --base main --head "$BR" \
     --title "<real title for the bundle>" \
     --body "<one-line summary; enumerated commit subjects; issue refs>"
   ```
3. **Choose the merge method:**
   - `HEAD_REF` matches `sync/upstream-*` (or its tip is a merge commit whose second parent is an ancestor of `upstream/main`) → **upstream sync → `--merge`.**
   - Otherwise → **feature → `--squash`.**
4. **Mergeability:** poll `gh pr view N -R Studio-Pronto/unity-mcp --json mergeable,mergeStateStatus` while `UNKNOWN` (GitHub computes it async — **never treat `UNKNOWN` as `CONFLICTING`**). `CONFLICTING`/`DIRTY` → base moved → re-enter GATE step 2.
5. **Final base recheck** (`--match-head-commit` pins the head, not the base): `git fetch origin main && git merge-base --is-ancestor origin/main "$GATED_SHA"` → else re-enter GATE step 2.
6. **Merge:**
   ```bash
   # feature PR
   gh pr merge N -R Studio-Pronto/unity-mcp --squash --delete-branch \
     --match-head-commit "$GATED_SHA" \
     --subject "<PR title> (#N)" \
     --body "$(git log --no-merges --reverse --format='- %s' origin/main..HEAD)"
   # upstream-sync PR
   gh pr merge N -R Studio-Pronto/unity-mcp --merge --delete-branch \
     --match-head-commit "$GATED_SHA"
   ```
   With `required_approving_review_count: 0` and no required checks, the owner merges an unreviewed PR directly — no approval needed. `--match-head-commit` refusal = head moved between gate and merge → fetch, surface `git log $GATED_SHA..origin/$head`, re-enter GATE.

### CLEANUP + report (both modes)

1. `gh pr view N -R Studio-Pronto/unity-mcp --json state,mergeCommit` → confirm `MERGED`; capture the merge/squash SHA.
2. **Return to base:** Mode A `git checkout main && git rev-list --count main..origin/main` ≠ 0 → `git pull --ff-only`. Mode B restore the original branch (`git checkout "$ORIG"`) — landing someone else's PR is a side quest; don't strand the user.
3. **Delete the branch** if not already gone: `git branch -D "$HEAD_REF"` locally (a squashed branch isn't an ancestor of `main`, so `-d` refuses — the MERGED check is the proof) and `git ls-remote --heads origin "$HEAD_REF"` empty (`--delete-branch` removed it).
4. **Close referenced issues with evidence** (only issues the PR/commits/branch name explicitly reference; never close a partial — flag "leave open" instead). Parse `fix/issue-<N>-…` branch names and `#N` in the PR body/commits. Check each is still open (`gh issue view N -R Studio-Pronto/unity-mcp --json state`), then:
   ```bash
   # bare (#N) ref — GitHub does NOT auto-close these here:
   gh issue close N -R Studio-Pronto/unity-mcp -r completed -c "Fixed in <sha> (PR #<n>): <one line>"
   # closing-keyword ref (Fixes/Closes/Resolves #N in the PR title/body) auto-closed on merge —
   # add the same evidence as a comment instead:
   gh issue comment N -R Studio-Pronto/unity-mcp -b "Fixed in <sha> (PR #<n>): <one line>"
   ```
5. **Report:** merged SHA + method (squash/merge); new fork version; branch disposition; gate result (or the no-op skip / compile-only fallback, stated with why); issues closed and any left open (with why); the branch you're now on.

## Safety rules

- **Every `gh` call carries `-R Studio-Pronto/unity-mcp`** — a bare `gh` hits upstream CoplayDev.
- **The local gate is authoritative** — `/land` never waits on or requires GitHub Actions.
- **Never force-push a shared ref**; `--force-with-lease` on your own feature branch only.
- **Never `git reset --hard`** — `git reset --keep` / `git merge --abort` are the rollback tools. Never `git restore .` — targeted paths only.
- **Never hand-merge Unity YAML** — wholesale side-picks only, asset + `.meta` together.
- **Never merge a tree that differs from what the gate verified** — `--match-head-commit "$GATED_SHA"` on every merge, plus the final base recheck.
- **Squash feature PRs; `--merge` upstream-sync (`sync/upstream-*`) PRs** — never squash an upstream sync (it destroys the merge-base).
- **Never treat `mergeable: UNKNOWN` as `CONFLICTING`.**
- **Never close an issue silently or before its fix has landed** — every close carries an evidence comment with the merge SHA.
- A failure exit leaves: `main` untouched, nothing force-anything, and (Mode B) the original branch restored.
