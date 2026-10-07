# unity_default_6

Gema's Unity base project. **Every client project is branched from here** — anything under
`Assets/01_Scripts/EnigmaVirtual/` is shared code, so a bug fixed (or introduced) here ships
to all of them. Treat framework changes as breaking-change candidates.

Unity 6 (6000.3) · Built-in RP · legacy Input Manager · .NET Framework API profile · Mono (Standalone)

---

## Read before making changes

- **The most recent file in [`docs/sessions/`](docs/sessions/) — read this first, every new
  session.** It is the only place the *reasoning* survives: what was tried and rejected, what a
  measurement actually returned, what is half-finished, and which plausible-sounding explanation
  turned out to be wrong. The other three docs tell you the state; this one tells you how it got
  there and what the last person would have told you in person. `## Open / next` at the bottom is
  where to start. (Then create today's log — see *Session workflow*.)
- [`docs/architecture/overview.md`](docs/architecture/overview.md) — layers, boot sequence,
  global state, config pipeline, platform matrix, conventions.
- [`docs/architecture/known-issues.md`](docs/architecture/known-issues.md) — the open defect
  register with stable IDs (C1–C8, H1–H22, M1–M84, L1–L16, E1–E2). **Check this before touching a
  file** — the bug you're about to find is probably already logged, and the entry says whether
  it's live or dormant. Its **Worklist** section at the bottom is the status board: what's next,
  in order, with severity/effort/impact. Start there when asked "what's left?".
- [`docs/reviews.md`](docs/reviews.md) — archive of review commentary that used to live inside the
  source files. **Not an instruction file and not a second register**: `known-issues.md` still owns
  open defects, statuses and the worklist. Consult only the section for the file you are working on.
  **Source comments no longer carry defect ids** — as of 2026-08-14 all 642 were removed, along
  with 55 attributions to the review process. Keep it that way: a comment should state the
  constraint a maintainer needs, and the id belongs in the register. Two things are deliberately
  exempt — migration markers (`renamed in v0.28.0`, `internal since v0.24.0`) are documentation, and
  **47 ids remain inside string literals and `[Obsolete]` messages** across 16 files, which are
  code: their text reaches an operator, so changing one is a behaviour change. `BuildValidator`
  accounts for 14 and names them on purpose. Two traps when counting these: grep the comment
  *portion* of every line rather than anchoring to `^\s*//`, which also misses a file whose BOM sits
  before the slashes; and exclude `%`-prefixed tokens, because `%C0`–`%C9` and `%E0`–`%E9` in
  `StringExtensions.cs` are percent-encodings that look exactly like ids — 20 of them, enough to
  turn 47 into 67. Every figure quoted here was published wrong at least once before being measured
  properly, so re-measure rather than quoting. **And write the *reason* beside the number, not just
  the number**: a count that travels alone is an input to the next measurement, while a count that
  travels with why it is that value is a constraint on it. L17's holder/blocked split was corrected
  in five places in v0.50.0 and had drifted back in three of them by v0.54.0 — the two later slices
  recomputed it as the census moved, and only the one site carrying the reason ("it declares
  `GetActive(this ToggleGroup)` and so must itself stay global") survived. The three bare counts
  did not, and one of them was a worklist row instructing someone to do the forbidden work.

## Review before committing

Eight agents live at user level (`~/.claude/agents/`), so they're available in every project.
Conventions in *this* file take precedence over their generic guidance.

**Six reviewers.** All are read-only, diff-scoped, cite `file:line`, drop findings below 80%
confidence, and are terse by default — add `verbose` to the invocation for the full report with
confidence scores.

| Agent | Run it when |
|---|---|
| `code-reviewer` | Any code change, before committing. General correctness. |
| `silent-failure-hunter` | Something "does nothing" with no error; changes to error handling, coroutines, config loading, or reflection; before shipping to a site. |
| `performance-reviewer` | Changes to per-frame code, loops, collections, data loading, or UI; anything that stutters or grows over hours. |
| `security-reviewer` | Changes touching credentials, paths, network, serialization, input, or process control — and before any client deployment. |
| `architect-reviewer` | A new module, a refactor, an interface or public-signature change, anything crossing a layer boundary. Traces dependency direction and cycles — **the M20 check**: it looks for exactly the framework→`App` references that block the `.asmdef` split. Also flags a changed public signature as a breaking change for every branched project, even when it compiles here. |
| `doc-reviewer` | `docs/`, this file, or XML doc comments change — **and after any fix lands**, since `known-issues.md` cites ~100 `file:line` locations that drift as soon as a file is edited. |

**Two that aren't reviewers**, so the rules above don't apply to them:

| Agent | Run it when | Note |
|---|---|---|
| `unity-game-developer` | Building or changing Unity/C# code, editor tooling, or build config. | **Can write and edit.** It reads `ProjectVersion.txt`, `GraphicsSettings.asset`, `activeInputHandler` and `manifest.json` before writing, so it won't reach for URP, the new Input System, Addressables or ECS here — none of which this project uses (see M19 for what happened when URP assets landed anyway). It cannot compile, and is told never to claim it did. |
| `task-decomposition-expert` | A goal too large to start on — a migration, the `.asmdef` split, a multi-phase refactor. Produces a work breakdown, critical path and risk register; doesn't execute. | Not for work that's already one clear task. |

The C#/Unity blocks encode the failure modes this codebase actually produced, so they're
tuned for it: `&&`/`||` precedence, destroyed-object null checks, wrong-object `GetComponent`,
undisposed `UnityWebRequest`/streams, undestroyed textures, off-main-thread Unity calls,
coroutine exit paths that never signal a `WaitUntil`, hardcoded path separators, destructive
`File.Delete` on computed roots, and reflection over the wrong member kind. `unity-game-developer`
carries the same list as things *not to write*, so the builder and the reviewers agree.

**`silent-failure-hunter` is the one to reach for first on this codebase.** Silent failure is its
dominant defect pattern — C3, C8, H3, H5, M13 and M26 are all "does nothing, says nothing" — and it
reports *dormant* findings
(real defects on a path the shipped config currently disables) separately, which is exactly the
category that bites when a project flips `TextureCache.isActive` on. Note `AssetDownload.isActive`
ships **`true`** — asset downloading is live out of the box, and has been since before anyone wrote
otherwise here (H21).

> **Client project note (Museu Salvador Caetano):** this branch overrides the template and ships
> `AssetDownload.isActive: **false**`. Its content is offline sample data with no asset host. Every
> "ships `true`" in this file and in `known-issues.md` describes the shared template, not this
> project. See the client subsection at the end of `overview.md` §5.

## Review findings go in `docs/reviews.md` — never in a source comment

**Standing rule, from 2026-08-14.** Every review finding — from an agent, from a human, from your
own reading — is written to [`docs/reviews.md`](docs/reviews.md). Nothing about the review lands in
the code.

**What belongs where:**

| | Goes to |
|---|---|
| A finding, its severity, its status, who found it, what an earlier draft got wrong | `docs/reviews.md` |
| An **open defect** that needs scheduling — id, tier, worklist row | `docs/architecture/known-issues.md` |
| The **constraint** a maintainer must not break | the source comment |

The third one is the test. A comment says *what is true and must stay true*; it does not say who
noticed, when, or what it used to be. "Do not add `.webp` — Unity cannot decode it" is a comment.
"`silent-failure-hunter` caught this in v0.49.4 (L8)" is a review finding.

**Concretely, never write into a source comment:**

- a defect id — `(M38)`, `H17's lesson`, `same shape as C3`
- an agent or reviewer name, or "found in review", "both reviews", "caught by review"
- "an earlier draft of this comment claimed…", or any record of a correction to the comment itself
- a version-stamped account of a fix — *"broken until v0.32.0"*. A **migration marker** is fine and
  wanted (`renamed in v0.28.0`, `internal since v0.24.0`): that tells a branched project what moved
  under it, which is documentation.

**Format** — follow what is already in the file. Per source file, then per member:

```markdown
## `Assets/01_Scripts/.../Thing.cs`

### `Thing.Method()`

**Status:** Open | Resolved | Won't fix
**Category:** Bug | Potential Bug | Performance | Architecture | Maintainability | Readability |
Code Quality | Design | Security | Unity-specific | Documentation | Other
**Original location:** Line 142

> the finding, quoted

**Context:** what a reader needs to understand it.
**Notes:** anything that qualifies it.
```

When a finding is resolved, **update its entry in place** — `Status: Resolved`, add `Resolved in`.
Do not add a second entry, and do not delete the first: the point of the archive is that the
history stays somewhere other than the code.

**Do not fragment the truth.** `known-issues.md` owns open defects, ids, statuses and the worklist;
`reviews.md` owns review narrative. A finding that is also a schedulable defect gets an id in the
register and a cross-reference here — not two competing descriptions. This project has already been
bitten by two competing version truths (M27); two competing defect truths would be worse.

**Two habits the 2026-08-14 cleanup earned, both learned the hard way:**

- **Re-read the whole sentence after removing a clause from it.** Stripping an attribution
  mid-sentence leaves a verbless fragment, and a scripted find-and-replace will not notice.
  Three review rounds each found danglers created by the previous round's fix.
- **Measure with a parser, not a grep, and never quote a number you have not just measured.**
  Every count in this area was published wrong at least once: a grep anchored to `^\s*//` misses a
  BOM-prefixed file; `%C0`–`%C9`/`%E0`–`%E9` percent-encodings look exactly like defect ids; and a
  grep for two-argument calls counts the commas inside `string.Concat(...)`.

## Docs are part of the change, not a follow-up

**Every fix and every review updates the `.md` files it invalidates, in the same commit.** Not
"if something changed" — check all five, every time. A doc that describes a state which no longer
exists is worse than no doc, because the next person trusts it.

| File | Update it when |
|---|---|
| `docs/reviews.md` | **Any review produces a finding.** This is the standing home for review output — see the section below. Never leave a finding in a source comment. |
| `docs/architecture/known-issues.md` | An issue's **status** moves (`open`→`fixed`/`dormant`/`partly fixed`), a new defect is found, or a finding turns out to be wrong. Also re-point any `file:line` citation in a file you edited — the register cites ~100 and they drift on every edit. Re-point by **grepping the anchor**, not by shifting the number — a mechanical remap keeps a citation landing on real code while still describing the wrong construct, which is most of what the 2026-08-13 sweep found. **And update the Worklist at the bottom**: move the row out of its tier, re-count the `n of N defects fixed` header, and re-order what's left if the fix changed what's now most urgent. A worklist that still lists a fixed item is the fastest way to lose trust in the whole file. |
| `docs/architecture/overview.md` | The **described behaviour** changed: boot sequence, config pipeline, platform matrix, threading model, conventions, prefab contents. If the fix changes what the framework *does*, this is stale until you touch it. |
| `CLAUDE.md` (this file) | A **convention or gotcha** changed, or you discover a stated rule isn't true. Both have happened — see the framework→`App` note and the `DO_LOGS` gotcha. |
| `docs/sessions/<today>.md` | Always. What changed, decisions **with the why**, what's still open, what was verified and how. |

Two habits that make this work:

- **Write the doc edit before reporting the fix as done.** Saying "I updated the register" and then
  not doing it has happened; the file is the source of truth, not the summary.
- **Run `doc-reviewer` after a fix lands**, not just after editing docs. It cross-checks doc claims
  against source and catches drifted `file:line` citations, stale platform claims and conventions
  that no longer hold.

Grade the fix by whether someone reading only the docs afterwards would get the right picture.

## The editor is drivable — check it, don't describe it

`com.unity.pipeline` (added 2026-08-13) runs an HTTP server inside the live editor, so compiling,
reading the console, evaluating C# and running tests are all things to **do** rather than steps to hand
over. Port and token live in `Library/Pipeline/.unity-pipeline-port`; `POST /api/exec` with
`{"command": …, "parameters": {…}}`.

- **The key is `parameters`, not `args`.** An unrecognised key is silently ignored and the command runs
  with every default — so a call with a filter you never applied looks like it worked. This cost three
  calls on the first use.
- **`recompile` is not a compiler and `up_to_date` is not a pass.** It reports Unity's *last
  successful* compile, so a file with an error in it produces `status: up_to_date, errors: 0` over a
  **stale** assembly, indefinitely. On 2026-08-17 that read as "0 compile errors" for a
  `NotifyController` change that could never compile (`this.StopCallLater(x)` on a static,
  non-extension method), and it cost two things at once: the commit reached `master` broken, and
  every `eval` probe in that round silently ran against the **previous** assembly — so ten
  assertions "passed" for code that was not loaded, and the two that failed were blamed on a
  LeanTween mystery that did not exist. Both went away the moment it compiled.
  - **`dotnet build` is the compile authority - but ONE csproj is not the project.** There are four
    assemblies that matter and `Assets/Editor/` is in none of the two obvious ones: editor tooling
    compiles into **`Assembly-CSharp-Editor`**, which neither `EnigmaVirtual.csproj` nor `App.csproj`
    references. On 2026-08-17 that let a deleted framework type ship with a broken editor assembly
    while both framework builds reported `0 Error(s)` - the console was the only thing that knew.
    Build `EnigmaVirtual`, `App`, `Assembly-CSharp` **and** `Assembly-CSharp-Editor`, or ask the
    pipeline's `console` command, which sees all of them.
  - **But `console` returns HISTORY, and history here does not expire.** It is not the live Console
    buffer and `LogEntries.Clear()` does not touch it, so a compile error from an earlier round stays
    in it after the offending file is deleted and every assembly rebuilds clean — on 2026-08-17 it
    still reported 28 `CS0246`s for a file that no longer existed, which reads as a broken project and
    was not one. Entries carry `timestampUtc`: compare the newest error against the mtime of
    `Library/ScriptAssemblies/*.dll`, and a pass is **zero errors newer than the build**, never a
    zero total.
  - **A change inside an `#if UNITY_ANDROID`/`UNITY_WEBGL` branch is NOT compiled by any of the four
    builds** - the Windows csproj does not define those symbols, so a broken call in there reports
    `0 Error(s)` from all four and only fails when somebody switches platform. Compile the branch on
    purpose by adding the symbol to the existing defines:

    ```bash
    dotnet build EnigmaVirtual.csproj --nologo -p:DefineConstants="<existing defines>%3BUNITY_ANDROID"
    ```

    Two traps, both hit on 2026-08-17. MSBuild parses a literal `;` in a property value as a switch
    separator, so every semicolon must be `%3B` or you get `MSB1006: Property is not valid` — and that
    message contains `error` but **not** `error CS`, so a grep for `error CS|Error\(s\)` finds nothing
    and the failure reads as a clean build. Assert `0 Error(s)` is **present**; never treat "no error
    lines matched" as a pass.
  - **A csproj is generated, so it goes stale in both directions.** Deleting a file leaves it listed
    (`CS2001` naming a file that is correctly gone); restoring one leaves it absent (`CS0246` for a
    type whose file is right there). Either way: `AssetDatabase.Refresh()` then
    `SyncVS.SyncSolution()`, then build again. The rename note below says the same thing; it applies
    to deletes and restores too.
  - If you do use the pipeline, prove the assembly moved: compare the `.cs` mtime against
    `Library/ScriptAssemblies/*.dll` **and** grep the dll for a symbol only your edit introduces. An
    mtime alone cannot distinguish "rebuilt" from "rebuilt without my file".
  - **When a type exists in two places, grep both dlls and assert both directions.** Deleting one of
    two same-named types is only proved by the *pair*: for L22, `Positioner` had to be **absent** from
    `EnigmaVirtual.dll` and **present** in `Assembly-CSharp.dll`. Either check alone is ambiguous —
    absent-only cannot tell a successful delete from a stale dll, and present-only cannot tell the
    surviving copy from the one you meant to remove.
  - **But a byte search cannot answer a *declaration* question at all, and L22 only worked because
    nothing referenced the deleted type.** A type name sits in `#Strings` whether it is **declared**
    or merely **referenced**, so if any surviving code mentions the type, its name is in that
    assembly's dll either way. L27's `UIGeneratorModel` read as present in `Assembly-CSharp-Editor.dll`
    both before and after the copy in it was deleted, because `AssetList.cs` touches a field of that
    type and emits a typeref. Ask the loaded domain instead —
    `System.Type.GetType("Ns.Type,AssemblyName")` is non-null **only** where the type is declared —
    and keep asserting both directions. The pair rule is right; the instrument was wrong.
  - **Grep for a member *name*, not a string literal, and mind the encoding.** Names live in the
    metadata `#Strings` heap as UTF-8; string literals live in `#US` as **UTF-16**. So a plain ASCII
    byte search finds `HideOrReportAssetProblems` and never finds `"ASSET(S) REFUSED"` — which on
    2026-08-17 read as a stale dll for a build that was perfectly current, and sent me re-triggering
    compiles for ten minutes. In Python: `lit.encode('utf-16-le') in dll_bytes`.
  - A stale dll is also why `AssetDatabase.Refresh()` looks like it did nothing. It did; the compile
    that followed it failed. Fix the error and the rebuild lands in about five seconds.
- **`eval` is the useful one here.** Most of this codebase's defects are in private methods on
  `MonoBehaviour`s, and `eval` can build a GameObject **inactive** (so `Awake` never runs), set a
  `[SerializeField]` by reflection and invoke the handler directly. M36's retry/clamp/counter-test table
  came from one such call. `UnityEngine.Object` must be fully qualified inside `eval` — bare `Object` is
  `CS0104` against `object`. **`Core` needs qualifying too** — `EnigmaVirtual.Core`, or `CS0103`.
- **`Core.settings` and `Core.main` are NULL in `eval`**, because the editor is not running the boot
  chain. A probe that needs either has to build it and put it back: both are `{ get; private set; }`,
  so reflect the setter, install a stand-in, and restore the previous value in a `finally`. Assert the
  restore in the probe's own output — a probe that leaves `Core.settings` populated poisons the next one.
- **Build settings with `JsonUtility.FromJson`, never `new CoreSettings()`.** Only `EditorSettings` has
  a C# initialiser; `AppData`, `DebugSettings`, `System` and seven more are plain fields - ten of eleven - so a constructed
  `CoreSettings` has null blocks one level down and NREs the moment you touch `AppData.Cache`. In
  production `JsonUtility` builds depth 1 whether or not the JSON mentions it (M60), which is why this
  never shows up outside a probe. Mention each level you need — it builds one level below whatever the
  JSON names (M99).
- **`eval` does NOT see the project's scripting defines, so `#if` inside a probe lies.** With the
  Android build target active, `#if UNITY_ANDROID` in an `eval` snippet still took the `#else` branch -
  and so did `UNITY_STANDALONE_WIN` and `DO_LOGS`, which is what gave it away. The pipeline compiles the
  snippet with its own define set, so a probe that detects platform this way is measuring the probe.
  **Ask the assembly instead, and assert both directions**: for the Android target,
  `EnigmaVirtual.dll` must *lack* the desktop-only string literals and *contain* the Android-only member
  names. Related, when driving requests from a probe on that target: an `http://` URL never leaves the
  process (`InvalidOperationException: Insecure connection not allowed`), so use `https://`.
- **An editor-visible file has TWO consumers, and `git checkout` only satisfies one.** Rewriting a
  `.cs` file and then reverting it while Unity is running writes the path twice in seconds. Unity's
  `Library/SourceAssetDB` can record a timestamp for the state that existed in between, the import that
  would correct it fails with `Import Error Code:(4)` (`... in SourceAssetDB has modification time of X
  while content on disk has modification time of Y`), and because the *import* is what failed, the
  record is never updated - so every refresh retries it. Unity refreshes when it regains focus, so
  merely opening the file in Visual Studio re-fires the loop; it reached 999+ warnings here and left the
  editor unresponsive to the pipeline, which also stopped the csprojs regenerating.
  - **A clean `git status` is not evidence the world is restored.** It proved the file matched `HEAD`
    byte for byte while the project was still wedged.
  - **Fix, cheapest first**: right-click the asset → **Reimport** (this was enough). Failing that, close
    Unity *completely* and delete `Library/SourceAssetDB` and `Library/SourceAssetDB-lock` - `Library/`
    is gitignored on line 1 and nothing under it is tracked, so it is fully regenerable. Never delete it
    while the lock is held. `Reimport All` is slower and re-runs the same failing import.
  - **Avoid it**: do not revert a file under a live editor as a *tidying* reflex. If an edit has to be
    abandoned, revert it, then give the editor one clean settling signal and confirm it recovered -
    rather than assuming the revert restored the project because it restored the file.
- **`AssetDatabase.ImportAsset(..., ForceUpdate)` on a TMP font asset breaks the thing you are measuring.** It makes Unity 6000.3.22 re-serialise the asset (`serializedVersion` 6 -> 8, a 190/66-line rewrite) **and** leaves a dynamic atlas unable to rasterise, so `TryAddCharacters` returns `false` on a font that is perfectly wired. **Reverting the `.asset` file does not fix it** - only a domain reload (`EditorUtility.RequestScriptReload()`) does. Cost four consecutive identical FAILs on a correct fix, which is worse than a flaky one: a stable reproducible failure reads as proof. Also note `TryAddCharacters` **writes** glyphs into a dynamic asset, so any probe using it dirties a tracked file - revert it and assert `git status` clean.
- **TMP font assets have TWO source-font fields and only one is a runtime reference.** `m_SourceFontFile` is serialized outside any `#if` and is what a player uses; `m_SourceFontFile_EditorRef` is inside `#if UNITY_EDITOR`. A **Static** asset deliberately has `m_SourceFontFile: {fileID: 0}` so the `.ttf` is not dragged into the build, and keeps only the editor ref - so asserting `sourceFontFile != null` on a Static asset reports a correct state as broken. Assert the editor ref for Static, the runtime ref for Dynamic.
- **A `400` with `"Main thread operation timed out after 5000ms"` means the editor is busy**, usually
  still finishing a domain reload after a recompile. Retry with a delay; do not go looking at the
  payload. Worth a retry loop around every `eval` in a probe that runs straight after `recompile`.
- **`simulate_pointer` and `simulate_key` do NOT work in this project.** They inject through the Input
  System (`InputSystem.QueueEvent`) and need `ENABLE_INPUT_SYSTEM`; this project ships the **legacy
  Input Manager**, so both return `"Input simulation requires the Input System package to be present
  and active"` rather than doing nothing quietly. Two routes remain for input-driven behaviour, and the
  second is the honest one. Split the handler so it **takes the `Event`** instead of reading
  `Event.current` - the `RFIDReader.HandleKeyEvent` pattern - and drive it from a probe with
  constructed `Event` objects; that tests the logic but not delivery or coordinate space. For real
  end-to-end, drive the **OS mouse** from PowerShell (`SetCursorPos` + `mouse_event` via `Add-Type`),
  which produces genuine input IMGUI receives. Mind two scale factors when predicting where a click
  lands: the editor **Game view zoom** and any `GUI.matrix` the window applies. Measured 2026-08-28 on
  the console's drag-scroll: a 200 editor-px drag moved the list 432.8 GUI units, against 429.6
  predicted by `200 / 0.49 / 0.95`.
- **What it still cannot reach**: anything needing a real failure — an unreachable host, a locked file, a
  denied permission — and every non-Windows target. Those still get written steps. The rule below is
  unchanged; what changed is how much of it now belongs under "verified" instead.

## Every patch ends with a test plan

**After the fix and the docs, before saying it's done: numbered steps someone can follow to see the
change work or fail.** Not "test it in a player" — the exact file to edit, the exact log line to look
for, and what a pass and a fail each look like. If a step cannot be checked here, say so and say what
it would take.

Three things make the difference between a plan and a gesture:

- **A falsifiable observation per step.** "The patch is synchronous" is not testable; "`Patched
  settings.json…` appears *above* the first `Boot stage:` line" is. Ordering, counts and exact strings
  are the currency — this codebase's defects are mostly silent, so the log is usually the instrument.
- **Name what the test cannot reach.** Most fixes here land on paths the shipped config disables
  (`Api.isActive` and `TextureCache.isActive` are `false`; `AssetDownload.isActive` is **`true`**, so
  that one is live) or on targets that have never
  been run (see the platform matrix). Say which steps only prove the Windows half. An untestable fix
  is still worth shipping; an untestable fix reported as verified is not.
- **A counter-test where a fix could over-trigger.** A guard that refuses too much looks identical to
  a guard that works, from the pass side. If the change added a refusal, an abort or a cap, include
  the case that must still succeed.
- **A setup line that normalises state can delete the precondition of the bug.** This is the sibling
  of the rule below and it bit on 2026-08-28 (M124). Drag-to-scroll on the console shipped with two
  passing tests - 11 of 11 on constructed `Event`s, plus a real-OS-mouse drag that moved the list
  432.8 units - and was broken for every real user. Both tests opened by setting `scrollPosition` to
  1500 *to get a deterministic starting point*, and that one line replaced the `int.MaxValue` sentinel
  the console actually parks there while following the tail. The bug lived entirely in that sentinel.
  **Arrange the state the running app really holds**; if a value must be forced for determinism, force
  it to one the app could actually be in, and add a case that starts from the untouched state.
- **Check the step actually reaches the code under test.** This is a different mistake from writing a
  vague observable, and it has now happened twice in one day. H6's editor test said "the folder must
  not appear" — but an unrelated always-on write creates that folder, so the step could never fail for
  the reason it named. M46's cache test said "corrupt the cached appData and expect a parse error" —
  but that cache is the *offline fallback*, so on a normal run the remote fetch succeeds and overwrites
  it, and the step never touched the branch being tested. Before writing a step, trace which branch it
  enters: **most fixes in this codebase live on failure paths, and a failure path needs the failure
  arranged first** (unreachable host, missing file, locked file, denied permission).
- **Name the artefact, and make every pass a positive observation.** Say *where* to look — Console,
  `Editor.log`, the file on disk — because a `Debug.LogWarning` from a build callback arrives with ten
  lines of native stack trace after it and is easy to scroll past. And never write a pass criterion that
  is an absence: "confirm no X is reported" is indistinguishable from "the check never ran". Pair it with
  something that must be *present*. E2's step 3 got this wrong immediately after this section was written,
  and E2's step 2 was reported as a failure because the wording buried the observable inside a compound
  sentence.

Reach for the negative test first when the fix is a gate: the H6 round turned on config seeding
everywhere, and the single most useful step is *"press Play in the editor and confirm the folder is
**not** created"* — that one is cheap, runs in five seconds, and is the one that catches a regression
nobody would otherwise notice for months.

## Architecture docs: what each one owns, and in what shape

Two files, and the split is by **question asked**, not by topic. Getting this wrong is how the same
fact ends up in both and then disagrees with itself.

| Question | File |
|---|---|
| "How does this work?" — layers, boot sequence, config pipeline, platform matrix, threading, conventions | `overview.md` |
| "What is wrong with it, and what is next?" — defects, ids, statuses, the worklist | `known-issues.md` |
| "What did a review say?" | `docs/reviews.md` |

**`overview.md` describes the system as it is today.** Present tense, no defect ids, no history —
except §5b *Breaking changes by version*, which is the one place history belongs, because a branched
project needs to know what moved under it and when. **Add a row there whenever a change forces a
branch owner to act**, and say what they must *do*, not what you did.

**`known-issues.md` entry rules:**

- **Check the table's *shape* before trusting any count taken from it.** Every entry row is
  `| id | description | location | status |` — exactly four cells. On 2026-08-17 four rows were not:
  H20 had a stray ` | ` inside its prose, L1 was missing its location, and **L10 and L11 were
  missing `status` entirely**, so two open entries had no status field and anything reading by
  position got the wrong answer. Months of ledger checks passed over it, because they counted ids
  in the header columns and never looked at the rows. Assert four cells per row *first*: a
  validator that only checks the values it can parse will not tell you the parse is wrong.
- **IDs are permanent.** Next free number in the severity band; never reuse, never renumber, never
  delete an entry. A finding that turns out to be wrong is marked `not a defect` and **kept**, so
  the correction is discoverable — this has already saved a defect from being re-logged twice.
- **Cite code, and re-point by grepping the anchor** rather than shifting the number. A mechanically
  remapped citation lands on real code while describing the wrong construct, which is most of what
  the 2026-08-13 sweep found. Verify the line you land on still contains the thing you named.
- **Nothing speculative.** Every entry cites the code it is about. Mark it **unverified** if you
  could not exercise the behaviour, and say what exercising it would take.
- **Say whether it is live or dormant**, and dormant *by what* — `TextureCache.isActive: false` and
  "no caller in this repo" are different dormancies, and only one of them protects a branched
  project.
- The status vocabulary and the worklist tier criteria are defined **in the file itself**, at the
  top and in *Sequencing notes*. Read them before adding a row.

**An entry is a hypothesis until someone measures it.** Several here were wrong about their own
prescription — L8 asked for `.webp` support that would have made things worse, M13's complexity
claim held for no caller this repo has, and three counts in `reviews.md` were published wrong before
being measured. Read the current code before acting on any entry, and re-measure any number you are
about to quote.

## Session workflow

- **Start of session:** read the most recent file in `docs/sessions/` to pick up context, then
  create today's log at `docs/sessions/<YYYY-MM-DD>.md` by copying
  `docs/sessions/_template.md`.
- **End of session:** fill in today's log, and reconcile the architecture docs and
  `docs/reviews.md` against what actually changed. The session log is a trail; the architecture
  docs are the truth.
- **The log is the only place the *reasoning* survives.** The register records what a defect was and
  the code records the constraint, but neither holds why an approach was rejected, what a
  measurement actually returned, or which of two plausible explanations turned out to be wrong.
  Write decisions **with the why**, and record what you *verified* separately from what you
  *changed* — "compiles" and "tested on the device" are different claims and the difference matters
  three weeks later.
- **Record corrections, including your own.** An entry saying "I believed X, measured it, and X was
  false" is worth more than a clean narrative, because the next person will otherwise believe X too.
- Durable lessons do not stay here: a convention or gotcha goes to `CLAUDE.md`, a defect to
  `known-issues.md`, a review finding to `docs/reviews.md`. The log is where they are *found*, not
  where they live.

## Every script update: bump the version

Bump the semver in the repo-root **`VERSION.txt`** — patch for fixes, minor for features,
major for milestones. `Assets/Editor/AppVersionSync.cs` propagates it on the next domain reload
(or via *Gema → Sync App Version*) to **both** consumers:

```
VERSION.txt ──► PlayerSettings.bundleVersion   (binary ProjectSettings.asset)
            └─► Descriptor.asset .Version      (splash screen, analytics, build folder + exe name)
```

**Never set a version anywhere else.** Not Player Settings (binary, undiffable), and not the
Version field in *Gema → App Descriptor* — that field is read-only for a reason. The App
Descriptor's Major/Minor/Patch buttons are fine: they write `VERSION.txt` and re-sync.

Both of those were real bugs, not hypotheticals (M27): the descriptor kept its own version and
reached `1.0.0` while `VERSION.txt` said `0.5.1`, so the splash screen, the analytics payload and
the built `.exe` name all reported a version unrelated to the repo.

> Path note: this project has no `Assets/_Script/` folder. Editor tooling lives in
> `Assets/Editor/`, runtime code in `Assets/01_Scripts/`. `AppVersionSync.cs` is at
> `Assets/Editor/AppVersionSync.cs`.

---

## Where code goes

```
Assets/01_Scripts/
  EnigmaVirtual.asmdef  ← assembly "EnigmaVirtual": the framework AND External/ (see below)
  App/
    App.asmdef          ← assembly "App": references EnigmaVirtual
    ...                 project-specific code — the only folder a derived project should edit
  EnigmaVirtual/        shared framework (Core, Data, Components, Extensions, IO, Attributes)
  External/             third-party (UnitySerialPort, UnityRawInput) — don't edit, wrap*
Assets/LeanTween/
  Framework/LeanTween.asmdef   ← assembly "LeanTween". On Framework/, not LeanTween/, so the
                                 Editor/ subfolder stays in Assembly-CSharp-Editor.
Assets/Editor/          editor tooling — Assembly-CSharp-Editor, which Unity always compiles
                        separately whether or not an asmdef exists
```

**Anything NOT in that map silently lands in `Assembly-CSharp`** — which sits *above* both
`EnigmaVirtual` and `App` and can reference either, so a stray `.cs` outside the three roots above is
the one place a type can shadow a framework type without any dependency-direction check noticing.
**15 files are outside it today**: `ThirdPart/` (7), `MidiJack/` (5), `08_Examples/` (3). It was 16
until v0.54.4, when a duplicate `ApplicationDescription` was deleted from **`Assets/Resources/`** —
it carried the same `[CreateAssetMenu]` path as the framework's own and had lived there since the
initial import (M118). A `.cs` under `Resources/` gains nothing from being there: scripts are not
runtime-loadable (`MonoScript` is editor-only), so the folder bought it no reachability and only
changed which assembly compiled it. **Check the assembly, not the folder name**, when asking where a
type lives.

**`External/` shares the framework's assembly, and that is deliberate.** The two are *mutually*
dependent — `MainController` uses `RawKeyInput` while `RawKey.cs` calls `Development`, and
`SerialPortsManager` uses `UnitySerialPort` which calls `Development` in seven places — so External can
be neither above nor below the framework. Hence the asmdef at the `01_Scripts` **root** rather than on
`EnigmaVirtual/`, with `App.asmdef` nested inside `App/` to take precedence for its own subtree. It also
states the real relationship: these forks are not independent third-party code, they are framework-
dependent forks.

**Adding a framework dependency now means adding an asmdef reference.** The list is `UnityEngine.UI`,
`Unity.TextMeshPro` and `LeanTween`. Precompiled DLLs (Newtonsoft, Phidget22, MidiJack) are
auto-referenced and need nothing — as long as `overrideReferences` stays `false`. **Never narrow
`includePlatforms`**: an empty list means all platforms, and restricting it to "what is tested" would
silently un-compile the framework for Android, iOS and WebGL.

Extend the framework's base classes from `App/` (`AppDataBase`→`AppData`,
`Api`→`ApiControllerBase`→`ApiController`, `ScreenControllerBase`→`ScreenController`).

> \* **Both packages are already forked, not pristine** — `Unity_SerialPort/UnitySerialPort.cs`
> calls Gema's `Development` logger in 7 places and `UnityRawInput/Runtime/RawKey.cs` in 1, so
> neither can be swapped for an upstream copy and "wrap, don't edit" describes an intent that was
> abandoned before it was written down. Treat it as: keep edits to the seams that already exist
> (logging, event surface), and anything larger deserves a real wrapper first. Check with
> `grep -rc "Development\." Assets/01_Scripts/External` before assuming a file is untouched.

**Framework code must not reference `App/`, and as of v0.23.0 none of it does.** The cycle that
blocked the `.asmdef` split (M20) is broken: verified by stripping comments and string literals and
matching all 18 `App` type names in type position — **0 references** from `EnigmaVirtual/` or
`External/`.

How each was resolved, because none matched the description the register had carried since v0.4.0:

| App type | Was | Resolved by |
|---|---|---|
| `ScreenType` | 17 refs, 7 files | **A file in the wrong folder.** It already declared `namespace EnigmaVirtual.Screens`; `git mv` into `EnigmaVirtual/Screens/` fixed every reference with no code change (v0.21.1). |
| `MediaVO` | 4 signatures | App-side extension methods, with the thumb→url fallback kept framework-side as `Load(url, fallbackUrl)` so it stays shared (v0.22.0). |
| `AppData`/`AppSettings` | 8 refs, one file | `AppDataController` deserializes into the `*Base` types, told what to build by two `protected virtual Type` properties (v0.23.0). |
| `ScreenView` | — | Resolved v0.4.0. |

**The `AppData` one has a step that is silent if you skip it.** `App/Data/ProjectAppDataController.cs`
overrides `AppDataType`/`AppSettingsType`, but it only applies if it is **authored on a GameObject under
`Core`** — `MainController.SetupAppData` discovers it, and falls back to a plain `AppDataController`
when it finds none. Unauthored, the model deserializes into `AppDataBase` and the project's own members
are never populated, with **no compile error**: `MediaWalker` is reflective and every consumer takes
`object`. With `cleanUnlistedAssets` on, the resulting empty keep-list also clears the media cache.
It **is** authored here — verified 2026-08-13 by GUID-mapping `CoreSetup.unity`, along with the rest
of the Core object; the list is in `overview.md`. Map the **scene** if you check this yourself. The
same list used to be mapped from `Core.prefab`, which nothing referenced and which is now deleted
(M74), and it was wrong for three components as a result.

**Do not add new framework→`App` references.** If framework code needs project data, take the
primitive it actually uses, or add a member to the `*Base` class.

## Conventions that hold

- **Settings and content DTOs** are `[System.Serializable]` classes of **public fields** with
  defaults, nested to mirror the JSON. Never auto-properties — `JsonUtility` and Unity's serializer
  both ignore them. `MediaWalker` reads either, so nothing forces you into properties (M4).
- **The field name *is* the JSON key, in both directions.** `JsonUtility` matches by field name, and
  `FileManager.SaveCoreSettings` merges `ToJson(Core.settings)` back into the operator's file — so a
  field whose name disagrees with its key is not merely unread, it is **dead**, and a save writes a
  second key beside the operator's. That was M54: `SystemSettings.Android` against a shipped
  `AndroidSettings` block, so Android's settings had never been read once. Renaming a settings field is
  therefore a config-format change, and it also invalidates any `PlayerPrefs["Settings"]` snapshot,
  which `Core.Awake` prefers over the file.
- **That snapshot defeats *adding* a settings field too, not just renaming one.** `Core.Awake` prefers
  `PlayerPrefs["Settings"]` whenever it exists, and an old snapshot has no key for a field you just
  added - so the C# initializer wins and the new field reads as unset on exactly the machines that have
  been used. H14 hit this: a machine that ever saved from the settings panel keeps authenticating against
  the plaintext password inside the snapshot, and the hash migration silently does nothing. **On Android
  and iOS the snapshot is the only save target**, so it is the normal state there, not an edge case.
  Any config-format change therefore ships with "clear Player Prefs" as a step - *Gema → Clear
  PlayerPrefs* in the editor, or the Reset button in the app's own settings panel on a deployed machine.
- **An absent key for a nested `[Serializable]` class gets a constructed instance, not `null` -
  but only ONE level below whatever the JSON actually mentions (M99).** `JsonUtility` does not
  build the graph all the way down. Measured 2026-08-14:
  `FromJson<CoreSettings>("{}")` returns a built `DebugSettings` and a **null**
  `DebugSettings.Console`, while `{"DebugSettings":{}}` returns both. So the paragraph below
  is right about M60's `Phidgets` - depth 1, safe - and was wrong to generalise it: deleting a
  block whose *children* are dereferenced is a boot-killing NRE, and
  `MainController.Init` reads `DebugSettings.Console.isActive`. **Give every nested settings
  field at depth 2 or deeper an `= new X()` initialiser** rather than reasoning about its depth;
  the four that lacked one now have it. The **top level of `CoreSettings` is deliberately not
  initialised** - ten of its eleven fields are bare, and `JsonUtility` builds depth 1 regardless, so
  production never notices. Only code that says `new CoreSettings()` does, which is why a probe must
  build settings through `JsonUtility.FromJson` instead.
  `JsonUtility.FromJson` builds the whole object graph whether or not the JSON mentions a block, so
  `Core.settings.Phidgets` is a real `PhidgetsSettings` with its own initializers applied even when
  `settings.json` has no `Phidgets` at all — which means **an absent block means "module off"**, since
  every settings class defaults `isActive = false` and `MainController.Init()` only creates a module when
  `isActive`. Measured 2026-08-12 (M60): the block was deleted and the editor booted through to
  `Core :: Boot complete.` while dereferencing `Phidgets.isActive`. So a nested-class field does **not**
  need `= new X()` for safety. **Arrays are the exception** — no initializer really does mean `null`
  there, so either give them one or guard with `.IsNullOrEmpty()` as `OneCore` does.
- **An absent JSON key keeps the C# field initializer** — it does *not* arrive as `default`.
  `JsonUtility.FromJson<T>` constructs the object normally, so `public int timeout = 30;` stays 30
  when the deployed `settings.json` has no `timeout` at all. Only a field with **no** initializer
  lands on zero. So: give every new settings field an initializer, and read a `0` as something an
  operator typed on purpose rather than as "unset". (I had this backwards while writing the H9 fix,
  and used E1's key as the example — wrongly, since `SystemSettings.skipAssetValidationKeyCode` does
  have an initializer. Caught by `architect-reviewer` against the register's own H5 entry, then by
  `doc-reviewer` against the source.)
- Every module's settings block has `isActive` and `doLogs`; `MainController.Init()` only
  creates a module when `isActive`.
- **Editor-only code** goes under `Assets/Editor/` or inside `#if UNITY_EDITOR`. The
  `Attributes/` folder does this correctly in all 18 files — copy that pattern.
- **An editor script must not name a type declared under `App/`.** `Assets/Editor/` compiles into
  `Assembly-CSharp-Editor`, which auto-references `App`, so naming one *compiles here* and makes the
  whole editor assembly — `BuildValidator` and `AppVersionSync` with it — a `CS0246` for any branch
  that renames it, reported by **no** framework csproj. Two ways round it, both used in v0.54.2
  (L26): an inspector targets the framework **base** with `[CustomEditor(typeof(XBase), true)]`,
  which also covers whatever a branch calls its subclass; and code needing a *concrete* type asks
  `UnityEditor.TypeCache.GetTypesDerivedFrom<XBase>()` for it. **Moving the file to `App/Editor/` is
  not the answer** — a folder under an `.asmdef` gets no `Editor/` special-casing, so it needs its
  own asmdef, and an asmdef assembly can never reference a predefined one like
  `Assembly-CSharp-Editor`, so it could not call back into the editor tooling it left behind.
  Note this is about **names**: a `Resources.Load` of a prefab carrying an `App/` MonoBehaviour is a
  GUID reference, cannot fail the assembly, and is fine.
- **Paths** go through `AppPaths` (`Core/Managers/AppPaths.cs`) and `Path.Combine`. Never a
  literal `"\\"` in a path — it breaks every non-Windows target (C4).
- **An `AssetDatabase` path is always forward-slash, on Windows too, and a backslash fails
  *silently*.** `LoadAssetAtPath` does not throw on `@"Assets\Resources\X.asset"` — it returns
  **null**, so any `if (asset == null) CreateAsset(...)` branch below it runs against an asset that
  is already there and destroys it. That is H19 (`Descriptor.asset`), M28 (four editor files) and
  H23 (`UIGeneratorData.asset`) — the same defect three times, two years apart, because each fix
  swept a folder instead of the tree. **Sweep the whole tree, `Assets/ThirdPart/` and
  `Assets/Editor/` included**, and never write a `[MenuItem]` test step without reading what the
  body does first: H23's own counter-test would have destroyed the asset it was checking.
- **`Path.*` is wrong for a `[CallerFilePath]`, and that is not a contradiction of the line above.**
  `Path` splits on the separator of the platform that is *running*; a caller-info literal carries
  the separator of the platform that *compiled*. On Android `Path.GetFileName(@"C:\Work\…\Core.cs")`
  finds no `/` and returns the whole thing, so `Development` printed the full build path in front of
  every log line on the device (M90). Split on `{ '/', '\\' }` for any string baked at compile time
  — `StringUtil.GetFileNameFromUrl` already does exactly that and is the one to call, *except*
  from `Development` itself, where it would put a class that logs underneath the logger.
- **A path from the CMS is a path you did not write. Contain it where it is *used*, not where it
  is built.** `AppDataSettings.GetAssetPath` returns a rooted value verbatim, and that is deliberate -
  five of its six callers only ever read, and an operator pointing media at another drive is a real
  configuration. The sixth writes, and that is the one that needs `Path.GetFullPath` + `IsUnder`
  against the asset root (H22, v0.37.0). Guarding the shared resolver would have changed five read
  sites to fix one write. And containment against the root is not sufficient on its own: both halves
  of `assetsPath` are operator-editable, and emptying them — `downloadFolder` used to be a third term
  here and was removed in v0.51.0 — collapses the root onto the **parent of the config folder**, so `config/settings.json`
  becomes "inside the root". Refuse that folder explicitly.
- **Handing a path to a shell command? Wrap it in `AppPaths.ForShell()`.**
  `Application.persistentDataPath` returns `/` on Windows while `Path.Combine` joins with `\`,
  so composed paths are mixed-separator. That is fine for `File`/`Directory` but
  **`explorer.exe` silently does nothing** with a forward slash in the path — no exception, no
  log. This regressed Ctrl+Alt+S in v0.4.0 and compiled clean the whole way.
- Off-main-thread work marshals back through `MainThreadDispatcher.RunOnMainThread`.
- **For a stream, prefer a worker thread plus a `ConcurrentQueue` over marshalling each item.** The worker
  touches only the socket, the queue and `volatile` fields; the main thread drains it in `Update` and raises
  every event, log and notification from there. `SocketConnection` is the worked example (C6). The rule this
  encodes: **a `UnityEvent` invoked off the main thread is not a marshalling bug you can patch at one call
  site** - the old code marshalled one notification correctly and invoked the event on the very next line,
  which is what made the design unfixable in place.
- **TCP framing belongs in the socket class, never in the caller.** One `Read` is not one message: it can
  carry several, or half of one. While `ReadSocket` returned a raw buffer, its two callers each invented
  their own framing - one split on `'|'`, the other on `'\n'` and kept only the first result - so half the
  protocol was implemented twice, differently, and neither reassembled a message split across packets.
  Decode with a **stateful** `Encoding.UTF8.GetDecoder()`: a multi-byte character can straddle two reads.
- **A failure path must never travel through the success callback.** `DownloadableImage` showed its
  error placeholder by calling the loader's own callback, and that callback is what raises
  `onLoadCompleteEvent` — so a failed load announced completion, synchronously and before the
  failure event, on both graphic paths (M89). Re-ordering the two invokes would not have fixed it;
  the placeholder had to stop being shown through the success callback. (I first wrote that the
  two paths diverged because `ImageLoaderThread` deferred one of them. It does not — it is
  synchronous and says so in its own remarks. Two reviewers caught that after it had already
  reached a commit message: **read the class you are describing, not the entry describing it.**) Split "show something" from "announce
  the outcome" and give the announcement one exit — here `FailLoad`, which also clears **both**
  event lists, because a load that failed will never complete and a surviving `onLoadComplete`
  listener is waiting for the *next* load (M88).
- **`ShowNotification` logs on its own** (v0.8.7). An `Error` notification calls `LogError`, a
  `Warning` calls `LogWarning`, everything else `Log` — before it touches the UI. So don't pair a
  `Development.LogError` with a `ShowNotification` call unless you mean to — you'll get the message
  twice. (`Core`'s single settings-parse report, `ReportSettingsFailure`, pairs them deliberately: that
  failure is the one people grep for, and an explicit log there survives any later change to the
  notifier. It null-checks `notificationManager` first, because `Core` carries on without one.)
  It works the
  other way round too: reporting a failure *only* through the notification manager is now enough to
  get it into the log on every platform, which is what M26 was about.

## Conventions to stop following

These are in the codebase but should not be copied into new code:

- ~~`Development.Assert`~~ **is `Development.LogIf` as of v0.45.0** (M9) — it logs when `cond` is
  **true**, at Log level, which is what all 70 call sites here want. `Assert` still compiles as an
  `[Obsolete]` forwarder for branched projects; do not write it in new code. Its `LogType`
  overload does not default that argument (M25): pass it, or use the two-argument form and get
  Log level.
- `Invoke("MethodName", delay)` — string-based, refactor-hostile, strippable (M10). Use
  `MonoBehaviour.CallLater(delay, Action)`, and the `ref Coroutine` overload wherever a second
  call can arrive before the first fires. Note `CancelInvoke`/`IsInvoking` do **not** see it.
- `if (!Core.IsSet) return;` in `Start` — leaves fields null while `Update` still runs.
  Use `enabled = false` (H13).
- `ActionExtensions.InvokeLater` — runs on a thread-pool thread, so any Unity call inside
  throws (C5). Use a coroutine.
- Auto-properties in content VOs — invisible to `JsonUtility` and Unity's serializer (M3, fixed
  v0.14.2: the template is all fields now). `MediaWalker` covers both member kinds as of v0.8.0,
  so the asset walk no longer forces the choice — but **write content models with public
  fields** anyway, because `JsonUtility` round-trips `appSettings.json`: `SaveAppSettings`
  writes with `JsonUtility.ToJson`, so a property on a settings DTO is not just unread, it is
  **erased on the next save**.

## Gotchas

- **`Core.IsSet` gates everything.** If settings parsing fails, `Core.settings` stays null and
  `CoreSetupComplete()` never runs, so the app sits on the preloader. It is no longer *unsurfaced*:
  as of v0.49.18 every way the parse can fail goes through `Core.ReportSettingsFailure`, which
  always logs at `LogError` and names the source - including the empty-payload case, which used to
  surface as a bare `NullReferenceException` because `JsonUtility.FromJson<CoreSettings>("")`
  returns null without throwing. The hold on the preloader is unchanged (C3); what changed is that
  it says why. When something "does nothing", check this first.
- **The boot chain has no timeout of its own.** `Core → MainController → AppDataController →
  AssetManager → TextureCacheManager → LoadNextScene`, each hop a single one-shot event, and any link
  that never fires hangs startup permanently. Since v0.2.0 it is at least *reported*:
  `Core.BootWatchdog_IE` announces `BOOT STALLED` and latches `_bootFailed`, after which
  `LoadNextScene` refuses. It names where startup stopped; it does not recover it.
- **That watchdog resets its deadline only when the `_bootStage` *string* changes**, which quietly makes
  some progress numbers load-bearing. `MainController` marks `assets {done}/{total}` from *both* asset
  events, and on a retry only the download event's raw count moves — verification's
  `++validatedAssets - totalRetryCount` cancels out. So the two events' disagreeing counts are a
  constraint, not an untidiness: unify them and a 4-attempt asset holds one stage string for 240s
  against a 120s budget, and a healthy boot reports `BOOT STALLED` (M70). **Correct a progress number in
  the handler that displays it, never in the event payload.**
- **`TextureCache.isActive: false` does not switch the texture cache off**, and reading it that way
  cost most of a round on M2. The flag gates the **boot stage** — the bulk pre-cache. It does not gate
  the manager: `MainController.SetupTextureCacheManager()` sits outside every `isActive` check, so
  `Core.main.TextureCacheManager` is non-null, and `GetOrSetTexture2D` has no gate of its own — any
  `TextureCacheLoader.Load` downloads, caches and serves with the flag off. So a defect in the *loader*
  or in `GetOrSetTexture2D` is **live**, while one in the pre-cache pass is dormant. The pair is a
  reliable trap: H3 (manager always null) and H4 (cache leaked everything) were both fixed in v0.2.0
  and the boot hop got a stall watchdog, and it was easy to file the whole file under "never runs".
  Check *which* function the defect is in before calling it dormant.
- **The asset-cleanup step deletes files** under a computed root. Guarded as of v0.7.0 (C2), behind
  its own `Cache.AssetDownload.cleanUnlistedAssets` flag, which ships **`false`**. Enabling downloads
  no longer enables a recursive delete.
  **This line has been wrong twice, so check the file rather than trusting it.** The flag shipped
  `true` from v0.15.0 to v0.30.0 while this said otherwise (H21), and again from 2026-08-19 to
  2026-08-28 (M123) — that second time committed by a change whose own message said *"No code
  change"*, alongside a `Sockets.isActive` flip from the same operator's machine. Both windows were
  found by accident, months late, and only because someone attempted a **release** build: L12's gate
  is the only thing that reads the shipped config, and development builds skip it by design. One
  command settles it —
  `grep -n cleanUnlistedAssets Assets/StreamingAssets/config/settings.json`. Its M3/M4 blocker cleared in v0.8.0, so it is defensible to
  enable; still verify the delete set against a **copy** of a media folder first, and note every
  refusal logs `Asset cleanup REFUSED: …` with the reason.
- **`DEBUG_MODE` is a per-developer working-tree toggle, not a repo setting.** The committed value
  is `Standalone: DO_LOGS`. It changes behaviour, not just logging — it is one of the two terms in
  `AppPaths.UsesDeployedConfig`, so it switches the whole config-seeding path on in the editor — and
  `BuildValidator` fails any build while it is defined; that is its purpose. Don't commit it (M18).
  Note it leaves a seeded config behind in LocalLow that a later run *without* `DEBUG_MODE` half-reads
  (M46).
- **`DO_LOGS` is only defined for Android + Standalone**, and it now controls **verbose logging
  only**. **`Standalone` is Windows, macOS *and* Linux** - one BuildTargetGroup for all three - so the
  targets that strip `Log`/`LogWarning`/`LogIf` are **iOS and WebGL, and nothing else**. Read that
  twice before writing it down: "stripped on iOS, macOS, Linux and WebGL" was repeated across five
  documents for months, including as the stated rationale for choosing `LogError` over `LogWarning`,
  and it doubles the apparent size of the hole. `Development.LogError`/`LogException` are always compiled (C7, v0.5.0) — do not add
  `[Conditional]` back to them; there's a banner in `Development.cs` saying so.
  `Development.LogIf` **must** stay conditional: its arguments are stripped with the call, and
  nearly every site passes `Core.settings.System.doLogs`, which is null when settings parsing
  failed (H17). **So never put a `LogIf` on a path that can run before the boot chain
  completes** — `Update`, `Awake` and `OnGUI` are Unity messages and run regardless of whether
  `Core.IsSet` was ever reached, and on Standalone/Android `DO_LOGS` *is* defined, so the argument
  really is evaluated and really does throw. `MainController`'s shortcut and Android-permission
  breadcrumbs use a plain ungated `Development.Log` for exactly this reason (v0.8.8).
- **`timeout` is one field name over two different policies, and as of v0.49.9 both go through
  `WebRequestExtensions` (M43, M106, M107).** Every settings-driven `www.timeout` assignment goes
  through one of **two named policies** — `SetApiTimeout` or `SetFileTransferTimeout` — so 0 can no
  longer mean "wait forever" anywhere, and no call site passes a number. The policies differ in
  both the ceiling and what 0 becomes, and the constants live in the helper: `Api.timeout` **and `AppData.timeout`** cap at 300 and
  resolve 0 to 30, because there 0 means *unset*; `AssetDownload` and `TextureCache` cap at 3600
  and resolve 0 to **3600**, because there 0 has always meant *no limit*. `EffectiveApiTimeout` /
  `EffectiveFileTransferTimeout` expose the resolved value for callers that need it without
  issuing a request — which is how the boot watchdog sizes its budget.
  - **The default is the dangerous half to get wrong, not the ceiling.** A ceiling only bites a
    number somebody typed deliberately; the default decides what 0 becomes. The first version of
    this fix shared one 30s default across all five sites, which made `timeout: 0` on a download
    *shorter* than the shipped 60 — on the one path that cannot resume. Two reviewers caught it
    independently.
  - **Why the download ceiling is twelve times Api's:** `www.timeout` bounds the whole request,
    and a failed attempt calls `TryDeletePartialFile`, so **nothing resumes** — every retry
    restarts at byte zero. A ceiling under the honest transfer time does not slow a large asset,
    it makes it permanently undownloadable, and the only trace is one "Giving up" line.
  - **Clamp before you narrow.** `(int)uint.MaxValue` is `-1`, and UnityWebRequest stores a
    negative timeout as **0** — measured. The old `(int)` casts turned an absurd value into
    exactly the unbounded wait they looked like they were avoiding.
  - **A clamp is reported, once, at boot** (M106, v0.49.10). `Core.ReportClampedTimeouts` names
    the key, the typed value and the applied value at `LogError` - the only level always
    compiled. It is silent on the shipped config, so a line there always means somebody edited a
    timeout and did not get what they asked for. `TextureCache` is deliberately **not** gated on
    its own `isActive`, because that flag gates the pre-cache pass and not the loader.
  - **`Core.BootWatchdog_IE` sizes its stall budget off the *effective* timeout** (M107,
    v0.49.10), recomputed on every deadline reset. As of v0.49.20 it is one helper,
    `StageBudgetFor` - `max(90, 2 x widest + LocalDataFallbackSeconds)`, where *widest* spans
    `AppData.timeout` and both cache timeouts. **125s on the shipped config**, and the floor stops
    dominating above 42.5s. The doubling is not padding: one `"loading appData"` stage issues two
    bounded requests on Android/WebGL - remote, then the local fallback through the same method - and
    the delay between them is a term rather than an assumption that it fits. A budget wrong in the
    **small** direction latches and refuses to boot, so over-estimating is the safe error. It used to read the configured value once,
    before the settings parse - and everything before a coroutine's first `yield` runs at
    `StartCoroutine`, so it always read a null `Core.settings` and the budget was **always the
    90s floor**. The register had filed the opposite failure. Two lessons, both general: a value
    read once in a coroutine started before the boot chain is a value read too early, and a
    watchdog whose budget is wrong in the *small* direction latches and refuses to boot, which is
    worse than one that reports late.
- **`UNITY_EDITOR` cannot tell edit mode from play mode** — it is defined for both, so it is the wrong
  tool for "behave differently when running". Use `Application.isPlaying`. An `#if UNITY_EDITOR` that
  picks `DestroyImmediate` sends editor *play* mode down a path the shipped player never takes, which
  diverges exactly where you test most (`ImageResizer`, v0.18.1). Compile-time for *what exists*,
  runtime for *what is happening*.
- **Adding `[SerializeField]` to an existing field moves where its default lives, for every scene
  that is ever saved afterwards.** The safe half is safer than it looks: Unity constructs the
  `MonoBehaviour` normally — C# initializers run — and then applies the scene YAML over it, so a key
  the YAML does not carry is never overwritten. Newly exposing a field therefore does **not** land a
  serialized `0` on an already-authored component; checked in the YAML and measured live for
  `AssetManager.maxRetryCount` (L7). The trap is the next save: opening and saving that scene bakes
  the *current* value in, after which the C# initializer is decorative **for that scene**, and
  changing the framework default later reaches a branched project only if it never saved. The two
  paths do not coexist in one run — `MainController.SetupAssetManager` only `AddComponent`s when
  `GetComponentInChildren` finds none — so this is a cross-project divergence, not two live
  instances disagreeing. Serialize a number when an operator should tune it, keep it a `const` when
  it is arithmetic other code depends on, and **bound it with `[Range]` rather than `[Min]`** when
  both ends are hazardous, because the inspector accepts whatever gets typed.
  **Be careful what you claim the bound prevents.** I justified `maxRetryCount`'s as stopping a
  boot-*stalling* typo. It does not — `onAssetDownloadEvent` carries the **raw** count, so every
  retry moves the stage string and resets the watchdog deadline, which is what the M70 note above
  already says. `maxRetryCount = 50` makes one dead asset take ~51 minutes and never stalls. A boot
  nobody waits out is the real risk, and it is harder to diagnose than a hang, not easier.
- **A GUID protects a component reference, NOT an Inspector-wired UnityEvent.** A `Button.onClick`
  authored in the Inspector stores `m_TargetAssemblyTypeName` — a **namespace-qualified type-name
  string** resolved with `Type.GetType` — beside the GUID. So renaming a type's **namespace** breaks
  that call with **no compile error, no `dotnet build` error and nothing in the Unity console**: the
  button silently stops doing anything. This is why a namespace migration needs a scene grep and not
  just a `using`:

  ```bash
  grep -rn "m_TargetAssemblyTypeName: \S" Assets --include=*.unity --include=*.prefab
  ```

  **The `\S` matters**: 50 entries exist here and 46 of them have an *empty* value - those bind by the target object's own type and are GUID-safe, so an unfiltered grep buries the four that are not under ten times as many that are. The four name `FieldsVisibilityController` in `CoreSetup.unity` — and they record the
  assembly as `Assembly-CSharp` while the file compiles into `EnigmaVirtual.dll`, so that qualifier
  looks stale already.

  **MEASURED 2026-08-18, in play mode, and the answer is that a live target wins.** All four of those
  calls fire: `OnButtonClick` runs on every one despite the qualifier naming an assembly that does not
  contain the type. So a `PersistentCall` with an `m_Target` object reference resolves the method from
  **that object's runtime type**, and the type-name string is not consulted. A stale qualifier on an
  instance call is cosmetic.

  **So the paragraph above overstates the risk, and this is the correction.** Renaming a namespace does
  NOT silently break an ordinary Inspector-wired UnityEvent - those carry a live target. The string is
  the only resolution route for a **static / null-target** call, and that case remains **untested here**
  because this repo has none. The grep is still worth one command before a namespace migration, but it
  is cheap insurance against a case nobody has demonstrated, not a known failure mode - and it must not
  be used to budget a migration as blocked. L25 records the measurement; it is `not a defect`. The v0.50.0 slice was safe
  only because **zero** of the nine moved types appeared in any such entry (L17), and for a while the
  next candidate was the one that did - `FieldsVisibilityController`, with four entries in
  `CoreSetup.unity`. It moved in v0.53.0 once L25 measured those entries as cosmetic, and L17 closed
  in v0.54.6, so there is **no next candidate**: every type still global is one for which global is
  correct. The grep remains worth one command before any *future* namespace move. Checking `[SerializeReference]`, `AddComponent("Name")` and
  `Type.GetType` string literals is **not** sufficient on its own — I checked all three, concluded
  namespace moves were GUID-safe, and this is the mechanism that was missing.

- **Wrapping an existing file in a namespace changes where extension-method lookup starts.**
  Lookup walks outward from the innermost enclosing namespace, so a call inside a newly-wrapped
  file searches that namespace **before** global. Nothing rebinds the day you wrap it - verified
  for `RegisterField.cs`'s `IsValidEmail()` and `GetActive()` against the 7 holders inside
  `EnigmaVirtual.*` - but adding an applicable extension method under that namespace *later*
  silently rebinds every wrapped call site. The compiler will not tell you, because both
  candidates are valid.
- **Renaming a script means moving its `.meta` with it, or every scene and prefab reference
  detaches.** Unity binds a `MonoBehaviour` by the GUID in the `.meta`, not by the file name, so
  `git mv Foo.cs Bar.cs` **and** `git mv Foo.cs.meta Bar.cs.meta` keeps every reference — while
  renaming the `.cs` alone makes Unity mint a fresh GUID and every component becomes a "missing
  script". The class name must still match the file name for a `MonoBehaviour`. Then run
  `SyncVS.SyncSolution()` (`AssetDatabase.Refresh()` first), or `dotnet build` keeps failing on the
  old filename while Unity itself compiles clean — that divergence cost a round in v0.49.2. Confirm
  the GUID survived by grepping it out of the new `.meta`; it takes one command and it is the whole
  question. Done this way for the v0.49.5 analytics rename (L6).
- **Never translate `KeyCode` to a character yourself - `Event.character` already did it.**
  A `KeyCode`→`char` switch is a keyboard *layout* written out in code, and it is always the US
  one - **misremembered**, in practice. `RFIDReader` carried a 50-line table indexing the US
  shift row `{'!','@',…}` from `Alpha0`, so all ten shifted digits came out one position off
  and it was wrong on a US keyboard too. Any key the table had no `case` for returned `'\0'`
  and was **dropped silently** - measured: the entire numeric keypad (`KeyCode` 256-265, well
  outside the `Alpha0`..`Alpha9` the switch covered) and `KeypadEnter`, which meant a keypad
  Enter never terminated a code (M15). `Event.character` is what the OS resolved under the active
  layout — **measured 2026-08-18 on the pt-PT layout this client uses**: pressing `ç` arrives as
  `keyCode = BackQuote`, `character = [ç]`. The physical key and the character it produces are simply
  different things, and only the second is the one you want.

  Three more facts from that same measurement, all load-bearing. **Each keystroke is a PAIR of
  events** — one carrying `keyCode` with an *empty* `character`, then one with `keyCode = None`
  carrying the character — so a handler must check terminators on **both** halves and skip `'\0'`,
  which is what `RFIDReader.HandleKeyEvent` does. **IMGUI key events survive the new Input System
  backend** (`Active Input Handling = Input System Package (New)`), so `OnGUI` does *not* block a
  migration — believed until now, tested since, and it closed M15 by disproving its last clause. And
  **on Windows the app must have focus** to receive any of it: for an unattended kiosk with a wedge
  scanner, a stolen focus or a minimised window loses reads silently, and nothing in the reader can
  detect that.

  One caveat still untested: it is **believed** not to carry IME composition (that
  arrives through `Input.compositionString`) - untested, there is no IME machine here - and
  `Event.current` is bound to a live IMGUI pass, which *is* measured: assign it
  from anywhere else and it reads back **null**, so an `OnGUI` handler cannot be driven from
  a test unless you split it into a method that takes the `Event`.
- **`Invoke` fires after its GameObject is deactivated; a coroutine cannot even start there.**
  So "replace `Invoke` with a coroutine" is not a refactor, it is a behaviour change - and the
  first version of M10's fix made it on the one path that cannot take it. `AppInfo` deactivates
  itself and *then* relies on a delayed `Hide` to raise `onHiddenEvent`, which is the only
  forward link from the splash screen to `LoadScene`; hosting that coroutine on `AppInfo` left
  the app on the preloader with `BOOT STALLED`. `CallLater` runs on a persistent host for
  exactly this reason, and only a **destroyed** caller cancels it. Two reviewers found it and
  the boot test confirmed it - **a delayed call that outlives its own object is a requirement
  here, not an accident.**
- **A coroutine's `finally` does not run when Unity *stops* it.** Measured twice with a control
  (M95): `finally` runs on normal completion and on a throw, but `StopCoroutine`,
  `StopAllCoroutines`, `SetActive(false)`, `enabled = false` and `DestroyImmediate` all abandon
  the iterator without disposing it. So **cleanup for a cancelled coroutine cannot live in the
  coroutine** - it belongs in `OnDisable`/`OnDestroy`, or in whatever does the stopping.
  `TextureCacheManager` claimed the opposite in a comment for months and both reviewers repeated
  it back to me as precedent; M79 survives only because it marks its requests done *before*
  stopping. Note `OnDisable` covers destroy **and** deactivate, and `OnDestroy` alone misses the
  worse half (M94).
- **Catching an exception deletes the stack trace Unity would have printed.** On a coroutine that
  previously died, the exception was at least *reported* — with frames. Wrap it in a `try` and all
  anyone gets is your message. So `Development.LogException(e)` comes first in every catch that exists
  to keep the app running; it is always compiled (C7). Fixing a hang is not worth trading away the only
  evidence of its cause (M80).
- **Scope a `catch` to one mechanism, or the message will lie.** `AssetSorting(data, callback)` invokes
  the callback, so a `try` around it also covers the sort, the events it raises, their subscribers, and
  on one branch `LoadNextScene` itself — a scene missing from the build settings reported as "the asset
  walk threw". Pass a null callback and guard the follow-up separately.
- **A delegate targeting a destroyed `MonoBehaviour` is still a live delegate**, and Unity's
  fake-null does not help you: the invoke succeeds, and the *first* member access inside the handler
  throws `MissingReferenceException`. So a loop that invokes stored callbacks needs a `try`/`catch`
  **per callback**, or one destroyed consumer silently strands every subscriber after it. That is
  exactly how M79's fix reintroduced M79. Note `name` and `gameObject` both throw — `this == null` is
  the safe test.
- **`GetFileType()` throws on null or empty**, and the `MediaFile.isValid*FileType` helpers call it
  unguarded — so `MediaFile.isValidVideoFileType(null)` is an `ArgumentException`, not `false`.
  `TextureCacheManager` has a local `isValidImageFileType` that *does* guard, which makes the two look
  interchangeable when they are not. Check the string before asking `MediaFile` about it.
  **And pass the url, never `url.GetFileType()`** — those helpers extract the extension themselves,
  so handing them one applies it twice. That survives `"a.mp4"` only by luck, because
  `Path.GetExtension(".mp4")` returns `".mp4"`; an **extension-less url — which is what a live stream
  is** — extracts to `""`, and the second application throws. `VideoPlayerControllerBase` did exactly
  that, so the guard threw past its own `DisplayError()` and the failure path was disabled by the
  check meant to trigger it (L19, v0.49.4). A double-applied extractor is invisible in the common
  case and only fails on the input you test least.
- **`.webp` is not supported, and adding it to an extension list makes things worse, not better.**
  Unity decodes neither route: `DownloadHandlerTexture.GetContent` returns null with
  `DataProcessingError`, and `Texture2D.LoadImage` returns false — measured against a PNG control.
  The register carried its absence as a defect for a year (L8); admitting it would buy a per-image
  download and a per-image error in place of one aggregate rejection. Supporting webp means shipping
  a decoder. Note the neighbouring `.webm` **is** accepted for video and that is not an
  inconsistency — different decoder. Related: nothing here calls `Texture2D.LoadImage` at all, so
  reason about image decoding through `DownloadHandlerTexture`, which is what actually runs.
- **Optional trailing parameters make overload resolution order-dependent, and the order that
  reads best is usually the broken one.** `Development`'s caller-info parameters declare
  `[CallerLineNumber] int` **before** `[CallerFilePath] string`, which looks backwards and is
  load-bearing: with the natural order, `Log("tag", "message")` is CS0121, because the tag
  overload and the message overload are each better in one argument and neither wins. (Not at
  "~400 call sites" - the register said that for a year, and ~574 was no better. The measured
  answer is **zero here**: ten non-`LogIf` calls are two-argument - seven pass a
  `UnityEngine.Object`, three forward a caller-info line - and none passes a string, so none is
  ambiguous. The order is still right, for branched projects. Measure before you quote, and
  measure with a **parser**: a grep counts the commas inside `string.Concat(...)`, which is how
  "three" stood for months and how "seven" replaced it wrongly on the first correction.) An `int` in that slot is unreachable from a string argument, so the tag
  overload is the only candidate. The mirror survives and is a rule now, not a surprise:
  `Log("tag", someInt)` is CS0121 - pass a string. And `Log(message, someInt)` **compiles** while
  binding the int to the line number, which is the only silent one in the set.
  `Assets/Editor/DevelopmentOverloadGuard.cs` compiles the canonical shapes so a reorder fails
  there instead of in a client's tree; settle questions like this **by compiling**, not by
  reading the spec (M25).
- **A detector that compares against shipped content stops working the day the shipped content
  changes - and it stops working *silently*.** M85's first fix decided "is this file the
  placeholder?" by comparing it with `StreamingAssets/config/appData.json`. But the *deployed* copy
  is seeded once and **never patched**, while every branched project edits the shipped one
  eventually - so after the first content release every already-deployed machine has a seed that no
  longer matches its origin, and the check reports "real cache" over a stub. A detector that has
  quietly stopped working is indistinguishable from one that found nothing. **Stamp the artefact at
  write time instead** - `ConfigManager.StampSeededAppData` writes `length|ticks`, which
  self-invalidates when anything rewrites the file - and keep the comparison only as the fallback
  for artefacts that predate the stamp.
- **Serving a cache is a success; serving it silently is the defect.** A fallback that hands back
  yesterday's content and reports `Success` is *correct* - the caller has something to render - but
  without an age nobody can tell a CMS that answered from one that has been dead a week. Mark it and
  date it (`fromCache`, `cacheAge`), and put the age in the log in words an operator can act on:
  "3 day(s)", never a raw `TimeSpan`. Two traps found by review, both worth copying: a file dated in
  the **future** floors to "0 seconds ago" and asserts freshness for arbitrarily old content, and a
  single `default` for "unknown age" and "zero age" silently defeats every `if (age > threshold)` a
  consumer writes (M42).
- **A failed validation is not an error.** `IsValidEmail` logged an exception for every malformed
  address, because `IdnMapping.GetAscii` throws on a bad domain — so the log filled with what the method
  exists to detect. Return the `bool`; that is the report. Keep the log only for the genuinely
  exceptional case, which there is the regex *timeout* — 200ms of CPU on a pathological input, and a
  ReDoS signal if it repeats.
- **Two serializers coexist:** `JsonUtility` for `settings.json`/`appSettings.json`,
  Newtonsoft for `appData.json`. They disagree about properties and dictionaries.
  **A model type can be filled by BOTH, and then its constraints are the union of the two.**
  `OneCoreCommand` is the worked example: it is reached from `settings.json` *and* from an
  activity inside `appData.json`, so a change verified against one serializer can break the
  other. M55's register entry prescribed typing its payload fields as `string` - safe under
  `JsonUtility`, and `JsonReaderException` under Newtonsoft, which is what real project data
  actually uses. Before changing a field on a shared model, grep for every parser that fills it.
  As of v0.49.23 the `OneCore` block of `settings.json` is deliberately re-read AND re-emitted
  with Newtonsoft for exactly this reason - `JsonUtility` cannot see an `object` field.
- **Config resolution is persistentDataPath first, then StreamingAssets** — an operator can
  override a deployed build, so "it works in the editor" proves little about a machine that
  already has a persistent config. Whether a deployed copy exists at all is **one** predicate,
  `AppPaths.UsesDeployedConfig` — asked by the seeder (`ConfigManager`) and the settings reader
  (`FileManager.CoreSettings`). Two `#if`s that were meant to agree and didn't is exactly what H6
  was, so don't reintroduce a local spelling. `AppPaths.Config()` asks it too as of v0.16.1 (M46) — but the appData *cache* read deliberately
  does not, because a runtime cache has to be readable on every target. Gate an override, never a
  cache: gating the cache made the editor's offline fallback load the shipped stub and report
  success, since that stub's `"code": 0` is `APIResponseCode.Success`.
- **Route on the path, not the platform, and use `AppPaths.ToFileUrl` rather than writing it out.**
  Whether a config or asset path needs `UnityWebRequest` and a `file://` scheme depends on whether
  it *is* a URL, not on which target is compiling. Five sites decided this for themselves and got
  four different answers (M47), including one that prepended unconditionally and produced
  `file://jar:file://…` on Android. **String concatenation is not good enough**: `"file://" + path`
  404s on any filename containing `#`, because `#` opens the URI fragment - measured, on a file
  that loads fine as a bare path. `UriBuilder("file", "", -1, path)` is the construction that
  handles `#`, `%` and spaces on both a Windows drive path and an Android rooted one — provided
  `%` is escaped to `%25` first, or a filename containing `%20` is taken for an already-escaped
  space;
  `Uri.TryCreate(…, UriKind.Absolute, …)` returns **false** for `/data/user/0/…` and would leave
  Android broken. Test `path.Contains("://")` if you must ask the question yourself. Never `StartsWith("/storage/")`: that is only true with Write Permission =
  External(SDCard), and this project ships `ForceSDCardPermission: 0`, so a real `persistentDataPath`
  never matched it. All of it is one helper as of v0.42.0: five prefix tests across three files got
  this wrong, review found four more sites with no test at all, and every one of them now calls
  `AppPaths.ToFileUrl` — including the two config sites in `AppDataController`, which had the
  right test written out by hand and the `#` bug with it (M47).
- **`Assets/Plugins/Android` is generated, not hand-maintained.** Every `.aar`/`.jar` in it is EDM4U
  output (`Assets/MobileDependencyResolver`), resolved from a `*Dependencies.xml` under `Assets/` and
  recorded in `ProjectSettings/AndroidResolverDependencies.xml`. So **deleting an aar does not remove a
  dependency** - the next Force Resolve puts it back. Remove the declaration and the record too, and
  check `AndroidManifest.xml` for a `tools:replace` the SDK needed: dropping the aar that supplies a
  `<provider>`'s class turns that element into a launch `ClassNotFoundException`, because the manifest
  merger emits it whether or not a library declares it. The record is also the answer to "who pulled
  this in?" - it lists the packages resolved and every file installed for that set (two flat lists, so
  it proves set ownership rather than per-file attribution). **Both files are absent today**: zero
  declarations as of v0.18.3, and the next resolve regenerates whichever it needs (M58).
- **LINQ's cost depends on the runtime type you hand it, not on the parameter type you declared.**
  `Enumerable.Skip` type-tests its argument: a `List<T>` gets a `ListPartition` and indexes
  straight to the offset, a `HashSet<T>` gets an `EnumerablePartition` and **walks**. So the same
  `ICollection<T>` method is O(n) or O(n²) depending on what the caller passed - measured on
  `MiscExtensions.Split` at n=20000: 0.6ms flat for a `List`, up to 12.9ms for a `HashSet` (M13).
  Two consequences. A performance claim about a LINQ chain is **not checkable by reading it** -
  the register carried "O(n²/chunkSize)" for a year and it was wrong for every caller this repo
  has. And a `public` framework method whose complexity varies with the argument's concrete type
  is worth rewriting even when the measured win is zero, because a branched project cannot see
  which side of that test it is on.
- **`ConcurrentQueue<T>.Count` does not exist in Unity's .NET profile.** It is there in .NET Framework,
  so it compiles in a scratch project and in any reference you read, then fails here with `CS1061` (hit
  while writing C6). `ToArray().Length` and LINQ's `Count()` both walk the queue, so neither belongs on a
  per-message path - track the length yourself with `Interlocked`. The wider lesson is the same one
  `Rfc2898DeriveBytes` taught in H14: **"the API exists" and "the API exists in this profile" are different
  questions**, and the only way to settle the second is to compile.
- **`internal` is now real** (v0.24.0). Four assemblies, not one: `Assembly-CSharp` went from 260
  scripts to 13, so an `App/` edit recompiles 12 files instead of everything. The catch is that
  `internal` framework members are no longer visible from `App/` — `LocalizedField.GetLocalized`,
  `AppDataSettings.GetAssetPath` and `Timer.GetProgress` have each had to become `public`, and the
  next one will surface as `CS0122` rather than as anything subtle (M20, M16).
- **Widening one `internal` at a time is the right response; `InternalsVisibleTo` is not.**
  Before v0.24.0 one assembly made `internal` decorative — App code could call any of it — so the
  split turned roughly ninety framework `internal` members into latent `CS0122`s for a branched
  project that had been calling them (34 in `ScrollSnapBase.cs` alone, 9 each in `AppDataSettings.cs`
  and `SettingsParse.cs`). Three have surfaced so far and each was a **restoration**, not new API.
  When the fourth arrives and it starts feeling tedious, the one-line temptation is
  `[assembly: InternalsVisibleTo("App")]` — **don't**. It undoes the boundary M20 took four
  versions to build, silently and everywhere at once. Widen the member that has a caller.
- `settings.json` currently ships a plaintext password and a live client hostname (H14) —
  don't add secrets to it, and scrub it before any new project branch.

## Build

Scenes: `CoreSetup` (0) → `Intro` (1). Windows Standalone is the most exercised target; **Android
was built and run for the first time on 2026-08-14** and boots to `Core :: Boot complete.` on an
SM-T510. iOS, macOS, Linux and WebGL have still never been run — see the platform matrix in
`overview.md` before promising one.

`Assets/Editor/BuildValidator.cs` refuses builds that would ship a known-bad state.

**Always**, development builds included: `DEBUG_MODE` defined (M18), and a backup-shaped filename
in `Assets/StreamingAssets/config/` — that folder is copied into the player verbatim, so a
`settings_bck.json` carries the same password, key and client hostname to the site (L13).

**Release builds only**: `VERSION.txt` disagreeing with `bundleVersion` or the descriptor (M33),
and the panel password (H14) — the default one, any plaintext one, and a hash *of* the default. Development builds skip those three and say so in the console — the template ships the
default password by definition, so a hard gate would stop this project building at all, and a gate
that blocks test builds is a gate someone deletes.

All rules run before any of them throws, so one failed build tells you everything that is wrong
rather than one thing per attempt.

**Two rules warn instead of failing.** `cleanUnlistedAssets` enabled in the shipped `settings.json`
(L12) was a hard release gate until v0.56.2 and is now a warning: a release build is often
pre-configured for one site, where the flag may be deliberate, and unlike H14 there was no legitimate
spelling that expressed "on purpose" — so the gate left such a build no way through but to disable the
validator. It still says loudly what the flag does on first run. Note what that gives up: this rule is
the only thing that has ever caught the flag being wrong in the template (H21, then M123), and a
warning will not stop a third — though it caught neither promptly, since nothing here builds release
routinely.

The other (E2, v0.17.0): every key in the shipped `settings.json` is walked
beside the `CoreSettings` field that would receive it, and any key no field can receive is reported. That
is M54's failure mode — `JsonUtility` matches by field name and says nothing about a key it cannot place,
so a mismatch is a block that is simply never read. It cannot be a gate because legitimately unreachable
keys exist (M55's `dynamic` fields), and a gate would need a suppression list from day one. It does **not**
descend into array elements, and does not check `appData.json`; both limits are in the message.
