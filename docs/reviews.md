# Code Review

**This is where review findings live, from 2026-08-14 onward.** Every finding — from an agent, from
a human, from your own reading — is written here. None of it goes into a source comment. The rule
and the reasoning are in [`CLAUDE.md`](../CLAUDE.md) under *"Review findings go in
`docs/reviews.md`"*; this file is the destination.

The line to hold: a source comment states **what is true and must stay true**. It does not say who
noticed, when, or what the code used to be. *"Do not add `.webp` — Unity cannot decode it"* is a
comment. *"`silent-failure-hunter` caught this in v0.49.4 (L8)"* is a finding, and belongs here.

**Adding a finding.** Follow the format already used below: a `##` section per source file, a `###`
per member, then `Status` / `Category` / `Original location`, the finding quoted, then `Context` and
`Notes`. When it is resolved, **update the entry in place** — set `Status: Resolved`, add
`Resolved in` — rather than adding a second entry or deleting the first.

**This is historical documentation, not an instruction file.** Nothing here is a task list. Before
acting on any finding, read the current code — several of these describe defects that have already
been fixed, and the comment survived the fix.

**Relationship to the defect register.** `docs/architecture/known-issues.md` remains the source of
truth for *open defects* — it carries the stable IDs (C1–C8, H1–H23, M1–M121, L1–L37, E1–E2),
statuses, and the tiered worklist. This file archives *review narrative removed from source
comments*, and cross-references those IDs rather than restating them. When a finding here has a
register ID, the register wins on status.

**How to use it.** When working on a script, consult only that script's section. Do not read the
whole file for unrelated work.

---

## `Assets/01_Scripts/EnigmaVirtual/MainController.cs`

### `MainController.Instance` (field)

**Status:** Resolved — fixed in v0.2.0 (H1)

**Category:** Potential Bug / Unity-specific

**Original location:** Lines 17–25 (XML summary on the field)

> `private static` on both counts, and both mattered.
>
> It was an *instance* field, so `Awake`'s `if (Instance != null)` read a fresh component's own
> field and was therefore never true — the duplicate guard could not fire (H1). And it was *public*
> on a MonoBehaviour, so Unity serialized it and offered it in the inspector: anyone dragging a
> MainController into that slot would have made this destroy itself on the next launch. The scene
> had it empty, so the trap was never armed.

**Context:** Singleton backing field for the framework's main controller. Both defects are fixed;
the field is `private static` today.

**Notes:** The forward-looking half — *why it must stay `private static`* — was kept in the source
summary, because it is a constraint on future edits rather than history. Only the account of the
former state was moved here.

---

### `MainController.Awake()` — duplicate-instance branch

**Status:** Resolved

**Category:** Bug / Unity-specific

**Original location:** Lines 72–75

> Destroy, not DestroyImmediate-then-throw. The old form destroyed the component and then threw out
> of Awake anyway, from an object that no longer existed — and DestroyImmediate is not for runtime.

**Context:** The branch that runs when a second `MainController` is found in the scene.

**Notes:** "DestroyImmediate is not for runtime" was kept in source as a standing rule. The
description of the previous implementation was moved here.

---

### `MainController.Awake()` — `enabled = false` before `Destroy`

**Status:** Resolved

**Category:** Unity-specific

**Original location:** Lines 82–83 (closing sentences of the block comment)

> The old DestroyImmediate did not have this gap, but it also threw from an object it had just
> destroyed.

**Context:** Trailing note on the comment explaining why `enabled = false` precedes `Destroy(this)`.

**Notes:** The substantive part of that comment — that `Destroy` is deferred to end of frame while
`Core` resolves `main` via `GetComponentInChildren<MainController>()`, and `Awake` order between two
instances is undefined — is a genuine timing dependency and **remains in the source**. Only the
comparison against the previous implementation was moved.

---

### `MainController.Awake()` — Android storage permission

**Status:** Resolved — verified on device 2026-08-14

**Category:** Potential Bug / Unity-specific

**Original location:** Lines 93–97

> Same H17 shape as the shortcuts below, and worse placed: Awake ordering between components is
> undefined, so `Core.settings` is very likely still null here — this runs before `Core.Awake` has
> necessarily parsed settings.json. DO_LOGS is defined for Android, so the argument was evaluated
> and this threw during Awake.
>
> Ran on device 2026-08-14 (SM-T510, Android 11) and the boot completed with no NRE here.

**Context:** `#if UNITY_ANDROID` block requesting `ExternalStorageRead` before the boot chain runs.

**Notes:** The rule (use plain `Development.Log`, never `LogIf`, because `Core.settings` may be null
and `DO_LOGS` is defined for Android so the argument is evaluated) was kept in source. The H17
cross-reference and the device-verification record were moved here — the register and the session
log are where verification records belong.

---

### `MainController.CtrlAlt` (property)

**Status:** Resolved

**Category:** Bug / Readability

**Original location:** Lines 116–120 (XML summary)

> …retyping it per shortcut is what produced the precedence bug where Right Ctrl alone triggered a
> full restart.

**Context:** Shared modifier check used by every operator shortcut in `Update()`.

**Notes:** The reason to keep the check in one place is retained in source, phrased as the standing
hazard (`&&`/`||` precedence is easy to get wrong in a way that fires on a single modifier) rather
than as the incident report.

---

### `MainController.SkipAssetValidationKey` (property)

**Status:** Resolved — correction to an earlier comment, not a code defect

**Category:** Documentation

**Original location:** Lines 158–161

> An earlier version of this comment said H5's config patching would add the key to deployed
> configs. That is **false for this field**: the patcher copies keys the *shipped* StreamingAssets
> file has, and until that file carries the key there is nothing to copy. So the fallback is the
> mechanism, not a safety net for it.

**Context:** Explains why the `DefaultSkipAssetValidationKey` fallback is load-bearing for E1's
escape hatch on installations whose `settings.json` predates the feature.

**Notes:** This was a correction *of a comment*, not of code. The corrected claim now stands in the
source on its own, without the record of what the previous wording got wrong.

---

### `MainController.Update()` — operator shortcut logging

**Status:** Resolved — fixed in v0.8.8 (H17)

**Category:** Bug

**Original location:** Lines 178–187

> …the argument really was evaluated and really did throw, killing the shortcut before it ran (H17).

**Context:** The three `Ctrl+Alt` operator recovery shortcuts (`S`, `I`, `R`) and their deliberately
ungated `Development.Log` calls.

**Notes:** The whole rationale for ungated logging — that an operator reaching for a recovery hotkey
is exactly the breadcrumb you want on a machine where startup already failed, and `doLogs` is
unreachable in that state — **remains in source**, because it explains why apparently inconsistent
code must stay as it is. Only the past-tense account of the defect was moved.

---

### `MainController.KillExplorerRequested`, `SkipAssetValidationKey`, `AppData`/`AppSettings`

**Status:** Resolved (register IDs H17, E1, M20)

**Category:** Documentation

**Original locations:** Lines 126, 142, 35–38

Inline register cross-references (`(H17)`, `(E1)`, `(M20)`) removed from otherwise-technical
comments. The reasons they annotate — that `Update` is a Unity message and does not wait for the
boot chain so `Core.settings` may not exist; that the key fallback covers configs predating the
feature; that these properties are declared as framework base types to keep the framework free of
`App/` references — all remain in source, stated as constraints rather than as citations.

---

### Redundant comments removed (no review finding)

**Category:** Readability

**Original locations:** Lines 360, 395, 399, 403, 407

Five comments that restated the `if` condition on the immediately following line, removed without
archiving because they carried no information:

| Line | Comment | Restated |
|---|---|---|
| 360 | `// Kill requested processes` | `if (!…Processes.killProcesses.IsNullOrEmpty())` |
| 395 | `// Kill explorer process` | `if (…Processes.killExplorer)` |
| 399 | `// Minimize Window option` | `if (…Presentation.minimizeWindow)` |
| 403 | `// Bring to front option` | `if (…Presentation.bringToFront)` |
| 407 | `// Force screen resolution` | `if (…Screen.forceResolution)` |

---

## `Assets/01_Scripts/EnigmaVirtual/Core/Core.cs`

### `Core.BootStallTimeoutSeconds` (const)

**Status:** Resolved — C3

**Category:** Architecture

**Original location:** Lines 54–58

> This is the same reasoning as TextureCacheManager's cache watchdog, which was the one hop that
> already had this (v0.2.0); C3 is about the other six.
>
> …(H17's lesson).

**Context:** The 90s boot stall budget.

**Notes:** Both the stall-vs-total rationale and "a const, not a setting, because it exists to catch
a failed settings parse" remain in source. Only the scope-of-C3 sentence and the ID citations moved.

---

### `Core.MarkBootStage()`

**Status:** Resolved

**Category:** Potential Bug

**Original location:** Lines 141–143

> That regression was caught in review when C3 was written and again in the working tree afterwards,
> which is why the two concerns are separate parameters now instead of one call site's judgement.

**Context:** The `log` parameter. Gating a whole `MarkBootStage` call on `doLogs` starves the
watchdog, because the asset pass is the longest phase of startup.

**Notes:** The hazard remains documented in source; only the account of when it was caught moved.

---

### `Core.Awake()` — PlayerPrefs settings branch

**Status:** Resolved — M53

**Category:** Bug

**Original location:** Lines 220–224

> It was a plain Development.Log, so it was invisible on exactly the platforms that cannot see it
> any other way (M53).

**Context:** The `#else` branch warning that settings came from PlayerPrefs rather than
`settings.json`.

**Notes:** The rule — `LogError`, not `LogWarning`, because `LogWarning` is
`[Conditional("DO_LOGS")]` and would vanish on iOS and WebGL — remains in source. (Corrected
2026-08-17: this said "macOS, Linux and WebGL". `DO_LOGS` is defined for `Android` and `Standalone`,
and Unity's Standalone group includes macOS and Linux, so those two do compile it.)
**The user-facing log string still contains the literal `(M53)`.** Left unchanged: it is a string
literal in an executable statement, and altering it changes what operators see in the log.

---

### `Core.OnDestroy()`

**Status:** Resolved — M7, M68

**Category:** Bug

**Original location:** Lines 292–294

> That is also the mechanism that armed M7: with _isSet false and the live Core still up, the old
> `if (!Core.IsSet) return;` in each OnDestroy skipped its unsubscribe (M68).

**Context:** Why `_isSet` is cleared only when `_instance == this`.

**Notes:** The surviving comment still explains that clearing it unconditionally makes every
`if (!Core.IsSet) return;` bail forever while Core keeps running. Only the second-order account of
what that armed was moved.

---

### `Core.SetLanguage()` / `LanguageChangedEvent`

**Status:** Resolved — M6, M7, M69

**Category:** Maintainability

**Original locations:** Lines 182, 423, 438–439

> outside code gets += and -= only (M7).
>
> Both are now visible, and only this one is normal (M69).
>
> …which argues for keeping M6's on-screen [key] fallback, not for escalating this line (M7).

**Context:** `LanguageChangedEvent` being an `event`, and the deliberate `Development.Log` level on
the notify path.

**Notes:** All three technical points stay in source — that this is the only place that can clear
the event, that both outcomes are now distinguishable in the log, and that `Development.Log` is
deliberate because the screen rather than the log is the instrument on iOS and WebGL.

---

### `Core.BootWatchdog_IE()` — stall budget from an unclamped timeout

**Status:** Open
**Category:** Bug
**Original location:** Line 322
**Register:** M107

> sixth reader of `AssetDownload.timeout`, not converted — `stallBudget = max(90, unclamped*2)` while
> the request is now capped at 3600, so `timeout: 86400` gives BOOT STALLED a 48h deadline for a
> stage whose worst honest gap is now 1h → the project's loudest boot diagnostic is disarmed ~48x; a
> hung boot looks like a slow one.

**Context:** Found by `code-reviewer` and `silent-failure-hunter` independently while reviewing M43.
Pre-existing rather than caused by M43 — before the clamp the request was unbounded *and* the budget
enormous, so the watchdog was equally useless. M43 only makes the fix cheap.

**Notes:** Deliberately not fixed in v0.49.9. It changes boot-watchdog timing, which deserves its own
test rather than riding along in a timeout commit, and there is a trade-off to settle: reading the
effective value is correct by the comment's own logic, but a project configuring `0` then gets a
2-hour budget, which is accurate and indistinguishable from a watchdog that is off. Filed as M107
with the trade-off written down. `silent-failure-hunter` rated this the finding most likely to reach
production unnoticed, because the symptom is the *absence* of a log line.

## `Assets/01_Scripts/App/Model/AppData.cs`

### File header — content model member kinds

**Status:** Resolved — M3, M4

**Category:** Design / Unity-specific

**Original location:** Lines 5, 18–22

> Content models are public FIELDS, never auto-properties (M3).
>
> …which is the same shape as M4: a model written with fields collected zero assets because the
> asset walk read properties only.

**Context:** The template every branched project starts from. Three serializers are in play and only
fields are visible to all of them; `JsonUtility` round-trips `appSettings.json`, so a property on a
settings DTO is erased on the next save.

**Notes:** The serializer table and the erasure warning remain in source — they are the reason the
rule exists. Only the ID citations and the account of M4's symptom moved.

---

### `MediaVO` (class)

**Status:** Resolved — M6, M62

**Category:** Design

**Original locations:** Lines 72, 79, 84, 92

> …decision</b> (M62).
>
> v0.22.0 removed its last call site and v0.24.1 deleted it; this comment replaces it.
>
> …those overloads moved under `App/` in v0.22.0, which is what made this a project decision rather
> than a framework one.
>
> …not `string.Empty` (M6)…

**Context:** Why `thumb`/`url` ship as plain strings while `title`/`description`/`keywords` are
`LocalizedField<string>`, and what changing that would cost.

**Notes:** The migration guidance, the content-format warning and the `GetLocalized()` null trap all
remain in source. The deleted `string.GetTranslation()` extension is now phrased forward — *do not
reintroduce it* — rather than as a record of which version removed it.

---

## Project-wide sweep (2026-08-14)

Beyond the per-file findings above, three categories of review residue were removed from source
comments across the whole project. They are recorded here as categories rather than as ~630
individual quotations, because each one's *technical* content stayed in the source — only the
review bookkeeping was removed.

**On the counts.** Earlier versions of this file said 585 ids, then 642; and 44 attributions, then
55. **642 is exact** and independently confirmed — comment portions at `b91dcc7`, `%`-prefixed
tokens excluded, three-digit ids included (`M100` alone accounts for 4).

The attribution figure is definition-dependent and should be read as approximate: counting only
`` `agent-name` `` mentions gives 32; adding "an earlier draft"/"an earlier version of this comment"
gives 55; adding every "found in review"/"the two reviews" phrasing gives about 64. The widest
definition is the honest one, and the last eight under it were only removed in the third review
round — a regex looking for `reviewers` and `by review` never matched `reviews` or `in review`.

Recorded at this length because three separate counts here were published wrong before being
measured properly. Re-measure; do not quote.

### 1. Register citations — 642 removed, 89 files

**Category:** Documentation

Inline defect ids (`(M38)`, `(C7)`, `(H17's lesson)`, `(M20, M16)`, `"same lesson as H9"`,
`"found by reviewing the M71 fix"`). These point at a `known-issues.md` entry, not at anything a
reader needs to understand the code.

Every sentence they annotated survives. Examples:

| Before | After |
|---|---|
| `M85: File.Exists at the cache path is not evidence of a cache` | `File.Exists at the cache path is not evidence of a cache` |
| `the same never-signals defect M75 fixed one branch below` | `the same never-signals defect as the branch below` |
| `which is what resets the C3 watchdog` | `which is what resets the boot watchdog` |
| `Same lesson as H9.` | *(removed — the id was the only content)* |

To find the history behind any of these, search `docs/architecture/known-issues.md` for the id.

### 2. Review-process attributions — 55 removed, 25 files

**Category:** Documentation

Who found a defect and what a previous draft of a comment got wrong: ``` `code-reviewer` caught
it ```, `An earlier draft of this comment claimed…`, `Both reviewers found it independently`,
`the register was not precise about this`.

The measurement or conclusion each one introduced always stayed. `"…measured in the editor, after
an earlier version of this comment asserted the opposite"` became `"…measured in the editor"` — the
measurement is the useful half.

### 3. Redundant comments — 5 removed

**Category:** Readability

Listed in the `MainController.cs` section above.

---

## Deliberately kept

Recording these so a future pass does not remove them as residue:

- **Migration markers.** `Renamed from TranslatableField<T> in v0.28.0`, `internal, so CS0122 from
  App/ since v0.24.0`, `Called EditorSettings until v0.49.2`, `Was Analitics until v0.49.5`. Every
  client project branches from this repo, so "what changed under you, and when" is documentation.
- **Every "why" that explains non-obvious code**: the `enabled = false`-before-`Destroy` timing gap,
  the ungated operator-hotkey logging, `AppData.cs`'s three-serializer table, the webp decoder
  measurement, the stopped-coroutine `finally` finding, the `UriBuilder`-not-concatenation reasoning.
- **Two original developer `TODO`s**, neither review feedback:
  `OSCBundle.cs:66` (`Pack bundle with timestamp in NTP format`, on a method that throws
  `NotImplementedException`) and `AssetManager.cs:301`, which quotes a former TODO while explaining
  the fix that replaced it.

## Known exceptions — ids that survive in *code*

**47 defect ids remain inside string literals, `[Obsolete]` messages and attribute arguments**,
across 16 files: `BuildValidator.cs` (14), `SocketConnection.cs` (7), `PanelPassword.cs` (7),
`EditorSettingsLockScreen.cs` (4), `AnalyticsSender.cs` (2), `TextureCacheLoader.cs` (2),
`Development.cs` (2), and one each in `Core.cs`, `ConfigsPanel.cs`, `FileDownloader.cs`,
`PhidgetsManager.cs`, `AppDataController.cs`, `RFIDReader.cs`, `SerialPortsManager.cs`,
`OneCore.cs` and `AppDescriptor.cs`.

`Core.cs` is the one to know: its PlayerPrefs `LogError` string ends `(M53)`, and operators grep
for that message.

An earlier version of this section said 59 across 15 files. Two errors: the count matched the
`%C0`–`%C9` **and** `%E0`–`%E9` percent-encodings in `StringExtensions.cs` — 20 accented-character
escapes that are not ids at all — and the file list omitted `Core.cs`, the single item the section
exists to record.

None were touched, and that is deliberate: they are executable code, not comments. Their text
reaches an operator's log, an inspector tooltip or a compiler warning, so editing one is a
behaviour change rather than a documentation edit.

`BuildValidator` is the clearest case — every rule's failure message names its id **on purpose**,
so an operator hitting a failed build can look it up. The class doc says so.

Whether to strip ids from operator-facing strings is a separate decision from this cleanup, and it
needs its own pass with the log output in view.

---

## `Assets/01_Scripts/EnigmaVirtual/Core/Display/ui/core/CoreSystemUI.cs`

### `CoreSystemUI.Init()` — the M77 report gate

**Status:** Resolved

**Resolved in:** v0.49.6 (2026-08-17)

**Category:** Bug

**Original location:** Lines 115–117

> over-triggers on `AppData.isActive:false` + `AssetDownload.isActive:true` — `SetupAssetManager`
> never runs, so binding the Text changes nothing, but the message tells the operator to bind it

**Context:** The first version gated the missing-Text report on
`AssetDownload.isActive || TextureCache.isActive`, with no `AppData` term. Raised independently by
both `silent-failure-hunter` and `code-reviewer`.

**Notes:** Tracing it made the case stronger than either review reported. The texture pre-cache is
reached from `AssetManager_onAssetCleaningCompleteEvent`, so it is downstream of the **asset** phase —
`TextureCache.isActive` alone can never produce a readout and needs no term at all. Final gate:
`AppData.isActive && AssetDownload.isActive`, with the coupling stated in the comment so a future
entry point for the texture phase does not silently escape it.

---

### `CoreSystemUI.Init()` — the `Awake`/`Init` ordering comment

**Status:** Resolved

**Resolved in:** v0.49.6 (2026-08-17)

**Category:** Documentation

**Original location:** Line 100

> comment asserts "every Awake runs before any Init" — false in general:
> `MainController.AddComponent<TextureCacheManager>()` runs that Awake *during* Init. Conclusion
> holds, stated reason doesn't.

**Context:** Justifying why `textureCacheManager` must be re-resolved in `Init`.

**Notes:** Corrected to "`CoreSystemUI.Awake` runs before `MainController.Init`", which is the claim
the fix actually rests on. Worth keeping as an entry because the general form is a plausible-sounding
Unity rule that is false, and it would have become precedent in a branched project.

---

### `CoreSystemUI.Init()` — always-compiled `LogError` for a missing readout

**Status:** Won't fix — accepted trade

**Category:** Design

**Original location:** Line 119

> always-compiled LogError with no off switch — a branch that deliberately ships no preloader readout
> gets a red error every boot, on screen too when the debug console is active.

**Context:** `Development.LogError` is always compiled; `LogWarning` and `LogIf` are
`[Conditional("DO_LOGS")]`, and `DO_LOGS` is defined only for Android and Standalone.

**Notes:** Kept as `LogError` deliberately. A `LogWarning` would be invisible on iOS, macOS, Linux and
WebGL — precisely the targets where the screen is the only instrument, and where a silently absent
preloader is hardest to diagnose. Recorded here rather than left implicit so a branch owner who wants
no preloader knows the error is expected and why, rather than filing it as a defect.

---

### `CoreSystemUI.cs:107` — dead `Core.main != null` guard

**Status:** Won't fix

**Category:** Code Quality

> `Core.main != null` is dead — `Core.cs:311` dereferences `main` one line earlier.

**Notes:** Both reviews confirmed it cannot be null there and both said to keep it. Harmless, and the
comment does not claim it guards a real case.

---

### Pre-existing, found in passing — not fixed, not yet filed

**Status:** Open

**Category:** Potential Bug

Raised by `silent-failure-hunter` while reviewing this diff. Recorded here so they are not
rediscovered; none has a register id yet, and each wants its own measurement before one is allocated.

- **`CoreSystemUI.cs:311` and `:330` divide by the event's `total`** — a `DivideByZeroException` when
  it is 0. This is the strongest candidate for an id of the three. (Its citation was wrong when first written and drifted again afterwards - `reviews.md` citations rot exactly like the register's, so re-grep them when touching the file.)
- **The readout is only ever hidden by the two `…CompleteEvent` handlers**, so a download phase that
  aborts leaves the last progress string frozen on screen.
- **`Core.cs:20` `systemUI` is a second serialized reference** to the component `Core.cs:44` `_coreUI`
  already finds by `GetComponentInChildren`. A branched scene that drops `systemUI` NREs at
  `Core.cs:312` — loud rather than silent, but it is the same "scene dropped a reference" class M77 is
  about, and one of the two fields is redundant.

---

### `CoreSystemUI.Init()` / `WaitForAssetManager()` — M102's first version was a regression

**Status:** Resolved

**Resolved in:** v0.49.7 (2026-08-17), before commit

**Category:** Bug

**Original location:** Lines 171–185 (the first `WaitForAssetManager`)

> the event can already have fired before Init, and then the readout silently never wires. With
> `AppData.useLocalAppData: true` on Standalone/editor, `AppDataController.Init → TryLoadData →
> LoadLocalData → File.ReadAllText → OnGetAppData → ParseAppData → OnDataLoadedEvent?.Invoke()` is
> fully synchronous, all inside `MainController.SetupAppData` — i.e. inside `main.Init()`, BEFORE
> `systemUI.Init()`. MainController's handler creates the manager and unsubscribes, then
> `CoreSystemUI.Init` finds its own field still null and subscribes to an event that will never fire
> again. **Worse than the old code: no LogError either.**

**Context:** M102's fix only ever *waited* for `OnDataLoadedEvent`, on the premise that the manager is
created strictly after `Init`. True on the remote path, false on the local one.

**Notes:** Blocked by `code-reviewer` before commit. Fixed by resolving from `Core.main.AssetManager`
first and waiting only if still null — resolve first, wait second. Dormant on the shipped config
(`useLocalAppData` is false, and Android/WebGL route `LoadLocalData` through a coroutine), which is
exactly why it would have shipped: the path it breaks is one a branched project turns on.

The lesson worth keeping: **a fix built on "X always happens after Y" needs the synchronous case
checked**, because an event-based handoff that is a coroutine on one path is often a straight call on
another. Two probe tests now pin it — the manager already existing at `Init` must be resolved, and
must **not** produce a subscription.

---

### `CoreSystemUI.OnDisable()` — visibility

**Status:** Resolved

**Resolved in:** v0.49.7 (2026-08-17), before commit

**Category:** Unity-specific

> `private void OnDisable()` vs the overview row telling branch owners to "call `base.OnDisable()`".
> Impossible — it is private (CS0122), and a subclass declaring its own `OnDisable` silently takes
> over the message, leaking exactly the subscription `_awaitingAssetManager` exists to guard.

**Notes:** I had written the doc advice before checking the modifier. Now `protected virtual`, matching
`ScreenControllerBase` and `VideoPlayerControllerBase`, so the advice is both possible and correct.
Caught by me while drafting the `doc-reviewer` brief, and independently by `code-reviewer`.

---

### `WaitForAssetManager()` — missing `AppData.isActive` term

**Status:** Resolved

**Resolved in:** v0.49.7 (2026-08-17), before commit

**Category:** Readability

> the "no AppDataController either" LogError lacks the `Core.settings.AppData.isActive` term that the
> sibling check deliberately carries. With `AppData.isActive` false and `AssetDownload.isActive` true,
> `MainController` never calls `SetupAppData`, so `AppDataController` is null and there was never an
> asset phase to report.

**Notes:** Same false-alarm shape as the M77 gate, one method over. Term added, and a counter-test now
covers it.

---

## `Assets/01_Scripts/EnigmaVirtual/Core/Managers/NotifyController.cs`

### `Show()` — M96's first fix was a regression of the same class

**Status:** Resolved

**Resolved in:** v0.49.8 (2026-08-17), before commit

**Category:** Bug

> Show arriving inside the 0.5s Hide fade OR the 0.8s entrance now cancels those tweens, then skips the
> entrance because activeSelf is true -> `_rect.anchoredPosition` stays frozen wherever the cancelled
> moveY abandoned it (up to sizeDelta.y, fully off-screen), so the replacing notification is invisible
> until the next full Hide->Deactivate->Show cycle -> this is M96's own failure class, downgraded from
> permanent to per-notification

**Notes:** The entrance now runs unconditionally; only the instant alpha stays conditional. Worth
keeping because of *how* it slipped through: my counter-test "a replacing Show swaps the text without
blanking" **passed for the broken version**, because it only checked the text. A counter-test that
asserts the wrong property is worse than none — it converts an unverified claim into a verified-looking
one.

---

### `LeanTween.alpha(gameObject, …)` — a no-op on this panel

**Status:** Open — filed as M104, deliberately not fixed

**Category:** Design

> It routes to TweenAction.ALPHA -> `alphaRecursive(Transform,…)` which writes only
> `Renderer.materials`; a `[RequireComponent(typeof(Image))]` object has a CanvasRenderer, which is not
> a Renderer, and every child is Text/Image -> the panel has always popped in and out on position
> alone, no fade, ever

**Notes:** Verified in `Assets/LeanTween/Framework/LTDescr.cs`. This is why M96's register entry
described an alpha-0 symptom that cannot happen, and why two comments of mine repeated it. Retargeting
to the `RectTransform` overload would add a fade every branch has never had, so it is filed rather than
done. **The register entry was wrong about its own mechanism for a year** — the real symptom was
positional all along.

---

### `Deactivate()` — the guard is not the fix

**Status:** Resolved — labelled rather than removed

**Category:** Code Quality

> `cancel` does not fire `onComplete` … So **part 1 is load-bearing, part 4 is unreachable defence** —
> no path invokes `Deactivate` after a `Show`. Keep it if you want, but don't credit it with the fix.

**Notes:** Confirmed at `LeanTween.cs:561`/`:572`. Also flagged: `cancel(_rect)` and
`cancel(gameObject)` are the *same call* — `cancel(RectTransform)` forwards to
`cancel(rect.gameObject)` — so one of the two lines I wrote was dead and is gone. The guard stays, now
explicitly labelled unreachable, because a future change to how `Show` clears its tweens would make it
load-bearing without anyone noticing.

---

### Probe limits found while testing M96

**Status:** Open — recorded, not a code defect

**Category:** Other

Three things the editor could not settle, all written into the test plan as play-mode steps:

- **The positional assertion.** Five isolated reproductions of the exact sequence — including the
  two-tween `Hide` shape with its `setOnComplete`, cancelled and re-pushed — all show the move-to-zero
  correctly queued. The same sequence driven through the real methods reports **zero** registered
  tweens, and I could not explain the difference. The fix is verified by isolated reproduction, not
  end-to-end.
- **`CallLater` cannot be armed in the editor**, because coroutines do not run outside play mode, so
  M103's clause is unverified.
- **`LeanTween` needs `LeanTween.init()`** before it registers anything in edit mode, and destroying a
  GameObject while its tweens are live leaves LeanTween holding dead transforms — after which pushes
  for a *fresh* object silently register nothing. Both cost probe rounds and both look exactly like a
  failing assertion.

### `NotifyController.Show()` — `this.StopCallLater(...)` did not compile

**Status:** Resolved
**Category:** Bug
**Original location:** Line 118 (v0.49.8)
**Resolved in:** v0.49.9
**Register:** M105

> `this.StopCallLater(coroutine)` is `CS1061` — `MonoBehaviourExtensions.StopCallLater` is a plain
> static, not an extension — so commit `e9dcb24` (v0.49.8) does not build, and nothing in this diff
> was exercised in a compiled tree until that line changed.

**Context:** Confirmed by all three reviewers and by `dotnet build`. `StopCallLater` is deliberately
not an extension: the coroutine runs on `CallLater`'s persistent host, and Unity only stops a
coroutine from the MonoBehaviour that started it.

**Notes:** The important part is not the typo, it is that `com.unity.pipeline`'s `recompile` reported
`status: up_to_date, errors: 0` over a **stale** assembly for the whole round — so the commit went
out, and every `eval` probe that round measured the *previous* build. M96's ten passing assertions
attested to code that was not loaded, and its two failures were attributed to a LeanTween mystery
that does not exist; both cleared once it compiled. `dotnet build EnigmaVirtual.csproj` is the
compile authority now, and the rule is in `CLAUDE.md`.

## `Assets/01_Scripts/EnigmaVirtual/Extensions/WebRequestExtensions.cs`

### `WebRequestExtensions.EffectiveTimeout()`

**Status:** Resolved
**Category:** Bug
**Original location:** Line 10 and 37, first version
**Resolved in:** v0.49.9

> the "unconfigured" fallback is a hardcoded 30s shared by every caller, so `timeout: 0` on the
> download paths now yields 30s — shorter than the shipped 60 and the shortest bound in the
> framework — on exactly the requests whose own ceiling is 3600. The method parameterises the
> ceiling and not the default, so the download callers cannot state the half of the policy that
> bites first.

**Context:** `code-reviewer` and `architect-reviewer` reached this independently and proposed the
same fix: on the download blocks `0` has always meant "no limit", so the nearest bounded reading of
it is the *ceiling*, not the smallest available timeout. `Api` must keep 30, because there `0` means
"unset". The first version would have turned a deliberate "no limit" into a value shorter than the
shipped default, on the one path where a cut-off download cannot resume.

**Notes:** Fixed by making the default an argument alongside the ceiling. The probe now also reads
the call sites' *source* to confirm each download site passes its ceiling in both positions — a
call-site fact no runtime assertion can observe. One assertion states the rejected design explicitly
(a download `0` must **not** resolve to 30), because a clamp that is merely present looks identical
to one that is right.

### `WebRequestExtensions` (class accessibility)

**Status:** Resolved
**Category:** Architecture
**Original location:** Line 7, first version
**Resolved in:** v0.49.9

> `public` with no caller outside this assembly — no `UnityWebRequest` anywhere in `App/`,
> `Assets/Editor/` or `Assembly-CSharp`. This is new public framework surface, so it becomes a
> permanent contract for every branch the moment one adopts it.

**Context:** `CLAUDE.md`'s rule is to widen a member when it has a caller, not in advance. Changed to
`internal`. The durable cost named by the reviewer is the point: an `internal` helper can change
signature for years, a `public` one in the global namespace cannot.

**Notes:** Global namespace was *kept*, and that is the project's recorded position (L17) — the ~21
`*Extensions` holders are global on purpose, because moving one gives every branch a `CS1061` that
does not name a namespace. `MonoBehaviourExtensions`/`CallLaterHost` are the anomaly, and this round
showed their cost: `NotifyController` has to spell out `MonoBehaviourExtensions.StopCallLater`.

### `WebRequestExtensions.EffectiveTimeout()` — the non-positive-ceiling branch

**Status:** Won't fix
**Category:** Code Quality
**Original location:** Line 33

> a non-positive ceiling does not merely "fall back" — measured `SetTimeout(120, 0)` →
> `www.timeout = 30`, so a caller bug DISCARDS a valid operator value.

**Context:** Both reviewers flagged it; `architect-reviewer` suggested deleting it with the
`internal` change, `silent-failure-hunter` suggested keeping it and adding an always-compiled
`Development.LogError`.

**Notes:** Kept as-is, and the doc comment rewritten to state what it actually does — including that
it discards a good configured value — instead of promising callers a contract it does not enforce.
Reasoning: with the class `internal` and all **five** call sites passing positive `const`s the branch is
unreachable, and of the three possible wrong answers for a bad ceiling (`0` = unbounded, `1` =
broken, the default = bounded and near-normal) the current one is the least harmful. Deleting it
would make `Math.Min(configured, 0)` assign `0`, which is the unbounded wait the method exists to
prevent.

## `Assets/01_Scripts/EnigmaVirtual/Core/Models/ApiSettings.cs`

### `ApiSettings.timeout`

**Status:** Resolved
**Category:** Documentation
**Original location:** Line 26
**Resolved in:** v0.49.9

> the only written-down semantics for the download timeouts still says they "pass it straight
> through" and that unifying them "is logged separately" → a maintainer or operator reads the doc and
> writes `timeout: 0` expecting "wait forever", which is now exactly the value that gets overridden;
> `AppDataSettings.cs:388` and `:397` have no doc comment at all.

**Context:** Found by `silent-failure-hunter`, and independently by `code-reviewer` as one of four
doc sites the change invalidated. The stale sentence was the *only* written record of the download
blocks' semantics, and it survived the fix that made it false.

**Notes:** Rewritten to describe the two policies that now exist rather than the divergence that no
longer does, and both `uint timeout` fields in `AppDataSettings` gained the doc comment they never
had — they are the ones an operator actually edits.

## `Assets/01_Scripts/EnigmaVirtual/Core/Modules/Connections/FileDownloader/FileDownloader.cs`

### `FileDownloader` — the `www.timeout` comment

**Status:** Resolved
**Category:** Documentation
**Original location:** Line 74
**Resolved in:** v0.49.9

> comment cites "the AssetDownload/TextureCache timeouts" as the exemplar of the trap those sites no
> longer have → stale pointer, next reader copies the wrong precedent.

**Context:** Found by `silent-failure-hunter`. A comment pointing at another file as an example is a
citation, and it rots the same way a `file:line` does.

**Notes:** Rewritten to say why this site *correctly* stays out of the helper — its timeout is a
local `const`, not operator config. The two other direct assignments (`ConfigManager`, `FileManager`)
are the same case and were confirmed out of scope by both reviewers.

## `docs/architecture/known-issues.md`

### Citation layer after the M43 round

**Status:** Resolved
**Category:** Documentation
**Original location:** 17 citations across `AssetManager.cs`, `TextureCacheManager.cs`, `Api.cs`,
`NotifyController.cs`
**Resolved in:** v0.49.9 (follow-up commit)

> The re-point pass covered some citations in the edited files and missed 17. Each below was correct
> at `e9dcb24` and now names a different construct.

**Context:** Found by `doc-reviewer`. Two independent causes, both mine. First, the citation syntax
has three shapes - `:255`, `:255-296` and `:434,698` - and my script's regex matched only the single
form, so every ranged or listed citation was invisible to four consecutive passes. Second, the script
only re-pointed a citation whose current line looked *weak* (blank, brace, comment); a citation that
drifted onto other real code was skipped by design, which is precisely the failure mode `CLAUDE.md`
warns about.

**Notes:** Fixed by mapping through `difflib`'s equal-opcodes between `e9dcb24` and the working tree,
gated on whether the citation still carried its pre-commit number - so an already-re-pointed one is
left alone and nothing is double-shifted. One spec (`H9`, whose range spanned the edited region) was
resolved by hand. Verified afterwards: 387 citation targets, 0 out of range.

Also caught in the same review, and worth keeping separate because it is **pre-existing and much
larger**: 49 of the register's 299 single-line citations name nothing at all - a bare brace or a blank
line - across about twenty files. Recorded in the session log as a bounded pass to do, not a defect.

### The `DO_LOGS` platform claim

**Status:** Resolved
**Category:** Documentation
**Original location:** `known-issues.md` M8 and M40, `overview.md` (2 places), `reviews.md`,
`sessions/2026-08-17.md` (2 places)
**Resolved in:** v0.49.9 (follow-up commit)

> M106 says LogWarning/LogIf "vanish on iOS/macOS/Linux/WebGL" and names "the two targets where
> DO_LOGS *is* defined" — ProjectSettings.asset:772 defines DO_LOGS for BuildTargetGroup `Standalone`,
> which is Win+macOS+Linux.

**Context:** Found by `doc-reviewer`, and verified directly against
`ProjectSettings/ProjectSettings.asset:771-772`, which defines `DO_LOGS` for `Android` and
`Standalone` only. Unity's `Standalone` BuildTargetGroup is one group for Windows, macOS and Linux, so
`Development.Log`/`LogWarning`/`LogIf` compile on all three, and the targets that strip them are
**iOS and WebGL**.

**Notes:** The error was pre-existing and repeated in five documents, one of which used it as the
rationale for choosing `LogError` over `LogWarning` in the M77 round - that decision still stands on
the two targets that do strip, but its stated reach was double the truth. Corrected in place with a
dated note in each entry, per the archive's rule, and `CLAUDE.md`'s `DO_LOGS` gotcha now states the
group membership explicitly so it is not re-derived wrongly.

## `Assets/01_Scripts/EnigmaVirtual/Core/Core.cs` (M106 / M107 round)

### `Core.ReportClampedTimeouts()` - the `AppData.isActive` gate

**Status:** Resolved
**Category:** Bug
**Original location:** Line 384, first version
**Resolved in:** v0.49.10

> the `!settings.AppData.isActive` early return silences the TextureCache report that the comment
> immediately below it says must never be silenced - `MainController.SetupTextureCacheManager()` sits
> outside every `isActive` check, so `TextureCacheLoader` clamps and downloads with `AppData` off.

**Context:** Found by `architect-reviewer` and independently by `silent-failure-hunter`. My own comment
argued that `TextureCache.isActive` must not gate the report, and then a parent gate two lines above
defeated exactly that reasoning. Verified directly: `SetupTextureCacheManager()` is at
`MainController.cs:354`, outside the `if (Core.settings.AppData.isActive)` block at `:347`.

**Notes:** The TextureCache report is now guarded only by `cache != null`. The probe assertion that
used to read "AppData off reports neither download block" - which encoded the bug - now asserts the
asymmetry: AppData off silences `AssetDownload` but **not** `TextureCache`.

### `Core.StallBudgetSeconds()` - ungated read, and the report/budget asymmetry

**Status:** Resolved
**Category:** Bug
**Original location:** Lines 348-351, first version
**Resolved in:** v0.49.10

> the watchdog reads `AssetDownload.timeout` **ungated**, while the report for that same field is
> gated - so a project with downloads off and `timeout: 0` gets a 7200s budget sized off a request
> that is never issued.

> the M106 report returns early when `configured == effective`, but `StallBudgetSeconds` scales the
> budget off the **effective** value - so every *un-clamped* legal timeout inflates the watchdog with
> nothing logged. `AssetDownload.timeout: 1800` → 3600s budget, no report, because 1800 is under the
> ceiling.

**Context:** First from `architect-reviewer`, second from `silent-failure-hunter`, which rated it the
finding most likely to reach production unnoticed: "a project raising `AssetDownload.timeout` to 300
for a slow CMS, silently taking the boot-stall watchdog from 120s to 600s, with no log line anywhere
stating the new budget."

**Notes:** Both taken. The budget is now gated on the same flags as the report and takes the widest
*active* download path. For the second, `silent-failure-hunter` offered a cap (`Mathf.Min(..., 600f)`)
or a log; the log was chosen and the cap explicitly rejected, because bounding the budget below a
legitimately long request reintroduces a **latching** false `BOOT STALLED` - and this codebase has
already settled that a late report beats a false one. `AnnouncedStallBudget` logs the budget whenever
it changes.

### The placement rationale, and the TextureCache branch's premise

**Status:** Resolved
**Category:** Documentation
**Original location:** Lines 302-304 and 362-367, first version
**Resolved in:** v0.49.10

> the placement rationale is false - `MainController.Init` runs unconditionally, only module
> *creation* inside it is gated.

> the TextureCache branch of `StallBudgetSeconds` rests on a false comment - the pre-cache does NOT
> mark per-image boot stages, so 2x one request timeout is not that stage's honest gap.

**Context:** Both from `code-reviewer`, and the second verified by grep: `TextureCacheManager` never
calls `MarkBootStage`, and the only marks around the pass are `"caching textures"`
(`MainController.cs:704`) and `"textures cached"` (`:712`). So the pre-cache is **one** stage covering
every image.

**Notes:** Two comments rewritten to say what is true. The branch is kept - removing it would leave a
texture-cache-only project on the floor - but it now states that twice one request timeout is a *lower
bound* on that phase's honest gap, not a match for it. The underlying problem is filed as **M109**,
with the real fix named: per-image `MarkBootStage(..., log: false)` off `onTextureCachedEvent`.

### `ApiUnconfiguredSeconds` and `ApiSettings.timeout` had to agree, and nothing enforced it

**Status:** Resolved
**Category:** Design
**Original location:** `WebRequestExtensions.cs:28` / `ApiSettings.cs:36`
**Resolved in:** v0.49.10

> `ApiUnconfiguredSeconds = 30` and `ApiSettings.timeout = 30` must stay equal for "0 lands on the
> field's own initializer" to hold, and nothing enforces it - the one coupling the move did not
> remove.

**Context:** Found by `architect-reviewer`. The invariant is load-bearing for a claim made in two doc
comments and in the register.

**Notes:** The field is now initialised *from* the const rather than from a literal, so the two cannot
drift. A probe assertion checks it as well, since the initialiser could be changed back without
anything failing to compile.

## `Assets/01_Scripts/EnigmaVirtual/Data/AppDataController.cs` (M48 / M56 round)

### `LoadRemoteSettings()` - the consequence clause, and the platform scope

**Status:** Resolved
**Category:** Bug
**Original location:** Line 1307, first version
**Resolved in:** v0.49.11

> "continues with the appSettings it already had" is false - `Init()` constructs a fresh `AppSettings`
> at `:246` and this coroutine only ever runs from `LoadSettings()`, so on failure it is an
> ALL-DEFAULTS instance, not retained values.

> `LoadRemoteSettings` is started only inside `#if UNITY_ANDROID || UNITY_WEBGL`, and only when
> `AppData.isActive && !oneCoreDataModel.isActive`. None of the M48/M110 code runs on Windows
> Standalone.

**Context:** Both from `code-reviewer`; the first also from `silent-failure-hunter`. The first is the
sharper of the two: M48 exists to make that one line actionable, and the line then sent the reader
looking for a previous value that never existed. The second is a scoping error of mine - I filed M110
as "Live, not dormant" from reading the method without checking what compiled it.

**Notes:** The message now says "continues on DEFAULT appSettings - nothing was loaded, so every
remotely-configured value is absent, not merely stale". The platform gate is recorded in both register
entries and in the test plan, whose step 5 originally said "press Play" for a path the Windows editor
does not compile.

### `LoadRemoteSettings()` - reporting before notifying, and the right null test

**Status:** Resolved
**Category:** Bug
**Original location:** Lines 1292 and 1325
**Resolved in:** v0.49.11

> `Core.notificationManager.ShowNotification(...)` is unguarded and runs *before* the new `LogError`.
> `Core.cs` explicitly anticipates a null here and promises "error paths that report through it will
> still be logged" - which is false for these two sites: the NRE kills the coroutine before the log
> line ever runs, reproducing exactly the blank-record defect M48 was written to remove.

> use `if (Core.notificationManager != null)`, not `?.` - `?.` bypasses Unity's destroyed-object
> fake-null.

**Context:** First from `silent-failure-hunter`, second from `code-reviewer` after I had applied `?.`.
The second is the more interesting: `?.` tests the C# reference, which is still non-null once a
`UnityEngine.Object` is destroyed, so the member access runs and throws `MissingReferenceException`. The
`!= null` operator overload is the one that answers the question being asked.

**Notes:** Both sites now log first and notify second, behind `!= null`. Dormant here - the panel is
authored in `CoreSetup.unity` - and live for any branch that reparents or deletes it.

### `LoadRemoteSettings()` - the parse catch had no `LogException`

**Status:** Resolved
**Category:** Bug
**Original location:** Line 1326
**Resolved in:** v0.49.11

> the parse catch was edited to add `path` but still logs `ex.Message` only, with no
> `Development.LogException(ex)`. The cast throws `InvalidCastException` here as readily as a JSON error
> does, and "Unable to cast object of type X to Y" with no frames points nowhere.

**Context:** Found by `silent-failure-hunter`, which rated it the finding most likely to reach
production unnoticed - it lands on the only platform where this method compiles, and the app then boots
on defaults looking healthy. `CLAUDE.md` already carries the rule: `LogException` comes first in every
catch that exists to keep the app running.

**Notes:** I had edited that catch to add the url and did not notice it was missing the exception. Fixed.

### `ConfigManager.WriteAtomic` - two context strings that were wrong about their own callers

**Status:** Resolved
**Category:** Documentation
**Original location:** `Api.cs:626`, `AppDataController.cs:1361`
**Resolved in:** v0.49.11

> `Api.cs:626`: "re-fetched if missing" is true online and misses the cost - this cache is read only in
> the failure branch, i.e. it IS the offline fallback.

> `AppDataController.cs:1361`: "losing this discards the operator's settings" contradicts the rest of
> the line - `WriteAtomic` leaves the existing file in place, so what is lost is the unsaved change.

**Context:** First from `silent-failure-hunter` (as a dormant finding), second from `code-reviewer`.
Both are the same class of error: a context string added to make failures honest, which was then itself
inaccurate about the consequence. `FileManager`'s named the settings panel, which misattributes any
other caller of a `public` method - also reworded.

**Notes:** Worth recording that both exemplars M56's entry uses are dormant: `Api.isActive` ships
`false`, and `SaveAppSettings` is `internal` with zero callers repo-wide. The live callers are the two
in `ConfigManager` and the one in `FileManager`.

## `docs/architecture/known-issues.md` (citation regression, M106 round)

### The re-point pass moved 20 citations the wrong way

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.49.11

> every `TextureCacheManager.cs` citation outside the M43/M106-M109 entries was shifted the wrong
> direction ... The commit moved them down 15-16, restoring the pre-`52c3036` numbers and undoing that
> commit's re-point. 20 citations, 8 entries.

**Context:** Found by `doc-reviewer`. The cause is worth writing down, because the pass was
arithmetically correct: between the two commits I both *added* and *deleted* doc comments in that file,
and the register had already been re-pointed in between. **Two individually correct diff-maps do not
compose into a correct map when the thing being mapped was edited between them.**

**Notes:** Repaired from the reviewer's per-entry targets, each printed and eyeballed against the file
before applying, rather than by re-deriving a map. The lesson for the next pass: a diff-based re-point
is only safe against the commit the citations were last verified at, and "last verified" is not the same
as "last committed".

## `Assets/01_Scripts/EnigmaVirtual/MainController.cs` (L18 / L10 round)

### `WaitForExit()` unbounded on the boot thread

**Status:** Resolved
**Category:** Bug
**Original location:** Line 409
**Resolved in:** v0.49.12
**Register:** L21

> `WaitForExit()` with no timeout, on the boot thread. `Kill()` is asynchronous and a process in an
> uninterruptible kernel wait never dies, so this blocks `Init()` permanently - and `BootWatchdog_IE` is
> a coroutine, so a blocked main thread means no `BOOT STALLED` either. Frozen boot, empty log.

**Context:** Found by `code-reviewer`, inside the block the L10 fix had just restructured. Pre-existing,
and a worse failure shape than either defect the round set out to fix - the watchdog interaction is what
makes it so, and it is the kind of thing only visible when you hold both the loop and the watchdog's
implementation in mind at once.

**Notes:** Fixed in the same commit because it is one argument. Filed as **L21** rather than folded into
L10 so it is discoverable on its own.

### The `killExplorer` banner promised a restore a setting can disable

**Status:** Resolved
**Category:** Documentation
**Original location:** Lines 450-453
**Resolved in:** v0.49.12

> the banner asserts "OnDestroy restarts it, so a clean exit restores the shell" unconditionally, but
> that is gated by `restoreExplorerOnQuit` ... the one message an operator relies on promises a restore
> that never happens.

**Context:** Found by `code-reviewer`. The message was added *this round* to fix the absence of any
record; adding a message that is wrong in the one configuration where it matters most would have been a
worse outcome than the silence it replaced.

**Notes:** The sentence now interpolates the setting and states the actual consequence in both branches.

### The outer catch still abandoned the name loop

**Status:** Resolved
**Category:** Bug
**Original location:** Lines 436-441
**Resolved in:** v0.49.12

> the outer catch is now scoped to `GetProcessesByName`, but it still sits outside the *name* loop - so a
> `GetProcessesByName` failure on `killProcesses[0]` still abandons every remaining name, which is
> verbatim the abandonment the fix's own comment says was the point of the change.

**Context:** Found by `code-reviewer`. The fix moved a catch inward one level and left the same shape
above it - which is a pattern worth naming, because the comment I wrote asserting the defect was fixed
was true of the inner loop and false of the outer one.

**Notes:** Two nested trys now, each scoped to one mechanism, per `CLAUDE.md`'s rule about a catch's
message lying when its scope is too wide.

## `Assets/01_Scripts/EnigmaVirtual/Components/Video/VideoPlayerControllerBase.cs` (L18)

### The error message named a consequence that cannot happen

**Status:** Resolved
**Category:** Documentation
**Original location:** Lines 327-330
**Resolved in:** v0.49.12

> `VideoPlayer_errorReceived` calls `Stop()` *before* `DisplayError()`, and `Stop()` does
> `_targetTexture.Release()` when `resetTextureOnStop` - which defaults true - and `renderTexture` *is*
> `_targetTexture`. So the RT was just released: the operator sees a cleared/undefined surface, not
> "whatever frame it last held".

**Context:** Found by `code-reviewer`, by tracing the call order rather than reading `DisplayError` in
isolation. L18 exists to make this path diagnosable, and the message I wrote to do that described a
symptom the default configuration cannot produce.

**Notes:** Also fixed alongside: the guard tested `errorSprite != null` only, while the precedent it
cites (`DownloadableImage`) tests the sprite and its texture. A sprite whose texture reference broke on
import would have reached `Graphics.Blit` with a null source.

## `Assets/01_Scripts/External/Unity_SerialPort/UnitySerialPort.cs` (M101)

### Three stale premises, all in documentation I was working from

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.49.13

> `SerialPorts.isActive` ships `false`, not `true` ... one port on **COM7**, not COM3. Commit `8b25ff3`
> (v0.15.0) flipped it; H20's "live in the shipped template" has been stale since.

> There is no periodic health check. `SerialPortsManager.cs:112` is the only reader of `IsRunning`
> anywhere outside this file, and it runs once inside `InitSerialPorts` at boot.

> `LoopMethods.Threading` is unreachable via the shipped path - `LoopMethod` is never assigned in any
> file, so the C# initializer `Coroutine` always wins.

**Context:** All three from `silent-failure-hunter`, all three verified directly afterwards. The second
is the significant one: **M101's own entry described the cost as "a permanent false OK for
SerialPortsManager's H20 health check"**, and no such check exists. The real cost was silence, not a
false signal. I had also repeated H20's `isActive: true` / COM3 claim to the reviewer and written it into
a breaking-change row before checking it.

**Notes:** H20's entry, M101's entry, the §5b row and the session log are corrected.
`docs/sessions/2026-08-10.md` carries the same stale claim and is left as written - a session log records
what was believed at the time.

### The quiet return left M101 unfixed for the failure it was written for

**Status:** Resolved
**Category:** Bug
**Original location:** Lines 958-966, first version
**Resolved in:** v0.49.13

> the quiet-return branch does **not** clear `isRunning`, and its comment's premise only holds for a
> *deliberate* stop. If a removed USB adapter's `SerialStream` disposes itself (`IsOpen == false`), the
> loop returns quietly, then spins forever on `if (SerialPort.IsOpen)` with no exception, no count and no
> log, `IsRunning` still **true** - M101 is unfixed for exactly the failure it was written for, while the
> register says fixed.

**Context:** The worst finding of the round. My fix inferred "deliberate stop" from `IsOpen == false`, and
a vanished adapter produces exactly that state without an exception - so the two cases were
indistinguishable and I had picked the wrong default for the ambiguity.

**Notes:** Intent now comes from an explicit `stopRequested` flag set by both stop methods, as the
reviewer proposed. **And my probe had asserted the buggy behaviour as correct** - its assertion 4 read "a
null SerialPort does not clear isRunning - a closed port is a stop, not a fault", and passed. Worth naming
as a pattern: when a fix turns on distinguishing two cases, a counter-test that exercises only one of them
agrees with whichever behaviour exists. Third occurrence today.

### `ParseSerialData` inside the read's try

**Status:** Resolved
**Category:** Bug
**Original location:** Line 941
**Resolved in:** v0.49.13

> `ParseSerialData` -> `SerialDataParseEvent` -> App subscribers all run inside the read's `try`, so an
> App-side throw increments `consecutiveReadFailures` and is reported as `Serial read failed on COM7` -
> pointing an operator at the cable for someone else's bug - at **Error, once per incoming message,
> forever** ... It can also never trip the limit, because the reset precedes the parse.

**Context:** Found by `silent-failure-hunter`. This is `CLAUDE.md`'s "scope a catch to one mechanism, or
the message will lie", and I had widened the wrong side of it while narrowing another.

**Notes:** Hoisted out, with its own catch naming it a consumer fault. The read's try wraps the read only.

### `CloseSerialPort` stopped the loop after its catch

**Status:** Resolved
**Category:** Bug
**Original location:** Lines 716-725
**Resolved in:** v0.49.13

> `CloseSerialPort`'s catch calls `Core.notificationManager.ShowNotification` **unguarded** ... and
> `StopSerialCoroutine`/`StopSerialThreading` sit *after* the catch - so a `Close()` that throws while the
> port is still open (what a removed adapter does) escapes into `OnDestroy` and the loop is never stopped.

**Context:** Found by `silent-failure-hunter`. It defeated the `OnDestroy` change in precisely the case it
was added for - the same class of error as the finding above: a fix whose own failure path was the path it
existed to handle.

**Notes:** The loop is stopped before the close, the close is null-guarded, and that catch's
`notificationManager` is guarded like its sibling.

### The limit counted iterations, not time

**Status:** Resolved
**Category:** Design
**Original location:** Line 172
**Resolved in:** v0.49.13

> the limit is 5 *iterations*, not a time window, and there is no delay between them - microseconds in
> Threading mode, ~5 frames (~83 ms at 60fps) in Coroutine mode. A USB re-enumeration takes hundreds of
> ms, so the documented tolerance is not achieved.

**Context:** Found by `silent-failure-hunter`. The comment I wrote asserting the tolerance is what made
this checkable, and it was wrong by an order of magnitude.

**Notes:** Giving up now needs 5 consecutive failures **and** 2 seconds since the first. Both numbers
remain reasoned rather than measured, and the code says so.

### Findings recorded but deliberately not fixed

**Status:** Won't fix
**Category:** Code Quality
**Original location:** `UnitySerialPort.cs:987`, `:1127`, `:817`

> `portStatus` has no consumer - `PortStatus` is read nowhere in the repo and `StatusMsgBox` is null on
> manager-created ports. Dead write, not a report.

> after the stop the port is left non-null and open, so `CanWrite` still returns true - writes keep
> reporting success on a port nothing reads.

> `KeepAliveLoop`'s `while (isRunning)` exits at the next interval with no log.

**Context:** All three from `silent-failure-hunter`, all three real.

**Notes:** Left out deliberately: each is a further behaviour change in shared `External/` code, the whole
subsystem is dormant behind `SerialPorts.isActive: false`, and this round has already needed two versions
- bundling more in is how the first one acquired its own defects. Worth their own entry when the serial
subsystem is next touched, particularly `CanWrite`, which would actively mislead an operator rather than
merely doing nothing.

## `Assets/01_Scripts/EnigmaVirtual/Data/AssetManager.cs` (M73)

### The fold-in was on the success path; three `yield break` exits skipped it

**Status:** Resolved
**Category:** Bug
**Original location:** Line 1395, first version
**Resolved in:** v0.49.14

> all three `yield break` exits do `chunkValidated++` and bail **before** the fold-in, so that chunk's
> tallies never reach `MissingAssetCount` ... `downloadThreads: 1` ships, so there is exactly **one**
> chunk - on the dead-host abort (the classic site failure, and the case with the most missing media)
> `MissingAssetCount` stays **0** and the label hides exactly as before the fix. **M73 is unfixed on the
> path it was written for.**

**Context:** Found by `silent-failure-hunter`. The same shape as M101's first version earlier the same
day: the fix sat on the success path, and the failure paths were what the entry was about.

**Notes:** Moved into a `finally` around the whole loop, as the reviewer proposed - which covers every
exit at once including any added later. The deliberately-uncounted abandoned tail is unaffected.

### Nothing ever cleared the readout

**Status:** Resolved
**Category:** Bug
**Original location:** `CoreSystemUI.cs:377`
**Resolved in:** v0.49.14

> nothing ever deactivates the label again, so the remarks ("survives to the end of boot") understate it
> ... `N ASSET(S) MISSING - SEE LOG` stays painted over the client's content for the whole session, and
> it is stale from the moment the app starts.

**Context:** Found by `silent-failure-hunter`, which traced `Hide(true)` to an alpha-fade of `Background`
- a *sibling* of the label, both under `DontDestroyOnLoad(Core)` on a sorting-order-100 canvas. My own
remark asserted a bound that did not exist.

**Notes:** `Hide()` clears it now, so the report is visible during boot and gone afterwards. The
`LogError` remains the durable record.

### The reset could not be reached by the empty-media path

**Status:** Resolved
**Category:** Bug
**Original location:** `AssetManager.cs:342-343`
**Resolved in:** v0.49.14

> the reset lives only in `OnAssetsSorted_IE`, so the empty-media early return reaches
> `RaiseCleaningComplete` without resetting - a second run whose walk finds no media reports run 1's
> counts.

**Context:** Reported as dormant (nothing calls it twice today) and live for any branch that re-runs on
an appData refresh.

**Notes:** Moved above the early return. The stale-chunk race the reviewer also noted - a run-1 chunk
still alive folding into run 2 after its reset - is pre-existing for `validatedAssets` too and is
recorded rather than fixed.

### Cleared by the same review

**Status:** Resolved
**Category:** Design

> `refusedAssets` `continue`s before any `missingAssets++` for that asset, so the two counts are disjoint
> and `missing + refused` is a legitimate sum. No double-count risk.

> `assetManager` is serialized to the single scene instance, which is the same object
> `GetComponentInChildren<AssetManager>()` runs - so the counts are read off the manager that did the
> work.

**Context:** Both were questions I asked rather than findings, and both came back clean. Recorded because
"we checked and it was fine" is worth as much as a finding when the next person wonders.

## `docs/architecture/known-issues.md` - L17 (first slice)

### The review did not run

**Status:** Open
**Category:** Other
**Original location:** n/a

> Agent terminated early due to an API error: You've hit your session limit

**Context:** The architecture review of L17's first slice was launched with five specific questions -
whether deletion beat namespacing for a dead `public` type under `CLAUDE.md`'s rule, what `Colors`
actually is, a parser-based measurement of the global-type counts, whether `Gema` or `EnigmaVirtual` was
the right target, and how loudly the change breaks a branch. It returned only "Compiles clean. Now the
counts - measured from compiled metadata, not grep." before hitting the limit.

**Notes:** Recorded rather than quietly skipped, because every other fix on 2026-08-17 had a reviewer
pass and this one's register entry would otherwise read as though it had too. The two questions still
owed are the type counts and the deletion-vs-deprecation judgement; the namespace question was resolved
by the operator instead, which was the better answer anyway.

### Found without a reviewer: a duplicate class hidden by the namespace split

**Status:** Resolved
**Category:** Code Quality
**Original location:** `Core/Utils/DetectLongClick.cs`
**Resolved in:** v0.49.15

**Context:** Not a review finding - the compiler's. Moving `Gema.DetectLongClick` into `EnigmaVirtual`
produced `CS0101`, revealing a second class of the same name: 110 lines under `Core/Display/ui/utils/`
in `EnigmaVirtual` with one scene reference, and 48 lines under `Core/Utils/` in `Gema` with none.

**Notes:** The dead copy is deleted. Worth keeping in this archive because it is the strongest available
argument for L17 as an entry: while the two sat in different namespaces, nothing in the codebase could
report them as duplicates, and no amount of reading would have surfaced it. Retiring a namespace is how
you find out.

## `Assets/01_Scripts/EnigmaVirtual/Core/Models/` and `Assets/Editor/PositionerEditor.cs` (L22)

### A diverged fork of the `AssetGenerator` tool, four files, unreachable

**Status:** Resolved
**Category:** Code Quality
**Original location:** `Core/Display/ui/utils/Positioner.cs`, `Core/Models/vo/AssetsVO.cs`,
`Core/Models/Assets.cs`, `Assets/Editor/PositionerEditor.cs`
**Resolved in:** v0.49.17

> the framework cluster is a fork of the AssetGenerator tool ... the two `Positioner`s differ in one
> thing only: the framework copy reads `assetData.DesignSize`, the AssetGenerator's has a serialized
> `designSize` field. Both editors carry `[CustomEditor(typeof(Positioner))]` in the same assembly and
> don't collide because they target *different* `Positioner` types.

**Context:** Not an agent finding - it came out of the operator asking whether `PositionerEditor` was
used and could be removed. The cluster was closed (every reference among the four pointed at another of
the four), had 0 scene/prefab GUID references, no runtime entry point, and no `assets.json` to read.

**Notes:** Deleted on the operator's decision. The residual risk is a branch scene binding
`EnigmaVirtual.Positioner` by GUID, which is silent - recorded in `overview.md` §5b. Worth keeping in
this archive as the second duplicate that L17's namespace retirement exposed: while two same-named types
sit in different namespaces, nothing in the codebase can report them as duplicates.

### My own claim that `AssetsVO` had an outside consumer was wrong

**Status:** Resolved
**Category:** Documentation
**Original location:** `Assets/ThirdPart/AssetGenerator/Editor/AssetList.cs:120`
**Resolved in:** v0.49.17 (corrected before the deletion, not after)

> `AssetsVO` — 10 refs, and one **escapes the cluster**: `AssetList.cs:120`,
> `foreach (AssetsVO layer in item.layers)`

**Context:** It does not escape. `AssetList.cs` declares `using UIGenerator;` and there is a
`UIGenerator.AssetsVO`, so it resolves the tool's own type. I had matched on the type *name* across two
namespaces that each declare it.

**Notes:** The check that separates them is grepping for the **declarations** (`class AssetsVO`) before
trusting any reference count - one command, and it is what turned a wrong answer into the right one
before anything was deleted. Same trap as the two `PositionerEditor`s and the two `DetectLongClick`s.

### The Unity console is historical and does not clear

**Status:** Resolved
**Category:** Other
**Original location:** `CLAUDE.md`, the pipeline section
**Resolved in:** v0.49.17

> Identical output after a clear - 69 errors, same counts.

**Context:** `CLAUDE.md` had just been given the rule "ask the pipeline's `console` command, which sees
all of them" after the v0.49.15 breakage. It does see all four assemblies, but it returns a persistent
log: the 28 `CS0246`s from that breakage were still present, at full count, after the file had been
deleted and every assembly rebuilt clean. `LogEntries.Clear()` does not affect it.

**Notes:** Entries carry `timestampUtc`. The reliable form is to compare the newest error against the
mtime of `Library/ScriptAssemblies/*.dll` - here 0 errors after the build, where the raw count of 69 read
as a failure and was not one. `CLAUDE.md` now says so.

## `Assets/ThirdPart/AssetGenerator/Editor/AssetList.cs` (H23)

### `Init` destroyed `UIGeneratorData.asset` on every open

**Status:** Resolved
**Category:** Bug
**Original location:** Line 34
**Resolved in:** v0.49.17

> `AssetList.cs:34` has exactly the M28 shape: `@"Assets\Resources\UIGeneratorData.asset"` - a
> backslash `AssetDatabase` path. And it has the H19 shape too: `LoadAssetAtPath` with a backslash path
> returns null, so the `if (_data == null)` fallback calls `CreateAsset`, overwriting
> `Assets/Resources/UIGeneratorData.asset` every time the window opens.

**Context:** Found while deleting L22's fork of this tool, by checking whether the surviving copy carried
the defects the deleted copy had received. `Assets/Resources/UIGeneratorData.asset` exists in this repo,
so the defect was live; the only symptom is an asset that has quietly reverted to empty.

**Notes:** One path separator, plus a comment explaining that a backslash here is silent rather than
fatal. The durable lesson is scope: M28 swept four editor files in v0.6.0 and this fifth one sat under
`Assets/ThirdPart/`, outside it. Also worth copying as a habit - **when you delete one of two forks, read
the survivor for the fixes the deleted one received.**

### A counter-test I wrote was itself destructive

**Status:** Resolved
**Category:** Documentation
**Original location:** `docs/sessions/2026-08-17.md`, L22 test plan step 5
**Resolved in:** v0.49.17

> **Counter-test, and it is the one that catches over-deletion:** in the editor, open
> *Window ▸ UIGenerator*.

**Context:** Written before reading `AssetList.Init`. Executing it would have destroyed the asset the
step existed to prove was intact.

**Notes:** The step now requires backing up `UIGeneratorData.asset` first, and confirming it survives is
H23's own test. Distinct from the three counter-test failures earlier in this session, which asserted the
wrong observable - this one had a **side effect**. Before writing a step that opens an editor window,
read what its `[MenuItem]` body does.

## `Assets/01_Scripts/EnigmaVirtual/Core/Core.cs` and `Core/Managers/FileManager.cs` (M45, M111)

### The empty return meant two things, and one of them hung the boot silently

**Status:** Resolved
**Category:** Bug
**Original location:** `Core.cs:266`, `FileManager.cs:303` (first version of the fix)
**Resolved in:** v0.49.18

> a `settings.json` that exists and is 0 bytes (or BOM-only ...) - `File.Exists` passes, `ReadAllText`
> doesn't throw, so neither `LogError` in `LoadStreamingFile` fires; `Awake` takes `IsNullOrEmpty` →
> `return`, waiting for an `OnLoadJson` that can never fire. Silence for 90s, then `BOOT STALLED … the
> cause is logged above this line` - and nothing is above it.

**Context:** Found independently by all three reviewers - `code-reviewer` graded it HIGH and returned
**BLOCK**, `silent-failure-hunter` isolated the zero-byte sub-case as the one with no log at all, and
`architect-reviewer` reached the same conclusion from the contract side. It was a regression introduced
by M45's first version, not a pre-existing defect.

**Notes:** Fixed by distinguishing the states rather than adding a check: `LoadStreamingFile` returns
`null` only when it started a fetch. `code-reviewer` and I arrived at the same `null` shape
independently; `architect-reviewer` confirmed it needed no new type. The general lesson is that
overloading a sentinel is only safe when the *other* producers of that value are also checked - here
three of the four paths that returned `""` had nothing to do with the async case.

### `FromJson` on an empty string returns null for a class and throws for a struct

**Status:** Resolved
**Category:** Bug
**Original location:** `Core.cs:274`
**Resolved in:** v0.49.18

> `JsonUtility.FromJson<CoreSettings>("")` returns **NULL and does not throw** (`"   "` throws
> `ArgumentException`). That measurement decides findings 1 and 3.

**Context:** `code-reviewer` measured this in the live editor. I had measured the same call with
`Vector3` - a struct - got a `NullReferenceException`, and built the empty-string handling on the
conclusion that the existing catch would report it. It would not have.

**Notes:** The empty payload was reaching the `settings.System` dereference on the next line, and in
`OnJsonLoaded` that dereference sat **outside** the `try`, so the exception escaped a coroutine.
`ApplySettingsJson` now checks empty first and null-after-parse second. Recorded because the mistake
was not "I did not measure" but "I measured the wrong type", which looks identical from the outside.

### The settings parse existed three times, and the fix added a guard to one of them

**Status:** Resolved
**Category:** Architecture
**Original location:** `Core.cs:231-241`, `Core.cs:274-284`, `Core.cs:660-669`
**Resolved in:** v0.49.18

> they **already disagree**: 284 null-checks `notificationManager`, 664 dereferences
> `Core.notificationManager` unguarded; 284 passes `ex.Message`, 664 `ex.ToString()`. This change added
> the guard to one of three copies (231-241 is the third, also unguarded). That is the M44 pattern -
> mixed guarding in one class, where the next reader copies the wrong half.

**Context:** `architect-reviewer`. My patch consolidated two branches into one and null-guarded that
one, which left the class *less* consistent than before rather than more.

**Notes:** Extracted `ApplySettingsJson(json, source)` and `ReportSettingsFailure(source, reason)`,
called from all three sites. Logged as **M111**. Not an abstraction added on speculation - three
existing copies that had already drifted, replaced by one.

### `OnJsonLoaded` completed the boot for any filename, and M45 made that reachable

**Status:** Resolved
**Category:** Bug
**Original location:** `Core.cs:668-674`
**Resolved in:** v0.49.18

> `OnJsonLoaded` calls `CoreSetupComplete()` unconditionally after a switch with only
> `case "settings.json"`, and `Core.cs:254` now subscribes on EVERY platform ... `LoadJsonWeb` is
> public: a branched project fetching any other json now completes boot early on desktop too, where
> before there was no subscriber.

**Context:** Raised by `code-reviewer` and `architect-reviewer` independently. Dormant in this repo -
`LoadJsonWeb` has no other caller - and live for a branch.

**Notes:** A `default:` case that logs and returns. Folded into **M111**. Worth keeping because it is a
defect the *fix* created out of a latent one: widening a subscription turns "no caller reaches this" into
"any caller reaches this", and the switch had no default to catch it.

### Cleared by the same review

**Status:** Resolved
**Category:** Design

> **Double `CoreSetupComplete()`**: no, not on any in-repo path. The `return` at Core.cs:267 is the only
> thing preventing it and it is correctly placed ... inline-parse and event are mutually exclusive.

> **The two new `return`s**: nothing is skipped. `CoreSetupComplete()` ... is the last statement in the
> `_instance == null` block; the only code after it is the duplicate-destroy `else`.

> `notificationManager != null` is the right test on a Unity object reference (not `?.`), and
> `Development.LogError` ... sits outside the guard, so the log survives a missing NotificationManager.

**Context:** All three were questions I asked rather than findings, and all came back clean. The
watchdog question also cleared: `silent-failure-hunter` confirmed `BootWatchdog_IE` starts above every
new early return and reports the right stage.

**Notes:** Recorded because "we checked and it was fine" is worth as much as a finding when the next
person wonders whether the early returns skip anything.

## `Assets/01_Scripts/EnigmaVirtual/Data/AppDataController.cs` (M86)

### The entry's prescribed same-file test would not have been enough

**Status:** Resolved
**Category:** Bug
**Original location:** `LoadRemoteData`, the appData cache write
**Resolved in:** v0.49.19

> **Shape 2 (no cache)**: destination is `PersistentConfig[/downloadFolder]/appData.json`, source is
> `jar:file://…` - different files, same-file test passes, write proceeds. Confirmed insufficient.

**Context:** `silent-failure-hunter`, asked directly to verify or refute my reasoning for departing from
the register's prescription. It confirmed the departure and corrected the mechanism.

**Notes:** Fixed by provenance - a required `bool isLocalFallback` - rather than by comparing paths.

### My comment named the wrong mechanism

**Status:** Resolved
**Category:** Documentation
**Original location:** the gate comment, first version
**Resolved in:** v0.49.19

> in shape 2 the write does *not* "land on ConfigManager's seeded copy and invalidate the stamp" - if a
> seeded copy existed at the cache path, `File.Exists(cached)` would be true and you'd be in shape 1 ...
> the write **creates** it, and then `ConfigManager.Apply` sees `File.Exists(deployed)` ... so
> `StampSeededAppData` **never runs at all**. The stamp isn't invalidated; it never exists.

**Context:** `silent-failure-hunter`. My comment was internally plausible and wrong in a way that would
have sent the next person to fix `ConfigManager.Apply` instead of the write.

**Notes:** Comment rewritten. The conclusion (suppress by provenance) was unaffected, which is why this
is worth recording separately - a right fix can carry a wrong explanation, and the explanation is what
survives into the next change.

### The optional default was the direction that writes

**Status:** Resolved
**Category:** Design
**Original location:** `LoadRemoteData(string path, bool isLocalFallback = false)`
**Resolved in:** v0.49.19

> `isLocalFallback` defaults to false -> a third call site that omits it silently re-arms the write, no
> compile error; private method, 2 call sites, so the default buys nothing

**Context:** `silent-failure-hunter` as a finding, `code-reviewer` as a LOW - independently.

**Notes:** Made required; the remote site passes `false` explicitly. Two call sites, both in this file,
and the method is `private` and non-virtual, so this is not a breaking change for a branch.

### M86's stated symptom was not reachable

**Status:** Resolved
**Category:** Documentation
**Original location:** `known-issues.md`, M86
**Resolved in:** v0.49.19 (entry corrected; the gap logged as M112)

> **the M86 entry's stated symptom is wrong**: there is no "age readout says just now forever" on that
> route, because there is no age readout.

**Context:** Both reviewers, independently. The Android branch returns before the marking block, so
`fromCache`/`cacheAge` are never set on that route at all.

**Notes:** M86's entry now carries the correction, and the missing marking is **M112** - it had been
documented only inside the *fixed* M83 entry and in a log string. `silent-failure-hunter` pointed out
that this is precisely the situation M86 was created to avoid, which is the argument for giving it an id.

### Found in passing, left open on the reviewer's advice

**Status:** Open
**Category:** Bug

> `AppDataController.cs:340`: the appData request has NO timeout - the M43/M106 sweep missed this site.
> A half-open CMS socket never returns ... boot hangs until `BootWatchdog_IE`.

> `www` is never disposed on any path - the exact defect M110 fixed yesterday in `LoadRemoteSettings` one
> method over.

**Context:** `code-reviewer`, which graded both MEDIUM and returned **APPROVE WITH SUGGESTIONS**,
recommending they go in a separate commit with their own register entries rather than widening an M86
fix.

**Notes:** Logged as **M113** and **M114**, both open. M113 needs a decision rather than just a line -
whether an appData fetch is governed by `Api.timeout` when `Api.isActive` ships false and
`AppData.isActive` ships true. Two stale XML claims in the same file were corrected in v0.49.19: one said
this request used `Api.timeout` (it uses none), and one said `failedRemoteJson` and `fromCache` are
"true together" on a failed remote fetch, which holds on the desktop route only.

### Cleared by the same review

**Status:** Resolved
**Category:** Design

> The cache write is `LoadRemoteData`'s only side effect keyed on the payload being a CMS response.
> There is no retry, no `TryDeletePartialFile`, no counter, no HTTP-result-keyed event in this method.

> **Boot completes — confirmed.** ... The write block ends at `:462` and the coroutine ends at `:463` —
> nothing follows. Your change only narrows an `if` condition; it adds no `yield`, no `yield break`, and
> no early return.

> **No path inconsistency introduced — the reverse.** The gate removes the only
> `TryGetCachedDataJsonPath` call on the local route, so the resolver is now consulted only where its
> result is used.

**Context:** All three were questions I asked. `silent-failure-hunter` separately confirmed that
`ConfigManager` still seeds the no-cache case, so suppressing the write does not leave Android with no
cache file ever - it removes a writer that could **pre-empt** the seed.

**Notes:** Recorded because the boot-chain question is the one that would have made this unsafe, and it
came back clean from a reviewer that had the whole method in front of it.

## `Assets/01_Scripts/EnigmaVirtual/Core/Core.cs` and `Data/AppDataController.cs` (M113)

### The stall budget was 5s short whenever the appData timeout dominated

**Status:** Resolved
**Category:** Bug
**Original location:** `Core.StallBudgetSeconds`, first version
**Resolved in:** v0.49.20

> The budget is `Mathf.Max(90, widest · 2)`. That is short by exactly 5s whenever
> `widest == AppData.timeout` and `2·T >= 90` - e.g. `AppData.timeout: 60` with downloads off gives a
> 120s budget against a 125s worst case. Shipped config is safe (120 vs 65) because `AssetDownload`'s 60
> dominates. **add it explicitly.**

**Context:** `architect-reviewer`, which also confirmed the second request exists and that
`failedRemoteJson` caps the stage at two, so `2T + 5` is exact rather than optimistic. `code-reviewer`
independently agreed the term is the right call.

**Notes:** One `StageBudgetFor` helper now owns the formula, so the two return paths cannot drift. The
knock-on is that the shipped budget moves 120s -> 125s, which made four prose statements stale -
including a remark edited in the same round.

### `widest` was assigned rather than maxed, which would have discarded the download timeout

**Status:** Resolved
**Category:** Bug
**Original location:** `Core.StallBudgetSeconds`, the `AssetDownload` branch
**Resolved in:** v0.49.20

> The `Mathf.Max` on the `AssetDownload` branch is the bug you say it is, and it would have been live on
> the shipped config.

**Context:** Found while writing the fold-in rather than by review; `code-reviewer` confirmed `:363` is
the only plain assignment and that nothing after `:388` touches `widest`.

**Notes:** Worth recording because pre-seeding an accumulator is exactly where this class of bug lives,
and the shipped config was the case that would have broken.

### Four places quoted the old budget, one of them edited in the same round

**Status:** Resolved
**Category:** Documentation
**Original location:** `AppDataController.cs` remarks, `CoreSystemUI.cs`, `TextureCacheManager.cs`,
`overview.md` §5b (v0.49.10 row)
**Resolved in:** v0.49.20

> the remark you edited in this diff still says "twice `AppData.Cache.AssetDownload.timeout` when that
> exceeds 45s - so 120s as shipped" ... the floor is exceeded above 42.5s, not 45s

**Context:** `code-reviewer`. The `AppDataController` remark was one I had rewritten in v0.49.19, so it
went stale within a single day.

**Notes:** All four corrected; the §5b row for v0.49.10 keeps its history and gains a forward pointer, the
same pattern the v0.49.15 row uses. The general form: a **derived** number quoted in prose is stale the
moment the derivation changes, and grep is the only thing that finds every copy.

### The fold-in over-estimates for one configuration

**Status:** Resolved
**Category:** Design
**Original location:** `Core.StallBudgetSeconds`
**Resolved in:** v0.49.20 (comment, not code)

> the fold-in widens the budget from `AppData.timeout` even with `useLocalAppData:true` on desktop, where
> `LoadLocalData` does a synchronous `File.ReadAllText` and no request is ever in flight

**Context:** `code-reviewer`, graded LOW and only observable above a 42.5s timeout.

**Notes:** Deliberately **not** gated. On Android and WebGL that same configuration still routes through
`UnityWebRequest`, so a `!useLocalAppData` gate would need a platform fork to shave time off a watchdog
in the *safe* direction - and per M107 a budget too narrow latches and refuses to boot while one too wide
only reports late. The comment now says so instead of the code pretending otherwise.

### Cleared by both reviews

**Status:** Resolved
**Category:** Design

> `AppDataSettings.cs:71` is byte-for-byte the shape of `ApiSettings.cs:40` ... so it belongs on
> `AppDataSettings`, not inside `Cache`. `int` vs the Cache blocks' `uint` is a non-issue

> Two meanings for `0` under one tree is a documented policy split, not new debt - and this key lands on
> the *self-announcing* side ... An operator who guesses wrong gets told.

> **Dereference safe.** `_isSet` is only set true ... after `CoreSetupComplete` has already dereferenced
> `settings.System`, so `IsSet` implies `settings != null` regardless of whether the `LogIf` was stripped.

> No conversion trap. `int -> long` is implicit and widening; `EffectiveTimeout` clamps in `long` and
> narrows after, so the `(int)uint.MaxValue == -1` hazard is structurally absent.

**Context:** The placement, the two-meanings-of-zero question, the null-safety of the new dereference and
the int/uint question were all things I asked rather than findings.

**Notes:** Recorded because "we checked and it was fine" is worth as much as a finding - particularly the
`IsSet` implies `settings != null` chain, which is the reason the new dereference needs no guard and is
not obvious from the call site.

## `Assets/01_Scripts/EnigmaVirtual/Data/AppDataController.cs` (M112, M114, M115)

### An operator-facing log line stated the opposite of what the commit does

**Status:** Resolved
**Category:** Documentation
**Original location:** `FailRemoteAppData`
**Resolved in:** v0.49.21

> `FailRemoteAppData`'s operator-facing LogError still says "On Windows the load reports the cache's age;
> on Android and WebGL it does not, because that path routes through LoadRemoteData and never reaches the
> report." That is M112, which this commit fixes. A string literal reaches an operator, so this is a
> behaviour claim, not a comment.

**Context:** `code-reviewer`, graded MEDIUM and named top priority. The line was written when the gap was
real and became false in the same commit that closed it.

**Notes:** Rewritten to say the following line reports what was served and how old it is, on every
target. The general rule this earns: **when a fix changes what a platform does, grep the file for that
platform's name before committing** - this is the second time in three commits that closing a defect
falsified prose inside the same file.

### The XML evidence for a still-correct conclusion had gone false

**Status:** Resolved
**Category:** Documentation
**Original location:** `failedRemoteJson` remarks
**Resolved in:** v0.49.21

> `failedRemoteJson`'s XML doc uses the Android/WebGL marking gap as the load-bearing evidence for "they
> diverge in both directions" ... Both false now. The conclusion survives on the `useLocalAppData:true`
> direction alone; the true-while-fromCache-false direction also survives but for a different reason - a
> `www` that fails outright never reaches the marking.

**Context:** `code-reviewer`. Worth keeping separately from the finding above because it is the subtler
shape: the *claim* was still true, only its stated reason had stopped being.

**Notes:** Evidence re-pointed at the outright-failure case; the conclusion and the design decision it
supports are unchanged.

### The extraction, checked mechanically rather than by eye

**Status:** Resolved
**Category:** Code Quality
**Resolved in:** v0.49.21

> Compared `git show HEAD` against the working tree with comments stripped and whitespace normalised: the
> block ... is **identical** after only `cacheWrittenUtc`→`writtenUtc` and `{filePath}`→`{localPath}`.

**Context:** `code-reviewer`, answering the question I asked - I wanted exactness over elegance, because
this class had produced three copy-paste divergences in two days.

**Notes:** It also cleared the one ordering change (the stat now runs after the JSON parse) as benign,
since nothing rewrites the file in between. Recorded because "I checked the extraction by eye" is exactly
what produced the divergences this was guarding against, and there is a cheap mechanical alternative.

### The `using` declaration disposes on every exit, including abandonment

**Status:** Resolved
**Category:** Unity-specific
**Resolved in:** v0.49.21

> Lowered to `try`/`finally` spanning to the end of the enclosing block - identical IL to a block ... In
> an iterator the `finally` is emitted into the state machine, so it runs *inside `MoveNext`* on
> `yield break` and on fall-through, plus from `Dispose()` if abandoned mid-iteration. The disposal does
> not depend on Unity calling `Dispose`, which is the stronger guarantee.

**Context:** `code-reviewer`, answering whether a using-declaration is safe in a coroutine. It is, and it
avoided re-indenting 130 lines.

**Notes:** The M95 caveat still holds and is still noted in the code: a coroutine Unity *stops* abandons
the iterator without disposing it. Zero such call sites in this file.

### Cleared: the future-dated file, and both call sites

**Status:** Resolved
**Category:** Design

> **Leave the future-dated case alone - it is already handled where it shows.** `StringUtil.FormatElapsed`
> returns "an unreadable amount of time (the timestamp is in the future)" for a negative span ... there
> are zero such consumers. If you clamp it, `Api.cs` has the byte-identical shape and would have to be
> clamped in the same commit.

> Both call sites correct. `false, false` - the second argument is unread under `!isLocalFallback` ...
> `true, isPlaceholder` - the same value the desktop branch passes, so the two routes are symmetric.

**Context:** Questions I asked. The future-date one I had expected to be told to fix.

**Notes:** Recorded because the reasoning for *not* fixing it is the useful part - a guard whose only
symptom is unreachable, whose twin would have to change in the same commit, is scope the entry should
carry rather than the diff.
### The new line cried wolf in local-appData mode

**Status:** Resolved
**Category:** Design
**Original location:** `MarkServedFromLocalFile`
**Resolved in:** v0.49.21

> in `useLocalAppData: true` mode `LoadLocalData` is the **primary** source, yet the extraction now fires
> "because the remote copy was not used… If the CMS is reachable this is a configuration problem" and
> sets `fromCache = true` on intended content - on Android/WebGL too, where it previously never ran ...
> The risk is credibility: this is the one `LogError` M112 exists to make actionable, and in local mode
> it cries wolf every boot.

**Context:** `silent-failure-hunter`, reported as dormant (`useLocalAppData` ships `false`) and as
pre-existing on desktop, widened by this diff to two more targets.

**Notes:** Local mode now gets the same facts at `Log` level without the fallback framing. Kept because
the finding is the *inverse* of the defect being fixed - M112 was about a missing report, and its fix
introduced a misleading one. `fromCache = true` is deliberately unchanged there: the XML on
`failedRemoteJson` documents that pairing as the intended divergence.

### `File.GetLastWriteTimeUtc` returns 1601-01-01, not `default`, for a missing file

**Status:** Resolved
**Category:** Bug
**Original location:** `MarkServedFromLocalFile`
**Resolved in:** v0.49.21

> `File.GetLastWriteTimeUtc` on a **missing** file does not throw - it returns `1601-01-01`, which is
> **not** `default(DateTime)` - so the `== default` → "unknown age" guard cannot catch that case and the
> line would read "155000 day(s) ago". Not reachable today ... but this is now a shared helper and it is
> M42's exact trap.

**Context:** `silent-failure-hunter`, dormant. Three independent things make it unreachable today: the
scheme test, `File.Exists` on the desktop route, and request success proving existence on the Android one.

**Notes:** Guarded with `Year < 1900` anyway. The reasoning worth keeping is the one about shared helpers:
an unreachable case in an inlined block becomes reachable the moment the block gains a second caller, and
this one had just gained its second caller.

### M115's stated rationale was wrong on two counts

**Status:** Resolved
**Category:** Documentation
**Original location:** the `LocalReadTimeoutSeconds` call site
**Resolved in:** v0.49.21

> comment claims "this method is compiled only for Android and WebGL" → false; `LoadRemoteSettings` has
> no `#if` ... Only the call site at `:1339` is gated.

> M115's const bounds a **real network fetch** on WebGL, not a package read → `UsesDeployedConfig` is
> **false** on WebGL, so `AppPaths.Config` returns `StreamingConfigFile`, and WebGL StreamingAssets is
> served over HTTP by the web server. 10s with no retry and no operator knob.

**Context:** `silent-failure-hunter`. Both claims were mine, written in this batch, and both were the
justification for choosing a `const` over a settings key.

**Notes:** Corrected in place. The `const` is still right for Android, and the WebGL case is recorded as a
number to revisit rather than a settled one - dormant while WebGL has never been built. A rationale that
sounds obvious is exactly the kind that goes unchecked; both of these did.

### Cleared: no silent failure introduced, and the one check that would have voided M112

**Status:** Resolved
**Category:** Design

> I traced every route and could not find one.

> Also checked the one that *would* have made M112 a total no-op on both routes: `APIResponseModel` is a
> **class**, so mutating `response` inside a method propagates.

> `StringUtil.cs:56` - `if (age < TimeSpan.Zero) return "an unreadable amount of time (the timestamp is
> in the future)"`. **Your negative-`TimeSpan` case is handled explicitly.**

**Context:** `silent-failure-hunter` also reproduced the compile proof independently, asserting both
directions on the dlls - the desktop-only literal absent under `UNITY_ANDROID` while the two new literals
are present.

**Notes:** The class-versus-struct check is the one I would not have thought to make, and it is the
difference between this fix working and doing nothing at all on either route.

## `Assets/01_Scripts/EnigmaVirtual/Core/Models/AppDataSettings.cs` (M46)

### The fix broke the consumer that discriminates on its result

**Status:** Resolved
**Category:** Bug
**Original location:** `AppDataController.LoadLocalData`
**Resolved in:** v0.49.22

> the new middle candidate is not in `LoadLocalData`'s placeholder discrimination, so the file the fix now
> finds gets announced as the thing it isn't ... logs `... so the shipped placeholder at [...] is being
> used instead. It parses as a success and declares no media` over the operator's **real** content

**Context:** `code-reviewer` (**BLOCK**) and `architect-reviewer`, independently and in the same terms.
Both prescribed the same fix: route the deployed case through `IsShippedPlaceholderCopy`, whose stamp
check is exactly what distinguishes `ConfigManager`'s seed from a real deployment.

**Notes:** Fixed before either review landed, having reached the same conclusion from the M112 round. The
rule earned: **when a resolver gains a candidate, grep for every caller that compares its result against
one of the old ones** - this is the second time in one day that adding an outcome left a two-way consumer
behind.

### Whether M46 could be closed - the answer was no

**Status:** Open (still `partly fixed`)
**Category:** Architecture

> **`partly fixed`. Do not mark it fixed.** Your reasoning about the *blocker* is correct ... But M46's
> *symptom* is untouched: with the shipped `downloadFolder: ""` the cache and the override are still one
> file ... **Do not give the collision a new id** - the collision *is* M46.

**Context:** `architect-reviewer`, asked to decide rather than to list options. I had thought the entry
could close.

**Notes:** Taken as given, including the instruction not to split the collision into its own id. Recorded
because the distinction is a useful one to have in writing: retiring a *blocker* is not the same as
fixing a *symptom*, and an entry whose prescription turns out to be unnecessary still has its original
defect to answer for.

### The extraction, and the empty-segment question

**Status:** Resolved
**Category:** Code Quality
**Resolved in:** v0.49.22

> Mono implements `Path.Combine(a,b,c)` by delegating to `Combine(new[]{a,b,c})`, so ... the identical code
> path minus three up-front null checks the params version performs itself ... Mono's params `Combine`
> `continue`s on `Length == 0` before the separator bookkeeping, so `{root, "", file}` yields `root\file` -
> no doubled separator, no throw, identical to the old three-arg call.

**Context:** `code-reviewer`, answering whether the `params` refactor was faithful and whether
`downloadFolder: ""` still composes the same string. Both confirmed at the implementation level rather
than by inspection.

**Notes:** It also confirmed the new candidate is bounds-checked and that an absolute `dataJsonPath` -
which makes `Combine` discard the root entirely - is caught by the `StartsWith(root)` test rather than
escaping. That was the reason for sharing one containment implementation rather than writing a second.

### Noted, not fixed

**Status:** Open
**Category:** Readability

> when `localSettings.dataJsonPath` is itself the bad segment (invalid chars, `..`, absolute-outside),
> both resolvers now refuse, so the operator gets two near-identical `REFUSED ...` lines differing only in
> `the appData cache path` / `the deployed appData path`. Noise, not wrong.

**Context:** `code-reviewer`, LOW.

**Notes:** Left alone deliberately - deduplicating it would couple the two resolvers to each other's
outcome for a message an operator sees once, on a config that is already broken.

## `Assets/01_Scripts/EnigmaVirtual/OneCoreClient/Model/OneCoreCommand.cs` (M55)

### The register's prescription was refuted by the operator's data

**Status:** Resolved
**Category:** Bug
**Original location:** `known-issues.md`, M55
**Resolved in:** v0.49.23

> thats why it was used a dynamic field, and yes it was read in unity with no problems
> `string str = string.Format("{0}", JsonConvert.SerializeObject(activity.data.command_data));`

**Context:** Not an agent finding - the operator's, with real appData from shipped projects showing
`command_data` as a JSON **object** in one activity and `""` in another. M55 prescribed typing the field
as `string`; I had implemented that and verified it against `JsonUtility` only. Measured afterwards:
Newtonsoft deserialising an object into a `string` field throws `JsonReaderException`.

**Notes:** Reverted before commit. The durable lesson is about **which serializer**, not about testing
more: this type is filled by `JsonUtility` (settings.json) *and* Newtonsoft (appData.json), so its
constraints are the union of both, and I verified against the one the entry happened to mention. A shared
model type is a shared contract.

### Enabling the read created a data-loss path on the write

**Status:** Resolved
**Category:** Bug
**Original location:** `FileManager.SaveCoreSettings`, `ConfigsPanel`'s PlayerPrefs snapshot
**Resolved in:** v0.49.23

> the save half still emits `JsonUtility.ToJson(Core.settings)`, which cannot see `object` — so every
> `command_data` an operator now writes into settings.json is destroyed on the first panel Save

> the PlayerPrefs path is worse ... `Core.Awake` **prefers that snapshot and gates the file read out** —
> so on Android/iOS, where the prefs toggle is the only save target, one panel Save permanently disables
> what this change adds, silently

**Context:** I found and measured the `SaveCoreSettings` half myself and fixed it; `architect-reviewer`
found the `ConfigsPanel` half, which is the worse one and the one I had missed.

**Notes:** Both writers now go through `FileManager.SettingsAsJObject`. Third time today that enabling a
capability without its counterpart created a new defect - M112's marking, M46's resolver, and this. The
rule that keeps falling out: **a read and its write are one change.**

### `PopulateObject` defaults that differ from `JsonUtility`

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.49.23

> `PopulateObject` defaults to `NullValueHandling.Include`, so `"Authentication": null` now nulls a field
> `JsonUtility` never nulls — the exact NRE `Settings.cs:12`'s initializer exists to prevent

**Context:** `architect-reviewer`. `code-reviewer` separately confirmed the array case is safe today
because `ConnectCommands` is a fixed-size array, and that a branch changing it to a `List<>` would get
entries **appended** rather than replaced.

**Notes:** `NullValueHandling.Ignore` and `ObjectCreationHandling.Replace` both passed. The second is a
no-op for the current shape and exists so the `List<>` trap cannot open later.

### Cleared, and one claim of mine narrowed

**Status:** Resolved
**Category:** Design

> There is no `SerializeObject(string)` overload, so `JObject`, `string` and `null` all bound to
> `SerializeObject(object)` under the runtime binder and bind to it statically now.

> Your CS1974 rationale is correct: a dynamic call site cannot be elided, so those three `LogIf`s
> genuinely were not stripped on iOS/WebGL and now are.

> "removes a DLR dependency" overstates — `AppDataController.cs:1334-1342` still uses `dynamic` in the
> same assembly, so the reference is unchanged.

**Context:** `code-reviewer` on all three. The first two confirm the change is behaviour-neutral where it
must be; the third corrects a claim I had written into the XML docs.

**Notes:** The doc now says it removes the DLR from *these call sites*, not from the assembly. Also
flagged and taken: the method name `PopulateOneCoreCommandData` understated its scope - it re-reads the
whole block, so it is `RereadOneCoreWithNewtonsoft` now - and a present-but-non-object OneCore block used
to return silently, which is an operator typo that now gets a line.

## `Assets/01_Scripts/EnigmaVirtual/MainController.cs` and `Core/Display/ui/core/CoreSystemUI.cs` (M76)

### The first fix did not cover the configuration that ships

**Status:** Resolved
**Category:** Bug
**Original location:** `CoreSystemUI.Init`, the subscribe-or-wait branch
**Resolved in:** v0.49.24

> on the SHIPPED config the new preference is inert: `useLocalAppData` is false, so
> `AppDataController.Init` starts a coroutine and `Core.main.AssetManager` is null at `systemUI.Init()`.
> `AdoptOwnedAssetManager` therefore always early-returns at site 1 ... so `WaitForAssetManager()` never
> runs and site 2 is unreachable.

> the change's only new diagnostic can never fire, so a branched project that splits the two managers
> gets the identical silent preloader M76 already describes.

**Context:** `code-reviewer` and `silent-failure-hunter`, independently and in the same terms. The fix
worked on the local-appData path, and on the remote path only when `Awake` had found nothing.

**Notes:** The gate reads `Core.main.AssetManager` now instead of the local field, so the remote path
always takes a second resolve once the owner exists. `code-reviewer`'s version was four words against my
draft's `_listeningTo` field plus an extracted teardown - it removes the unsafe window instead of making
the code tolerate it. **When a fix needs machinery to stay safe, check whether the invariant can be
restored instead.**

### The `==` comparisons are Unity's operator on purpose

**Status:** Resolved
**Category:** Unity-specific
**Resolved in:** v0.49.24 (confirmed, not changed)

> `owned` destroyed -> `owned == null` true -> return, keeps the existing binding ... `assetManager`
> destroyed, `owned` alive -> falls through; then `assetManager != null` is **false**, so it rebinds
> *silently* instead of falsely reporting "the scene has two AssetManagers" ... Nothing here wants
> `ReferenceEquals`; using it on either operand would break the second case.

**Context:** `code-reviewer`, answering the question I had flagged as the one I was least sure about.

**Notes:** Recorded because the instinct to "tidy" a `UnityEngine.Object` comparison into
`ReferenceEquals` is a real one, and here it would have turned a silent correct rebind into a false
report about a scene that has exactly one manager.

### Cleared: the shipped scene, listener safety, and FindAnyObjectByType

**Status:** Resolved
**Category:** Design

> `MainController.assetManager` is `{fileID: 0}` (unassigned), the `AssetManager` component sits on GO
> `AssetManager` with `m_IsActive: 1` ... So `GetComponentInChildren` wins and `FindAnyObjectByType`
> never executes.

> Both adopt sites precede `SetupAssetManagerListenners` ... no stranding, no double-subscribe.

> The parameterless overload defaults to `FindObjectsInactive.Exclude` in Unity 6 ... a *disabled
> component* on an active GameObject **is** returned - and that is fine, since a disabled
> `MonoBehaviour` on an active GameObject can still run coroutines.

**Context:** All three were questions I asked. `silent-failure-hunter` added that `FindAnyObjectByType`
also scans the `DontDestroyOnLoad` scene, which `Core` lives in, so additive scene loading in a branched
project can bring a second manager into range.

**Notes:** Kept because the `DontDestroyOnLoad` point is the one that could surprise someone later, and
because "the new rung never runs on the shipped scene" is the claim that made this safe to land.

## `Assets/01_Scripts/EnigmaVirtual/Data/AppDataController.cs` (M84)

### The guard I added removed a fallback

**Status:** Resolved
**Category:** Bug
**Original location:** the `ToObject(AppDataType)` catch, first version
**Resolved in:** v0.49.25

> the `return` in the ToObject catch drops the recovery the throw used to reach - regression on both
> paths

> a CMS content block that no longer matches the project's `AppDataType` now leaves the app on the
> preloader with two log lines and no fallback, where before it booted on the local copy

**Context:** `code-reviewer` (**BLOCK**) and `silent-failure-hunter`, independently, with the same trace:
the bare `return` skips both the end-of-method check and the `else` that calls `FailRemoteAppData`, so
control returns normally through `OnGetAppData` to `LoadRemoteData`, which only declines to cache.

**Notes:** The catch now sets `dataLoaded = false`, calls `FailRemoteAppData` with the tail's own message
and returns. **Reporting better while recovering worse is not an improvement** - the second time in two
days that a guard added for clarity removed a fallback.

### The one shape that leaked through routed to the worst exit

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.49.25

> resolver validates the ENTRY is an object but not `content` itself, so `"content": [ ... ]` or
> `"content": "x"` returns non-null -> ToObject throws -> lands in the catch -> the strand above. The one
> shape that leaks through routes into the worst exit.

**Context:** `silent-failure-hunter`; `code-reviewer` reached the same conclusion and added that
`jTokenContentData["Settings"]` would throw `InvalidOperationException` on a `JValue` as soon as the
catch stopped returning.

**Notes:** `content.Type != JTokenType.Object` closes it, with a message of its own. A guard that covers
seven of eight shapes and sends the eighth somewhere worse is not seven-eighths of a fix.

### Cleared: the refusals are not a regression, and the well-formed path is untouched

**Status:** Resolved
**Category:** Design

> the caller's `if (!contentToken.IsNullOrEmpty())` applied the *identical* predicate before this change,
> so every one of those shapes already took the "Content unavailable!" branch and the end-of-method
> recovery. The only change is that it now says which one.

> `(JArray)modules` -> `entries[idContent]["content"]` resolves the *same* `JToken` reference as
> `jTokenModulesData[idContent]["content"]` did ... No log fires on a valid payload.

> The resolver itself is good work - hoisting the caller's own `IsNullOrEmpty` into it means the four new
> refusals reject exactly what the old code already rejected, which is the hard part of this kind of fix
> to get right.

**Context:** `code-reviewer`, answering whether the shape checks reject anything that previously worked -
the question any new guard has to answer.

**Notes:** Recorded because the reason it is safe is structural rather than lucky: the predicate was
hoisted out of the caller, not invented. `silent-failure-hunter` separately confirmed the null path
reaches the end-of-method check with `dataLoaded` true and `AppData` unchanged, which is the assumption
the whole change rests on.

### Smaller findings, all taken

**Status:** Resolved
**Category:** Code Quality

> catch logs `e.Message` only - no `Development.LogException(e)`, so Newtonsoft's stack (which names the
> failing member path) is deleted. Sibling ParseAppData does log it.

> empty `modules` array reports "valid range is 0 to -1"

> `private` helper for a `protected virtual` method - an overriding branch keeps the old chained
> expression and cannot call it

> the end-of-method line still says "parsed without error but produced no content model" and attributes
> it to idContent-as-position - untrue and misdirecting for the non-array shape

**Context:** `silent-failure-hunter` and `code-reviewer`, one each on the first two, both on the last.

**Notes:** All four applied. The last is the subtle one: a second, more general message that contradicts
a specific one directly above it sends an operator to edit the wrong setting.

## L17 slice 2 — nine types into `EnigmaVirtual` (v0.50.0)

### The risk check missed UnityEvent persistent calls

**Status:** Resolved
**Category:** Unity-specific
**Resolved in:** v0.50.0 (documented; no code change needed — zero hits in this repo)

> `m_TargetAssemblyTypeName` is a **namespace-qualified type-name string**, resolved by
> `Type.GetType`, not by GUID. It is live in this repo: `Assets/00_Scenes/CoreSetup.unity:3182,13574,
> 17576,19079` carry `FieldsVisibilityController, Assembly-CSharp`.

> A branch with such a wiring gets a call that stops resolving with **no compile error and no runtime
> error**.

**Context:** `architect-reviewer`. Before the change I checked reflection-by-string-literal,
`[SerializeReference]` and `AddComponent("...")`, and concluded a namespace move was GUID-safe. That is
true for an ordinary component reference and false for an Inspector-wired UnityEvent — which is the one
that fails silently.

**Notes:** Verified independently: **zero** of the nine appear in any `m_TargetAssemblyTypeName`, so
this slice ships safe. The mechanism is now in `CLAUDE.md`, in L17, and as step 2 of the `§5b`
branch-owner instructions — `TransitionBase` exposes ~20 public void methods that are exactly what
people wire to buttons, so a branch is far more exposed than this repo.

### The next slice's landmine, found in passing

**Status:** Open — carried into L17's remaining work
**Category:** Potential Bug

> `FieldsVisibilityController.cs:6` is still global and is bound by that string in four places in
> `CoreSetup.unity`. Moving it without rewriting the YAML breaks those four persistent calls silently.
> Side note: the recorded assembly is `Assembly-CSharp`, but the file is under the `EnigmaVirtual`
> asmdef — that qualifier looks stale already.

**Context:** `architect-reviewer`.

**Notes:** Confirmed both halves: four entries exist, and `FieldsVisibilityController` compiles into
`EnigmaVirtual.dll`. Whether those four calls still fire today is **unverified** and worth checking
before the next slice — it is recorded in the session log's *Open / next*, not as a defect entry,
because nothing has been measured yet.

### Cleared: the mechanical rewrite is clean

**Status:** Resolved
**Category:** Code Quality

> Brace balance: final depth 0 in all 7, and min-depth never goes negative. Preprocessor: **zero**
> `#if`/`#region`/`#endif` in all 7 files. Verbatim strings: **zero** `@"..."` in all 7 files. Your
> dangerous case doesn't exist here.

> Indentation: every body line gained *exactly* 4 spaces — 0 mismatches across 1229 body lines.

> Name-resolution shift (the real silent risk — `EnigmaVirtual.X` now outranks global `X` *and*
> file-level usings): intersected every identifier in the 7 files against the 143 `EnigmaVirtual` root
> types. Only hits are their own 9 names plus `AppPaths`/`Development` ... Checked the tempting
> collisions explicitly — `Console`, `Timer`, `Navigation`, `Dictionary`, `ConcurrentQueue`, `State`,
> `Placement` all exist in `EnigmaVirtual` and none is referenced by a touched file.

**Context:** `code-reviewer`, verdict **APPROVE WITH SUGGESTIONS**, verified with its own tokenizer
rather than by eye.

**Notes:** Recorded because the name-resolution check is the one I did not think to run and could not
have done by reading: wrapping a file changes which declaration wins for *every* identifier in it, not
just the ones being moved. The verbatim-string result is worth stating precisely — it found **zero**
such literals, so it excluded nothing; the risk was absent rather than ruled out.

### Two LOW items, both taken

**Status:** Resolved
**Category:** Maintainability
**Resolved in:** v0.50.0

> `MainThreadDispatcher.cs:1`: `using EnigmaVirtual;` still there, now as redundant as the one deleted
> from `ConfigManager.cs`. The script's own rule was applied to one of the two files that had it.

> 5 of 7 files rewritten with CRLF against `.gitattributes` `*.cs text eol=lf` ... Git normalizes on
> commit so the blob is unaffected, but every `git diff`/`status` now emits a CRLF warning per file.

**Context:** `code-reviewer`.

**Notes:** Both applied. The second changes nothing in the commit and everything in the noise level of
`git status`, which is the kind of thing that gets ignored until it hides something real.

### The count this entry had been carrying was wrong, and so were both reviews

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.50.0

> Measured with a parser (BOM-safe): **15 ordinary types remain global**, not L17's `~24`.

> `grep -q "^\s*namespace "` reports **43** global files here — 13 are false positives where the BOM
> sits before `namespace`, exactly the trap `CLAUDE.md` records. I hit it myself this session.

**Context:** `architect-reviewer`. My own parser returned 14 against its 15.

**Notes:** Neither figure was wrong; they counted different populations. Mine included the three
`External/` forks, its did not. Its "trapped inside extension holders" set included
`ArrayExtensions`, `ProcessExtensions` and `WindowUtils`, all three of which contain **zero** `this`
parameters — classified from the filename rather than the contents. Resolved by reporting the split
instead of a total: **3** External / **11** cleanly movable / **22** genuine holders / **6** needing
extraction. The lesson generalises past this entry: when two measurements of "the same" number
disagree, check they are measuring the same set before deciding which is broken.

### The `§5b` branch-owner instruction did not render

**Status:** Resolved
**Category:** Documentation
**Original location:** `docs/architecture/overview.md:350`
**Resolved in:** v0.50.0

> the new §5b row splits into **8 table cells**, not 3 — the four unescaped `|` in the PowerShell regex
> ... terminate cells in GFM even inside a code span. A renderer drops cells 3-7 ... i.e. step 2, the
> whole reason the row exists, does not reach a branch owner.

**Context:** `doc-reviewer`, its single blocking finding.

**Notes:** The source looked correct and the rendered page silently lost the command, what to do with a
hit, and the "zero hits in this repo" note. Fixed by dropping the type-name alternation entirely in
favour of `m_TargetAssemblyTypeName: \S`, which needs no pipe and is the better command anyway: 46 of
the 50 entries in this repo have an empty value, bind by target-object type, and are GUID-safe.

### Three citations wrong, one of them wrong before I touched it

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.50.0

> `ConfigManager.cs:13,153` — :13 is `/// config up to date with any keys the shipped file has gained.`
> ... Neither concerns `DEBUG_MODE`. This was already wrong at HEAD (`12,152`) and the +1 shift
> preserved it — the exact "mechanical remap lands on real code while describing the wrong construct"
> failure CLAUDE.md names.

**Context:** `doc-reviewer`. M51 and M56 were separately stale.

**Notes:** Now `ConfigManager.cs:76` (the `if (!AppPaths.UsesDeployedConfig) return;` predicate),
`Api.cs:578,626`, `AppDataController.cs:496,1676` and `:1678`. Each target verified against source
before applying. **The lesson is about anchor re-pointing itself:** it preserves whatever relationship
existed between citation and code, so it moves a correct citation correctly and a wrong one wrongly.

### A type bucketed as blocked *by* an extension holder was the holder

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.50.0

> `ToggleGroupExtension` is in the "Blocked, needs extracting first" bucket, but it **is** the extension
> holder in that file — `public static Toggle GetActive(this ToggleGroup aGroup)` at
> `RegisterField.cs:182`. By the table's own rule it must stay global. Split is **3 / 11 / 22 / 6**.

**Context:** `doc-reviewer`.

**Notes:** My classifier decided "is this an extension holder?" partly from the *filename*, so a holder
not named `*Extensions.cs` fell through to the blocked bucket. Corrected in all five places the split
appears. Same mistake as the `ArrayExtensions`/`ProcessExtensions`/`WindowUtils` misfiling in the
architecture review, in the opposite direction — a file's name is not evidence about its contents
either way.

**REGRESSED and re-fixed in v0.54.1**, which is the part worth more than the original finding.
`architect-reviewer` found `ToggleGroupExtension` back in the blocked bucket on 2026-08-18 — in the
L17 entry, its worklist row, and `overview.md`'s v0.54.0 breaking-change row. It was not reverted:
the v0.53.0 and v0.54.0 slices each **recomputed** the split from scratch as the census moved
(42 → 38 → 29 → 27), and each recomputation re-derived the classification with the same
filename-shaped assumption this entry had already ruled out. The correction lived in five prose
sites and in none of the tooling, so every later measurement was free to contradict it.

**The lesson is not "re-check the split", it is that a doc fix with no executable form is a doc fix
with a half-life.** `overview.md`'s census table — the one artefact carrying the *reason*
("it declares `GetActive(this ToggleGroup)` and so must itself stay global") — is the only one of
the four sites that survived three recomputations intact. The three that carried the bare number
all drifted back. **Write the reason next to the number, or the number will be recomputed without
it.** The regression's cost was not the wrong count: the worklist row was an *instruction* to
extract a type that must never be extracted.

### Cleared, and worth recording because it is the expensive half to check

**Status:** Resolved
**Category:** Documentation

> **42 global types** confirmed with a BOM-safe brace-depth parser; every other name is in the bucket
> claimed ... `ConfigManager.cs:215-231` — the manually-resolved range is **right** ... No out-of-range
> or missing-file citations anywhere in `known-issues.md` (412 checked).

> **§5b PowerShell command runs** (enumerates all 38 `.unity`/`.prefab`), its regex covers all nine
> names, and returns **zero hits**; control run with `FieldsVisibilityController` added returns 4.

**Context:** `doc-reviewer`.

**Notes:** The control run is the part worth copying: a grep returning zero is indistinguishable from a
grep that never matched anything, so it re-ran the same command with a term known to be present. That
is the counter-test discipline applied to a documentation claim.

### Four `§5b` rows had been silently dropping content, none of them new

**Status:** Resolved
**Category:** Documentation
**Original location:** `docs/architecture/overview.md`, rows v0.27.0, v0.28.0, v0.40.0, v0.49.19
**Resolved in:** v0.50.0

**Context:** Found by the cell-count check written to confirm `doc-reviewer`'s blocking finding on the
v0.50.0 row — not by any reviewer, because the source reads perfectly and only the *rendered* table is
wrong.

**Notes:** Each carried an unescaped `|` that terminates a GFM cell even inside a code span, so the row
rendered with phantom extra columns that renderers drop. v0.40.0 and v0.49.19 lose the second half of
`length|ticks` and the sentence around it; v0.28.0 loses its find-and-replace command
(`"TranslatableField|TryGetTranslation|HasAnyTranslation"`, three pipes, six cells); and v0.27.0 loses
the GUID grep that finds `UnityEvent` persistent calls broken by that release — the same silent-break
mechanism this session's L17 work documented, unreadable in the one place it was written down. All four
rejoined with `\|`; all 75 rows now parse to exactly 3 cells.

## M46 write-side + M116 (v0.50.1)

### An accurate report whose implied recovery is the damaging one

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.50.1

> the message says the file "will NOT be read back" and stops there — but the editor and any player
> built on this machine share persistentDataPath, so AppPaths.Config:176 makes that leftover LocalLow
> appSettings.json the config the next locally built player PREFERS. Acting on the message as written
> ("it's inert, leave it") is what arms it. The sibling at FileManager.cs:225-230 carries exactly this
> sentence and the new one dropped it

**Context:** `silent-failure-hunter`, answering the question I asked it directly — whether the diff
reported a failure while making recovery worse. `code-reviewer` flagged the same omission as LOW.

**Notes:** Third instance of this shape in a week, and the first where the corrected wording already
existed in a sibling I had open. Copied, including the "delete that file if you did not mean that"
clause.

### `Define DEBUG_MODE` is unactionable on the WebGL half

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.50.1

> a WebGL build with DEBUG_MODE is refused outright by BuildValidator.cs:113, and if forced it makes
> AppPaths.Config return the idbfs persistent path to UnityWebRequest, which 404s

**Context:** Both reviewers, independently, at MEDIUM.

**Notes:** The remedy is scoped to the editor now and WebGL named separately. Worth recording because
the message named two cases in one breath and offered a fix for only one of them — a remedy that is
wrong for half its audience is worse than no remedy, since it invites an action that breaks the read
differently.

### M116: an empty appSettings.json stored a null

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.50.1

> JsonUtility.FromJson returns null for an empty/truncated/"null" appSettings.json without throwing
> ... downstream AssetManager.cs:874 / TextureCacheManager.cs:372 NRE on AppSettings.isMediaType and
> get reported as "the asset walk threw" (wrong cause)

> the diff's own context comment claims a truncated file "lands on the 'failed, so use a fresh
> AppSettings()' path" — no such path exists ... The comment asserts a safety net that isn't there,
> which is why the line above stayed unclosed

**Context:** `silent-failure-hunter`, its highest-risk remaining finding, raised against a diff that
was about something else.

**Notes:** Both taken. The second is the sharper observation: a comment asserting a safety net that
does not exist is *why* the gap survived — every reader who checked was reassured by prose.
**The finding's own description of the trigger was wrong**, and my first comment repeated it:
measured, only `""` returns null; `"   "` and `"null"` both raise `ArgumentException` and were already
caught. The probe failed on that assertion, which is how it surfaced. Register entry M116 carries the
measurement.

### Verified clean, and one that decides the test plan

**Status:** Resolved
**Category:** Code Quality

> Extraction is behaviour-neutral. `WriteAtomic` is `internal static bool`, never throws (catches
> everything, returns `false`) ... Evaluation order is unchanged, the `context` string is
> byte-identical, and both statements sit in the same `try`.

> `WriteAtomic` has one `return false`, in its `catch`, and it always `Development.LogError`s first —
> it never returns false silently. Firing on `false` would print "written but won't be read back" over
> a file that was not written: a second, contradictory cause. Gating on `saved` is correct.

> SaveAppSettings is `internal` with zero callers repo-wide, and `internal` is real since v0.24.0, so
> a branched project's settings panel in App/ gets CS0122 and cannot reach this code at all. The new
> LogError is unexercisable here without adding a caller by reflection or widening the method.

**Context:** `code-reviewer` (**APPROVE WITH SUGGESTIONS**) and `silent-failure-hunter`.

**Notes:** The last one is why the test plan says steps 4 and 5 cannot run here, rather than claiming
coverage it does not have. Not widened: `CLAUDE.md`'s rule is to widen the member that has a caller,
and this one has none — the `CS0122` is the *correct* signal for a branch that needs it.

## M46 closed — removing `downloadFolder` (v0.51.0)

### The deleted method's doc block survived it

**Status:** Resolved
**Category:** Documentation
**Original location:** `AppDataSettings.cs:172-212`
**Resolved in:** v0.51.0

> This was TryGetCachedDataJsonPath's `<summary>`/`<remarks>`; deleting the method left it stacked on
> TryGetDeployedDataJsonPath, which now carries TWO summaries. It contradicts the block below it ... and
> describes downloadFolder as live at :179-182, :192, :210. Compiles silently.

> No DocumentationFile in any csproj, so no CS1571 and 0 warnings; IDE hover shows the stale block

**Context:** Both reviewers, independently.

**Notes:** Deleted, with the one paragraph the replacement lacked (why the resolver returns `false`
rather than throwing) merged in rather than lost. The pairing is what makes it worth recording: nothing
in the toolchain reports two summaries on one member, so the only detector is a reader — and a reader
hovering the member is exactly who gets the stale answer.

### The branch-owner hazard, traced end to end

**Status:** Resolved — documented in `overview.md` §5b
**Category:** Bug

> a branched project with a non-empty downloadFolder has its media root move ... every asset misses
> AssetManager.cs:1053's File.Exists and the whole media set silently re-downloads (AssetDownload.
> isActive ships TRUE); old tree orphaned outside the cleanup root permanently. Symptom: a boot that
> takes hours, no error - the watchdog won't fire because onAssetDownloadEvent's raw count keeps moving
> the stage string (M70). Only trace is LogIf(doLogs).

**Context:** `silent-failure-hunter`. `architect-reviewer` reached the same conclusion from the schema
side and called it "M32's migration verbatim".

**Notes:** No code change can prevent it — the field is gone, so nothing is left to detect the old
layout. It is the first thing the `§5b` row says, with "delete the old tree by hand and expect one full
re-download" as the instruction. The chain from *removed settings field* to *hours-long silent boot* is
four hops and none of them logs above `LogIf`.

### Five defect ids in source comments

**Status:** Resolved
**Category:** Maintainability
**Resolved in:** v0.51.0

> 5 new defect ids (M46 x4, M32) added to source comments - HEAD had 2 in AppDataSettings.cs and 1 in
> AppDataController.cs, working tree has 4 and 3. Against the 2026-08-14 standing rule.

**Context:** `silent-failure-hunter`; the architecture review did not raise it.

**Notes:** Measured back to the HEAD baseline with a comment-portion scan: 2 and 0. Version markers were
kept — "gone as of v0.51.0" is a migration marker, which is the deliberate exception, while "(M46)" is
an id and belongs here instead.

### Cleared: nothing was lost by collapsing three candidates to two

**Status:** Resolved
**Category:** Design

> `isDeployed` is exactly "downloadedDataJsonPath returned the root file" — the union of the old
> isCache || isDeployed, since those two tested identical strings.

> The old chain's first two candidates differed only by the downloadFolder segment; with that segment
> gone, a refusal from TryGetCachedDataJsonPath and one from TryGetDeployedDataJsonPath had exactly the
> same cause, so the second try could never have rescued the first. The write site now uses the
> identical expression as the read's first candidate, so writer and reader cannot diverge by
> construction — strictly better than before.

**Context:** `silent-failure-hunter` and `architect-reviewer`, answering separately.

**Notes:** Recorded because "does removing a fallback remove a recovery?" is the question I have got
wrong three times this week, and here the answer is structural rather than lucky: the two candidates
were the same string, so the second could never have rescued the first.

### The operator-facing label outlived its meaning

**Status:** Resolved
**Category:** Readability
**Resolved in:** v0.51.0

> `AppDataSettings.cs:236` passes `"the deployed appData path"` into the operator-facing `REFUSED …`
> message, and that method is now the cache write site; an operator whose cache write is refused reads a
> line about a "deployed" path. One string, not a rename.

**Context:** `architect-reviewer`, answering whether the method should be renamed. It argued *against*
the rename — `internal`, so a rename costs branches merge noise and buys nothing — and for changing the
one string that reaches a human. Now `"the appData path under config"`.

## L23 — four duplicate global types deleted (v0.52.0)

### No reviewer pass: two attempts died on API 529

**Status:** Open — the review itself, not a finding
**Category:** Other

**Context:** `architect-reviewer` was launched twice against this diff and terminated both times with
`API Error: 529 Overloaded` — server-side, unrelated to the change. This is the **same gap L17's first
slice shipped with** (its review died on a session limit), and it is recorded rather than glossed: L23
has compile verification, probe assertions and self-inspection, and no independent read.

**Notes:** The five questions put to the reviewer were answered by measurement instead, below. The one
that most wanted an outside opinion — the extension-holder question — turned out to have a stronger
answer available from the code than the argument I was going to defend, which is a reason to re-run the
review when the API is healthy rather than to treat it as settled.

### Self-checked in place of the review

**Status:** Resolved
**Category:** Design
**Resolved in:** v0.52.0

**Context:** The five items, each measured rather than reasoned:

1. **Right copy kept.** The probe asserts the `[CustomPropertyDrawer]` binds
   `EnigmaVirtual.MinMaxRangeAttribute`; the deleted copies were compared declaration-by-declaration
   with whitespace normalised and were identical on all four.
2. **Serialization: no subject.** Zero `.unity`, `.prefab` or `.asset` file names any of the four, so
   nothing holds a serialized field and no type-name string is recorded anywhere. The risk is **absent,
   not excluded** — a branched project with such a field is in a different position, though both
   declarations were structurally identical so a field would deserialize into the survivor unchanged.
3. **Extension-holder scope — the argument got better, not weaker.** L17 says extension holders belong
   in the global namespace, and the surviving `RangedExtensions` is namespaced. It holds anyway, and for
   a stronger reason than the one I had: the *global* holder that remains in that file,
   `MathExtensions`, already declares `InRange(this float value, RangedFloat range)` — a global
   extension method whose **parameter** type is now `EnigmaVirtual.RangedFloat`. So "global holder,
   namespaced parameter types" is a pattern already live in this exact file. A caller cannot have a
   `RangedFloat` to pass without the import, so the extension is never the thing that is missing. There
   are also zero callers of `LerpFromRange` anywhere in the repo.
4. **Breaking-change surface.** A global-namespace file naming any of the four gets `CS0246`, which
   names the type. Nothing else: no serialized data, no reflection by name, no `.meta` moved.
5. **Leftover prose and M20.** `MathExtensions.cs` mentions the types only where it uses them, plus one
   comment explaining the import. No `App` reference in the file — M20 clean, and the change removes
   framework surface rather than adding any.

**Notes:** Recorded at this length because it is standing in for a review. If the next person wants to
trust it, these are the things they can re-run; if they want to redo it properly, the five questions are
above.

## `Assets/01_Scripts/EnigmaVirtual/Core/Modules/Connections/Api/Api.cs` (M44)

### The invariant's strongest leg was the one I left out

**Status:** Resolved
**Category:** Design
**Resolved in:** v0.52.1

> **The leg you and the comment both omit is the decisive one:** `MainController.cs:351` reads
> `Core.settings.Api.isActive` to decide whether to call `SetupApiController()` at all. The controller
> cannot exist unless a non-null `settings.Api` was already dereferenced. That single fact subsumes the
> Unity-message and scene-authoring arguments.

**Context:** `code-reviewer`, verifying the invariant the whole change rests on.

**Notes:** I led with the two weaker legs — no Unity messages, nothing authored in a scene — and both are
facts about today's code rather than structural ones; the review noted an editor button can change the
second. The `isActive` gate is structural: it cannot be true and the settings null. Comment and register
both now lead with it.

### The `?.` I removed had never made the class null-safe

**Status:** Resolved
**Category:** Potential Bug

> the old `?.` never made this class null-safe: `Api.cs:540` (`if (Core.settings.Api.doLogs)`) is *not*
> inside a `LogIf`, so it dereferences on every platform, and `ApiControllerBase.cs:34` does too,
> unconditionally, and runs first. Any null-settings scenario crashed with or without the guard — the
> guard just moved the crash a few lines.

**Context:** `code-reviewer`. I had written that the guard "only ever covered iOS and WebGL".

**Notes:** Stronger than my version and it is the argument that actually closes the question: the guard
covered nothing, anywhere. Worth recording because I reached for a plausible narrowing ("it protects the
platforms where the `LogIf` is stripped") without checking whether an *unconditional* dereference ran
first — and one does, in the same file.

### Post-mortem moved out of the source comment

**Status:** Resolved
**Category:** Maintainability
**Resolved in:** v0.52.1

> the comment records a correction to itself ("This line used to use `?.`, justified as… That was half
> true and not the point") — CLAUDE.md's standing rule puts exactly that in docs/reviews.md, which this
> diff does not touch at all.

**Context:** `code-reviewer`.

**Notes:** Correct, and the second time today I have written review narrative into a comment (the L23/M46
round put defect ids in five). The comment now states the constraint and the reason not to "fix" it with
`?.`; the history is here. Note the diff touching no `.md` at all was itself the tell.

### Cleared: direction, and the `?? 0` equivalence

**Status:** Resolved
**Category:** Design

> `?? false` on a `bool` is the wrong choice for a base project on this codebase specifically: it
> converts a boot-order violation from an NRE with a stack trace into *silent absence of logging plus a
> default timeout* — the dominant defect class here … and it deletes the evidence for the same reason
> M80 says not to swallow.

> `EffectiveTimeout` maps `0` → `ApiUnconfiguredSeconds` = 30; with settings non-null the old expression
> *was* `Core.settings.Api.timeout`.

**Context:** `code-reviewer`, answering which way to unify and whether behaviour changed.

**Notes:** Both confirmed. The first is the general lesson and it points the opposite way to instinct: on
a codebase whose dominant defect is silent failure, adding a null-guard to a path that should never see
null buys silence, not safety.

**Two residual holes it named, both currently vacuous and neither introduced here:** a second
`ApplySettingsJson` with a payload parsing to `null` clobbers `Core.settings` before reporting failure —
reachable only through `FileManager.LoadJsonWeb("settings.json")`, which nothing calls; and a branched
subclass declaring `Awake`. Recorded because the first is a real shape (a setter that assigns before
validating) even though no caller reaches it today.

## M97 + L16 + L20 (v0.52.2)

### The replacement comment pointed at a flag that is never cleared on success

**Status:** Resolved (comment and register); the flag itself is **M117**
**Category:** Bug
**Resolved in:** v0.52.2

> the replacement comment's advice is false: `loadingData` has ZERO readers and is never cleared on
> success

> All four clear sites are failure/early-exit paths ... So after one successful appData load it is stuck
> `true` for the process lifetime, and a restored `if (!loadingData) TryLoadData()` would fire exactly
> never. That is a worse trap than the latch it replaced, which at least self-released after 5s.

> The identical claim is in `docs/architecture/known-issues.md:149` — fix both, or the register
> certifies the wrong prescription.

**Context:** `code-reviewer`, on a diff whose purpose was deleting a latch with no reliable exit.

**Notes:** I recreated the defect in prose while removing it in code, and put the same claim in the
register. The lesson generalises past this entry: **a replacement narrative is new code with no compiler
and no test.** Deleting dead machinery is mechanically safe; the sentence explaining what to do instead
asserts runtime properties nothing checks. Mine asserted one about a flag I had not read.

### Two orphaned doc fragments, and one comment claiming completeness

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.52.2

> orphaned unterminated `/// <summary>` left by the const deletion

> `_pendingCooldown`'s summary outlived its field; the next line's "for the same reason" now
> back-references nothing

> `IndexOf('?')` doesn't exclude a `?` inside a `#` fragment (fix: pre-existing, but say so)

**Context:** `code-reviewer`.

**Notes:** Third and fourth instance today of a deletion leaving prose that still reads as true — the
same species as M46's orphaned 41-line doc block. The cross-reference one is the instructive half: "for
the same reason" was fine until the reason was deleted, so the reason is now inlined. The L16 comment is
a different fault — completeness implied rather than stated — and it now names what it does not handle.

### Cleared: the two containers cannot drift, and nothing else broke

**Status:** Resolved
**Category:** Design

> `AddMediaAsset` is the sole writer, both are re-initialised together at `:170-171` before the only
> walk, and there is no `.Clear()`, `.Remove` or reassignment ... `HashSet<string>` and `List<string>`
> both use `EqualityComparer<string>.Default` — ordinal and null-tolerant — so null/case/culture
> behaviour is identical.

> `IndexOf(char)` is ordinal, so `%3F` correctly does *not* count as a query start ... a trailing `?`
> yields `?&a=b` and a trailing `&` yields `?x=1&&a=b`, both empty pairs that every mainstream parser
> drops.

> No dangling references to any of the six deleted members ... No orphaned coroutine host

**Context:** `code-reviewer` (**APPROVE WITH SUGGESTIONS**), answering the three risks put to it.

**Notes:** The drift question was the one I most wanted checked, because a `HashSet` beside a `List` is
two containers holding one fact and my probe only covered `AddMediaAsset`. The answer is structural —
sole writer, co-initialised, never cleared — rather than a happy accident. Also confirmed: the hoisted
`if` costs one field-chain read per value on iOS/WebGL, where `Development.Log` is stripped and the body
vanishes; it cannot NRE because `Cache` and `AssetDownload` both carry `= new X()`.

## M109 (v0.52.4)

### The fix reported progress on the healthy path only

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.52.4

> `onTextureCachedEvent` is raised only on `www.result == Success`. The failure branch and the three skip
> paths both advance `imageDictionaryCount` and never raise it. So the new mark is per *successfully
> cached* image, not per image - and failure is the slow outcome, not success.

> Three consecutive failed images out-run the budget at any timeout value ... the watchdog latches
> `_bootFailed`, and `LoadNextScene` refuses permanently - M109's exact fail-closed shape, un-fixed on
> the path that matters.

**Context:** `silent-failure-hunter`, on a diff written to prevent exactly that.

**Notes:** The clearest instance yet of a pattern that has recurred all week: an accurate report added to
the path that was never the problem. Fixed by moving the raise onto the one line every outcome reaches.
**Review said three skip paths; there were four** - the fourth found by asserting
`increments == raises == 1`, not by re-reading the list.

### Off-by-one, and a comment naming the wrong risk

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.52.4

> `done` is `imageDictionary.Count` read **before** the `Add`, so the first success marks `textures 0/N`
> and the last marks `textures N-1/N` - the string never reaches `N/N`

> the comment names duplicates as the case where the string stalls, but `AddMediaAsset` de-duplicates
> `mediaAssets` ... while the actual freeze (failed downloads) goes unmentioned. A comment that documents
> away the wrong risk is worse than none here.

**Context:** `silent-failure-hunter`.

**Notes:** Both taken. The second is the one worth keeping: I had written a caveat that sounded rigorous
and pointed at the case that cannot be slow.

### Cleared, and one stale premise

**Status:** Resolved
**Category:** Design

> unsubscribe covers every exit, because `RaiseVerificationComplete` is the single raise point and is
> idempotent per pass - the walk-threw, sort-threw, null-data, empty-list, stall-break and clear-mid-pass
> paths all route through it.

> `Core.cs:386-389`: the rationale for widening the budget still reads "the pre-cache marks exactly two
> stages". Stale as of this diff; the next person sizing the budget reads a premise that no longer holds.

**Notes:** The subscription-leak answer is structural rather than lucky, which is what I wanted checked.
The stale premise is fixed and now names *processed* rather than *cached* as the load-bearing word, so a
future change moving the mark back onto the success path invalidates the budget visibly.

## L26 — the editor assembly stops naming `App` types (v0.54.2)

### `GemaMenuItems.CreateScreenControllerCanvasGO()`

**Status:** Resolved
**Category:** Bug
**Resolved in:** v0.54.2
**Original location:** Line 146-148

> `go` (Canvas + CanvasScaler + GraphicRaycaster + controller) is abandoned in the scene when
> `CreateScreenViewGO` returns null, and `Undo.RegisterCreatedObjectUndo` is never reached — so the
> stray object cannot be undone.

**Context:** Found independently by `silent-failure-hunter` and `code-reviewer`, which is what makes
it worth recording: both traced the same failure path from opposite starting questions ("what fails
without saying so" and "what is incorrect"). Fires when a branch has a `ScreenControllerBase`
subclass but no `ScreenViewBase` one.

**Notes:** Fixed by resolving **both** types before anything is created, rather than by destroying
the partial object afterwards. The general shape: **a guard added part-way through a construction
sequence converts a compile error into a half-built object**, which is worse than what it replaced —
the original code could not reach this state because the types were statically guaranteed. When a
lookup can fail, do every lookup before the first side effect.

### `GemaMenuItems.ConcreteSubclassOf<T>()`

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.54.2
**Original location:** Line 20-21

> The remark says "the caller names what it added in the Undo entry". It does not — both Undo labels
> are `"Create " + go.name`, never the resolved type.

**Context:** Both reviewers flagged it. I wrote a mitigation into a doc comment and did not implement
the mitigation.

**Notes:** The clause was not merely inaccurate, it was **load-bearing for the design**: it was the
reason the arbitrary-first-match was acceptable. Removing the sentence would have left the ambiguity
genuinely silent, so the fix was to make the code do what the comment claimed — `ConcreteSubclassOf`
now counts candidates and reports at `LogError` when there is more than one. **A comment that
justifies a shortcut is a promise; either keep it or drop the shortcut.**

### `GemaMenuItems.ConcreteSubclassOf<T>()`

**Status:** Resolved
**Category:** Potential Bug
**Resolved in:** v0.54.2
**Original location:** Line 26

> `IsAbstract` is false for a generic type definition, `AddComponent` returns null for one, and the
> caller then NREs on `SetScreenType`.

**Context:** `code-reviewer` and `silent-failure-hunter` both reached it; dormant, since it needs a
branch declaring `class Foo<T> : ScreenViewBase`.

**Notes:** Filtered with `!t.IsGenericTypeDefinition`. Cheap because the line was being edited anyway
— which is the whole argument for fixing a dormant defect while you are inside the file.

### `GemaMenuItems` — `Resources.Load` of `App/`-backed prefabs

**Status:** Won't fix
**Category:** Architecture
**Original location:** Lines 197, 206

> `Prefabs/TextPanel` and `Prefabs/ImageGalleryPanel` resolve by GUID to prefabs carrying
> `App/UI/` MonoBehaviours. No compile-time reference, so L26's stated failure is genuinely gone —
> but `Assets/Editor/` is not App-free.

**Context:** `code-reviewer`. Verified: `Assets/01_Scripts/App/UI/TextPanel.cs` and
`ImageGalleryPanel.cs` both exist and are instantiated by resource path.

**Notes:** Deliberately left. A GUID reference cannot fail an assembly, so it is outside what L26 was
about — a branch deleting `TextPanel.cs` gets a missing-script instantiation, not a build that will
not compile. Recorded because the distinction is exactly the one this fix turns on: **L26 removed
App *names* from the editor assembly, not App *dependencies*.** The register entry says so in those
words, so that "the editor assembly is framework-only" is never inferred from the fix.

### The three inspectors — `editorForChildClasses` widening

**Status:** Resolved
**Category:** Design
**Resolved in:** v0.54.2

> `[CustomEditor(typeof(Base), true)]` applies to every subclass, where before only the type named
> `ScreenController` was covered.

**Context:** Raised as a risk in the review brief; both reviewers checked it and cleared it.

**Notes:** Cleared on evidence rather than assumption. All three bases are `abstract`, so nothing
binds to them exactly and the binding set in this repo is unchanged — measured live: `CreateEditor`
on each App component still returns the matching Gema inspector. All three editors draw with
`DrawPropertiesExcluding(serializedObject, "m_Script")`, which is type-agnostic, so widening cannot
hide a subclass field. `AppDataControllerEditor.cs:9` already used the idiom. The one real change is
recorded in `overview.md` §5b: a *second* subclass in a branch now gets the Gema inspector instead of
Unity's default.

### Pre-existing, not introduced, and now wider

**Status:** Open
**Category:** Potential Bug

> None of the three `OnInspectorGUI` overrides calls `serializedObject.Update()` before drawing, so
> displayed values can go stale after an undo or an external modification.

**Context:** `silent-failure-hunter`, noted as pre-existing.

**Notes:** Unchanged by v0.54.2, but it now applies to every subclass rather than one named type.
Not given a defect id: no symptom has been reported, and the register's own rule is that nothing
speculative gets an entry. Recorded here so the next person to touch these files has it.

## L27 — the duplicate `UIGeneratorModel` deleted (v0.54.3)

Two reviewers on a two-file deletion. `code-reviewer` **APPROVE WITH SUGGESTIONS**;
`architect-reviewer` **proceed as-is**, with three follow-ups. Both independently confirmed the
deleted copy was unreachable, and both raised the same branch-merge hazard.

### `Assets/ThirdPart/AssetGenerator/Editor/UIGeneratorModel.cs` — the deletion itself

**Status:** Resolved
**Category:** Architecture
**Original location:** Line 7
**Resolved in:** v0.54.3

> the only field of this type is `UIGeneratorData.cs:14`, in `Assembly-CSharp`, which cannot
> reference `Assembly-CSharp-Editor`. The Editor copy could never have populated `psdData` from any
> code, in this repo or a branch.

**Context:** stronger than the register's "zero consumers", which was a grep result. This is a
structural argument: the direction of the assembly reference makes the Editor copy unreachable by
construction, so no future editor script could have used it either without declaring its own field.
`architect-reviewer` reached the same conclusion from the other side — the deleted `.meta` GUID
`b912a6d8…` has zero references in any `.unity`/`.prefab`/`.asset`/`.meta`, and the type is a plain
class rather than a `MonoBehaviour`, so there is no missing-script risk of the kind L22 accepted
knowingly.

**Notes:** deleting the other copy was never an option — it is `CS0246` in `UIGeneratorData.cs:14,19`
inside `Assembly-CSharp`, which cannot see the Editor copy. There is no opposite case on *which
file*; there is one on *which behaviour*, which is L28.

### `Assets/ThirdPart/AssetGenerator/photoshopScript/UIGenerator_v1_00.jsx` — the exporter settles the intent

**Status:** Open — logged as L28
**Category:** Bug
**Original location:** Lines 52-54

> `UIGenerator_v1_00.jsx:52-54` walks `app.activeDocument.layers[0..n)` — Photoshop index 0 is the
> *topmost* layer — so JSON arrives top-first

**Context:** I had recorded the z-order question as unanswerable from the repo and written that into
the plan. It was not: the Photoshop exporter is in the tree, and `code-reviewer` found it. That
turns L28 from a speculative entry into an evidenced one — the exporter walks top-first,
`AssetList.cs:156` parents in array order, and a later sibling draws on top.

**Notes:** the correction is the point. I asserted a limit on what could be measured without having
searched the whole tool's folder — `photoshopScript/` sits beside `Editor/` and `Scripts/`. The
register entry records it, because "I said this was unmeasurable and it was not" is worth more than
a clean narrative. What genuinely cannot be settled here is the one Photoshop semantic the argument
rests on, and the absent `Assets/Resources/Demo/logo.json` that would have shown it.

### `Assets/ThirdPart/AssetGenerator/Scripts/model/UIItemsVO.cs` — keep `ReverseData()`

**Status:** Resolved
**Category:** Maintainability
**Original location:** Line 13
**Resolved in:** v0.54.3

> It must cite `UIItemsVO.ReverseData()` explicitly as "dead on purpose, do not sweep" — otherwise
> the next unused-code pass deletes the last trace of the intent and the defect becomes
> unreconstructable.

**Context:** both reviewers said keep it, for different reasons. `code-reviewer`: it is `public` on a
`public` type in `Assembly-CSharp`, so removing it is source-breaking for a branch, for a saving of
four lines. `architect-reviewer`: it is the last executable record of the reversal semantics. The
instruction is now in L28's entry in those words, because the ordinary reading of the code after
this commit is "unused method, delete it".

**Notes:** `code-reviewer` also found, out of scope, that `ReverseData()` NREs when `layers` is null
— a JSON element without a `layers` key deserializes to `null`, not an empty list. Folded into L28
as a trap for whoever restores the reversal, rather than given its own id, because it is only
reachable through the thing L28 describes.

### `docs/architecture/overview.md` §5b — the silent rebind for branches

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.54.3

> a branch editor script naming `UIGeneratorModel` binds to its own source copy today (CS0436,
> source wins) and will silently rebind to the non-reversing one after merge — no compile error

**Context:** raised independently by both reviewers, and it is the reason this deletion earns a §5b
row at all. It also corrects L27's own entry: there was no `CS0436` *here*, because no file in this
repo's editor assembly named the type — but there would be one in a branch that has such a file, and
that is precisely the project which breaks silently on merge. The entry had the warning right and
its location wrong.

### `Assets/Resources/Gema/Core/ApplicationDescription.cs` — a fourth duplicate, unregistered

**Status:** Open — logged as M118
**Category:** Bug
**Original location:** Line 13

> `Gema.ApplicationDescription` duplicates `EnigmaVirtual.ApplicationDescription` across
> Assembly-CSharp / EnigmaVirtual, with DIVERGED defaults (Client/Year/Copyright) and the SAME
> `[CreateAssetMenu(menuName = "Gema/Descriptor")]` on both

**Context:** found by `architect-reviewer` while answering "is there another instance of this
shape?", which is why the question was asked. Confirmed here independently before registering: the
GUID binding, all **9** consumer files, and both editor create paths were read. It is the same
silent-overwrite family as H19/M28/H23, reached a new way — a typed `LoadAssetAtPath<T>` returning
null for a *wrong-type* asset rather than an absent one, followed by an unconditional
`CreateAsset` over the same path.

**Notes:** not fixed in this commit, deliberately — it is a `ScriptableObject`, so a branch may hold
an asset bound to its GUID, and deleting the script would turn that into a missing script. That is
the risk L22 took knowingly and recorded, and it deserves its own round rather than a drive-by
delete inside another defect's commit. The census that found it also cleared the rest: zero
same-namespace-and-name pairs remain across all 288 `.cs` files, and three same-simple-name hits
(`FileDownloadEvents`, `PaginationVisibility`, `ScrollDirection`) are false positives — each nested
inside a different outer type. Recorded so they are not re-raised.

### `docs/architecture/known-issues.md` — the diff was code-only

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.54.3

> staged diff is code-only — L27 still "open", worklist row 3 and the "Low (27)" tier line
> unchanged, VERSION.txt still 0.54.2, no reviews.md/session entry

**Context:** correct at the moment it was reviewed, and the reason reviewers are run before the docs
pass rather than after. All of it is in this commit.

## M118 — the duplicate `ApplicationDescription` deleted (v0.54.4)

`code-reviewer` **APPROVE WITH SUGGESTIONS** on the code and **BLOCK** on committing without the doc
updates — correct at the time, and all of them are in this commit. `architect-reviewer` closed its own
finding from the day before, and opened two more (L29, L30) while confirming what M118 did and did not
close. **Seven findings**, one of which the two reviewers answered differently; the source settled it
against the answer I had already written into a §5b row.

### `Assets/Resources/Gema/Core/ApplicationDescription.cs` — reachability

**Status:** Resolved
**Category:** Architecture
**Original location:** Line 13
**Resolved in:** v0.54.4

> Nothing in `Assembly-CSharp` even *referenced* it.

**Context:** the pair rule applied in both directions and, unusually, the byte search was valid here —
`Assembly-CSharp.dll` (rebuilt 09:53, after the delete) contains neither `ApplicationDescription` nor
`Gema` as bytes, while `EnigmaVirtual.dll` contains the name. That works only because nothing
referenced the type; the L27 caveat about `#Strings` holding referenced names as well as declared ones
still stands as the general rule.

**Notes:** every indirect route was checked and is clean — GUID `624587…` and the folder GUID have zero
references; `m_TargetAssemblyTypeName: \S` returns only the four `FieldsVisibilityController` entries in
`CoreSetup.unity`; no `Type.GetType`, `AddComponent("…")`, `SendMessage` or `[SerializeReference]` route.
The two sibling prefabs bind 7 script GUIDs, none of them the deleted one, and
`MainControllerEditor.cs:126` loads `"Gema/Prefabs/Ui/VirtualKeyboard"`, which is unaffected because
only `Gema/Core/` was removed.

### A `.cs` under `Resources/` — why the location mattered, and why it did not

**Status:** Resolved
**Category:** Architecture
**Resolved in:** v0.54.4

> A `.cs` under `Resources/` is not runtime-loadable (`MonoScript` is editor-only), so the location
> bought it no reachability. What it *did* buy is compilation into `Assembly-CSharp` instead of the
> framework — which is precisely what made it the shadowed copy.

**Context:** worth recording because the first instinct is that a script under `Resources/` is
*more* reachable, not less. It is the assembly assignment that mattered: living outside any `.asmdef`
put it in `Assembly-CSharp`, which is scanned after `EnigmaVirtual` (the predefined assembly
references it), and registration order is what decides which duplicate `[CreateAssetMenu]` wins.

**Notes:** removing the last `.cs` from a `Resources` subfolder changes no `Resources.Load` path,
since paths resolve per asset rather than per folder.

### `docs/architecture/overview.md` §5b — the recovery window closes on its own

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.54.4

> the 5b row must say **"open `Assets/Resources/Descriptor.asset` in a text editor and copy your
> `Client`/`Year`/`Copyright` out BEFORE opening Gema → App Descriptor"**, not just "it becomes a
> missing script"

**Context:** the finding I had not reached on my own, and it changes the row from a description into an
instruction. My draft said "recreate the descriptor and re-enter your values", which assumes the owner
still knows them. They may not: with the script deleted the values survive only as YAML text, the
Inspector can no longer render them, and `AppDescriptor.cs:37`'s typed load returns null on a
wrong-type asset so `:40-41` `CreateAsset`s over that same path. Opening the window to look at the
problem destroys the evidence of it. The row is now three numbered steps in a required order.

**Notes:** the same review was precise about what this deletion does *not* do. The overwrite is not a
regression — a typed `LoadAssetAtPath<EnigmaVirtual.ApplicationDescription>` already returned null on a
`Gema`-typed asset before the delete, same null, same overwrite. What changed is **recoverability**,
and that distinction is why the row leads with "copy the values out first" rather than with a warning.

### Loud versus silent, for a branched project

**Status:** Resolved — recorded in `overview.md` §5b
**Category:** Design
**Resolved in:** v0.54.4

> | Own script naming `Gema.ApplicationDescription` | `CS0246` at compile | **Loud.** And an
> improvement: with both types present plus `using Gema; using EnigmaVirtual;`, a bare
> `ApplicationDescription` was `CS0104`.

**Context:** four of the five branch states are loud, and one is silent and destructive (an asset bound
to the deleted GUID sitting at `Assets/Resources/Descriptor.asset`). The deletion also *improves* two
cases: a branch with its own third `[CreateAssetMenu]` at that path now competes with one declaration
instead of two, and the `CS0104` ambiguity for a bare `ApplicationDescription` under both `using`s is
gone. A fix that removes a failure mode as well as a duplicate is worth noting, because the register
tends to record only what a change costs a branch.

**Notes:** the four runtime consumers (`AnalyticsSender.cs:24`, `AnalyticsPayload.cs:28`,
`DesignScaler.cs:26`, `AppInfo.cs:56`) all use soft `as` casts, so a null descriptor is silent at each
of them. That is pre-existing and not part of M118.

### `Assets/Editor/SystemUiEditor.cs` — a duplicate registration, one registry over

**Status:** Resolved
**Category:** Bug
**Original location:** Line 7
**Resolved in:** v0.54.5 (L29)

> second `[CustomEditor(typeof(CoreSystemUI))]`, duplicating `Assets/Editor/CoreSystemUIEditor.cs:5` —
> same target type, same assembly, same namespace. MEASURED: `Editor.CreateEditor` resolves to
> `CoreSystemUIEditor`; `SystemUiEditor` is dead code. Zero console messages.

**Context:** found while answering "does M118 close the shape?" — it closes the *cross-assembly type*
shape and not the *duplicated registration* shape, and this is a live instance of the second. Stronger
than L22's pair, which the register notes coexisted harmlessly because the two
`[CustomEditor(typeof(Positioner))]` classes targeted **different** `Positioner` types in different
namespaces. These target the same type, so one genuinely wins and the other can never run.

**Notes:** the two render differently — `CoreSystemUIEditor` draws `CoreEditorHelper.DrawHeader()` and
`DrawPropertiesExcluding(..., "m_Script")`, `SystemUiEditor` a plain `DrawDefaultInspector()` — so the
*winner* is visible in the UI while the *conflict* is not. Registered rather than fixed in M118's commit on the
reviewer's own advice: different registry, different file, and this family's history is four instances
found one at a time because each fix swept a folder instead of the shape. **Fixed immediately after,
in v0.54.5**, as its own change — which is the point of the advice rather than an argument against it.
The fix was verified by asking `Editor.CreateEditor` which inspector runs, before and after, rather
than by counting declarations: a census cannot tell whether the *surviving* inspector is the one that
was already winning, and that is the only thing an operator would notice.

### `Assets/Resources/Gema/Prefabs/Ui/AppInfoPanel.prefab` — dead, and shipped anyway

**Status:** Open — logged as L30
**Category:** Performance
**Original location:** the prefab itself

> `AppInfoPanel.prefab` has **zero** consumers of any kind — no GUID reference in any
> scene/prefab/asset, no string path in any script. A `Resources/` folder is packed unconditionally, so
> that is a dead prefab in every build.

**Context:** confirmed independently before registering — its GUID `322fb711…` appears nowhere, and no
script names it. Its sibling is the opposite case and is why the folder cannot simply go:
`VirtualKeyboard.prefab` has exactly one consumer, `MainControllerEditor.cs:126`, reaching it by
**string path**, which no GUID protects and no compiler checks.

**Notes:** the pairing is the useful part. `Gema/Prefabs/` looks exactly as dead as `Gema/Core/` did and
is not, so "the `Gema` namespace is retired" is not a licence to delete anything under a folder of that
name — that folder name has nothing to do with the namespace. Now stated in `overview.md` §5b's
v0.49.15 row, next to the retirement claim that would otherwise invite it.

### `Assembly-CSharp.Player.csproj` — stale by two years

**Status:** Won't fix
**Category:** Other
**Original location:** Line 136

> still `<Compile Include="Assets\Resources\Gema\Core\ApplicationDescription.cs" />` — CS2001 for
> anyone building that csproj

**Context:** it also still lists `Assets\Resources\ApplicationDescription.cs`, a path gone since
**v0.24.0**. `SyncVS.SyncSolution()` refreshes the four build csprojs and leaves the `.Player`
variants behind — confirmed by running it and re-checking.

**Notes:** `*.csproj` is gitignored (`.gitignore:25`), so this is a local IDE artefact, not shipped and
fully regenerable. `Assembly-CSharp.csproj` — one of the four compile authorities — is clean. Recorded
rather than fixed because it is a live demonstration of the "a csproj goes stale in both directions"
gotcha already in `CLAUDE.md`, and because chasing an untracked generated file is not worth a commit.

## L17 closed — the last three types leave the global namespace (v0.54.6)

Both reviewers **APPROVE WITH SUGGESTIONS**, and between them produced nine findings on a change with
no logic in it — which is the shape of a namespace migration: the code is trivial and everything it
invalidates is elsewhere.

### The 24 remaining global types — classified filename-blind

**Status:** Resolved — L17 closed on this evidence
**Category:** Architecture
**Resolved in:** v0.54.6

> Reproduced independently against the loaded `EnigmaVirtual` assembly, classifying by
> `ExtensionAttribute` on declared methods — which *is* the "declares a `this` parameter" test, and is
> filename-blind […] **21 declare ≥1 extension method; 3 declare zero** — `ComMessage`, `GUIManager`,
> `UnitySerialPort`, exactly the `External/` forks.

**Context:** this is the finding the close rests on, and I asked for it specifically because L17's own
recurring failure was bucketing a holder by what its file is called — corrected in five places in
v0.50.0 and drifted back in three of them by v0.54.0. A count I derived myself would have carried the
same risk. The smallest holders declare a single extension method each (`GraphicExtensions`,
`JsonExtensions`, `ToggleGroupExtension`), so none is a filename-only holder.

**Notes:** the review also refined the number that matters. **Three of the 21 holders are `internal`**
— `TransformExtensions`, `UIExtensions`, `WebRequestExtensions` — so they are invisible to `App` and to
a branch's own code and cannot collide with anything a branch declares. The collision surface L17
exists to describe is therefore **18 public holders plus 3 forks**, not 24. That is now in the entry
and in `overview.md`.

### `ToggleGroupExtension.cs` was filed where the next census would not look

**Status:** Resolved
**Category:** Architecture
**Original location:** `Core/Modules/UI/Forms/ToggleGroupExtension.cs`
**Resolved in:** v0.54.6

> the only 1 of 22 holder files outside an `extensions` folder […] this files the newly-extracted
> holder where the next census will not look

**Context:** I had extracted the holder into the folder it left, which is the intuitive choice and the
wrong one. 19 of the 20 other holders live in `EnigmaVirtual/Extensions/`. Leaving it under
`Core/Modules/UI/Forms/` would have reproduced this entry's exact failure mode one more time — a holder
sitting somewhere a filename-shaped census does not check. Moved to `Extensions/` with its `.meta`, so
the GUID travelled; re-verified afterwards with the full 17 assertions and four builds.

**Notes:** a good example of a finding that is not a bug and still worth acting on. Nothing referenced
the file, nothing would have broken, and the cost of leaving it was that the *next* person's census
would be wrong.

### Extension-method lookup order — the mechanism neither of us had listed

**Status:** Resolved — recorded in the L17 entry and `CLAUDE.md`
**Category:** Design
**Resolved in:** v0.54.6

> extension-method lookup from inside `namespace EnigmaVirtual` stops at the innermost namespace
> holding an applicable candidate, so `RegisterField.cs`'s `Field.text.IsValidEmail()` and
> `Group.GetActive()` now search `EnigmaVirtual.*` **before** global

**Context:** I had enumerated the silent-break vectors as GUIDs, `m_TargetAssemblyTypeName`,
`[SerializeReference]`, `AddComponent("…")` and `Type.GetType` literals. This one is introduced by the
*wrap* half of the change rather than by the move, and it is not a break today — the 7 extension
holders inside `EnigmaVirtual.*` declare neither name, so both calls still bind the global holders.
What it is, is a standing hazard: adding an applicable `IsValidEmail` or `GetActive` under
`EnigmaVirtual` later would silently rebind every call site in the wrapped file, with no compiler
complaint because both candidates are valid.

**Notes:** now a `CLAUDE.md` gotcha, because it applies to any future wrap and not to this defect.

### The move proved mechanical, and three of my numbers were wrong

**Status:** Resolved
**Category:** Code Quality
**Resolved in:** v0.54.6

> `StringExtensions.cs` HEAD lines 1–831 vs working-tree 1–831 — **identical**, zero diff hunks […]
> HEAD `StringExtensions.cs` was **859** lines, not 858 — the last line had no trailing newline, so
> `wc -l` undercounts by one.

**Context:** the byte-identity proof is what makes "behaviour-preserving" a measurement rather than a
claim, and it also confirmed the load-bearing detail: all 21 `Colors` members unchanged in name and
order, which matters because `Colored()` interpolates the member *name* into `<color={color}>`. The
corrections to my figures are small and worth keeping: `wc -l` undercounts a file whose last line has
no trailing newline, and `Group.GetActive()` is at `RegisterField.cs:66` after the wrap where I had
quoted the pre-wrap `:64`.

**Notes:** one real difference the proof surfaced, which I would otherwise have called a pure move:
`RegisterField.cs` line 19 held a **single space** and is now empty, because my indenter treated any
whitespace-only line as blank. Harmless, and stated rather than glossed.

### Two dead `using` directives

**Status:** Resolved
**Category:** Maintainability
**Original location:** `RegisterField.cs:2` and `:5`
**Resolved in:** v0.54.6

> `using System.Collections.Generic;` became unused when the holder took `IEnumerator<Toggle>` with it
> […] `using EnigmaVirtual;` inside `namespace EnigmaVirtual` is fully redundant

**Context:** both raised independently, and both are consequences of this change rather than
pre-existing untidiness, which is why they were fixed here. Verified before removing: no generic
collection type remains anywhere in the file. `System.Collections`, `System.Text.RegularExpressions`
and `System` were **already** unused at HEAD, so those were left alone as out of scope.

### `.meta` files for the two new scripts

**Status:** Won't fix
**Category:** Other
**Resolved in:** n/a

> both are hand-written 2-line stubs with no `MonoImporter:` block and no trailing newline, unlike
> every other `.cs.meta` here

**Context:** not hand-written — Unity generated them, and a forced
`AssetDatabase.ImportAsset(..., ForceUpdate)` reproduced the same 2-line form with the GUIDs preserved.
So this is what Unity 6000.3 emits for a `.cs` whose importer settings are all default; the fuller
`MonoImporter:` blocks on neighbouring files are legacy from older Unity versions.

**Notes:** the actionable half of the finding was real and was acted on — both `.meta` files were
**untracked**, and committing the `.cs` without them would have made every clone mint a fresh GUID.
Staged together with their scripts.

### The version marker

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.54.6

> the migration marker says "in v0.54.6", a version that will not exist — `VERSION.txt` went
> `0.54.5 → 0.55.0`

**Context:** the reviewers read the working tree's `VERSION.txt`, which holds **0.55.0** for a
concurrent session's package-removal work, not for this change. `HEAD` was 0.54.5, so 0.54.6 is the
next patch and the marker is correct once committed. A patch rather than a minor — as the three earlier
L17 slices used — because 0.55.0 is already spent in `overview.md` §5b on the other session's work, and
because the break here is compiler-caught. The §5b row carries the actual break regardless, which is
what a branch owner reads.

## L30 — the shipped duplicate prefab deleted (v0.54.7)

One reviewer, `performance-reviewer`, and it was the right single choice: the question was build weight,
and it answered by reading `Library/LastBuild.buildreport` rather than reasoning about file sizes. It
corrected two of my numbers, closed a gap in my audit method, and found something 39x larger than the
defect being fixed.

### `Assets/03_Textures/CoreSystem/GraphicGuide.png` — 34.7% of the asset payload, for a disabled object

**Status:** Open — logged as M119
**Category:** Performance
**Original location:** `CoreSetup.unity:19350`

> `GraphicGuide.png` — 26,222 B on disk, **8,294,585 B packed = 34.7% of the entire 23.9 MB asset
> payload** […] It is wired into scene 0 and **disabled**

**Context:** the largest packed asset in the build — bigger than Unity's own splash logo — and it is a
design-time alignment overlay with `m_IsActive: 0`. Verified independently before registering: the
import is 1920x1080 **RGB24** with 11 mipmaps, and the arithmetic lands exactly on the packed figure
(1920 x 1080 x 3 = 6,220,800, x 4/3 for the mip chain = 8,294,400, plus header). The 26 KB on disk is a
compressed PNG; what ships is the decompressed texture plus its mip chain.

**Notes:** it went to the register at **Tier 1** and is now the only item there. On this project's
hardware the memory half matters as much as the build half: unattended installations run for weeks, so
~8 MB held for something invisible at runtime never comes back. Not fixed here — it is a scene edit and
an authoring decision (delete the GameObject, or cap the texture at 256 with compression and no mips),
and it is not L30.

### Packed bytes versus YAML bytes — a 5.8x unit error, mine

**Status:** Resolved
**Category:** Documentation
**Resolved in:** v0.54.7

> **942 KB is the wrong number to quote.** `VirtualKeyboard.prefab` packs to **166,934 B (167 KB)** […]
> Same ratio on AppInfoPanel: 7,265 B YAML → 1,268 B packed.

**Context:** I had measured prefab weight with `wc -c` on the YAML and was about to write "~942 KB" into
a register entry. Serialized binary is far smaller than the text form — the keyboard's 964,758 bytes are
34,358 lines of `m_LocalEulerAnglesHint` and `m_AnchoredPosition` over 321 GameObjects. **L30's own
saving is 1,268 B, not the 7.5 KB its file size implies.** Both figures now come from the build report.

**Notes:** the same review confirmed the mechanism L30 rests on, in both directions and from one source:
the `Resources/` copy appears in `packedAssets` with zero references, and its byte-identical
`04_Prefabs/` twin is **absent entirely**. That is a cleaner proof than the entry had.

### `Core/Factory.cs` — the gap a literal sweep cannot close

**Status:** Open — recorded in L30, L31 and `overview.md` §5b
**Category:** Potential Bug
**Original location:** `Core/Factory.cs:32,41`

> `Resources.Load(folderPath + "/" + goName)`, `public static`, framework assembly, **zero callers in
> this repo** […] `Factory.cs:23-24` does `go.name = goName` on the result, so a null load is a
> `NullReferenceException`, not a clean null.

**Context:** I had enumerated every `Resources.Load*` **literal** in the repo (11, all resolving) and
treated that as covering the question. It does not: a public framework method that composes the path
from its arguments means a branch can reach any `Resources` path with no literal anywhere for a sweep to
find. And the failure is not a quiet null — the next line dereferences.

**Notes:** this is why §5b's v0.54.7 row tells a branch owner where the surviving copy is and what to
grep for, instead of asserting that nothing can break. The honest claim is "no consumer in *this* repo",
which is a different sentence.

### `Resources/` prefab duplication — sized honestly, and the intuition is wrong twice

**Status:** Open — logged as L31
**Category:** Performance
**Original location:** `Assets/Editor/MainControllerEditor.cs:126`

> Gross deps of the keyboard = 4,890,490 B packed. Marginal ≈ nothing […] Your hypothesis that
> dependencies "may be much larger than the prefab itself" is true gross, false marginal — reverse of
> the intuition.

**Context:** I asked whether the dependency graph dwarfed the prefab. It does gross — 4.89 MB — and does
not marginal, because `CoreUi.png` and `Lato-Medium.ttf` are already packed via `CoreSetup.unity`. Only
`coreUI/CoreUiEssential.png` (45,124 B) is unique to it. The whole pattern totals ~227 KB, 0.95% of the
payload, so L31 is registered at Tier 5 with that figure stated rather than as a build-size win.

**Notes:** one of the five small prefabs must **not** be swept up with the rest —
`PaginationItemPrefab` is a serialized `public GameObject` on `ScrollSnapBase.cs:60`, instantiated at
`:278`, and is held by GUID from `Resources/Prefabs/ImageGalleryPanel.prefab`, so it would be packed
regardless of the folder and moving it changes nothing. Exactly the kind of clause that stops a future
cleanup breaking something.

### Comparing prefab copies — use git blobs, not working-tree bytes

**Status:** Resolved
**Category:** Code Quality
**Resolved in:** v0.54.7

> your `556310ea…`/7,525 B is the CRLF working-tree form, so compare via `git show` or you will chase a
> phantom mismatch

**Context:** my identity check used `md5sum` on the working files. Both copies are identical either way
— blob `0bf33f66f0df08dc3971b0db850ed2cc`, 7,265 B — but anyone re-checking through `git show` would
have got different digits from the ones I published and had to work out why. The entry now quotes the
blob hash.

---

## `Assets/01_Scripts/EnigmaVirtual/Development/Console.cs` (L36, L37)

### `Console.OnGUI()` and `Console.DrawToolbar()`

**Status:** Resolved — fixed in v0.55.4 (L36)

**Category:** Bug / Unity-specific

**Original location:** Lines 191–192 (`OnGUI` width/height), 338–367 (`DrawToolbar`) — pre-fix, at `899a44a`

> the console draws 73 px past the right edge of a 1080-wide portrait screen, and the two causes
> point in opposite directions — the window *asks* to be 110 units too narrow and is *drawn*
> 168 units too wide

**Context:** Measured live in play mode at 1080×1920, `scaleFactor 0.95`. Every control in
`DrawToolbar` is `ExpandWidth(false)` and the row carried seven `GUILayout.Space(50)` plus one
`Space(20)`, giving it a hard minimum of **1168.7 GUI units** — 370 of them, 32%, pure spacing —
against **1096.8** available. `GUILayout.Window` sizes to the larger of the rect it is given and its
content's minimum, so it grew the window from the requested 1026 to **1194**, and `GUI.matrix`'s
0.95 scale put the right edge at 1153.3 px on a 1080 px screen. Separately, `Screen.width *
scaleFactor` should have been `/ scaleFactor`, since `GUI.matrix` scales GUI units *down*; that made
the *request* 110 units too narrow, and the `scaleFactor >= 1 ? (margin * 2) : 0` ternary subtracted
nothing at the shipped 0.95.

**Notes:** Three things worth carrying forward. **The two bugs masked each other** — a window
simultaneously asking to be too small and being drawn too big reads as one vague layout problem, and
fixing only the multiply would have left the toolbar still forcing the window off-screen.
**It is portrait-only**: at 1920×1080 the available width is 1981 units and the 1168.7 toolbar fits
easily, which is why it survived. And **a byte search could not prove the fix landed** — the
`FlexibleSpace` the fix introduces was already used by `UIKeyboard.cs:623`, so grepping the rebuilt
dll for it is true either way; the live `newWindowRect.width` measurement (1194 → 1096.84, exactly
the requested value) is the only proof.

---

### `Console.DrawLogList()`

**Status:** Open (L37)

**Category:** Bug / Unity-specific

**Original location:** Lines 302–305 (pre-fix, at `899a44a`); `:306–309` in the current file

> four `fixedWidth` writes onto the shared `GUI.skin`, inside the draw path, every frame, never
> restored

**Context:** `GUI.skin.verticalScrollbar`, `verticalScrollbarThumb`, `verticalScrollbarUpButton` and
`verticalScrollbarDownButton` are each set to 25 to widen the console's scrollbar for touch. `GUI.skin`
is global, not per-window, so the widening applies to every other IMGUI in the process — and the write
repeats per frame rather than once at init. This file already has the correct pattern for everything
else: build a `GUIStyle` in `EnsureStyles` and pass it to the scroll view.

**Notes:** **Unverified — the mechanism is read from source, not exercised.** Demonstrating it needs a
second IMGUI surface with a visible vertical scrollbar on screen at the same time as the console, and
no scene in this repo authors one. `Console` is the **only runtime IMGUI drawing surface in this repo**, which is what makes it dormant
here. `UIKeyboard.cs` looks like a second one and is not — every GUILayout call in it is
`EditorGUILayout` inside `#if UNITY_EDITOR` (`:518`) in `UIKeyboardEditor : Editor` (`:520`), and an
inspector draws against the *editor* skin, not the game skin this mutates. The only other runtime
`OnGUI` is `IO/RFIDReader.cs:122`, which draws nothing. Exercising it therefore needs a **new**
`OnGUI` with a scroll view. Live for any branch drawing its own IMGUI.

**The obvious fix is also not sufficient**, and that is worth knowing before someone writes it:
`GUI.VerticalScrollbar` resolves the thumb and the two buttons by name (`style.name + "thumb"`) from
`GUI.skin`, so a `GUIStyle` handed to the scroll view reaches only the trough — 3 of the 4 mutated
fields stay global. Save-and-restore around the draw is the shape that certainly works.

---

## `Assets/01_Scripts/EnigmaVirtual/Development/Console.cs` — review round of 2026-08-28 (M121)

Three agents on the v0.55.4 diff: `code-reviewer`, `performance-reviewer`, `doc-reviewer`.

### `Console.DrawLog()` — the unbounded repeat loop

**Status:** Resolved — fixed in v0.55.5 (M121)

**Category:** Bug / Performance

**Original location:** Line 294

> `maxLogCount` caps entries, not repetitions — one message repeated each frame coalesces into a
> single entry whose count grows forever, and the non-collapsed path draws one Label per repetition

**Context:** Found by `performance-reviewer` while reviewing an unrelated layout fix, and it outranks
the fix that prompted it. Verified independently by reading `ProcessLogItem` and `TrimExcessLogs`:
the coalescing branch increments `count` and does **not** call the trim, which only runs when a new
entry is appended. `isCollapsed` defaults to false, so the looping branch is the default, and
`settings.json` ships `DebugSettings.Console.isActive: true`.

**Notes:** The cost is gated by `isVisible`, which defaults false — so it is paid only when someone
opens the console, which is what you do when a message is repeating. The 216,000-labels-per-pass
figure is arithmetic from 60fps over an hour, not a measured run.

**Resolved in v0.55.5** with a `maxRepeatDraw` const of 50 and an overflow row carrying the true
total — a reported cap, not a silent one, because a cap you cannot see reads exactly like a message
that stopped repeating. Verified in play mode against a real 100,000-repeat entry: the assertion is
the scroll content size, since `BeginScrollView` clamps `scrollPosition.y` to content height minus
viewport. It clamps to **648.5** units; unbounded it would be roughly **2,300,000**. Three
allocation fixes rode along on the same draw path — a hoisted `LogType[]`, hoisted
`GUILayoutOption[]` for the two per-row options, and two prebuilt row styles replacing a per-row
managed-to-native `normal.background` write.

**The scroll view is virtualised as of v0.55.6.** Entries are measured once into a cumulative-offset
list, the visible range is binary-searched, and two `GUILayout.Space` calls stand in for the rows
above and below so the inner rect keeps its true height. The range is resolved on the **Layout pass
only** — IMGUI requires Layout and Repaint to emit identical controls, and `scrollPosition` can move
between them. Measured: 27 of 1000 entries drawn, and 52.7 fps at 1000 entries against 54.2 at 5,
which is the real claim — cost no longer scales with list length.

---

### `Console.OnGUI()` — dividing by an unvalidated public float

**Status:** Resolved — fixed in v0.55.4

**Category:** Bug

**Original location:** Line 195 (as introduced earlier the same day)

> `Screen.width / 0f` is `+Infinity`, and `Infinity - 40` stays `Infinity`, so an infinite Rect
> reaches GUILayout every frame — the old multiplying expression returned a finite 0 for that input

**Context:** Flagged independently by `code-reviewer` and `performance-reviewer`, which is why it was
treated as settled rather than argued. A regression introduced by the L36 fix itself: for
`scaleFactor == 0` specifically, the new expression is worse than the one it replaced. Negative
values were already broken both ways, and very small values are the case the fix gets right.

**Notes:** Fixed with a local `Mathf.Max(scaleFactor, .01f)` feeding both the matrix and the two
divisions — **a clamp, not a log**, because `OnGUI` runs every frame and a warning there would spam;
and not `[Range]`, which constrains the inspector but does not touch a value a branched project has
already serialized into its scene. Neither reviewer could exercise what Unity 6's IMGUI does with an
infinite `Rect`; the float arithmetic is certain, the downstream symptom is not.

---

### Two corrections to this file and the register, both from `doc-reviewer`

**Status:** Resolved — fixed in v0.55.4

**Category:** Documentation

> "fits down to `scaleFactor ~ 0.73`" is inverted; and the `Low` suggestion table's header is three
> columns while all 37 of its rows carry four cells

**Context:** The 0.73 claim had the direction backwards — available width is
`Screen.width / scaleFactor - 2 * margin`, so it *grows* as the factor falls, while content width
depends only on `logFontSize`. The row fits *up to* about 1.26; 0.73 is the occupancy fraction at the
shipped 0.95. Separately, the `Low` table header in `known-issues.md` was `| ID | Issue | Location |`
against four-cell rows, so every Low status — including the two written this session — was dropped by
any renderer.

**Notes:** The second one is the same defect class as the stray fifth cell found in the band table
earlier the same session, in the same file, and neither was found by reading — both came from parsing
the rows and counting cells. That is the argument for the file's own rule about checking table shape
before trusting any count taken from it.

---

### `Console.IsScrolledToBottom()` and `Console.ScrollToBottom()`

**Status:** Resolved — fixed in v0.55.7 (M122)

**Category:** Bug

**Original location:** Lines 502–513 (pre-fix, at `a29824f`)

> `Mathf.Approximately` on a content height of a few thousand pixels demands agreement to about six
> thousandths of a pixel, between two quantities derived differently — so the console never followed
> new messages at all

**Context:** Reported by the user, not by a reviewer, and pre-existing — the v0.55.4–v0.55.6 console
work changed neither method. Measured before touching anything: with 200 entries seeded and the
console open, `scrollPosition.y` stayed at `0.0` while the list grew to 206. Starting deliberately at
the bottom froze at 5030.8 while the true bottom moved to 5057.5.

**Notes:** Two causes, and fixing either alone would have left it broken. The equality is now a
one-row tolerance against the real maximum; and opening the console jumps to the newest message,
because following only resumes once you are already at the end, so a view that opens at scroll 0
could never catch up on its own.

**The tidier-looking half of this was wrong and got measured before it shipped.** Replacing the
`int.MaxValue` sentinel in `ScrollToBottom` with `innerScrollRect.height - outerScrollRect.height`
reads better and lands **one row short of the end** — the outer rect includes a border the visible
viewport does not, which is exactly what the old code's `- GUI.skin.box.padding.vertical` was
compensating for. Only `BeginScrollView` knows the exact clamp. The sentinel stays, with a comment
saying why, so nobody tidies it again.

---

## `Assets/StreamingAssets/config/settings.json`

### `AppData.Cache.AssetDownload.cleanUnlistedAssets`

**Status:** Resolved — fixed 2026-08-28 (M123)

**Category:** Bug / Security

**Original location:** Line 40

> a commit whose body says "No code change" flipped the recursive-delete flag on in the shipped
> template, and it stayed on for nine days

**Context:** `cb24c5f` (2026-08-19) changed two values in the shipped config —
`cleanUnlistedAssets` `false`→`true` and `Sockets.isActive` `false`→`true` — while stating no code
changed. Both read as an operator's local machine state committed by accident. The Sockets one was
reverted in v0.54.8; this one was not. With the flag on, `AssetManager` recursively deletes every
file under the asset root the current `appData` does not reference, at boot, unattended.

**Notes:** Three separate places asserted the flag ships `false` while it shipped `true` —
`CLAUDE.md`, `AssetManager.cs:306` and `AppDataController.cs:436` — so reading the code could not
have caught it. What did catch it was L12's `BuildValidator` gate, months later, during an unrelated
build-size measurement that happened to attempt a *release* build. Development builds skip that rule
by design, so nothing checks the shipped config on the path anyone actually uses day to day. **The
gate is not the gap; the detection delay is.**

---

### `Console.HandleDragScroll()` — the drag started from a sentinel

**Status:** Resolved — fixed in v0.56.1 (M124)

**Category:** Bug

**Original location:** Line 555 (the `MouseDown` case, as shipped in v0.56.0)

> two passing tests, and the feature did not work for a single real user, because both tests began by
> replacing the one value the bug lived in

**Context:** `_dragStartScroll = scrollPosition.y` at press time. While the console is following new
messages — its normal state — `ScrollToBottom` parks `int.MaxValue` there for `BeginScrollView` to
clamp, so a drag started from 2,147,484,000 and every plausible drag still landed past the end.
Fixed by recording what `BeginScrollView` actually resolved (`_clampedScrollY`), captured before the
tail-follow can park its sentinel.

**Notes:** The user's phrasing is what located it: *press does something, drag does nothing, release
does something*. That rules out event delivery and points straight at the value the drag starts from.
Worth keeping as a reminder that a precise symptom report beats a re-run of the same tests.

**The testing lesson outlives the defect.** v0.56.0 shipped with 11 of 11 unit assertions and a real
OS-mouse integration test, and both opened with `scrollPosition = 1500` "to get a deterministic
starting point". That line deleted the precondition. Now recorded in `CLAUDE.md` beside the existing
rule about steps that never reach the code under test — this is its sibling: the step reached the
code, but the arranged state was the safe one.

---

## `Assets/01_Scripts/App/Timeline/TimelineCard.cs`

Client feature (Museu Salvador Caetano timeline), reviewed before delivery. Not a framework defect,
so no register id: the code is under `App/` and never shipped.

### `TimelineCard.BuildDots()` — slide indicators overflow a narrow card

**Status:** Resolved — fixed 2026-10-07, before delivery

**Category:** Bug

**Original location:** Line 172 (the `TimelineUi.TopLeft` call placing each dot)

> fixed pitch overflows a category gallery larger than 19 on a narrow card; with 30 images the
> first dot lands at x = -122

**Context:** Dots count the *category* gallery, not the event's own images, so one card can carry
many. Each dot sat at `right - (count - i) * pitch + spacing` with a fixed 12 px pitch, which leaves
the card's 232 px inner width once the count passes 19. Fixed by `TimelineLayout.FitDots`: the
default 6 px dot and 6 px gap are kept while they fit, and beyond that both shrink by one common
factor, so the strip always ends at the right edge and starts at or after the left.
`TimelineLayoutSelfCheck` asserts the 30-image bounds, and the 3-dot and 1-dot cases as the
counter-test that small galleries are unchanged.

**Notes:** The first version of the fix put a shrunk strip's origin at `width - total * scale`. That
failed the self-check at -0.000008 px, which is float rounding. A shrunk strip now starts at exactly 0,
and the check's end tolerance is an explicit 0.001 px.

A very large gallery now gets very small dots - 30 images give 3.9 px dots on a narrow
card - rather than dots outside the card. No pagination was added, because nobody asked for it. The
dummy content cannot reach this case: it ships two image files, so every category gallery has at
most two slides. It is covered only by the self-check.

---

### `TimelineCard.Bind()` / `ShowSlide()` — cost scales with the category gallery

**Status:** Open — projection, unverified; no action at the current content size

**Category:** Performance

**Original location:** `BuildDots` (one Image per gallery slide on every card) and `ShowSlide` (called
from `Bind`, so every card requests its first texture when built, including cards off-screen)

> every card builds the full category dot gallery and eagerly loads its initial texture including
> offscreen; at 300 events and 30-image galleries that projects to ~11,700 GameObjects per rebuild and
> ~300 full-HD textures, ~2.3 GiB

**Context:** Both costs are real code paths, read from source. The *figures* are the reviewer's
projection for content this project does not have, and none of them was measured. The shipped
content is 30 events and two ~250 px placeholder images, deduplicated per category, so each gallery
has at most two slides. The texture cache keys by URL, so at most two textures are ever decoded.
Every filter or language change rebuilds all cards, which the code marks as a known ceiling.

**Notes:** Deliberately not acted on. Pooling, lazy texture loading or framework cache limits would
be speculative at this size. Revisit when the real content arrives: count events and unique images
per category, then measure a rebuild and the texture memory in a player before choosing a fix. No
register id, because there is no code-backed live case.

---

### `TimelineCard.AddFrame()` — an unsliced frame would cover the card

**Status:** Resolved — fixed 2026-10-07, before delivery

**Category:** Bug

**Original location:** `AddFrame`, the `TimelineUi.MakeImage(parent, "Frame", ...)` call

> with a missing frame sprite, or one whose border is Vector4.zero, MakeImage builds a Simple Image;
> fillCenter = false is then ignored and the overlay covers all text and the photo

**Context:** `TimelineUi.MakeImage` switches to `Sliced` only when the sprite has borders, and
`fillCenter` applies only to sliced Images. A null sprite gives a solid `frameColor` rectangle. A
borderless sprite is stretched whole. Either way the overlay sits on top of the card as its last
child. Fixed with a guard at the top of `AddFrame` that returns when the sprite is null or
borderless. The card then shows its black fill and content without an outline.

**Notes:** The shipped `Rectangle 45@2x` has a 5 px border, so the shipped configuration never
reached this. It matters for an empty Inspector style field, or a frame sprite swapped for one
without slicing borders.

---

## `Assets/01_Scripts/App/UI/CallBackgroundAnimation.cs`

Client feature, reviewed before delivery. No register id.

### `CallBackgroundAnimation.Awake()` — could disable an animation that had already started

**Status:** Resolved — fixed 2026-10-07, before delivery

**Category:** Unity-specific / Bug

**Original location:** `Awake`, the unconditional `enabled = false`

> Awake unconditionally sets enabled = false; it can run after Controller.Awake -> Call.Show -> Play
> and disable an already-playing animation, because Unity does not define Awake order across objects

**Context:** `ScreenControllerBase.Awake` navigates to the start screen, Call, and `CallScreenview.Show`
calls `Play`. When the component's own `Awake` ran after that, it switched off a playing animation:
the background stood still on the first screen with nothing logged. `Play` would not restart it,
because `_playing` stayed true. Fixed: `Awake` disables only when not playing, and `Update` returns
while not playing, so neither order can leave a stale or stopped state.

**Notes:** `SelfCheck` calls `Awake` explicitly in both orders, since outside Play mode Unity sends
none: Awake then Play, and Play then Awake. Test plan step 8 in the 2026-10-07 session log is the
live startup check.

---

## `Assets/01_Scripts/App/Screenview/TimelineScreenview.cs`

Client feature, reviewed before delivery. No register ids, for the same reason as above.

### `TimelineScreenview.Rebuild()` — events with an unknown category vanish silently

**Status:** Resolved — fixed 2026-10-07, before delivery

**Category:** Potential Bug

**Original location:** Line 451 (`categories.TryGetValue(item.categoryId, ...)`)

> an event whose categoryId is not in categories is never in any filter, not even "all", so it simply
> disappears with nothing logged

**Context:** "All categories" is the set of ids in `categories`, so an orphaned `categoryId` matches
no filter. Fixed by `ReportHiddenEvents`. Once per screen lifetime, it reports every such event at
`LogError` (compiled on every target), naming the event id, year and category id. It also reports
null entries and years outside 1920–9999 (see `TimelineLayout.cs` below). The before-1920 case used to be a single `LogWarning`
count, which iOS and WebGL strip.

**Notes:** Checked once rather than per rebuild, because content is fixed after boot and each
filter tap would otherwise repeat the same errors. The shipped sample data has no orphan.

---

### `TimelineScreenview.Fit()` — overflowing text reported at a strippable level

**Status:** Resolved — fixed 2026-10-07, before delivery

**Category:** Potential Bug

**Original location:** Line 492 (`Development.LogWarning` on `!fit.fits`)

> text that does not fit a wide card at the minimum size ends in an ellipsis, and there is no detail
> view, so the operator must be told - LogWarning is stripped on some targets

**Context:** Now a `LogError`, once per event and language, naming both. The overflow is
per-language, so the same event can fit in PT and not in EN.

---

### `TimelineScreenview` — back button not visible on screen

**Status:** Resolved — workaround verified 2026-10-07 by the final Play capture; mechanism unknown

**Category:** Bug

**Original location:** `BuildBackButton()` / `BuildChrome()`

> the Timeline back button object exists and is wired, but is invisible in a captured frame, while the
> same sprite on Home's button is visible

**Context:** Read from source, these causes are ruled out:
- **Geometry:** the runtime button has anchors and pivot (0,0), position (111,60) and size (54,50),
  the same values as Home's authored button.
- **Sprite:** `Rectangle 45@2x` has a 5 px border, so it draws sliced like Home's.
- **Layer:** the canvas is Screen Space Overlay, which does not cull by layer.
- **Covering objects:** nothing drawn after it overlaps that rect. The scroll area ends at
  y = 960 (top-based), above the button at 970–1020, and the footer is text with no background.

One case is now reported instead of silent. If `backButton` is assigned in the Inspector to a button
outside this screen, such as Home's, it fades out with that screen and no own button is built.
`BuildChrome` now logs that at `LogError` and builds its own button.

**Notes:** The Play-mode probe confirmed what the source suggested:
- world rect at (111,60), 54×50
- sprite `Rectangle 45@2x`, colour white, alpha 1, inherited alpha 1
- 36 vertices, not culled, Screen Space Overlay
- layer 0 or 5 made no difference

With `sprite = null`, colour magenta and `Type.Simple`, **both** the frame and the white arrow became
visible. So the hierarchy, layout and culling are fine. The failure is specific to the sliced frame
sprite on this runtime-built Image, and the exact mechanism is not established.

**Change made, not yet verified:** the frame is now four plain 1 px edge Images, with no sprite and a
transparent root Image as the hit area. `raycastPadding` is -24 px on each side for touch, so the 54×50
visual is unchanged. Transition is None, and the arrow is unchanged.

**Resolved in:** the final Play capture of 2026-10-07 shows the 1 px outline and the arrow at bottom
left, and the back callback returns to Home. **Still unknown:** why the sliced `Rectangle 45@2x` did
not render on this runtime-built Image when the same sprite renders on Home's authored button. The
edge outline avoids that path; it does not explain it. The enlarged touch area was not exercised
with real touch.

---

## `Assets/01_Scripts/App/Timeline/TimelineLayout.cs`

Client feature, reviewed before delivery. No register id.

### `TimelineLayout.LastDecade()` / `ItemsInDecade()` — the axis trusted any year in the JSON

**Status:** Resolved — fixed 2026-10-07, before delivery

**Category:** Security / Bug

**Original location:** `LastDecade`, the `Math.Max(last, DecadeOf(item.year))` line

> LastDecade trusts arbitrary JSON year; a year of 10000000 makes ~1M decades, each allocating GameObjects
> on the main thread

**Context:** `Rebuild` draws one boundary per decade from 1920 to `LastDecade`, so the year range
directly sets the GameObject count. A malformed or hostile `appData.json` could freeze the kiosk.
Fixed with `TimelineLayout.IsSupportedYear`, which allows four-digit years 1920–9999.
`LastDecade`, `ItemsInDecade` and `BuildGalleries` all exclude other years, so an event that is
never shown contributes no slides either. `TimelineScreenview.ReportHiddenEvents` logs each such
event once at `LogError`, naming its id and year. The highest possible decade is therefore 9990, so
`decade += 10` cannot overflow. The closing label after it is `10000`, drawn whole rather than split
into "10" over "000".

**Notes:** `TimelineLayoutSelfCheck` covers 10000000, `int.MaxValue` and 1919 as excluded, and
9999 → 9990 as included. The live run passed with 39 checks: 29 original, 5 dots and 5 year range.

---

## `docs/architecture/overview.md` and `docs/sessions/2026-10-07.md` — doc review of the timeline feature

### `overview.md`, client subsection — "UI is built in code" overstated

**Status:** Resolved — fixed 2026-10-07

**Category:** Documentation

**Original location:** Lines 343–346

> says both screens create back buttons at runtime and the scene objects carry only three components;
> Home uses an authored, serialized back button and authored logo and side chrome, and every screen
> object also has a background Image

**Context:** The paragraph was written before the collaborator's authored Home chrome was
accounted for. It is rewritten as two lists: what is **authored** in `Intro.unity` (component,
CanvasGroup, background Image, plus Home's logo, side texts and Back Button), and what each screen
**builds at runtime**. The second list says only the Timeline builds its own back button. The
setup utility's scope is listed separately.

---

### Session log 2026-10-07 — "Requirements as confirmed" contained defaults

**Status:** Resolved — fixed 2026-10-07

**Category:** Documentation

**Original location:** `## Requirements as confirmed with the user`

> the confirmed-requirements list includes choices the user did not confirm: URL deduplication,
> independent per-card gallery positions, scroll reset on filter change, allowing all categories off,
> the specific 260/380 widths, the decorative hint and footer options

**Context:** Some were set by the orchestrating session as routine defaults, and others came from
the implementation. Listing them as confirmed would make a later reader treat a default as a client
decision. They are now under *Decisions / defaults*, with who set each one, and the confirmed list
keeps only what the user stated.

---

### Session log 2026-10-07 — image-presentation test plan could not reach its subject

**Status:** Resolved — fixed 2026-10-07

**Category:** Documentation

**Original location:** `## Follow-up: image presentation`, test plan step 4

> step 4 says to drop portrait/square/wide files into StreamingAssets/assets/, but appData.json does
> not reference them, so the test never reaches those images

**Context:** Images are loaded only for URLs listed in an event's `images`, so an unreferenced file
stays on disk and the step passes or fails on the placeholders alone. The step now names the two
routes that reach the code: add the URL to an event in `appData.json` and restart, or set
`AspectRatioFitter.aspectRatio` directly on a temporary card. The log also says root used the
second route, which proves geometry rather than real portrait or wide files.


## `docs/sessions/2026-10-07.md`

### Axis baseline test plan

**Status:** Resolved
**Category:** Documentation
**Original location:** Line 301

> Re-enabling one sparse category does not necessarily restore baseline width to content width.

**Context:** The width is the maximum of content and viewport. The test now re-enables all categories for the content-width assertion.
**Resolved in:** 2026-10-07 marker follow-up, before reporting completion.

## `docs/architecture/overview.md`

### Dynamic category tag minimum

**Status:** Resolved
**Category:** Documentation
**Original location:** Line 386

> The stated minimum omitted the safety cap when configured card width is below tagWidth / 0.9.

**Context:** Corrected to min(tagWidth, 0.9 × card width); the default 260/380 px cards retain the requested 160 px minimum.
**Resolved in:** 2026-10-07 dynamic title follow-up. The session's pending-build statement was also replaced with measured results.

## `docs/sessions/2026-10-07.md`

### Text-only frame colour test

**Status:** Resolved
**Category:** Documentation
**Original location:** Line 467

> Screen capture samples are not identical to configured UI colour values in this rendering path.

**Context:** Root measured exact #191919 live Image colours but captured dark fills at RGB22. The plan now asserts live colour values and visual uniformity; marker centre/stroke capture values were measured separately. The pending-build claim was replaced with completed verification and explicit remaining checks.
**Resolved in:** 2026-10-07 text-only border follow-up.

## `docs/architecture/overview.md`

### Call animation setup scope

**Status:** Resolved
**Category:** Documentation
**Original location:** Line 392

> Setup now wires Call, so saying it does not touch Call is stale.

**Context:** Corrected scope to component/font/photos while retaining authored background colour. The session's obsolete manual-import/year-filename instruction was also removed; explicit original filename mappings and automatic import are documented.
**Resolved in:** 2026-10-07 Call background follow-up, after doc review.

## Call animation publication documentation review

**Status:** Resolved
**Category:** Documentation

The final doc review found stale manual import/year filename requirements, a pending-verification statement, and an Update description predating the alpha pulse in the session log. Corrected each against the current setup implementation and measured verification results before publication.

