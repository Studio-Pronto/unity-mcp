---
name: upstream
description: Check what's new upstream and present a summary of changes we're behind on (upstream/main and upstream/beta).
---

# Check Upstream Changes

You are checking what upstream (CoplayDev/unity-mcp) has that this fork doesn't yet.

## allowed-tools

Bash, Read

## Instructions

### 1. Fetch upstream

```bash
git fetch upstream
```

### 2. Check how far behind we are

Run these in parallel:

```bash
git log --oneline HEAD..upstream/main
git log --oneline HEAD..upstream/beta
```

If both are empty, report "Fork is up to date with upstream" and stop.

### 3. Build summaries

For each branch that has new commits, gather the raw data:

**upstream/main:**
```bash
git log --oneline --no-merges HEAD..upstream/main
git diff --stat HEAD..upstream/main
```

**upstream/beta (only what's not already in main):**
```bash
git log --oneline --no-merges upstream/main..upstream/beta
git diff --stat upstream/main..upstream/beta
```

### 4. Present results

Read the actual diffs and changed files to understand what changed. Present a **flat bulleted list** of every distinct feature, improvement, or fix. One bullet per change. No grouping by category — just list them. Skip version bumps and chores.

**Format:**
```
## upstream/main — N commits behind

- **manage_build** — new tool for triggering player builds with polling support
- **LoadSceneAdditive** — duplicate scene check before loading
- **max_poll_seconds** — polling pipeline now supports timeout for long-running tools
- **Object references** — restore original ref on incompatible component assignments

Files changed: X files (+A, -B lines)
```

For `upstream/beta`, only show what's beta-only (not already in main). Same format.

### 5. Highlight areas of concern

After the tables, note any changes that touch files we've modified in the fork. These are likely to cause merge conflicts. Check by comparing:

```bash
# Files changed upstream that we've also changed since our last upstream merge
git log --format=%H --merges --grep="upstream" -1  # find last upstream merge
```

Then intersect upstream's changed files with our fork-only changed files. List any overlapping files as potential conflict areas.

### 6. Suggest next step

If there are upstream changes, end with:
- "Run `/merge` to merge upstream/main into the fork."
- Or if beta has things main doesn't: briefly note what's beta-only so the user can decide whether to wait for the main release.
