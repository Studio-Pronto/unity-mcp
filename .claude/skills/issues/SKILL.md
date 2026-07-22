---
name: issues
description: Review open GitHub issues on the Studio-Pronto fork, pick the highest-priority one, independently verify its claims against both codebases (Python + C#), and produce a vetted plan — or a reasoned decline.
---

# Triage & Deep-Dive a Fork Issue

Pick the highest-priority open issue on **our fork**, **independently verify it against source**, decide whether it describes a real problem worth fixing now, and produce a plan (or a reasoned recommendation to defer/close).

## allowed-tools

Bash, Read, Glob, Grep, Agent, WebFetch, WebSearch, mcp__UnityMCP__*

## Scope: the fork, always

This skill triages **`Studio-Pronto/unity-mcp`** (our fork, `origin`) only. Upstream `CoplayDev/unity-mcp` is **out of scope** — we have read-only access there and can't self-assign/close/label it anyway.

**CRITICAL — pin the repo on every `gh` call.** This checkout has two remotes (`origin` = fork, `upstream` = CoplayDev) and **no default `gh` repo set**, so a bare `gh issue list` silently hits **UPSTREAM** and you'll triage the wrong backlog. Pass **`-R Studio-Pronto/unity-mcp`** on *every* `gh` command in this skill. (Optionally `gh repo set-default Studio-Pronto/unity-mcp` once — but don't rely on it; always pass `-R`.)

Single-run skill. Unlike some triage flows there is **no self-assignment / claim mechanism** — you're the only run, so ranking never skips issues and no GitHub write happens automatically. Every GitHub write is proposed for approval (see **Safety rules**).

## Usage

```
/issues                  # Rank all open fork issues, pick the highest-priority, deep dive it
/issues 14               # Skip selection — deep dive issue #14 directly
/issues profiler         # Rank within a scope (semantic topic), pick + deep dive
/issues patchers         # Semantic scope — issues touching the patcher layer
/issues transport        # Semantic scope — bridge/session/timeout issues
```

**Scope is semantic, not a literal label match.** `profiler` should match issues whose body is about `manage_profiler` / profiling regardless of label; `patchers` should match the `ComponentOps`/`ManageScriptableObject` patch layer; `transport` should match bridge/session/timeout issues. The full workflow still applies, just narrowed.

## The one rule that matters

**The issue is a lead, not a spec.** Our issue bodies are unusually rich — most are audit-style write-ups (a `Symptom` / `Audit @ <sha>` / `Fix` / `Effort` structure) citing exact files, line numbers, and SHAs. That precision is a **trap**: whoever filed it — including a past audit pass — could be wrong, stale, or over-cautious, and cited line numbers *drift the moment the file changes*. Your job is not to implement what the issue says. Your job is to find out what's actually true and do the right thing about it.

Three failure modes this skill exists to prevent:

1. **Blindly trusting the claim.** The `Symptom`/`Audit` section may describe code that has since changed, never behaved that way, or behaves that way for a good reason. Re-read the cited code yourself — by symbol, not by the line number in the issue.
2. **Blindly following the "Fix."** Almost every issue prescribes a fix. Treat it as one hypothesis among several. It may be the wrong layer (Python vs C#), more complex than the problem warrants, or a guard that masks the bug instead of fixing it. Evaluate it on its merits against alternatives — including doing nothing.
3. **Fixing an imaginary problem.** Be explicit about whether you are **solving a real problem** or **guarding against something that will never happen.** CLAUDE.md is blunt about this: *"Don't add error handling for scenarios that can't happen."* If the codebase actively maintains an invariant that makes the failure impossible, say so and decline.

**"This issue is not worth fixing" is a first-class, successful outcome.** You are never obligated to propose a fix for every finding. Closing with evidence is as valid as planning a fix.

## Workflow

### Step 1: Gather and rank

Pull open fork issues with structured labels so ranking is mechanical:

```bash
gh issue list -R Studio-Pronto/unity-mcp --state open --limit 200 \
  --json number,title,labels,createdAt,updatedAt
```

If `gh` errors (not authenticated), stop and tell the user to run `! gh auth login`.

Drop `wontfix`, `duplicate`, and `invalid`. If a scope was given, filter to it (semantically — read titles, not just labels).

Rank with this heuristic, **but treat every label as a claim, not ground truth** — a `P1` or `silent-failure` tag is itself something to verify, not a fact to defer to:

1. **`P1`** ("fix first: silent failures and hard blockers") **> `P2`** ("confirmed defects with workarounds") **> `P3`** ("features and large designs"). Most issues carry exactly one.
2. **`silent-failure`** ("reports success while doing nothing or the wrong thing") is a severity amplifier — a silent-data-loss bug outranks a loud one at the same priority. These are the highest-value fixes in the backlog.
3. **`bug` outranks `enhancement`** at equal priority.
4. **`needs-verification`** ("believed fixed; verify at runtime and close") is a *distinct, usually-cheap* path — the deliverable is verification, and the likely outcome is **close-as-verified**, not a fix (see Step 5, outcome C′). Good standalone pick when you want a fast, high-certainty win.
5. **Tie-break by `effort`** *within* a priority band: `effort:S` (single-file, pinpointed) before `effort:M` (cross-layer C#+Python or multi-site) before `effort:L` (design work). A `P1 effort:S` is the ideal pick — highest impact, fully scopable in one session.
6. Prefer issues you can fully verify and scope in one session over sprawling `effort:L` structural designs.

### Step 2: Pick (autonomously, but transparently)

If an issue number was passed, skip the shortlist and go straight to Step 3.

Otherwise present a compact ranked shortlist (top ~5: number, title, the labels that drove the rank) and call out **the one you're proceeding with and why** in one or two sentences. Then go straight into the deep dive — do **not** block on a confirmation question. State plainly that the user can redirect if they'd rather you take a different one. If two or three are genuinely neck-and-neck, name them and proceed with the strongest.

**Verify on current code.** Before the deep dive, make sure the checkout reflects `origin/main`, or your "confirmed"/"already-fixed" verdicts will be against a stale tree:

1. `git fetch origin` and note if the checkout is behind `origin/main` (verification should reflect landed fixes).
2. If `git status --porcelain` shows changes **you didn't make** — the user's or another session's — STOP and ask what to do with them before proceeding; their fate belongs to whoever owns them.

No branch is created yet — verification is read-only. The fix branch comes later, only for outcome A (Step 6).

### Step 3: Deep dive — verify the issue against source

Load context first, then verify. **Never trust the issue's summary, line numbers, cited SHA, or your own earlier research — re-read the actual files.**

1. **Read the issue in full**, including discussion: `gh issue view <n> -R Studio-Pronto/unity-mcp --comments`. Comments may note a partial fix, a counter-argument, or that it's stale.
2. **Locate the domain on both sides.** This system has **domain symmetry** — most tools exist in three places that are *not* generated from each other:
   - Python MCP tool — `Server/src/services/tools/manage_<domain>.py`
   - C# Editor tool — `MCPForUnity/Editor/Tools/Manage<Domain>.cs` (patchers live in `MCPForUnity/Editor/Tools/ManageScriptableObject.cs` and `MCPForUnity/Editor/Helpers/ComponentOps.cs`)
   - Python CLI — `Server/src/cli/commands/<domain>.py`

   Read CLAUDE.md's *Architecture* / *Three Layers* section if you're unsure how a claim crosses the WebSocket/HTTP boundary. Figure out **which side the claim is actually about** before opening files — e.g. "MCP schema drops params" is Python; "`SetDirty` without `SaveAssets`" is C#; a cross-layer `effort:M` bug may be both.
3. **Re-read the cited code yourself, by symbol.** Open every file/method the issue names. **Cited line numbers and SHAs drift** — locate by symbol (`Grep` the method name), and if the body cites a SHA (`Audit @ <sha>`), run `git show <sha> -- <path>` to see what that commit actually did. Confirm the code does what the `Symptom` says it does, *right now*.
4. **Check it isn't already fixed.** Search recent history for the symbols/area: `git log --oneline -30 -- <path>` and `git log -S '<symbol>' --oneline`. **Mandatory for `needs-verification` issues** — the whole point is "believed fixed, confirm it." Issues outlive the code they describe.
5. **Trace the claimed mechanism.** For a bug/risk claim, follow the real call chain (Python tool → `send_with_unity_instance` → C# `HandleCommand`, or the reverse for a response) and confirm the failure path is reachable. For a "missing coverage"/"mirror drift" claim (e.g. patcher `SerializedPropertyType` gaps), read *both* switch/handler sides and confirm they've actually diverged. For a perf claim, confirm frequency and cost.
6. **Check runtime state where the claim depends on it.** If the bug is about serialized asset values, actual over-the-bridge behavior, or live editor state — not just source — verify it for real:
   - If a Unity editor is connected (MCP server at `localhost:8080`, tools `mcp__UnityMCP__*`), drive the actual tool and observe. A field initializer in a `.cs` file is only the *default* — the real serialized value lives in the asset, so confirm against the asset, not the code.
   - For end-to-end bridge behavior, `python tools/local_harness.py` boots a headless editor and runs the smoke/EditMode/PlayMode legs (see CLAUDE.md → *Local headless test harness* for flags).
7. **Run the relevant tests.** `cd Server && uv run pytest tests/ -k "<area>" -v` for the Python side. For anything gated by `#if UNITY_*` or a compat shim, `tools/check-unity-versions.sh`. A test you can make fail *now* is the strongest confirmation the bug is real.

Delegate the mechanical parts in parallel (Explore/Agent): "find every caller of X," "did any commit touch this symbol," "read the mirror handler and diff the two switches." Keep the judgment for yourself — evaluate subagent findings independently, don't rubber-stamp them.

Record, for each material claim in the issue: **confirmed / partly-true / false / already-fixed**, with the evidence (`file:symbol` or commit SHA) that settles it.

### Step 4: The reality test — real problem or imaginary one?

This is the step that matters most. For the issue's core claim, answer in writing:

- **Is the failure reachable today?** Trace it. If no current call path can trigger it, say so.
- **If it "could break when X changes" — is changing X plausible?** Distinguish a likely future edit from a contrived hypothetical that violates an invariant the codebase actively enforces. Scaffolding against the impossible is the exact anti-pattern CLAUDE.md forbids.
- **If the fix is a guard/fallback:** would the state it guards be a *bug* if it occurred? Then the right fix **surfaces** it — return an error response / raise, not a silent guard that hides it. This is the archetype behind every `silent-failure` issue: the fix makes the failure **loud and correct** (actually save; actually write back; return an explicit error), it does **not** add a check that quietly papers over it. Don't absorb a bug to make a symptom disappear.
- **If the fix is a deletion/consolidation** (remove a duplicated writer, unify two divergent switch statements, collapse a redundant path): this usually *is* real value even when "benign today," because it removes a footgun — aligned with CLAUDE.md's *Delete Rather Than Deprecate* and *Minimal Abstraction*. Hardening that *removes* fragility ≠ defensive code that *masks* bugs.
- **Is the juice worth the squeeze?** A real-but-tiny issue gated behind an `effort:L` structural refactor can be the wrong thing to do now. "Defer" and "needs a design, not a patch" are valid verdicts.

### Step 5: Decide the outcome — and what happens to the issue

Land on exactly one, state it plainly, and propose the matching GitHub action for approval. **All `gh` writes carry `-R Studio-Pronto/unity-mcp`.**

- **A — Real, worth fixing now** → go to Step 6 and plan it. **Leave the issue open** — `/land` closes it with evidence when the fix lands (this skill stops at the plan).
- **B — Real, but not now (defer)** → disproportionate cost, or a large design that isn't this session's job. **Keep it open**; if it's tagged `P1`/`P2`, propose relabeling **down to `P3`** ("when needed") and comment the rationale. `P3` *is* our "deferred" state — there's no separate `deferred` label. Closing real debt to tidy up loses the tracking — don't.
- **C — Decline / not a real problem** → premise false, by-design, duplicate, or guards against the impossible. **Close it** with evidence and the right label. Do **not** write a fix plan for a non-problem.
- **C′ — `needs-verification` confirmed fixed** → you traced it to the commit that fixed it and (where relevant) confirmed at runtime. **Close as completed** citing the SHA and how you verified. If it turns out **not** actually fixed, propose removing the `needs-verification` label and treat it as outcome A (a real bug).
- **D — Blocked on a decision** → the fix needs a product/API call you shouldn't make alone. **Keep it open**, propose adding the `question` label, and surface the precise question with options (use AskUserQuestion if a clean choice unblocks it). Don't guess the answer.

#### GitHub actions (propose exact command, get approval, then run)

`gh issue close` takes `--reason` (`completed` | `not planned`) and `--comment`; **labels are a separate `gh issue edit`**. So a close-with-relabel is two commands.

| Situation | Action | How (all with `-R Studio-Pronto/unity-mcp`) |
|---|---|---|
| You'll fix it (A) | Keep open until the fix lands, then close | `/land` runs the close on merge: `gh issue close N -R Studio-Pronto/unity-mcp -r completed -c "Fixed in <sha> (PR #n): …"`. This skill stops at the plan, so it leaves A open. |
| Already fixed / `needs-verification` confirmed (C′) | Close — completed | `gh issue close N -r completed -c "Verified fixed in <sha> via <pytest/harness/live-MCP>: …"` |
| Premise false / not reproducible | Label `invalid`, close — not planned | `gh issue edit N --add-label invalid` then `gh issue close N -r "not planned" -c "<source that disproves it>"` |
| Guards an impossible state / by-design / cure worse than disease | Label `wontfix`, close — not planned | `gh issue edit N --add-label wontfix` then `gh issue close N -r "not planned" -c "<the invariant, or the cost/benefit>"` |
| Duplicate | Label `duplicate`, close — not planned | `gh issue edit N --add-label duplicate` then `gh issue close N -r "not planned" -c "Duplicate of #M"` |
| Real but deferred (B) | Keep open, relabel to P3 | `gh issue edit N --add-label P3 --remove-label P1,P2` (as applicable) then `gh issue comment N -b "<why now isn't the time>"` |
| Blocked on a decision (D) | Keep open, add `question` | `gh issue edit N --add-label question` then surface the question |

**Closing invariants:**
- **Never close silently.** Every close carries a comment with the evidence or reasoning — a bare close invites a re-file.
- **Never close an issue before its fix exists.** An open issue with an approved plan is honest; a closed issue with no fix is a lie. Close A only after the fix is verified in the tree.
- **Every GitHub write needs explicit user approval first.** Close, reopen, label, and comment are all outward-facing: propose the exact `gh` command, get the go-ahead, then run it. There is no auto-write exception in this skill.

### Step 6: Make the plan (only for outcome A)

Build a concrete implementation plan and take it through the standard flow — **plan mode → `/planreview` → ExitPlanMode**. Do not start editing until the plan is approved. Create the fix branch at implementation time, off fresh `origin/main`: `git fetch origin && git switch --no-track -c fix/issue-<N>-<slug> origin/main` (`feature/issue-<N>-<slug>` for enhancement-shaped work) — the fork's convention. If a live editor is attached, it still has the old tree loaded after a branch switch; trigger a recompile/refresh before any bridge-dependent test.

The plan **must** open with a one-line honest verdict:

> **Solving:** a real problem — `<the concrete way it bites, or the fragile coincidence being removed>`.

If you can't write that line without hedging ("in case," "to be safe," "just in case someone later…"), you're probably in outcome B or C — go back to Step 5.

The fix must clear this repo's bar:
- **Root cause, not a guard.** Prefer deletion/hoisting over a new flag; no fallbacks that mask bugs; make a `silent-failure` loud.
- **Domain symmetry.** If the bug spans layers, fix *every* affected layer — Python tool, C# handler, and CLI command — and keep them in step. A one-sided fix to a cross-layer tool is incomplete.
- **Tests required** (CLAUDE.md *Test Coverage Required*): a regression test **seen failing before the fix** — Python in `Server/tests/test_manage_<domain>.py`, and/or Unity in `TestProjects/UnityMCPTests/Assets/Tests/`. Run `uv run pytest` (and the Unity/harness legs if C# changed) before considering it done.
- **Minimal abstraction, focused tools.** Don't add a helper for one use; don't bloat the tool's parameter surface.
- **Conventions:** `ToolParams` for C# params, page large results, route version-specific APIs through a shim (`MCPForUnity/Runtime/Helpers/Unity*Compat.cs`), `.meta` files auto-generate. Evaluate the issue's prescribed "Fix" as *one* option against alternatives — adopt it only if it's genuinely best, and say why if you reject it.

The skill **stops at the approved plan.** It does not implement, commit, or bump the version — implementation happens on the issue's branch, and the fix lands through **`/land`** (branch → local tests → PR → squash-merge). `/land` closes the referenced issue with evidence on merge, so an outcome-A issue stays open until the fix actually lands.

## Output

- **Selection:** the shortlist + the pick and why (skip if a number was passed).
- **Verification ledger:** each claim → confirmed / partly-true / false / already-fixed, with evidence (`file:symbol` or SHA).
- **Reality verdict:** real problem vs guarding-against-the-impossible, argued — not asserted.
- **Outcome:** a plan (through `/planreview`), or a reasoned defer / close / decision recommendation — with the exact `gh` command proposed for approval.

## Safety rules

- **NEVER** run a `gh` command without `-R Studio-Pronto/unity-mcp` — a bare `gh` hits upstream CoplayDev, the wrong backlog.
- **NEVER** implement the issue's prescribed fix without independently confirming the problem and weighing alternatives.
- **NEVER** trust the issue's line numbers, cited SHA, severity label, or your own prior research — re-read the source by symbol.
- **NEVER** add a guard/fallback for a state that would be a bug — surface it (CLAUDE.md: *"Don't add error handling for scenarios that can't happen"*).
- **NEVER** comment on, label, close, or reopen an issue without explicit user approval — propose the exact `gh` command first.
- **NEVER** close an issue silently or before its fix exists — every close carries an evidence comment; a fix-it issue (A) closes only after the fix lands.
- **NEVER** commit unless the user asks; the skill stops at an approved plan.
- **ALWAYS** be honest about real-vs-imaginary; "not worth fixing" is a valid, complete result.
- When uncertain whether complexity/risk is real, ASK rather than assume.
