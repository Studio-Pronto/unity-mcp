---
name: commit-and-push
description: Commit staged work on the current feature branch and push it (to open or update a PR). Guards against protected branches — landing to main goes through /land, which owns the version bump and the test gate.
---

# Commit and Push (feature branch)

Commit the current changes on your **feature branch** and push them, so a PR can be opened or updated. This does **not** land anything on `main` — the fork is PRs-only and direct pushes to `main` are blocked by branch protection. Landing is `/land`'s job (it owns the fork version bump and the authoritative test gate).

## allowed-tools

Bash, Read, Edit, Glob, Grep

## Instructions

### 1. Guard: never on a protected branch

```bash
git branch --show-current
```

If it's `main`, `master`, or `beta` → **STOP.** Those are protected; the push would be rejected. Move your work to a feature branch first (`git switch -c fix/issue-<N>-<slug>` or `feature/issue-<N>-<slug>`), then, when it's ready, land it with **`/land`**.

### 2. Check what's changed

```bash
git status --porcelain
git diff --stat
git diff --cached --stat
```

If there are no changes at all, report "Nothing to commit" and stop.

### 3. Stage, draft the message, and commit

- Review all changed files. Stage them appropriately (prefer naming specific files over `git add -A`).
- Draft a concise commit message based on the changes and commit immediately. Do NOT ask the user to review or approve the commit message.

Do **not** bump the version here — `/land` increments the fork version once, at land time, so iterative WIP pushes don't inflate the `-fork.N` number.

### 4. Pre-push checks

**Verify fork invariants** (run from the repo root):
```bash
cd "$(git rev-parse --show-toplevel)"

# Only ClaudeCodeConfigurator should exist
ls MCPForUnity/Editor/Clients/Configurators/

# session_recovery.py should still exist (fork feature)
ls Server/src/transport/session_recovery.py

# activity_phase tracking should be present (fork feature)
grep -c "activity_phase" Server/src/transport/plugin_hub.py
```

If any invariant fails, warn the user before pushing.

**Check for secrets or large files accidentally staged:**
```bash
git diff --cached --name-only | grep -iE '\.env|credentials|secret|\.pem|\.key' || true
```

The authoritative test gate — `pytest` plus the local Unity legs — runs in **`/land`**, not here, so a WIP push doesn't need to be green.

### 5. Push the feature branch

```bash
git push -u origin HEAD
```

Non-fast-forward rejection → the remote branch moved; pull/rebase, never force a shared branch.

### 6. Report

Summarize:
- What was committed (brief)
- The branch pushed
- Any warnings from the invariant checks
- A reminder to run **`/land`** when the branch is ready to merge to `main`
