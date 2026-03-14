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

For each branch that has new commits:

**upstream/main:**
```bash
git log --oneline --no-merges HEAD..upstream/main
git diff --stat HEAD..upstream/main
```

**upstream/beta:**
```bash
git log --oneline --no-merges HEAD..upstream/beta
git diff --stat HEAD..upstream/beta
```

### 4. Present results

Show a table for each branch with new commits:

**Format:**
```
## upstream/main — N commits behind

| Commit | Description |
|--------|-------------|
| abc1234 | Add new feature X |
| def5678 | Fix bug in Y |

Files changed: X files (+A, -B lines)
```

Repeat for `upstream/beta` if it also has changes.

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
