---
name: upstream
description: Check what's new upstream (upstream/main and upstream/beta) and summarize what the fork would gain by merging it, so the user can decide whether a merge is worth it.
---

# Check Upstream Changes

You are checking what upstream (CoplayDev/unity-mcp) has that this fork doesn't yet, so the user can decide whether it's worth merging. Lead with what we'd gain. Leave merge conflicts out — `/merge` works through those once the user decides to merge.

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

For each branch that has new commits, gather the raw data.

**Important — use three-dot `...` for `git diff`, not two-dot `..`:**
- `git log A..B` (two-dot) — commits reachable from B but not A. ✓ what we want.
- `git diff A..B` (two-dot) — direct tree diff. Includes fork-only changes inverted (appearing as deletions). ✗ misleading.
- `git diff A...B` (three-dot) — diff of B against the merge base with A. Shows only what B did since divergence. ✓ what we want.

**upstream/main:**
```bash
git log --oneline --no-merges HEAD..upstream/main
git diff --stat HEAD...upstream/main
```

**upstream/beta (only what's not already in main):**
```bash
git log --oneline --no-merges upstream/main..upstream/beta
git diff --stat upstream/main...upstream/beta
```

### 4. Work out what each change gives us

Read the actual diffs and changed files to understand what changed. Skip version bumps and chores. For every other feature, improvement, or fix, work out what the fork gains:

- **A fix** — what goes wrong for us today without it: the error, the wrong result, the crash.
- **A feature** — what it lets us do that we can't do now.

Check the fork's own code before counting a gain. A change gives us nothing when:
- the fork already has it, fixed or built here independently (`git log --oneline upstream/main..HEAD -- <path>` lists the fork's own commits to a file),
- it changes code the fork removed, such as the stdio transport or the CoplayDev-only workflows, or
- it only serves upstream's own release pipeline.

If the fork already has part of a change, count only the part it lacks.

### 5. Present results

Open each branch's section with a one-line verdict: is it worth merging, and why. Then list the gains, most valuable first — a fix for a crash, lost work, or a wrong result outranks a new tool, and a new tool outranks docs or CI. One bullet per change, phrased as what we gain; don't group by category. After the gains, list what gives us nothing, one line each with the reason, so the user sees what was set aside.

**Format:**
```
## upstream/main — N commits behind

**Worth merging:** yes — fixes an Editor crash in play-mode screenshots and adds manage_build.

What we'd gain:
- **Play-mode screenshots stop crashing the Editor** — the capture no longer pumps `EditorApplication.Step()`, which could flood Editor.log until the Editor died
- **manage_build** — new tool: trigger player builds and poll them until they finish
- **Object references** — an incompatible component assignment no longer loses the original reference

Nothing for us:
- **LayerMask in set_property** — the fork already has this (eb9d63a9)
- **Release-notes sync** — upstream's release pipeline; the fork dropped those workflows

Files changed: X files (+A, -B lines)
```

For `upstream/beta`, only show what's beta-only (not already in main). Same format.

### 6. Suggest next step

End with the call that follows from the verdicts:
- upstream/main has gains worth having: "Run `/merge` to merge upstream/main into the fork."
- Only upstream/beta does: name the beta-only gains that matter, so the user can decide whether to wait for the main release.
- Neither does: say so — there's no reason to merge yet.
