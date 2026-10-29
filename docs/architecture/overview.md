# Architecture — unity_default_6

The Gema base project. Every client project is branched from here, so anything in
`EnigmaVirtual/` is shared code: a bug here ships to all of them.

- Unity 6 (6000.3), Built-in Render Pipeline, legacy Input Manager (`activeInputHandler: 0`)
- `apiCompatibilityLevel: 3` (.NET Framework profile), Mono for Standalone
- Four assemblies: `EnigmaVirtual` (framework + `External/`, 214 scripts), `App` (12),
  `LeanTween` (8) and what is left of `Assembly-CSharp` (13), plus `Assembly-CSharp-Editor`
- ~32k LOC across 282 runtime scripts

Companion docs: [known-issues.md](known-issues.md) · session logs in [`../sessions/`](../sessions/)

---

## 1. Layers

```
Assets/01_Scripts/
  App/                  project-specific code — the only folder a derived project should edit
  EnigmaVirtual/        the shared framework
    Core/               bootstrap, settings models, managers, UI shell
      Modules/          Connections (Api/OSC/Sockets/FileDownloader), Electronics (Phidgets), UI widgets
      Display/          core UI shell, transitions, screens, scaler
      Utils/ Models/    helpers and serializable settings DTOs
    Data/               AppDataController, Translations, AssetManager, TextureCache*
    Components/         reusable UI (scroll snap) and Video
    Extensions/         ~21 static extension classes, plus CallLaterHost (a runtime MonoBehaviour)
    IO/                 SerialPortsManager, RFIDReader
    Attributes/         custom inspector attributes (all #if UNITY_EDITOR guarded)
    Screens/ UI/        navigation, panels, keyboard
  External/             third-party: UnitySerialPort, UnityRawInput
Assets/Editor/          ~25 custom inspectors + BuildValidator, PreBuildProcessor, SceneSelector
```

Dependency direction is `App → EnigmaVirtual`, one way, and **as of v0.24.0 the compiler enforces
it**. It did not hold for most of this project's life: the framework referenced `App` concrete types
(`ScreenType`, `MediaVO`, and `AppData`/`AppSettings` construction in `AppDataController`), a genuine
cycle, and that is what blocked the `.asmdef` split. Broken across v0.21.1–v0.23.0 and split in
v0.24.0; `ScreenView` went earlier, in v0.4.0. See M20 for how each was resolved and `CLAUDE.md` for
the rule. A new framework→`App` reference is now `CS0246`, not a review finding.

A second leak: **24** framework types live in the **global namespace** — 51 before the v0.50.0
slice, 42 before v0.52.0 and 27 before v0.54.6 — not the seven this document claimed until v0.28.0, and two of the seven it named,
`LocalizedField` and `MediaFile`, were never among them. Measured with a parser that blanks comments
and string literals, tracks brace depth and ignores nested types, over `EnigmaVirtual/` and
`External/`. **Do not quote the total on its own**: it splits four ways and every bare figure
published for it so far has been wrong.

| | n | |
|---|---|---|
| `External/` forks | 3 | `UnitySerialPort`, `GUIManager`, `ComMessage` — left global on purpose. Not to keep a clean diff against upstream: both are already framework-dependent forks calling `Development` (7 sites and 1), so there is no upstream to merge back to. It is churn with no payoff |
| Genuine extension holders | 21 | global is **correct** — an extension method is only in scope where its namespace is, which is what makes `someString.Bold()` work with no `using` |
| Cleanly movable | 0 | `TextureCacheManager`, `TextureCacheLoader`, `PhidgetsSettings`, `FieldsVisibilityController`, `BatchFileCreator`, `OscCommand`, `OSCCommandType`, `Auto_MagneticInfiniteScroll`, and `ArrayExtensions`/`ProcessExtensions`/`WindowUtils` — the last three are named like holders but contain **zero** `this` parameters |
| Blocked, needs extracting first | 0 | Was 3 - `Colors`, `RegisterField`, `ValidationType` - each sharing a file with an extension method, so the file could not be wrapped wholesale. **Done in v0.54.6 by extracting, not wrapping**: `Colors` left `StringExtensions.cs`, and `ToggleGroupExtension` left `RegisterField.cs` and stayed global, which let the rest of that file be wrapped. The holder's global placement never gave way - it declares `GetActive(this ToggleGroup)`, and it is counted among the 21 above |

Nine moved in v0.50.0, and v0.52.0 deleted four duplicate global copies that shadowed nothing (L23). **The migration is complete as of v0.54.6 (L17 closed)**: every type still global is one for which global is *correct*, and the classification was verified by asking the loaded assembly which types declare a method carrying `ExtensionAttribute` - filename-blind, because bucketing a holder by its filename is the mistake this migration made twice. Three of the 21 holders are `internal` (`TransformExtensions`, `UIExtensions`, `WebRequestExtensions`), so a branch cannot collide with them at all; the real collision surface is 18 public holders plus the 3 forks.

## 2. Entry points and global state

Two scenes ship: `CoreSetup` (index 0) → `Intro` (index 1).

`Core` is the root. It is `DontDestroyOnLoad` and exposes the framework through statics:

| Static | Type | Set in |
|---|---|---|
| `Core.Instance` | `Core` | `Awake` |
| `Core.settings` | `CoreSettings` | `Awake`, from `settings.json` |
| `Core.main` | `MainController` | `Awake`, `GetComponentInChildren` |
| `Core.file` | `FileManager` | `Awake` |
| `Core.notificationManager` | `NotificationManager` | `Awake` |
| `Core.localizationSettings` | `LocalizationSettings` | field initialiser |
| `Core.IsSet` | `bool` | `CoreSetupComplete()` |
| `Core.LanguageChangedEvent` | `Action` (public **field**, not `event`) | — |

`Core.IsSet` gates almost everything. The prevailing pattern is
`if (!Core.IsSet) return;` at the top of `Start()`, which leaves fields null while
`Update()` keeps running — see known-issues H13.

One more object is `DontDestroyOnLoad`, and it is not authored anywhere: **`~CallLater`**,
created on the first `MonoBehaviour.CallLater(...)` call and held in the static
`MonoBehaviourExtensions._host`. Delayed calls run on it rather than on the caller, so they
survive the caller being deactivated exactly as `Invoke` did — `AppInfo` schedules its
boot-critical `Hide` *after* deactivating itself (M10). It carries no `HideFlags`, so it is
visible under DontDestroyOnLoad and a stuck delayed call can be found there.

`MainController` is a service locator: it owns `AppDataController`, `OneCore`, `Phidgets`,
`ActivityMonitor`, `SocketManager`, `Api`, `OscController`, `ForegroundEnforcer`,
`TextureCacheManager`, `SerialPorts`, and creates each on demand in `Init()` based on
`Core.settings.<Module>.isActive`.

## 3. Boot sequence

Ordering is enforced by a chain of one-shot events. Each hop is a single
`event` or `WaitUntil`, so any link that never fires used to leave the app on the preloader forever
(known-issues C3). As of **v0.10.0 the whole chain is watched**: every hop calls
`Core.MarkBootStage`, and a coroutine armed at the top of `Core.Awake` - before the settings parse,
so it covers that failure too - fires when no stage changes for `max(90s, effectiveRequestTimeout * 2)` - recomputed on every stage change, because until `settings.json` is parsed there is nothing to read and the 90s floor is the right answer. The widening applies only when downloading or the texture pre-cache is enabled; with both switched off the floor stands. It
logs at Error, shows a notification, and latches a flag that `LoadNextScene` honours, so boot cannot
continue into the start scene with incomplete data.

It measures **stall, not total elapsed time**: a legitimate first-run download of several hundred
assets outlasts any fixed budget, so per-asset progress marks feed it. Those marks are
**not logging** even though they look like it: `MarkBootStage(stage, log)` takes the deadline reset
and the log line as separate concerns precisely because gating the whole call on a `doLogs` flag -
which reads as a reasonable way to stop 200 lines of asset spam - starves the watchdog through the
longest phase of startup. Only the log line is optional. `_bootComplete` is set
immediately before `SceneManager.LoadScene` at all three call sites, not on entry to
`LoadNextScene` - with `UseAppInfo` the real scene load is several unguarded callbacks later.

```
ConfigManager.Preload()            [RuntimeInitializeOnLoadMethod, BeforeSceneLoad]
  └─ seeds persistentDataPath/StreamingAssets/config/ from StreamingAssets, patches keys
     (every player except WebGL as of v0.16.0; gated on AppPaths.UsesDeployedConfig, which
      FileManager.CoreSettings() shares — the two disagreeing *was* H6. Synchronous where the
      shipped file is a real file, so the patch lands before Core.Awake reads it; only a
      jar:file:// path is fetched, and that one is a launch behind)

Core.Awake()
  ├─ resolves main / systemUI / notificationManager, new FileManager
  ├─ reads settings.json (PlayerPrefs "Settings" wins if present)
  │    parse failure ⇒ early return ⇒ IsSet stays false ⇒ nothing else initialises
  └─ CoreSetupComplete() → onCoreSetEvent → main.Init() + systemUI.Init()

MainController.Init()
  ├─ creates the enabled modules
  └─ if (!AppData.isActive) Core.LoadNextScene()          ← the only short path

AppDataController.Init() → TryLoadData()
  ├─ remote: LoadRemoteData(dataJsonCompletePath)
  └─ local:  LoadLocalData()
       └─ ParseAppData / ParseOneCoreAppData → OnDataLoadedEvent

MainController.AppDataController_OnDataLoadedEvent
  ├─ localizationSettings.SetLanguages(AppData.languages)
  └─ AssetDownload.isActive ? SetupAssetManager() : LoadNextScene()

AssetManager.ValidateAssets()
  └─ AssetSorting (reflection walk) → download/validate → CLEANUP (deletes unlisted files, C2)
     → onAssetCleaningCompleteEvent

MainController.AssetManager_onAssetCleaningCompleteEvent
  └─ TextureCache.isActive ? TextureCacheManager.CacheAssets() : LoadNextScene()

TextureCacheManager → onAssetVerificationCompleteEvent → Core.LoadNextScene()
  └─ waits on chunk completion + its own stall watchdog   ← v0.2.0; whole chain watched v0.10.0

Core.LoadNextScene() → (optional AppInfo fade) → SceneManager.LoadScene(StartScene)
```

**What the template actually ships**, checked against the committed file rather than from memory,
because this paragraph was wrong until v0.30.0 (H21):

| | shipped |
|---|---|
| `AppData.isActive` | **true** |
| `AppData.Cache.AssetDownload.isActive` | **true** — this paragraph claimed `false` |
| `AppData.Cache.AssetDownload.cleanUnlistedAssets` | `false` — was **`true`** from v0.15.0 to v0.29.2 |
| `AppData.Cache.TextureCache.isActive` | `false` |
| `AppData.oneCoreDataModel.isActive` | `false` |

**This client branch matches the template here, but for its own reason.** The Museu Salvador
Caetano project ships `AppData.Cache.AssetDownload.isActive: true` because its content comes from the
client's live CMS, whose images are downloaded from the same host (see the client subsection at the
end of §5). From 2026-10-07 to v0.56.4 it shipped `false`, for offline sample data with no host.

So the download stage **does** run out of the box and the texture-cache boot stage does not. Note the limit of that sentence, which
M2 turned up: it is the *stages* that are gated, not the components. `TextureCacheManager` is created
unconditionally and `GetOrSetTexture2D` has no gate, so on-demand loads through `TextureCacheLoader`
run with the flag off. That is why the defects in those two stages
went unhit in the field. H3, H4 (v0.2.0) and **C2 (v0.7.0)** are fixed — the recursive cleanup is
now behind its own `cleanUnlistedAssets` flag, default false, so enabling downloads no longer
enables a delete. Enabling `AssetDownload.isActive` against a real installation is defensible; see
the C2 and M29-M31 entries for what the guards actually check.

### Operator shortcuts

Windows only (`#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN`), all behind Ctrl+Alt so none can be
hit by accident, all in `MainController.Update` - which is a Unity message and therefore runs whether
or not the boot chain completed. That is deliberate: these exist for the case where it did not.

| Shortcut | Effect |
|---|---|
| `Ctrl+Alt+S` | Open the persistent config folder |
| `Ctrl+Alt+I` | Open the install folder |
| `Ctrl+Alt+R` | Full restart |
| `Ctrl+Alt+K` | **Skip asset validation** (E1) - configurable via `System.Windows.skipAssetValidationKeyCode`, falling back to `K` when absent |

The skip is sticky and one-way: it can be pressed before the asset phase starts or during it, takes
effect at the next asset rather than the next chunk, and skips the cleanup step entirely. It reports
at Error and raises a notification, because an installation quietly running on stale media would be
worse than the wait it avoids.

### What is actually on the Core object

Verified 2026-08-13 by GUID-mapping **`CoreSetup.unity`**, which is the scene that boots. This
matters because `MainController`'s `SetupXxx` helpers behave differently depending on whether a
component is already there.

> The list below used to be mapped from `Core.prefab` instead, and was wrong for three components
> because of it. That prefab, and the two `SystemUI.prefab` copies beside it, were **deleted in
> v0.35.0** (M74): nothing referenced them, they were in the pre-2018 prefab format, and they still
> serialized fields the classes had renamed years ago (`HiddenEvent`, and a `LongTouxhZone` typo).
> `CoreChecker` loads the *scene*, not a prefab, so nothing ever read them. **Map the scene, not a
> prefab** — that is the whole lesson of this entry.

Present: `Core`, `ConfigManager`, `MainController`, `CoreSystemUI`, `NotifyController`,
`ImageLoaderThread`, `MainThreadDispatcher`, `AppInfo`, `Factory`, `DesignScaler`, `ConfigsPanel`,
`Preloader`, `SettingInput`, `DetectLongClick`, `NotificationManager`, `EditorSettingsLockScreen`,
`Timer`, and — corrected on 2026-08-13, all three previously listed as absent —
`TextureCacheManager`, `AssetManager` and `ProjectAppDataController`. The three that matter for the
boot chain sit at `Core/MainController/AssetManager`,
`Core/MainController/TextureCacheManager` and `Core/MainController/AppData`.

That last one matters most: `ProjectAppDataController` **is** authored under `Core`, which is the
condition `MainController.SetupAppData` needs in order to deserialize into the project's own
`AppData`/`AppSettings` types rather than silently falling back to the `*Base` ones. See the note in
`CLAUDE.md`; it is satisfied here, and a derived project that removes it gets no compile error.

Absent (so always created at runtime): `AppDataController` (the base type; the project subclass is
present), `ApiController`, `OSCController`, `SocketManager`, `ActivityMonitor`, `PhidgetsManager`,
`OneCore`, `SerialPortsManager`, `Console`, `ForegroundEnforcer`.

Consequence: the `SetupXxx` early-`return` bug (H2) is still **dormant**, but not for the reason
this section used to give. Two of the components it named *are* in the scene — and neither of their
helpers has an early `return`: `SetupAssetManager` (`MainController.cs:582`) adopts and then
subscribes unconditionally, and `SetupTextureCacheManager` (`:616`) has nothing to skip, since it
subscribes nothing and calls no `Init()` (its subscribe happens later, at `:671`). The **nine**
helpers that do early-`return` — `SetupSerialPorts`, `SetupForegroundEnforcer`, `SetupDebugConsole`,
`SetupOscController`, `SetupApiController`, `SetupSocketManager`, `SetupActivityMonitor`,
`SetupPhidgets`, `SetupOneCore` — all serve components with zero GUID hits in `CoreSetup.unity`. Add
any one of them to the scene and H2 goes live.

**H3's history was wrong in the same way, and is corrected here.** This section used to say
`SetupTextureCacheManager`'s wrong-object bug was **live in every build** because "the create path was
the only path". It was not: `CoreSetup.unity` has carried a `TextureCacheManager` under
`MainController` since the initial import (`3bb0a61`), and the pre-fix code adopted it with
`GetComponentInChildren` before reaching the buggy line, so the defect never executed in the shipped
scene — only in a project that removed the scene component. The claim came from the same
`Core.prefab` map as the rest of this section. **Fixed in v0.2.0** regardless: it now uses
`AddComponent`'s return value, adopts an existing `TextureCacheManager.Instance` first, and will not
accept an inactive component — both alternatives stalled the boot chain instead of failing loudly.

### Failure reporting

`NotificationManager.ShowNotification` is the framework's single reporting choke point and, as of
v0.8.7, it **logs every notification before rendering it** — `Error` through `Development.LogError`,
which C7 made unconditional, so it survives on platforms where `DO_LOGS` is undefined. Twelve
`catch` blocks (nine in `AppDataController`, three in `Core` around the settings parse) report
exclusively through it, so this is what keeps them out of the silent-failure category (M26).

Two consequences worth knowing: an explicit `Development.LogError` next to a `ShowNotification` call
now double-reports, so add one only where you want it (`Core`'s single settings-parse catch does, since
that failure is the one people go looking for); and
`Core.notificationManager` is resolved from the **scene** (`Core > SystemUI > NotificationsPanel` in
`CoreSetup.unity`) — deleting that panel in a derived project disables on-screen notifications,
which `Core.Awake` now reports at Error. (This used to read "not from `Core.prefab`"; there is no
`Core.prefab` as of v0.35.0, and the scene was always the only source.)

## 4. Configuration pipeline

Three JSON files, all under `config/`:

| File | Type | Deserializer | Purpose |
|---|---|---|---|
| `settings.json` | `CoreSettings` | `JsonUtility` | framework config — which modules are on, hosts, hardware |
| `appSettings.json` | `AppSettings` | `JsonUtility` | per-project settings |
| `appData.json` | `AppData` | Newtonsoft (`JObject`) | content: media, translations, galleries |

Resolution order is **persistentDataPath first, then StreamingAssets** — so an operator can
override a deployed build without rebuilding. `ConfigManager` seeds the persistent copy on
first run and patches newly-introduced keys into an existing one (H5, v0.9.0). Both halves are
gated on `AppPaths.UsesDeployedConfig`, so they are skipped in the editor and on WebGL — and on
Android the patch lands from the *second* launch, because a `jar:file://` read needs a coroutine
(M45).

Two serializers coexist: `JsonUtility` (fields only, no dictionaries, no properties) for
settings, Newtonsoft for content. This is the root of the field-vs-property confusion in
the VOs — see known-issues M3.

**Path convention:** all **config** paths go through **`AppPaths`**
(`Core/Managers/AppPaths.cs`) and `Path.Combine` — C4, fixed v0.3.0. The root cause was one value
with a separator baked in (`FileManager.configDirectory = "config\\"`), which broke even the call
sites that already used `Path.Combine`. `configDirectory` is deleted; treat any
`string.Concat(..., "\\", ...)` in a path as a portability bug.

Asset paths are still composed by `/` concatenation in `AppDataSettings.assetsPath` rather than
`Path.Combine` — deliberately, because the result is handed to `UnityWebRequest` as well as to
`File`/`Directory`, and because `GetAssetPath` concatenates a relative url onto it, so it must keep
its trailing separator. They do use `AppPaths.PersistentStreamingFolder` for the root segment, so
the download cache sits **inside** persistentDataPath/StreamingAssets alongside the config override
(M32) rather than beside it.

Three exceptions, all legitimate: code that emits a **Windows batch script or shell command** needs
literal backslashes (`BatchFileCreator`, the `explorer.exe`/`taskkill` paths), and
`DirectoryWrapper` deliberately normalises to `\` and splits on it as a Windows-specific utility. And because
`Application.persistentDataPath` returns `/` on Windows while `Path.Combine` joins with `\`,
`AppPaths` values are mixed-separator — fine for `File`/`Directory`, but `explorer.exe` silently
ignores a path containing a forward slash. **Wrap anything going to a shell in
`AppPaths.ForShell()`** (M24, learned the hard way).

## 5. Data and localization

`AppData` (project) extends `AppDataBase` (framework). Content VOs use
`LocalizedField<T> : Dictionary<string, T>` for per-language values, keyed by the
`Langs` enum name. (Named `TranslatableField<T>` before v0.28.0.)

Lookup path: `Core.main.GetTranslation(key)` → `AppDataController.GetTranslation` →
`Translations.GetTranslation` → `LocalizedField.GetLocalized`.

**Two vocabularies, and both are deliberate.** `Translations` is a *store* mapping a translation
key to a `LocalizedField`; a `LocalizedField` is *one value* with a variant per language. So the
path above does two different lookups, and the naming says which is which: on `LocalizedField`,
a `*Localized` member resolves a **language** (`GetLocalized`, `TryGetLocalized`, `HasLocalized`)
while a plain name looks up a **key** (`TryGet`, `GetValue`). The same type is used both ways —
`LocalizedField<string>` is keyed by language, and `Translations`'s
`LocalizedField<LocalizedField<string>>` is keyed by translation key and holds one of the former
per entry. Do not "finish" the rename by pushing `Localized` onto the key-lookup members.

`GetLocalized` returns `default` (i.e. **null** for strings) when a translation is missing, and
the `[Obsolete] GetTranslation` that returned `string.Empty` was deleted in v0.28.0 — one
accessor, one contract.

**A null there does not throw, and believing it did cost most of a round.** M6 asserted for
months that assigning `null` to `TMP_Text.text` risks an NRE in TMP's parser. It does not, in the
TMP that ships with Unity 6: the setter stores null (`TMP_Text.cs:131`) and every consumer
null-checks (`:2056`, `:2073`, `:2442`). The real failure was the opposite of a crash — the text
simply never changed, so the previous language stayed on screen looking correct. **When a doc
here claims a specific engine behaviour, read the package source before building on it**;
`Library/PackageCache/` has it.

Language changes go through `Core.ChangeCurrentLang(lang)` → `Core.LanguageChangedEvent`.
Subscribers must unsubscribe **unconditionally** in `OnDestroy`; the common
`if (!Core.IsSet) return;` guard there can skip cleanup and leave destroyed objects in the
invocation list (M7).

### This project: the Museu Salvador Caetano timeline

Everything in this subsection is client code under `App/` and client content. None of it is
framework, so a project branched from the template does not inherit it.

**Screens.** All three live in `Intro` under `Canvas ScreenController`: `Call` (start) → `Home`
(category picker) → `Timeline`. `App/UI/ScreenController` overrides `NavigateTo` to call
`SetData(navItem.data)` on the target screen before showing it; the base controller never passes data
on. Home sends an `int[]` of category ids: one id for a single category, every id for "all
categories". The Timeline's back button goes to Home.

**What is authored in the scene, and what is built at runtime.** No prefabs are involved.
- **Authored in `Intro.unity`:**
  - Each screen object carries its RectTransform, CanvasGroup, background Image and screen component.
  - `Home ScreenView` also has authored chrome: logo, rotated side texts, and the **Back Button** that
    `HomeScreenview.backButton` references.
  - `Timeline ScreenView` has no authored children.
- **Built at runtime from serialized `TimelineStyle` and geometry fields:**
  - Home: the category links and the "all categories" link.
  - Timeline: the scroll area, cards, axis, header, scroll hint, footer filter, and its own back button
    (four 1 px edges plus a transparent hit area), unless one is assigned under that screen.

`Assets/Editor/TimelineSceneSetup.cs` (`Gema/Timeline/Setup Scene`, or `TimelineSceneSetup.Setup()`)
wires the scene side idempotently:
- adds or reuses the screen components and sets their `screenType`
- wires Home's existing back button only if that field is empty
- fills empty style references
- sets the Home and Timeline backgrounds to the design's charcoal `#191919`
- adds or reuses `CallBackgroundAnimation` on `Call ScreenView`, fills its empty year font, and fills
  each empty group photo from `Assets/03_Textures/App/call/`, which holds the five original JPGs the
  user supplied:
  - **Matching:** 1926 → `Mask Group 1@2x.jpg` and 2026 →
    `Salvador-Caetano-faz-alianca-com-a-Neolix-para-logistica-inteligente-na-Europa-1_final@2x.jpg`
    are named explicitly, because those names carry no year. 1946, 1971 and 1982 take the first file,
    in ordinal order, whose name contains the year (`anossahistoria_<year>@2x.jpg`).
  - **Import:** only the files a group resolves to are set to Sprite, Single, mipmaps off; nothing
    else in the folder is touched.
  - A year with no file is reported and left empty, never given a placeholder.

**Call screen background** (`App/UI/CallBackgroundAnimation.cs`). Five groups, one per dated photo
(1926, 1946, 1971, 1982, 2026), each with a vertical line, a ring marker and a stacked year.
- **Layout:** positioned in reference pixels from the static design (Adobe XD, first screen); the
  positions are serialized and editable per group.
- **Build:** done in code on the first `Play`, as the first child of `Call ScreenView`, so every
  authored element (logo, titles, body, start button, language panel, side texts) stays on top and
  untouched. It is clipped to the screen and nothing in it is a raycast target.
- **Motion:** each group drifts on its own sine, in unscaled time, bounded by `travel`. The defaults
  are ±12 px horizontally over a 14 s `periodSeconds`, with a per-group phase. On the same period and
  phase, each **photo** (only the photo) pulses in opacity between `photoMinAlpha` (default 0.65) and
  1, following a cosine, so a phase-0 photo starts fully opaque. Lines, markers and years stay
  opaque. The motion is this project's choice; the design is static.
- **Lifecycle:** `CallScreenview.Show` calls `Play`, and a repeat call is ignored. `Hide` calls
  `Stop`, which disables the component, returns every group to its place and every photo to full
  opacity; `OnDisable` stops it
  too. The ring sprite is generated and destroyed by the component.
- **Missing photos:** a group without a photo shows its line and year only. No placeholder is ever
  substituted.

The setup wires Call's animation, font and photos while leaving its authored background colour unchanged. As of 2026-10-07, the scene
holds `#191919` on all three screen backgrounds and on the camera's clear colour, which shows
during screen fades.

**Timeline layout** (`App/Timeline/TimelineLayout.cs`, pure and checked by `TimelineLayoutSelfCheck`):
- Decades run from 1920 to the decade of the latest year in the **complete** `timeline`, so
  filtering never shrinks the axis. A closing line is labelled with the next decade.
- The axis is a horizontal baseline through the decade markers. It runs from the content's left edge
  to its end, and at least the viewport's width, so it still crosses the screen with every decade
  collapsed. It is drawn behind everything else on the timeline. Each marker is a grey ring with a
  `#191919` interior that hides the baseline inside it, and each vertical decade line stops at the
  top of its ring. Both colours are baked into the sprite and the marker is drawn untinted, because an
  Image tint would darken the interior too.
- A decade with no event in an active category collapses to a narrow strip. All categories off is
  allowed and collapses every decade.
- Two rows. Items fill top to bottom, then right, ordered by year then id.
- When active categories have events in the same year with identical localized card text, those events
  share one card. The tag lists each contributing category name, and the gallery combines their
  supported image URLs in event order with duplicate URLs shown once. Repeated matching events from
  the same category remain separate.
- Images sit at the bottom in a decade's first column and on top in its second, alternating, and the
  count restarts in each decade.
- A card's `Body` is an invisible raycast area over the whole card, so a drag outside the photo pans
  the timeline. Only the card's **text section** and the category **tag** are filled with
  `TimelineStyle.backgroundColor` (`#191919`), so the axis lines never show through them.
- The frame outline surrounds the text section and the tag only, never the photo. The text section,
  and its frame with it, sits at the top of the card when the image is at the bottom, and at the
  bottom when the image is on top. The frame is a non-raycast overlay with no centre fill, added as the last child of each, so it draws over the
  text and dots. A missing or borderless frame sprite is skipped, leaving the unframed fill.
- The category tag is sized to its one-line label each time the card is bound, which includes
  every language change. It is measured with TMP's preferred width, which includes the margins and
  character spacing. The minimum is `min(tagWidth, 0.9 × card width)` (160 with the default cards), and the maximum is 90% of the card's width
  (234 narrow, 342 wide). A label wider than that keeps the maximum width and is auto-sized down
  from `tagFontSize` until it fits on one line, so it is never cut.
- Cards have two fixed widths (default 260 / 380 reference px) and one fixed height. Narrow when the
  localized text fits at full size, measured with the card's own TMP font; otherwise wide, with the
  font shrinking in steps to a minimum. Text that still does not fit ends in an ellipsis and is
  reported at `LogError`, once per event and language.
- The footer filter shows each category in the accent colour. Selected categories are fully opaque;
  switched-off ones use `footerUnselectedAlpha` (default 0.35, Inspector range 0–1). The text style
  is always normal, with no strikethrough.
- Only four-digit years 1920–9999 are placed; others are excluded from layout and galleries, so a
  malformed year cannot extend the axis. Events the timeline can never show (a `categoryId` missing
  from `categories`, a year outside that range, a null entry) are reported at `LogError` once per screen lifetime. Without that report they
  simply disappear, even with every category on.

**Images are per event** (`TimelineLayout.Gallery`): only the event's own `images`, in order, empty
URLs skipped and identical URLs kept once. When matching events from different categories share a
card, that card's gallery is the ordered union of those events' images, with duplicate URLs removed.
Only URLs of a supported image type reach a gallery -
`.png`, `.jpg`, `.jpeg` (any case), checked by `TimelineLayout.IsImageUrl` through
`MediaFile.isValidImageFileType` - so a video, PDF, `.webp`, `.gif` or extension-less URL is never
requested; each one is reported once per screen lifetime at `LogError` (`Timeline: event N (year) lists
"url", which is not a supported image type`). An event whose only images are rejected is treated as
having none. A URL with a query string (`x.jpg?v=2`) is rejected too, because the extension check
reads the whole string; the live CMS sends none. An event with no images shows no photo and no slide
indicator. Its slot geometry is unchanged, but its text section always sits at the **top** of the
slot - on an image-on-top card too (`TimelineCard.PlaceTextSection`) - so it never floats under an
empty photo area; the transparent photo area is simply left below it. A card shows another event's
images only when those events are merged by matching year and localized text across active categories.
(Before v0.56.3 the gallery was shared by every event of the category, so an image-less card opened on
the category's first image.)
- **Slideshow:** a gallery of two or more advances by itself, one slide at a time, wrapping from the
  last image back to the first.
  - **Crossfade:** each change crossfades over `slideFadeDuration` (default 0.5 s, unscaled; 0 swaps
    at once) between two cached RawImage layers.
    - **Blend:** for opaque images, per pixel a lerp from the outgoing image to the incoming one, each transparent
      outside its own width. It is built from native UI layers, with no shader:
      - **Overlap:** the incoming image fades 0→1 **above** the outgoing one, which is held at alpha 1.
        UI blending is source-over, so coverage stays 1 and the colour is `t·in + (1−t)·out`, with no
        dip toward the background.
      - **Outgoing wider:** for the duration of the fade, the outgoing layer is cropped (`uvRect`) to
        the incoming width, at the same scale. A third RawImage, `Texture Strip`, shows the remaining
        columns at alpha `1 − t`, so that area fades smoothly to the background.
      - **Incoming wider:** the area only the incoming image covers fades in at alpha `t`.
      - At the end the outgoing layer and the strip are disabled, with their textures and crop reset.
  - **Loading:** the current image stays until the next texture has loaded, and a failed load keeps
    it. The first image of a card appears without a fade.
  - **Stale callbacks:** a generation token means only the newest texture request can change the
    photo; older, destroyed-card or disabled-card callbacks are ignored.
  - **Hold-back and disable:** the next slide waits for a running fade, and for a load in flight, to
    finish. Deactivating or destroying the card cancels its requests, clears both layers and the strip,
    and releases every texture lease.
  - **Visibility:** `TimelineScreenview` decides each frame in `LateUpdate` which cards are
    in view, from each card's layout span and the scroll offset, widened by `visibilityMargin` (default
    480 reference px) on both sides as a preload zone. It calls `TimelineCard.SetVisible`, which is
    independent of `enabled`, so single-image cards are tracked too. The span scan allocates nothing;
    entering cards can allocate binding and image-request state.
  - **Virtualization:** every event is laid out, which fixes its width, position and content width,
    but a card exists only while its event is in view or within the margin. An event leaving view
    returns its card to a pool. The card is released first, so no request, image or lease survives,
    and deactivated, and its shown slide is kept in the layout record. An event entering view takes a
    pooled card of the same fixed geometry kind (narrow/wide × image top/bottom), which is moved,
    activated before `Bind` (TMP measures the tag) and bound with the retained slide.
  - **Pool size:** leaving cards are returned before arriving ones are taken, so the pool holds at
    most the largest number of cards of one kind ever in view together. Scrolling or filtering can
    raise that historical peak; otherwise existing cards are reused. Cards are destroyed only at teardown.
  - **Prefetch:** only cards in view prefetch, and only the next slide, at low priority.
  - **Timing:** each card samples its own interval when it is bound, uniformly between
    `slideIntervalMin` and `slideIntervalMax` (default 4–8 s, unscaled time), so cards change out of
    step. A gap longer than one interval, such as a pause, restarts the timer rather than skipping
    several slides at once.
  - **Lifecycle:** the card's `Update` runs only for a visible cycling gallery.
    - **Rebuild** (a filter or language change) returns every card to the pool, recomputes the
      layout, and binds cards only for the events in view. Slides restart from each event's own
      image.
    - **`Hide`** returns every card to the pool and deactivates the cards' container.
    - **`OnDisable` / `OnDestroy`** release every card in place, because Unity refuses `SetActive`
      while the screen itself is being deactivated. On reactivation the cards in view load again.
    - A reused card's `Bind` cancels and releases whatever it held before binding.
- **No interaction:** a drag anywhere on a card, the photo included, pans the timeline. The photo area
  has no graphic of its own, so nothing on it is a raycast target.
- **Single touch only:** while the timeline is shown, `SingleTouchInput` (a `BaseInput`) is the event
  system module's `inputOverride`. It exposes only the first finger to touch down, until it lifts;
  every other finger is ignored, including one still held when the first lifts, and the mouse reads as
  absent while any finger is down. `TimelineScreenview` installs it in `Show` and removes it in
  `StopListening` (Hide/OnDisable/OnDestroy); the swap takes effect in `LateUpdate`, and fingers already
  down at install are reported once as cancelled so Home's pressed state is released. Other screens keep
  Unity's default multi-touch. A missing event system is logged at `LogError`.
- **Indicator:** bounded. A gallery of 2 to `maxIndicatorDots` (default 5) images gets one dot each.
  A larger one gets a single `current / total` counter, and no dots. The dots and the counter are built
  once per card and only shown, hidden and repositioned by each `Bind`. A single image and an empty
  gallery get neither. The indicator tracks the image actually on screen, not one still loading. The
  gallery itself is never truncated: every image of the category still cycles.
- **Fit:** each image fills the photo area's full height at its own aspect ratio, left aligned,
  through a native `AspectRatioFitter` in `HeightControlsWidth` mode, one per layer. A `RectMask2D` clips a wide
  image on the right. The photo area has no background, so a narrow image leaves whatever is behind the
  card showing on its right. Images are never centred and never cropped vertically.
- **Textures:** they come from `TimelineTextureCache` (App only, no framework change), which
  `TimelineScreenview` creates and owns, and `TimelineCard.Create` receives it.
  - **Loading:** requests are queued Visible before Prefetch. Requests for the same file, after
    normalizing the path, share one load, and a visible request joining a queued prefetch promotes it.
    At most 2 loads run at once, and an aborted load keeps its slot until it reports back.
  - **Leases:** each caller gets a disposable lease, and a leased texture is pinned. A card holds one
    lease per layer, plus one for its prefetch, and releases a lease only after clearing the image
    that shows it. The strip shares the outgoing layer's lease.
  - **Cancellation:** a pending load is abandoned only when its last subscriber leaves. A late or
    stale delivery disposes its lease.
  - **Budget:** `textureBudgetMiB` (128–256, default 256) is soft. Eviction takes unpinned textures
    first, least recently used first, then those pinned only by prefetch. Visible textures are never
    destroyed: a visible working set over budget is kept and logged once per crossing. A prefetch is
    never started while the cache is at or over budget.
  - **Counting:** each texture counts as `max(Profiler.GetRuntimeMemorySizeLong, RGBA32 with a full
    mip chain)`. This is taken when the texture loads and re-measured about once a second
    (`ReconcileBytes`, from the screen's `LateUpdate`), because the runtime figure can grow after
    load. This runs whenever the screen is active, hidden or not, so retained unpinned textures are
    trimmed too. An inactive screen pauses it: its cards' leases are already released, and the
    entries keep their last sizes until the next reconcile after reactivation. Between a load and the next reconcile, `ReadyBytes` and `HighWaterBytes` can briefly
    undercount, for at most about a second.
  - **Teardown:** `OnDestroy` releases the cards first, then disposes the cache.

  `TextureCacheManager` is no longer used by the timeline. The dot discs and axis-marker rings are generated at
runtime by `TimelineUi.MakeCircleSprite` (optionally with an interior colour) and destroyed by
`TimelineScreenview`.

**Content** is `appData.json`: `categories` (`id`, localized `name`) and `timeline` (`id`,
`categoryId`, `year`, localized `content`, `images[].url`). It ships **generated scaling sample
data**, written by `Assets/Editor/TimelineScalingDataset.cs` (*Gema → Timeline Scaling → Generate
Dataset*, or `Generate(first, count)` in chunks from eval): the ten real categories and languages are
kept from the file itself, and `timeline` is 100 events, ten per category, years spread 1920–2026,
PT/EN text in three lengths prefixed "Exemplo NNN:" / "Sample NNN:". Each event lists ten images of
its own, `scaling/c<cat>_e<NNN>_i<k>.png` under `StreamingAssets/assets/`, 1000 distinct opaque PNGs
(landscape, portrait and square, longest side ≤ 512) labelled with their ids. Galleries are per
**event**, so with this dataset every card cycles its own ten images. The same content is written to
`assets/scaling/appData.scaling.json` as the baseline fixture; `Validate()` checks both are
byte-identical and every count. Generation is deterministic and idempotent and never deletes; it
replaced the earlier 30-event placeholder sample. `placeholder.png`/`placeholder2.jpg` remain on disk,
now unreferenced. The 1000 PNGs ship in any player build (StreamingAssets is copied verbatim).

**Client config (as of v0.56.4): live CMS.** `settings.json` ships `AppData.useLocalAppData: false`
and `remoteSettings.hostUrl: https://salvadorcaetano.gemadigital.com/` with
`dataJsonPath: api/timeline/data`, so the content above is fetched from the client's API at boot, and
`AppData.Cache.AssetDownload.isActive: true` downloads the `uploads/...` image urls it lists from the
same host. The shipped `appData.json` (the scaling sample) is no longer what a connected machine shows;
it remains the local fallback content. The live data has 211 events and 30 images, at most one per
event, so most cards are text only. Card images still load through the timeline's own
`TimelineTextureCache`, which no setting gates. Measured in the editor on 2026-10-19: boot reaches
`Core :: Boot complete.` against the live API and the image-carrying cards show their photos. **Not
verified:** a first boot with no network, and a standalone player against the live host. A machine
that already holds a `PlayerPrefs["Settings"]` snapshot or a deployed `persistentDataPath` config
keeps its own values - clear both to pick up the shipped config; this branch writes neither.

## 5b. Breaking changes by version

The merge checklist for a branched project. The register holds the *why* for each issue; this is just
what breaks and what to do. Not retro-filled beyond the day it was introduced.

**Commands in this section are given for PowerShell first.** Every machine that merges this project is a Windows box at a `PS>` prompt, and `grep`/`ls -l`/`wc -l` are not available there - `ls` is an alias for `Get-ChildItem`, which rejects the flags outright. The POSIX form is kept in brackets for anyone on git-bash or WSL. Note `Select-String` has no `-Recurse` of its own - pipe `Get-ChildItem -Recurse` into it, which is what the forms below do and what was verified by running them.

**Deleting a framework member versus deprecating it** has been decided three times now, and the
discriminator is **visibility across the v0.24.0 assembly boundary**, not how much work the member
does. `public` means a branched project can reach it, so it earns an `[Obsolete]` naming its
replacement — `Timer.SetDuration` (v0.27.0). `internal` with no in-assembly callers means no
branched project's own assembly could ever have called it, so deprecation buys nothing and it is
deleted — `LocalizedField.GetTranslation` (v0.28.0). Either way, check first that there is no
**silent**-break path: a `UnityEvent` persistent call, a serialized reference, or reflection by
name. If every consequence is a compiler error, deletion is safe (M62).

| Version | Change | What to do |
|---|---|---|
| v0.55.3 | `StringExtensions` diacritics: `í` now percent-encodes correctly, `RemoveDiacritics(string)` no longer passes through an empty transliteration, and both `char` overloads are `[Obsolete]` (L34). Plus review fixes to the v0.55.2 build gate (M120) | **Three behaviour changes, all corrections.** `"í".ReplaceDiacritics()` returned `%FD`, which is `ý`'s encoding; it now returns `%ED`. If you stored or compared that output anywhere, the old value was wrong. `"ЪъЬь".RemoveDiacritics()` returned the characters unchanged and now returns an empty string, which is what the table always said they transliterate to. And `RemoveDiacritics(char)` used to throw `IndexOutOfRangeException` on those four characters; it now returns them unchanged.<br><br>**Two `[Obsolete]` warnings, no breakage.** `ReplaceDiacritics(char)` and `RemoveDiacritics(char)` still compile and still run. The first always returned `'%'` - it cannot be correct, because the table's values are three-character percent-encodings - and now returns its input; the second truncates 36 of 89 multi-character transliterations. Move to the `string` overloads, which are correct.<br><br>**Do NOT "fix" the 21 entries in `diacriticsChars` that look wrong.** That table is a **Windows-1252** percent-encoder, not a Unicode codepoint encoder: `€` is `%80` and `™` is `%99`, which are right for CP1252 and nothing like the codepoints. Counter-tests pin four of them.<br><br>The v0.55.2 font gate now scopes its scan to `Assets/`, so a package-owned font asset can no longer block your build with something you cannot edit. |
| v0.55.2 | New **always** `BuildValidator` gate: a Dynamic TMP font asset must be able to reach its source font (M120). Also `AndroidTargetArchitectures` 3 -> 2 | **Your build will now fail if any Dynamic TMP font asset has no reachable source, or has one with *Include Font Data* disabled.** That is deliberate and it is not release-only: such an asset renders every glyph outside its baked atlas as a **space**, in any build, with no error. If it fires, the message names the asset - restore the source font, or set the asset to Static if its baked atlas is meant to be the whole coverage.<br><br>**Two shapes it will NOT refuse**, both asserted: a **Static** asset with no runtime source (that is correct - it keeps the font out of the build), and a Dynamic asset backed by an **OS font** through `m_SourceFontFilePath`. If you hit a false positive beyond those, say so rather than deleting the rule.<br><br>**`AndroidTargetArchitectures` moved 3 (ARMv7 + ARM64) to 2 (ARM64 only)**, which Unity 6 reconciled after the 6000.3.22 upgrade - 32-bit ARM is no longer supported. **A 32-bit-only Android device will no longer get a compatible build.** Every ARM64 device is unaffected, including the SM-T510 this project has been tested on. |
| v0.55.1 | `LiberationSans.ttf` and its OFL licence **restored** (L32, L33) | **Take this - it repairs a silent failure introduced in v0.55.0.** The `.ttf` is the runtime source for `LiberationSans SDF - Fallback`, which is a **Dynamic** TMP asset that ships **empty** (`m_ClearDynamicDataOnBuild: 1`) and is the last-resort fallback for **every** text object in the project via `TMP_Settings.defaultFontAsset`. Without it, any character outside the static 250-glyph atlas rendered as a **space** - not a box - with no error a site would notice. That gap spans 16 of the 32 languages `Langs.cs` declares.<br><br>Nothing to do beyond merging: the GUID is unchanged, so both font assets still resolve. If you deleted the font in your own branch after taking v0.55.0, restore it the same way. |
| v0.55.0 (assets) | `splash.jpg` deleted; `TextMesh Pro/Fonts/` emptied - `LiberationSans.ttf` and its OFL licence removed (L32, L33) | **`splash.jpg` is safe to take: nothing referenced it.** Its GUID `407bbe56...` appears in no scene, prefab, asset or script, and Unity's own splash is off (`m_ShowUnitySplashScreen: 0`, `m_SplashScreenLogos: []`). The word "splash" in the codebase refers to the preloader screen, not this file.<br><br>**The font deletion needs a look if your content is not plain Latin.** `LiberationSans SDF` stays and is still TMP's default - it is **Static** with 250 glyphs and self-contained, and every character measured resolves from it, including the full Portuguese set, `EUR`/`GBP`, degree, ordinals, en-dash and curly quotes. What broke is its **fallback**: `LiberationSans SDF - Fallback` is *Dynamic* and its source font is now null, so a glyph outside those 250 - Greek, Cyrillic, rarer diacritics - **silently does not draw**, with no exception and no log. If your project's content reaches beyond Latin-1, restore `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` from history.<br><br>**And you can no longer regenerate the atlas**: both font assets have a null `sourceFontFile`, so adding glyph coverage means finding a source font first. See L32 for the two ways to resolve it, and L33 for the licence question - the OFL text was removed while the derived atlas still ships. |
| v0.54.9 | `GraphicGuide` capped at 1024 and **moved** to `Assets/Resources/Textures/` (M119, closing it) | **Nothing to do - the `.meta` moved with the file, so the GUID is unchanged and every scene reference still resolves.** That is the whole reason this was safe: moving a `.jpg` without its `.meta` would have minted a fresh GUID and left `CoreSetup.unity` with a missing sprite.<br><br>**Two things worth knowing if you merge it.** The texture is now under `Resources/`, which means it is packed into every build **unconditionally** - it is no longer removable by deleting the GameObject that references it, and if you were relying on that you should move it back out. In exchange it is loadable by path: `Resources.Load("Textures/GraphicGuide")`. And it imports at **1024x576 DXT1 with no mipmaps** where it was 1920x1080 RGB24 with 11 - runtime memory 19,764,245 -> 689,129 B measured, so if you had your own reason for full resolution, raise `maxTextureSize` rather than reverting the file.<br><br>The GameObject remains in scene 0 and remains disabled. It is a design-time alignment overlay; if you enable it to check something, disable it again before building - nothing detects a guide left switched on. |
| v0.54.8 | Operator asset pass: `GraphicGuide` re-imported compressed with no mips, six textures updated, `Sockets.isActive` **off** in the shipped `settings.json`, OSC `serverPort` 7700 -> 7000 (M119 partly) | **Two things to check, both in your own `settings.json`.** `Sockets.isActive` now ships **`false`**, so if your project relies on the socket module booting from the shipped default it will silently not start - `MainController.Init()` only creates a module when `isActive`, and an absent or false flag is indistinguishable from "module off" by design. And `OSC.serverPort` moved 7700 -> 7000; if your installation's OSC sender still targets 7700 it will simply hear nothing, with no error on either side. Both are operator-tunable values rather than API, so a merge conflict here is yours to resolve in favour of your site's config.<br><br>**Nothing to do for the textures.** `GraphicGuide.png` became `GraphicGuide.jpg` and `CoreSetup.unity` was re-pointed in the same change, so no reference dangles. It is a design-time overlay, disabled at runtime. The scene also gained `Timer.interval: 1` and `AssetManager.maxRetryCount: 3` as serialized values simply because it was opened and saved - both equal to their C# defaults, so behaviour is unchanged, but be aware that from now on **that scene carries the values rather than inheriting them**, so a later change to either framework default will not reach it. |
| v0.54.7 | `Assets/Resources/Gema/Prefabs/Ui/AppInfoPanel.prefab` **deleted** (L30) | **Act only if your project loads that path at runtime.** It had zero consumers here and packed to 1,268 B; a **byte-identical** copy survives at `Assets/04_Prefabs/Ui/AppInfoPanel.prefab`, which is not under `Resources/` and so is not packed into builds.<br><br>**If you do load it**, `Resources.Load("Gema/Prefabs/Ui/AppInfoPanel")` now returns **null**, and null is the whole problem: `Resources.Load` does not throw or log for a missing path. Worse, if you reach it through the framework's own `Factory.Get(folderPath, goName)` (`Core/Factory.cs:32,41`), the very next line does `go.name = goName` on the result, so you get a **NullReferenceException** rather than a clean null - and because that path is composed from arguments, no text search of your code will find the call sites for you. Grep for the *string fragments* instead: `Get-ChildItem -Recurse -Include *.cs Assets | Select-String 'AppInfoPanel'` [`grep -rn AppInfoPanel Assets --include=*.cs`].<br><br>**To restore it**, copy `Assets/04_Prefabs/Ui/AppInfoPanel.prefab` into any `Resources/` folder - it is the same asset, so nothing else changes - or better, reference it directly from an inspector field and drop the string path. Your scene is unaffected either way: `CoreSetup.unity` authors its `AppInfo` inline rather than instancing the prefab, so no component detached. |
| v0.54.6 | `Colors`, `RegisterField` and `ValidationType` move into `namespace EnigmaVirtual`; `ToggleGroupExtension` extracted to its own file and **stays global** (L17, closing it) | **Add `using EnigmaVirtual;` to any file of yours that names `Colors`, `RegisterField` or `ValidationType`.** The break is **loud** - `CS0246` - so the compiler finds every site; there is no silent path. `Colors` carries the real traffic, via `.Colored(Colors.red)`.<br><br>**Zero action for `someGroup.GetActive()`**, and that is the point of the asymmetry: `ToggleGroupExtension` changed *file* and not *namespace*, because an extension method is only in scope where its namespace is, so namespacing it would have been a `CS1061` at every call site that does not even name a namespace.<br><br>**Two cases worth naming.** If your project already declares its own global `Colors`, **it wins** over the using-imported one and you need do nothing; if you end up importing two namespaces that both offer the name, that is `CS0104` and the fix is an alias (`using Colors = EnigmaVirtual.Colors;`), not dropping the import. And a `[CustomEditor(typeof(RegisterField))]` or `[CustomPropertyDrawer(typeof(ValidationType))]` under your `Assets/Editor/` breaks **`Assembly-CSharp-Editor`**, which **neither framework csproj reports** - the v0.49.16 failure mode, visible only in the Unity console.<br><br>**Nothing breaks silently, and here is why rather than just the claim.** Prefab and scene components keep working because `RegisterField.cs` was *wrapped, not renamed*, so its `.meta` GUID is unchanged. `ValidationType` serializes as an `int`, so no serialized data is touched, and the `Colors` member names are unchanged, so nothing that round-trips them by name shifts. An Inspector-wired `UnityEvent` on `Validate()`/`SetData()` keeps a stale `m_TargetAssemblyTypeName`, which is cosmetic for an instance call - measured, L25. The honest residue: a **static or null-target** persistent call resolves only through that string, and no repo here has one, so that case is **untested** rather than safe. |
| v0.54.5 | `Assets/Editor/SystemUiEditor.cs` **deleted** - the unreachable second inspector for `CoreSystemUI` (L29) | **Act only if your project declares its own `[CustomEditor(typeof(CoreSystemUI))]`, and then only to re-check which one runs.** There were two in `Assembly-CSharp-Editor`, and Unity ran `CoreSystemUIEditor` - measured with `Editor.CreateEditor` before and after, unchanged. `SystemUiEditor` drew a plain `DrawDefaultInspector()` and could never run, so deleting it changes nothing an operator sees here. **For a branch with a third declaration the candidate set has changed**, and which inspector wins is not expressed by any of the declarations - so if you added your own, open a `CoreSystemUI` and confirm you still get the inspector you expect. There is no compile error either way, which is the whole hazard. Code naming `EnigmaVirtual.SystemUiEditor` - a subclass, say - is `CS0246`, which is loud. Nothing in this repo named it and its GUID `ac24ed45e8d65254c9c7464cd127df64` had zero references. |
| v0.55.0 | **Five packages removed** from `Packages/manifest.json`: `com.unity.ai.navigation`, `com.unity.timeline`, `com.unity.visualscripting`, `com.unity.multiplayer.center` and `com.unity.feature.development` — the last of which took `com.unity.ide.rider`, `com.unity.editorcoroutines`, `com.unity.performance.profile-analyzer`, `com.unity.testtools.codecoverage` and `com.unity.settings-manager` with it, since it was their only referrer | **Act only if your project actually uses one of them — none is used here** (zero `.cs` references, zero graph assets, no `PlayableDirector` or NavMesh component in any scene or prefab). Re-add any of them through Package Manager; nothing in the base project depends on them, so a re-add is a one-line manifest edit and cannot conflict.<br><br>**The built-in engine modules stayed**, which is the distinction that matters: `com.unity.modules.ai`, `.director`, `.animation` and `.particlesystem` are all still in the manifest, so `UnityEngine.AI.NavMeshAgent` and `UnityEngine.Playables.PlayableDirector` still compile — measured in the loaded domain, along with `Unity.AI.Navigation.NavMeshSurface`, `UnityEngine.Timeline.TimelineAsset` and `Unity.VisualScripting.ScriptMachine` all reading **absent**. What is gone is the package-level tooling: the NavMesh surface/modifier components and their bake window, the Timeline window and `TimelineAsset`, and every Visual Scripting graph type.<br><br>**If you use Rider**, re-add `com.unity.ide.rider` explicitly — it was only ever here as a child of `feature.development`, and the IDE integration is what generates its solution. `com.unity.ide.visualstudio` and `com.unity.test-framework` were already top-level entries and are unaffected; `com.unity.test-framework` is also required by `com.unity.pipeline`, so it could not have gone anyway.<br><br>**`ProjectSettings/NavMeshAreas.asset` and `ProjectSettings/MultiplayerManager.asset` are deliberately left in place** — Unity writes both regardless of which packages are installed, and deleting them only makes it rewrite them. Same for the `m_NavMeshLayer` field in every scene and prefab YAML, which is a `GameObject` field and has nothing to do with the navigation package. |
| v0.54.4 | `Gema.ApplicationDescription` **deleted**, and with it the `Gema/Core/` folder (M118) | **Act only if your project has a descriptor asset bound to GUID `624587063c4714c4b85d95774d219770`, or code naming `Gema.ApplicationDescription`.** There were two `ApplicationDescription` `ScriptableObject`s carrying the **identical** `[CreateAssetMenu(menuName = "Gema/Descriptor")]` - one in `Assembly-CSharp`, one in the framework - with diverged `Client`/`Year`/`Copyright`. Unity registers one menu item for a duplicated path, and it was measured to create the **framework** type, so a descriptor made from the menu on any version is unaffected and there is nothing to do. Code naming the deleted type is `CS0246`, and `using Gema;` is now `CS0234` because the namespace is empty - both loud, which is why this was a delete rather than an `[Obsolete]`.<br><br>**If you do hold such an asset, do this IN ORDER - the recovery window closes on its own.** **1.** Open the `.asset` in a **text editor** and copy out `FullName`, `Slug`, `Client`, `Year`, `Copyright`. First, because with the script gone the values survive only as YAML and the Inspector can no longer show them. **2.** Only then open *Gema > App Descriptor*: `AppDescriptor.cs:37`'s typed load returns null on a wrong-type asset and `:40-41` `CreateAsset`s **over that same path**, so opening the window to look at the problem destroys it. **3.** Re-enter the values - the deleted type's defaults were the stale 2016 ones.<br><br>**This deletion did not create that overwrite**: a typed `LoadAssetAtPath<EnigmaVirtual.ApplicationDescription>` already returned null on a `Gema`-typed asset, same null, same overwrite. What changed is **recoverability**, which is why step 1 leads. At runtime such an asset NREs rather than failing quietly - `DesignScaler.cs:27`, `AppInfo.cs:63` and `AnalyticsPayload.cs:32` dereference unguarded - and the `AppInfo` one surfaces as `BOOT STALLED` on the preloader, with the NRE above it the only thing naming the cause.<br><br>**One clause about the folder meta.** `Gema/Core.meta` is deleted because the folder is empty *here*. A branch that put its own file in `Gema/Core/` merges a meta delete while the folder still has content; Unity mints a fresh folder GUID with no error. Harmless unless that branch assigned an AssetBundle to the folder. Check with `Get-ChildItem -Recurse -Include *.asset,*.cs Assets | Select-String '624587063c4714c4b85d95774d219770|Gema.ApplicationDescription'` [`grep -rn '624587063c4714c4b85d95774d219770\|Gema.ApplicationDescription' Assets`] - zero hits in this repo. |
| v0.54.3 | The duplicate `UIGenerator.UIGeneratorModel` under `Assets/ThirdPart/AssetGenerator/Editor/` **deleted** (L27) | **Nothing to do unless the project has its own editor script naming `UIGeneratorModel` - and if it does, this breaks *silently*.** There were two classes of that name in that namespace, one per assembly, with different bodies: the `Editor/` one reversed the `assets` array and each element's layers, the `Scripts/model/` one does neither. An editor script naming the type bound to the `Editor/` copy (its own assembly's source wins, under `CS0436`); after this merge the name still resolves - to the non-reversing copy - so there is **no compile error** and the generated layer order flips. Check with `Get-ChildItem -Recurse -Include *.cs Assets | Select-String 'UIGeneratorModel'` [`grep -rn UIGeneratorModel Assets --include=*.cs`]; a hit inside an `Editor/` folder is the case that changes. If the reversal was wanted, **restore both halves** - `ReverseData()` alone is only half of it. The deleted `SetData` reversed the `assets` array *and* called `ReverseData()` on each element, so at your own site reverse the array yourself and call `ReverseData()` per element (guarding a null `layers`, which it does not). `ReverseData()` is still `public` and is deliberately kept for this. Nothing in this repo named the type, which is why it could be deleted rather than deprecated. A branch that *edited* the deleted file gets a loud modify/delete conflict instead; take the delete, since its edits were to a type nothing could reach. See L28 for whether the reversal was right in the first place. |
| v0.54.2 | The three Gema inspectors target the framework **base** class with `editorForChildClasses`, and the two screen menu items resolve your concrete subclass through `TypeCache` instead of naming it (L26) | **Nothing to do, and one thing to know.** Your editor assembly no longer names `ScreenController`, `ScreenView` or `VideoPlayerController`, so renaming or deleting any of them in `App/` no longer fails `Assembly-CSharp-Editor` - which used to take `BuildValidator`, `AppVersionSync`, `PreBuildProcessor` and `SceneSelector` down with it, reported by no framework csproj.<br><br>What changed for you: the inspectors now apply to **every** subclass of `ScreenControllerBase`/`ScreenViewBase`/`VideoPlayerControllerBase`, not only to a type named `ScreenController`. A second subclass that used to get Unity's default inspector now gets the Gema one, which hides `m_Script`. And if you have **two** concrete subclasses of one base, the `GameObject → Gema` menu items add whichever `TypeCache` enumerates first and log an error saying so - the choice is not stable across domain reloads. One subclass per base is the supported shape. |
| v0.54.0 | `OSCCommandType` and `OscCommand` leave the global namespace for `EnigmaVirtual` (L17) | **Add `using EnigmaVirtual;`** to any file of yours naming either, unless it is already inside `EnigmaVirtual` or a child. A miss is `CS0246`, which names the type. This completes the namespace migration for every type that could be moved cleanly: what remains global is 3 `External/` forks, 21 genuine extension holders where global is **correct**, and 3 types that share a file with an extension method and would need that file split first. (This row first said 20 and 4, counting `ToggleGroupExtension` as blocked rather than as a holder. It declares `GetActive(this ToggleGroup)`, so it must stay global whatever happens to the file - it is not work anyone should schedule. The census table in section 5a was right; this row was wrong.) |
| v0.53.0 | **Nine more framework types leave the global namespace** for `EnigmaVirtual` (L17): `ArrayExtensions`, `Auto_MagneticInfiniteScroll`, `BatchFileCreator`, `FieldsVisibilityController`, `PhidgetsSettings`, `ProcessExtensions`, `TextureCacheLoader`, `TextureCacheManager`, `WindowUtils` | **Add `using EnigmaVirtual;`** to any file of yours naming one of the nine that is not already inside `EnigmaVirtual` or a child. A miss is `CS0246`, which names the type - so the compiler finds them all for you.<br><br>**Check your editor scripts specifically.** `Assets/Editor/` compiles into `Assembly-CSharp-Editor`, which neither framework csproj references, so a framework-only build reports `0 Error(s)` while your editor assembly is broken. That is how this slice's one real break was found here.<br><br>Nothing else: no serialized data changed, no `.meta` moved, and scene components bind by GUID. In particular `FieldsVisibilityController` moved and its Inspector-wired buttons still fire - measured in play mode, because a UnityEvent persistent call resolves from its live target, not from the stored type name (see the v0.50.0 row, whose step 2 is narrowed accordingly). |
| v0.52.0 | Four duplicate global types deleted: `MinMaxRangeAttribute`, `RangedFloat`, `RangedInt`, `RangedExtensions` (L23) | **Nothing to change unless your code names one of these four without `using EnigmaVirtual;`.** Each existed **twice** - once in `EnigmaVirtual` (with the property drawer) and once in the global namespace, byte-identical. It compiled because C# name lookup never saw an ambiguity from either side: inside `EnigmaVirtual` the enclosing copy won, and from a global file the global one did. The global copies are gone, so a global-namespace file of yours naming any of them now needs `using EnigmaVirtual;` - `CS0246`, which names the type. Nothing serialized: the two `RangedFloat`/`RangedInt` declarations were structurally identical, so a scene or asset holding one deserializes into the survivor unchanged. |
| v0.51.0 | **`AppData.downloadFolder` is removed** from the settings schema (M46) | **Check your `settings.json` before taking this. If `AppData.downloadFolder` is empty - which is what shipped - there is nothing to do.**<br><br>**If yours is NOT empty, do this first: delete the old media tree by hand and expect one full re-download.** The field composed the media root as well as the appData cache path, so removing it moves your media from `StreamingAssets/<folder>/assets` to `StreamingAssets/assets`. Every asset then misses `AssetManager`'s existence check and the **entire media set silently re-downloads** on the next boot - `Cache.AssetDownload.isActive` ships `true` - while the old tree is stranded outside the cleanup root and is never pruned. There is no error: the download event keeps moving the boot stage string, so the stall watchdog will not fire either, and the symptom is simply a boot that takes hours.<br><br>**Code naming the field** is `CS1061` at the exact site; compose the path without it.<br><br>**A `downloadFolder` key left in your `settings.json`** is harmless but permanent: `BuildValidator`'s schema rule warns that no field can receive it, on every build, and `FileManager.SaveCoreSettings` merges on-disk-as-base so the orphan key **survives saves** rather than being cleaned up. Delete the line. A key inside a `PlayerPrefs["Settings"]` snapshot needs nothing - `JsonUtility` ignores keys it cannot place - so **no clear-PlayerPrefs step is required** here, unlike a rename or an addition.<br><br>Otherwise a no-op: the resolved appData path and media root are byte-identical to before. The one exception is Android, where the removed empty segment used to leave a literal `StreamingAssets//assets` - the `#if UNITY_ANDROID` return happens before the separator collapse. Filesystem-identical, and `AssetManager` canonicalises anyway. |
| v0.50.1 | Two new `LogError` lines around `appSettings.json` (M46, M116). No signature changed. | **Nothing to change; read this if either line appears.**<br><br>**"written to \[...\] but this build reads the SHIPPED config"** - your settings panel saved to the persistent folder on a build that reads the shipped one (the editor without `DEBUG_MODE`, or WebGL). The save had no effect here, **and the leftover file will be preferred by any player you build on this machine**, which shares the same `persistentDataPath` - delete it if you did not mean that.<br><br>**"deserialized to null"** - your `appSettings.json` is zero-length, almost certainly from an interrupted write. Previously this stored a null and surfaced later as an NRE inside the asset walk, reported as "the asset walk threw"; it now keeps the defaults and names the file. Delete or restore it. |
| v0.50.0 | Nine framework types leave the **global namespace** for `EnigmaVirtual` (L17): `ConfigManager`, `ConfigManagerHelper`, `KeyData`, `DownloadVO`, `MainThreadDispatcher`, `TransitionBase`, `TransitionIn`, `TransitionOut`, `TransitionType` | **Two steps, and the second is the one no compiler will do for you.**<br><br>**1. Add `using EnigmaVirtual;`** to any file of yours that names one of the nine and is not already inside `EnigmaVirtual` or a child of it. A miss is `CS0246` and names the type, so the compiler finds them all for you. Two notes: if your project declares its own **global** `ConfigManager` (or any of the nine), you need to do **nothing** - your global declaration wins over a `using`-imported one, and that is this change working as intended; and if you end up with `using EnigmaVirtual;` beside another namespace declaring the same name you get `CS0104`, which you fix with an alias (`using ConfigManager = EnigmaVirtual.ConfigManager;`), not by dropping the import.<br><br>**2. Optionally grep your scenes and prefabs.** This was described here as "the silent half" until it was measured on 2026-08-18: a persistent call carrying a live target resolves from that object's runtime type, so a stale type-name string is cosmetic and your buttons keep working. It matters only for a **static / null-target** call, which nothing here demonstrates. One command, worth running, not worth blocking on. A `Button.onClick` (or any UnityEvent) wired in the Inspector stores the **namespace-qualified** type name in `m_TargetAssemblyTypeName` and resolves it with `Type.GetType`. It is **not** GUID-protected like an ordinary component reference, so a stale entry fails with no compile error, no `dotnet build` error and nothing in the Unity console - the button simply stops doing anything. `TransitionBase` is the realistic victim: ~20 public void methods (`Play`, `SlideInRight`, `FadeOut`) that are exactly what people wire to buttons.<br><br>`Get-ChildItem -Recurse -Include *.unity,*.prefab Assets \| Select-String 'm_TargetAssemblyTypeName: \S'` — the value is empty for a call bound by target-object type, which **is** GUID-safe; only a non-empty one names a type and can go stale. Any hit needs its `m_TargetAssemblyTypeName` edited to `EnigmaVirtual.<Type>, <Assembly>`. **Zero hits in this repo** - that is why the change could ship - but your scenes are not in this repo.<br><br>No behaviour changed: the move was verified content-identical modulo indentation, and no `.meta` moved, so every ordinary component reference survives. |
| v0.49.23 | OneCore command payloads survive `settings.json`, and `dynamic` becomes `object` (M55) | **Read this if your project touches `OneCoreCommand.command_data` or `event_data`.**<br><br>**Source-breaking, loudly.** Both fields are `object` instead of `dynamic`. Assigning still compiles and `JsonConvert.SerializeObject(cmd.command_data)` is unchanged - that is the common shape and it was verified against real project data - but **member access stops compiling**: `cmd.command_data.devices` becomes `CS1061`. Migrate to `((JObject)cmd.command_data)["devices"]`, or deserialize the payload into a type of your own.<br><br>**One silent case to grep for:** `cmd.command_data == ""` compiled as dynamic value equality and now compiles as **reference** equality, so it changes answer instead of failing. Use `(cmd.command_data as string) == ""`.<br><br>**What you gain.** `command_data` written into `settings.json` under `ConnectCommands` or `DisconnectCommands` now actually arrives - `JsonUtility` skips `object`, so `Core` re-reads that one block with Newtonsoft after the main parse - and it survives a save from the settings panel, which it would not have: `JsonUtility.ToJson` cannot emit it and the merge replaces arrays wholesale, so the payload was being erased from the operator's own file. Both writers - the file and the `PlayerPrefs` snapshot - go through one emitter now.<br><br>**And a fix you did not ask for:** three `Development.LogIf` calls in `OneCore` were dynamically dispatched, which prevents the compiler eliding a `[Conditional("DO_LOGS")]` call - so on iOS and WebGL they were running, and evaluating `Core.settings.OneCore.doLogs`, where they should have vanished. Two of them also logged `command_data` while sending `event_data` |
| v0.49.22 | A deployed `config/appData.json` is now found whatever `downloadFolder` says (M46) | **Superseded by v0.51.0, which removed `downloadFolder` entirely - read that row instead; the three-candidate resolver described here is now two.** Kept because a branch merging across both versions passes through this state.<br><br>**Nothing to change if `AppData.downloadFolder` is empty - which is what ships. Read on if yours is not.**<br><br>`AppDataSettings.downloadedDataJsonPath` now tries **three** locations instead of two, most specific first: `config/<downloadFolder>/appData.json` (the remote pipeline's cache), then `config/appData.json` (the config root), then the copy that shipped in the build. With `downloadFolder` empty the first two are the same path, so resolution is unchanged.<br><br>**With a non-empty `downloadFolder`, this is a behaviour change in your favour.** `ConfigManager`'s seed and the generated deployment `.bat` both write to the config **root** - neither can consult `downloadFolder`, because the seeder runs before settings are parsed - so the reader previously skipped straight past that file to the shipped stub, which parses as a success and declares no media. It now finds it. If you had set `downloadFolder` and worked around this, the workaround is no longer needed; if you deliberately relied on the shipped copy winning, it no longer does.<br><br>Also fixed in the same version: a deployed file found that way is no longer **announced** as the shipped placeholder. `LoadLocalData` discriminated two cases where there are now three, so it would have logged "declares no media, expect empty content" over real content and switched off asset cleanup for it. It now uses the seed stamp to tell a seeded stub from a real deployment.<br><br>**Still true, and the reason M46 is not closed:** with `downloadFolder` empty the runtime cache and an operator override are the same file, so in the editor a stale copy left by a past `DEBUG_MODE` session is indistinguishable from a deliberate override. Separating them needs `downloadFolder` to have a real default, which also moves where downloaded media lives |
| v0.49.21 | The offline appData fallback now says what it served and how old it is, on every target (M112, M114, M115) | **Nothing to change. One new log line you will see, and one private signature moved.**<br><br>**The line:** when the CMS cannot be reached, the app now reports - at `Error`, on every target - `appData is being served from the local file [path], last written N ago, because the remote copy was not used`, followed by whether that is real content or the shipped placeholder. **Windows always did this; Android and WebGL never did**, because that route returns before the block that reported it, so a weeks-old cache was served exactly as if the CMS had just answered. If you have Android installations, expect this line on any boot that falls back - it is the intended behaviour, not a new fault.<br><br>An age of "unknown" means the file is inside the application package, where there is no timestamp to read. That is reported as unknown rather than as zero on purpose: "0 second(s) ago" over a shipped stub is the most reassuring possible way to describe content that was never fetched.<br><br>**If you override `AppDataController`:** `LoadRemoteData` is `private` and gained a third required parameter (`bool localIsPlaceholder`), and the marking moved into a new private `MarkServedFromLocalFile`. Nothing `protected` or `public` changed, so an override of `OnGetAppData` or `ParseAppData` is unaffected - but a project that copied the old `LoadLocalData` body will still have the desktop-only marking and should take this version's instead.<br><br>Also in this version: the appData request is disposed (it leaked a native handle once per boot, twice on a fallback boot), and the Android/WebGL appSettings package read is bounded at 10s where it previously had no bound at all |
| v0.49.20 | The appData fetch is bounded by a new `AppData.timeout` key (M113) | **Config-format change. Nothing to do to keep booting; two things to do if your CMS is slow.**<br><br>The appData request previously had **no timeout at all**, so a half-open socket hung the boot until the stall watchdog latched. It is now bounded by `AppData.timeout`, seconds, under the **API policy**: `0` means *unset* and resolves to **30**, and the ceiling is **300**. Note that is the opposite of `0` under `AppData.Cache.*`, where it means *no limit* and resolves to 3600 - the boot log names the key and the applied value whenever the two differ, so a wrong guess is reported rather than silent.<br><br>**The behaviour change to be aware of:** a CMS that honestly answers slower than 30s now **fails** where it previously waited and eventually succeeded - and a failed fetch serves the local cache, or the shipped placeholder with asset cleanup disabled. If that is your situation, add `"timeout": <seconds>` to the `AppData` block of your `settings.json`. The key is absent from a config that predates this version, and an absent key keeps the C# default of 30.<br><br>**Clear Player Prefs before tuning it, or the edit appears to do nothing.** `Core.Awake` prefers `PlayerPrefs["Settings"]` over the file, and an old snapshot has no key for a field that did not exist when it was written - so the C# default wins on exactly the machines that have been used. *Gema -> Clear PlayerPrefs* in the editor, or the Reset button in the app's settings panel on a deployed machine. **On Android and iOS the snapshot is the only save target**, so there this is the normal state rather than an edge case.<br><br>Two smaller consequences. The next `FileManager.SaveCoreSettings` merges `"timeout": 30` into your `AppData` block, so a config diff after this version gains a key nobody typed. And the **boot stall budget on the shipped config moves 120s -> 125s**, because the appData fallback delay is now a term in it rather than an assumption that it fitted - the safe direction, since a budget shorter than the request it times would refuse to boot |
| v0.49.19 | The Android offline fallback stops overwriting the cache it just read (M86) | **Nothing to change unless you call `AppDataController.LoadRemoteData` - it is `private`, so almost certainly not.** Its signature gained a **required** `bool isLocalFallback`, and the appData cache write is skipped when it is `true`. Required rather than optional on purpose: `false` is the direction that writes, so a forgotten argument would silently restore the defect.<br><br>**What changes on Android:** the local fallback no longer rewrites `appData.json` on its way through. That rewrite was destroying the framework's ability to tell a seeded placeholder from a real CMS cache - either by moving the file's timestamp so the `length\|ticks` seed stamp stopped matching, or, when no cache existed yet, by creating the file the seed would have created so the stamp was never written at all. With detection restored, `failedRemoteJson` is set correctly again and the "shipped placeholder / cleanup disabled" error fires when it should.<br><br>**Why it matters more than it sounds:** `failedRemoteJson` is the flag `AssetManager` checks before the recursive delete. With `cleanUnlistedAssets` enabled, an undetected placeholder means an **empty keep-list treated as authoritative** - which deletes the operator's media. That flag ships `false`, so this was dormant here; it was live for any branch that turned it on.<br><br>**Still true after this change, and worth knowing if you deploy to Android:** the offline fallback on Android and WebGL does not mark the response as cached and does not report its age, so a weeks-old cache is served with nothing in the log to say so. That is tracked separately and is not fixed here |
| v0.49.18 | One boot order on every target; the settings parse has one home (M45, M111) | **Read this if your project calls `FileManager.CoreSettings()` or `FileManager.LoadJsonWeb()`, or overrides `Core`.**<br><br>**`CoreSettings()` now returns the settings text on Android and WebGL too.** It used to return `""` on those targets and deliver through the `OnLoadJson` event instead; `Core` subscribed to the event and threw the return value away. If you copied that pattern - subscribe, discard the return - you stop receiving the event from the **second** launch onward on Android, silently, because the seeded config is then a real file and is read directly. Use the return value; treat the event as the fetch-only path.<br><br>**The new contract is three states behind one return, and `null` is the one that matters:** `null` means a fetch was started and the text will arrive through `OnLoadJson`; a **non-empty** string is the text; an **empty** string means the read was attempted and produced nothing, so no event is coming and the caller must report it. Testing `IsNullOrEmpty` instead of `== null` is the specific mistake - it turns a missing or zero-byte `settings.json` into a blank preloader with an empty console.<br><br>**`Core.OnJsonLoaded` now ignores any filename other than `settings.json`** instead of completing the boot for it. The subscription is no longer platform-gated, so if you fetch other json through `LoadJsonWeb` and relied on the old behaviour to advance the boot, it no longer does - and it previously would have advanced it twice.<br><br>**If you override `Core`**, the three settings parses are now one private `ApplySettingsJson(json, source)` with one `ReportSettingsFailure(source, reason)`. Behaviour change worth knowing: an **empty** payload is now reported as "it is empty" rather than surfacing as a `NullReferenceException`, because `JsonUtility.FromJson<CoreSettings>("")` returns null without throwing. On the event path that NRE used to escape the coroutine and hang the boot |
| v0.49.17 | A dead fork of the `AssetGenerator` authoring tool is deleted from the framework (L22), and the surviving tool stops destroying its own data asset (H23) | **Read this if any scene of yours has a `Positioner` component, or your code names `Positioner`, `AssetsVO` or `Assets`.** Four framework files are gone: `EnigmaVirtual.Positioner`, `EnigmaVirtual.AssetsVO`, `EnigmaVirtual.Assets` and the `PositionerEditor` in `Assets/Editor/`. They duplicated - with one divergence - four files under `Assets/ThirdPart/AssetGenerator/` (`namespace UIGenerator`), **which still ship and still work**. If you used this tool, use the `UIGenerator` one: `Window ▸ UIGenerator`.<br><br>**Code references fail loudly** (`CS0246`). **A scene reference fails quietly** - `Positioner` was a `MonoBehaviour`, so a scene that bound it will show a **missing script** with no compile error. That costs you nothing functional: `Positioner` had no `Awake`/`Start`/`Update` at all and did nothing at runtime - it was driven entirely from its inspector - so a missing script there is a placeholder for a component that never ran. Remove it, or re-add `UIGenerator.Positioner`, which also needs its `designSize` set on the component rather than read from the VO.<br><br>**Why they went:** the whole cluster referenced only itself, no scene or prefab in this project bound any of it, and the `assets.json` table `PositionerEditor` reads has never existed here. If your project *does* have an `assets.json` workflow, the `UIGenerator` copy is the live implementation of it.<br><br>**Separately, and it matters if you have ever opened that window: `Window ▸ UIGenerator` was overwriting `Assets/Resources/UIGeneratorData.asset` with a blank instance every time it opened** (H23). `AssetList.Init` loaded it with a backslash `AssetDatabase` path, which resolves to null rather than throwing, so the create-if-missing branch ran on an asset that was already there. Fixed here. **If your project's `UIGeneratorData.asset` is unexpectedly empty, that is why** - restore it from source control; the tool will now load it instead of replacing it |
| v0.49.15 | The `Gema` namespace is retired; two dead types deleted (L17) | **Read this if your project references anything in `Gema`.** That namespace held five framework types and now holds **none** - measured 2026-08-19 across **every loaded assembly**, not just the framework: 0 types in `Gema` or any `Gema.*`. `Positioner`, `AssetsVO` and `SaveScreenshot` are now in **`EnigmaVirtual`**, so replace `using Gema;` with `using EnigmaVirtual;` (you very likely already have it). **Superseded for two of the three: `Positioner` and `AssetsVO` were deleted outright in v0.49.17 - see that row. `SaveScreenshot` is the one you still need the `using` for.** The compiler tells you: a missed one is `CS0246`, not silent - and `using Gema;` is now `CS0234`, which names the namespace itself.<br><br>**This row has been wrong twice, in opposite directions, so trust the measurement and not the claim.** From v0.49.15 it read "no longer exists", and that was false from v0.49.15 until v0.54.4 - a range, because a version count here has been miscounted before: `Assets/Resources/Gema/Core/ApplicationDescription.cs` still declared `namespace Gema`, in `Assembly-CSharp` rather than the framework, shadowed by the framework's own `ApplicationDescription` (M118). v0.54.3 corrected the row to say one resident remained. **v0.54.4 deleted that file, so the claim is true now - true because the last resident was removed, not because anyone had checked.** A namespace believed gone is a namespace nobody greps, which is why a duplicate carrying its own `[CreateAssetMenu]` entry sat there from the moment the namespace was declared dead until v0.54.4.<br><br>**One thing that survives and must not be tidied.** The *folder* `Assets/Resources/Gema/Prefabs/Ui/` has nothing to do with the namespace and is live: `Assets/Editor/MainControllerEditor.cs:126` loads `"Gema/Prefabs/Ui/VirtualKeyboard"` by **string path**, which no GUID protects and no compiler checks. Deleting that folder because "`Gema` is retired" breaks *Add Virtual Keyboard* with an `Instantiate` exception and nothing else.<br><br>**One type was deleted; a second was deleted in v0.49.15 and restored in v0.49.16.** `Assets` (a JSON wrapper around `AssetsVO[]`) is **still here**, now in `EnigmaVirtual` and with its original GUID - v0.49.15 removed it on a search that missed its one caller in `Assets/Editor/`. If you took v0.49.15, take v0.49.16: on its own, v0.49.15 leaves `Assembly-CSharp-Editor` unable to compile. The type that *is* gone is one of the **two** `DetectLongClick` classes: the 48-line `Core/Utils/` copy in `Gema`, which no scene referenced. The 110-line `EnigmaVirtual` one under `Core/Display/ui/utils/` is the live one and is untouched - if a scene of yours bound the other, it will show as a missing script.<br><br>`EnigmaVirtual` is the settled target for the global types that remain, so this is the only namespace you will be asked to add. **Measured 2026-08-19: 24 authored global types**, and none is movable - 21 are genuine extension holders where global is *correct* and 3 are `External/` forks left alone deliberately. The three that shared a file with an extension method (`Colors`, `RegisterField`, `ValidationType`) were extracted in v0.54.6, which closed L17. This row said "~24" until v0.54.4 and "27" until v0.54.6 - the figure moves, so re-measure rather than quoting it. **Note a runtime count gives 29, not 27**: `<PrivateImplementationDetails>` and `UnitySourceGeneratedAssemblyMonoScriptTypes_v1` are emitted by the compiler and by Unity's source generator, so an assembly-reflection census must exclude them or it reads as drift |
| v0.49.14 | The preloader no longer clears the screen when assets were given up on (M73) | **Nothing to change; two additive members and one visible behaviour change.**<br><br>`AssetManager` gained `MissingAssetCount` and `RefusedAssetCount` (read-only, accumulated over the whole run). `CoreSystemUI` reads them when the asset phase completes: with nothing wrong it hides the readout as before, and otherwise leaves it up saying `N ASSET(S) MISSING - SEE LOG`. It is cleared when the splash goes away, so it is visible during boot and not afterwards.<br><br>**If your project overrides `CoreSystemUI`**, note the two completion handlers now call `HideOrReportAssetProblems()` rather than hiding the label directly - if you have overridden either, you will keep the old always-hide behaviour and lose the report.<br><br>`onAssetCleaningCompleteEvent`'s signature is **unchanged**: adding the count to it was the alternative and would have broken every branched project's handler |
| v0.49.13 | A serial port that goes away now reports itself as not running (M101) | **Only relevant if you set `SerialPorts.isActive: true`** - it ships **false**, on COM7. (An earlier version of this row and H20 both said it ships `true` on COM3; that has been wrong since v0.15.0.)<br><br>`UnitySerialPort`'s read loop never cleared `isRunning` when reads started failing, so unplugging the adapter mid-session left the loop spinning while `IsRunning` stayed **true** - and nothing said it had stopped. (`SerialPortsManager` reads `IsRunning` once at boot, not periodically - so this was never a false *health check*, it was silence.) It now gives up after **5 consecutive non-timeout** read failures, clears `isRunning`, and says so at `LogError`. A read *timeout* is not counted: that is normal traffic, and counting it would stop a healthy port.<br><br>**What changes for you:** a port that dies now goes quiet deliberately instead of spinning, and anything you have that polls `IsRunning` will start seeing `false` where it used to see a stale `true`. If your project treated `IsRunning` as "was configured" rather than "is reading", that assumption is now wrong - it was always wrong, it just could not fail before. Re-open the port to resume.<br><br>Also: the failure notification was being raised from the **read thread** in `Threading` mode, which is a Unity UI call off the main thread; it is marshalled now. And `OnDestroy` unsubscribed its events while leaving the thread reading and the COM handle held, so the next open failed with access denied - it closes the port now. **The 5 is reasoned, not measured** - there is no serial hardware here; if you have an adapter, pull it mid-session and check the count that actually occurs |
| v0.49.12 | Killing processes no longer stops at the first failure, and killing the shell says so (L10, L18, L21) | **Read this if your `settings.json` sets `System.Windows.Processes.killProcesses` or `killExplorer`.**<br><br>**1. The kill list is now completed.** The try/catch sat outside both loops, so the first process that refused to die abandoned every remaining name you listed - silently. It is per-process now: a failure is reported, named, and the loop continues. If you have been assuming a name later in the list was being killed, it may not have been.<br><br>**2. `killExplorer: true` now logs a red line every boot**, before it kills the shell, and that is deliberate: it is the most destructive thing the framework does to a machine, `doLogs` ships `false`, and it previously left no record at all. The `taskkill` call is also wrapped now - a blocked or missing `taskkill.exe` used to throw straight out of the boot sequence.<br><br>**3. Process catches admit two more exception types**, so the `HasExited`-then-`Kill()` race no longer escapes the method and takes your boot step with it. Measured: `Kill()` on a process that has already exited throws `InvalidOperationException`, which the old filter did not catch.<br><br>**4. And the wait for a killed process is now bounded to 5s.** It was `WaitForExit()` with no timeout, and `Kill()` is asynchronous - so a process in an uninterruptible kernel wait blocked startup **permanently**, and because the boot watchdog is a coroutine a blocked main thread meant no `BOOT STALLED` either. If you have ever seen this app freeze at startup with nothing in the log while `killProcesses` was set, that was this.<br><br>Nothing to change on your side for either. Also: the video error placeholder is null-checked, so a renamed `Resources/Textures/videoDownloadError` logs instead of raising an NRE on the error path |
| v0.49.11 | Two diagnostics: a failed appSettings fetch says what happened, and every config write says who asked for it (M48, M56, M110) | **Nothing to change. One thing to know if you call the framework's config writer.**<br><br>`ConfigManager.WriteAtomic` now takes a third argument, `string context` - a required phrase naming what is being written and what losing it costs, which is prefixed to its error. It is `internal`, so `App/` code cannot have been calling it; only a branch that added framework-side code will see a compile error, and the fix is to pass a phrase. The point is that six callers shared one message: a discardable API cache and a failed `appSettings.json` write were indistinguishable in the log, both blamed on `ConfigManager`.<br><br>Also **Android and WebGL only**: `AppDataController.LoadRemoteSettings` reported a failed appSettings fetch by logging `www.downloadHandler.text`, which is empty on a transport error - so a dead CMS produced one blank error line. It now names the url, result, HTTP code and reason, and states that the app continues on **default** appSettings rather than retained ones. The same method was leaking its `UnityWebRequest` on every call and now disposes it. Nothing on Windows Standalone is affected - that path is compiled out |
| v0.49.10 | A timeout you configured but the app will not use is now reported at boot, and the boot-stall watchdog stops firing early (M106, M107, M108) | **Nothing to change, but two behaviours changed and one of them is visible on the shipped config.**<br><br>**1. A new `LogError` at boot.** `settings.json: <key> is <typed value>, but requests will use <n>s - <why>` appears once per offending key if a `timeout` is not used verbatim - subject to the module being on: `Api` needs `Api.isActive` (which ships **false**), and `AssetDownload` needs `AppData.isActive` and its own flag. `TextureCache` is reported regardless, because its loader runs whatever the flag says. It fires only when somebody edited a timeout: the shipped 60 / 60 / 30 are all inside their ceilings and produce silence. If your project sets `timeout: 0` or a value above the ceiling you will now get a red line every boot - that is the point, and the fix is to write the number of seconds you actually need.<br><br>**2. `BOOT STALLED` now waits longer, and previously waited too little.** The watchdog's stall budget was documented as scaling off the configured request timeout, but it was computed once, before `settings.json` is parsed, so it was **always** the 90-second floor. Any project whose asset requests can be silent for more than 90s could therefore get a false `BOOT STALLED` - which latches and refuses to boot. It is now recomputed as boot progresses, from the *effective* timeout: shipped 60 gives 120s, and `timeout: 0` gives 7200s. **If you have been seeing unexplained `BOOT STALLED` on a slow network, this is why.**<br><br>Only relevant if you touched the timeout policy: the ceilings moved out of three `private const MaxTimeoutSeconds` into `WebRequestExtensions` as `ApiCeilingSeconds`/`FileTransferCeilingSeconds`, behind `SetApiTimeout` and `SetFileTransferTimeout`. All `internal`, so no branch could have been calling them.<br><br>**3. The texture cache's own stall watchdog got the same treatment** (M108). It used a flat 60s while `TextureCache.timeout` may legally be an hour, so one slow image ended the pass early and completed the cache half-empty. Only reachable with `TextureCache.isActive: true`. Note one limitation left open as M109: the pre-cache marks two boot stages for the whole pass, so a few-hundred-image first run can still hold one stage long enough to trip `BOOT STALLED`<br><br>**Figures superseded by v0.49.20**: the stall budget gained a `LocalDataFallbackSeconds` term and now also derives from the new `AppData.timeout`, so the shipped value is **125s** rather than 120s and the floor is exceeded above 42.5s rather than 45s. The mechanism described here is unchanged. |
| v0.49.9 | **v0.49.8 does not compile - take this version.** And `timeout: 0` no longer means "wait forever" (M105, M43) | **Two things, and the first is not optional.** v0.49.8 shipped a CS1061 in `NotifyController.Show`, so the whole `EnigmaVirtual` assembly fails to build; if you pulled it, merge v0.49.9. Nothing to change on your side.<br><br>**Then check your `settings.json` for a `timeout` of 0, or a very large one**, under `AppData.Cache.AssetDownload` or `AppData.Cache.TextureCache`. Those were passed straight to `UnityWebRequest.timeout`, where 0 means *no limit*. On those two blocks a 0 now resolves to **3600s**, not to 30 - on them 0 has always meant "no limit", so it lands on the largest legal wait rather than the smallest - and anything above 3600 is capped to it. **Nothing at or under 3600 changes at all**, so a working install stays working. What changes is the two values that meant "never give up": 0 becomes 3600, and anything above 3600 comes down to it.<br><br>Still worth replacing a 0 with the real number of seconds you need, because 3600 is a ceiling, not a recommendation: a download cut short deletes its `.part` and the retry restarts at byte zero, so any value below the honest transfer time makes that asset permanently undownloadable, and the only trace is one "Giving up" line. `Api.timeout` is unchanged - 0 was already refused there, resolves to 30, and still caps at 300. As of v0.49.10 a clamp **is** reported at boot, so the log will tell you - see the row above |
| v0.49.8 | A notification arriving while the previous one is leaving no longer kills it (M96, M103) | **Read this if your project calls `NotifyController.Show`.** `Show` now cancels the outgoing tweens first, so a `Hide` in progress can no longer deactivate the notification that replaced it — that was leaving the panel parked off-screen **for the rest of the session**, silently. The entrance move now runs on every `Show` rather than only the first, which is what brings a mid-exit panel back on screen; it is idempotent for a panel already in place. The three-argument `Show` also clears any pending auto-hide, so a timed notification followed by a persistent one no longer has the persistent one hidden by the dead timer (M103). **Behaviour change to expect**: a replacing notification now stays in place and swaps its text and colour instead of re-running the slide-in. **Not fixed, and worth knowing before you touch this class**: `LeanTween.alpha(gameObject, …)` here is a no-op — it writes `Renderer.materials` and a UI `Image` has a `CanvasRenderer` — so this panel has never faded and still does not (M104). |
| v0.49.7 | The download readout wires itself when the AssetManager appears (M102) | **Additive; nothing to do.** `MainController` gains a read-only `AssetManager` property, mirroring `TextureCacheManager` — **null for most of the boot**, because `SetupAssetManager` runs from the appData-loaded handler, so read it after `AppDataController.OnDataLoadedEvent` rather than at `Init` time. `CoreSystemUI.Init` no longer gives up when no AssetManager exists yet: it resolves from `Core.main.AssetManager` first — which covers the `useLocalAppData` path, where the appData load and its event complete **synchronously inside `MainController.Init`**, before the preloader initialises — and only waits for the event when the manager is genuinely still to come. A scene without an AssetManager now gets its download and verification progress instead of nothing, on both paths. If your project relied on the old `"no AssetManager found…"` error appearing at boot, it now appears only if the manager is *still* absent after the appData load — and not at all when the operator skipped validation, since no manager is created in that case. `CoreSystemUI` also gains an `OnDisable`; if you override it in a subclass, call `base.OnDisable()` or the event subscription leaks. |
| v0.49.6 | The preloader says so when it cannot draw, instead of failing silently (M77, M78) | **Nothing to do if your scene binds the preloader; one new error if it does not.** `CoreSystemUI.Init` now logs at **Error** when `assetManagerOutputText` is null and the asset phase will run — eight handlers previously returned silently, so a scene that dropped the Text lost the whole readout with no trace. **If your project deliberately ships no preloader Text, expect that error on every boot**: it is `LogError` on purpose, because `LogWarning` is `[Conditional("DO_LOGS")]` and would be invisible on iOS and WebGL (`DO_LOGS` covers `Android` and `Standalone`, and Standalone includes macOS and Linux), where the screen is the only instrument. `Init` also re-resolves `textureCacheManager` from `Core.main.TextureCacheManager`, so a scene with **no** TextureCacheManager now gets the texture readout instead of a misleading "not available". The two existing `"… not available"` messages were reworded to name cause and remedy — grep for them if you match on log text. **Not fixed: the same defect for `assetManager`** (M102) — `SetupAssetManager` runs from the appData-loaded handler, after `Init`, so a scene without one still loses the download readout, and now says so once. |
| v0.49.5 | The analytics types lose their spelling typo (L6, reversing M8) | **Source-breaking for any project that referenced them — grep before you merge.** `AnaliticsSender` → **`AnalyticsSender`**, its public static `SendAnalitics` → **`SendAnalytics`**, and the `EnigmaVirtual.OneCoreClient` VO `Analitics` → **`AnalyticsPayload`**. Nothing forwards, so a reference to any of the three is a `CS0246`/`CS0117` at compile time — loud, not silent. `OneCore.SendAnalitics` was renamed too but is `internal`, so it cannot break an `App/` caller. **Scene and prefab references survive**: both files moved with their `.meta`, so the script GUIDs are unchanged. The VO is `AnalyticsPayload` rather than the obvious `Analytics` because `UnityEngine.Analytics.Analytics` is a real type that `AnalyticsSender` itself calls, and a same-named type would be a `CS0104` in *your* files. Behaviour is unchanged: `SendAnalytics` still sends nothing and still says so at `LogError` (M8). |
| v0.49.4 | A video url with no file extension reaches its error handler instead of throwing (L19); `MediaFile`'s extension sets are shared (L8) | **Read this if your project plays stream urls.** `VideoPlayerControllerBase` validated with `isValidVideoFileType(url.GetFileType())`, applying the extraction twice; an extension-less url extracted to `""` and the second application threw `ArgumentException`, so `ValidateSourceAndRender` never reached its own `DisplayError()`/`onErrorEvent`. It passes `url` now, so that shape reports instead of throwing — **a behaviour change on a path that was previously an exception**, and if you catch around `Prepare`/`Play` you will see the error surface differently. The accepted extension sets are unchanged in membership (`.png .jpg .jpeg` / `.mp3 .wav .ogg` / `.ogg .mp4 .webm`), now `static readonly` rather than rebuilt per call. **`.webp` is still excluded on purpose** — Unity cannot decode it (`DownloadHandlerTexture` returns null with `DataProcessingError`), so adding it needs a decoder package, not a list entry. |
| v0.49.3 | `AssetManager.maxRetryCount` becomes a serialized, `[Range(0, 10)]` field; repeated literals named (L7) | **Nothing breaks at compile time, and that is the point to watch.** The default is unchanged at 3, and an already-authored `AssetManager` keeps it: the component carries no serialized field entries in `CoreSetup.unity`, and Unity applies scene YAML over a normally-constructed object, so an absent key never overwrites the C# initializer. The change lands **the first time your project opens and saves that scene** - `3` bakes into your scene file, and a later framework change to the default silently skips you. Same shape as `Timer.interval` in v0.27.0: it can break through scene data rather than at compile time. If you want the framework default to keep reaching you, don't author the value. The `[Range]` caps a dead asset at 11 attempts x `AssetDownload.timeout` (~11 min as shipped); it does **not** prevent a boot stall, because retries move the progress stage and reset the watchdog (M70). Also additive: `EnigmaVirtual.AppResources` holds `Resources/` paths used by more than one caller. |
| v0.49.0 | `Observable<T>` tightened: `event`, no-change is silent, subscribers isolated (L4) | **Three breaks if you use it.** `OnChanged` is an `event`, so `= handler` and `= null` no longer compile - use `+=`/`-=`. Setting the **same** value no longer notifies, and that one is **silent**: `obs.Value = obs.Value` to re-push, or mutating a reference in place then re-`Set`ting it, are now no-ops. And a subscriber that throws is logged and skipped rather than propagating to the assigning code. Subclasses notify by calling `Set` - an `event` cannot be raised from a derived class. |
| v0.48.1 | Asset progress counts only the assets that can actually be verified (M37) | **Read this if your CMS serves absolute URLs.** The progress total is now the count of *local* assets, excluding empty and external URLs, so a successful run reaches its total instead of stopping at e.g. 48/50. A content set that is entirely external URLs now shows `NO FILES TO PREPARE` and logs an error naming the count - those assets are still never downloaded, which was always true and is now said out loud. |
| v0.48.0 | `SetupXxx` initialises an adopted component instead of returning (H2); every nested settings block is initialised (M99) | **Two things to check in a branched project.** If you author `OSCController`, `ApiController` or the debug `Console` into your scene, they were previously adopted and then left uninitialised - OSC in particular never got a client. They are configured now, so behaviour changes for the better but it *does* change. And if you trimmed blocks out of `settings.json`, a depth-2 block such as `DebugSettings.Console` used to come back null and NRE the boot; all four such fields now default. |
| v0.47.2 | `DetectLongClick` checks `Core.IsSet` before touching `Core.settings`, and always disarms on release (M59, M98) | **Nothing to do, but two behaviours changed.** A long-press before boot completes is now refused with a log line instead of throwing a swallowed `NullReferenceException`; and releasing the press always clears it, where before a release was ignored while `EditorSettings.isActive` was false - which could leave the gesture armed and fire `onLongClickEvent` every frame. |
| v0.47.1 | `MiscExtensions.Split` walks the source once instead of `Skip`-ing per chunk (M13) | **Nothing to do; same chunks, same order.** The chunks are materialised `List<T>` now rather than lazy views, so a caller that mutates the source mid-enumeration no longer sees the change reflected in chunks already yielded. Not a speed-up - it costs ~1.1ms once per boot at 20k assets and exists so the method is O(n) for any `ICollection<T>` rather than only for ones with an indexer. |
| v0.47.0 | `RFIDReader` reads `Event.character` instead of a US-layout `KeyCode` table (M15) | **Behaviour change if you use this component.** Codes containing symbols were mis-transcribed on pt-PT/fr/de and characters with no entry in the old table - `ç`, `ã`, the whole numeric keypad, `KeypadEnter` - were dropped outright; they work now. Two new serialized fields, `codeTimeoutSeconds` and `maxCodeLength` (default 256): **raise `maxCodeLength` if your symbology is longer**, because an over-length read is now discarded rather than delivered truncated. A Tab-suffixed scanner still never terminates a code, but now says so. |
| v0.46.0 | `Invoke("MethodName", delay)` is replaced by `MonoBehaviour.CallLater(delay, Action)` (M10) | **Additive, but read the note on `CancelInvoke`.** The six framework call sites moved; nothing you call changed signature. Two things to know if you migrate your own `Invoke` calls: `CancelInvoke`/`IsInvoking` do **not** see a `CallLater` timer, so a project cancelling that way will compile clean and cancel nothing - stop the returned handle instead; and use the `ref Coroutine` overload wherever a second call can arrive before the first fires, because plain `Invoke` stacked and that was a live bug in two of our own sites. |
| v0.45.0 | `Development.Assert` is renamed to `LogIf` (M9) | **Rename, with a forwarder - nothing breaks today.** `Assert` fired when its condition was **true**, at `Log` level, so it was never an assert; all 70 call sites here pass `doLogs` and mean "log if logging is on". `Assert` still compiles as `[Obsolete]`, so your project gets a warning per call site and can migrate at its own pace - the fix is a rename, nothing else. Both names stay `[Conditional("DO_LOGS")]`. |
| v0.44.1 | A cancelled image load stops stranding its caller (M94) | Destroying **or deactivating** a `DownloadableImage` mid-load now raises `onLoadFailedEvent` and logs, where both were silent. If your project pools or hides gallery items, this is the bigger half: a `SetActive(false)` used to leave the component permanently unable to load again. `onLoadComplete` listeners are **dropped, not fired** on a cancellation - the log says so. A normal quit stays quiet. |
| v0.44.0 | `DownloadableImage.Load` returns `bool`, and a refused load stops reporting the wrong load's failure (M93) | **Signature change, source-compatible.** `Load(string)` and `Load(string, string)` return `bool` instead of `void` - `false` means the call was refused and no event will ever be raised for it. Statement calls are unaffected; only a method-group conversion to `Action<string>` would break, and there are none here. **If your code registers handlers before calling `Load`**, register them only when it returns `true`, or they will sit on the events belonging to the load already in flight and fire for *that* one - which is what `MediaVOExtensions.Load` now does. Also corrects v0.43.0's release note: the `Image` and `RawImage` paths never diverged, so the migration advice is the same for both. |
| v0.43.0 | A failed image load stops announcing that it completed (M88, M89) | **Behaviour change every branched project sees.** `DownloadableImage.onLoadCompleteEvent` no longer fires when a load fails - previously the error placeholder was shown *through the success callback*, so a failure raised completion, before the failure event on a `RawImage` and instead of it on an `Image`. If your project hides a spinner or counts loaded items on `onLoadComplete`, **subscribe to `onLoadFailedEvent` as well** or that work stops happening on failures. Both event lists are now cleared when a load fails, so a listener added for one load no longer fires for the next one on the same instance. The invalid-filetype case, which previously raised nothing at all, now reports like every other failure and reaches the `Load(url, fallbackUrl)` fallback. |
| v0.42.2 | *Gema → App Descriptor* stops rewriting the application identifier of whichever platform the editor is on (M91) | **Check your `ProjectSettings.asset` before your next commit.** If you opened that window on any target since branching, its application identifier was silently set to `com.gemadigital.<Slug>` - a different id from an already-deployed build means a second install rather than an upgrade. The identifier now follows the window's own **Group** field, is written only when it differs, and logs a warning naming the target and both ids when it does. |
| v0.42.1 | Log lines stop carrying the build machine's absolute path on non-Windows targets (M90) | Nothing to do; the prefix is what it always should have been. If you shipped anything built from v0.41.0 or v0.42.0 to a non-Windows target, its logs contain your build path. |
| v0.42.0 | One helper decides when a path needs a `file://` scheme, instead of five sites deciding differently (M47) | **New public API: `AppPaths.ToFileUrl(string)`.** Use it anywhere a local path is handed to `UnityWebRequest`; it returns URLs and empty values untouched and percent-encodes the rest. **Android media loading is fixed, and verified on a device** - a CMS image with spaces and a comma in its path loaded through `DownloadableImage` from `persistentDataPath` on 2026-08-14. `ImageSequencer` and `MediaFile.LoadAudio` remain unexercised - `DownloadableImage`, both `TextureCacheManager` request sites and `MediaFile.LoadAudio` sent bare paths, which fail there, and `ImageSequencer` sent `file://jar:file://…`. On Windows the visible change is in the *failure* log only - the "Downloading image:" line is emitted before the transform and still shows a bare path, while the error line now carries a percent-encoded `file:///…` URL. If your project builds its own `UnityWebRequest` for a local asset, route it through the helper - and note that a bare `"file://" + path` is **not** equivalent: it 404s on any filename containing `#`. |
| v0.41.0 | `Development` takes its file and line from caller attributes instead of walking the stack (M25, M87) | **Source-breaking for a branched project, and loudly - grep before you merge.** `Development.Log("tag", someInt)` is now `CS0121`; the collision set is `int`, `char`, `short`, `byte`, `sbyte`, `ushort`, and the fix per site is `.ToString()`. `Action<object> f = Development.LogError;` is now `CS0123` - wrap it in a lambda. `Assert(bool, object, LogType)` and `CheckPoint(LogType)` no longer default the `LogType`, which breaks nothing that compiled before. **One silent change**: `Log(message, someInt)` used to be a compile error and now binds the int to the line number, dropping it from the message. The class prefix comes from the file name now, so the six response-parsing errors in `Api.cs` read `Api ::` rather than `APIResponseModel ::`. Logs are 2.8x cheaper and, under IL2CPP, carry a real file and line where they used to print `Unknown`. `Assets/Editor/DevelopmentOverloadGuard.cs` compiles the canonical shapes so a future reorder fails here rather than in your tree. |
| v0.40.0 | A seeded `appData.json` is no longer mistaken for a CMS cache (M85) | **A new file appears in the deployed config folder**: `appData.json.seeded`, written once beside a freshly seeded `appData.json` and holding `length\|ticks`. Nothing reads it but `AppDataController`, and deleting it costs only the diagnosis - the shipped-copy comparison still runs. On a machine that has never reached the CMS, the fallback now says so at `LogError` and leaves `failedRemoteJson` set, which **keeps the C2 asset cleanup switched off** where it previously ran against an empty keep-list - on projects with `cleanUnlistedAssets` on, which the shipped config is not. `useLocalAppData` projects are exempt by design - there the shipped file is the content. Nothing to override: `LoadLocalData` is `private` and is re-entered only through `Invoke("LoadLocalData", 5f)`, so every project gets this. |
| v0.39.0 | A remote appData failure now always falls back to the local copy, instead of one branch hanging (M66) | **Behaviour change on the boot path, and a breaking-change candidate for anything overriding `OnGetAppData`.** A server answering with a non-Success `code`, and a payload that arrives whole but produces no model (no `data`/`modules` member, or a `ToObject` that threw), used to report and stop - the app sat on the preloader until `BOOT STALLED`. All three branches now call `FailRemoteAppData`, which schedules the local-appData fallback after 5s, matching the six sibling failures that already did - four since v0.31.0, two since v0.38.2. If your project overrides `OnGetAppData` or `ParseAppData` and calls `base`, it inherits that fallback. The `AssetDownload.isActive` gate that used to suppress the fallback entirely is gone, so **projects with downloads off get a fallback where they previously got a hang**. New ungated `LogError`s name the server's `code` and `msg`. Note M85: on a deployed machine the fallback may land on the *seeded* placeholder and report it as a fresh cache - **fixed in v0.40.0**, the row above. |
| v0.38.2 | A parse that produces no content model no longer announces success, and recovers instead of hanging (M82) | **OneCore projects: this is the one to read.** A payload whose `modules[idContent].content` is missing used to announce success with a null model, NRE inside `MainController`, and reach the local-appData fallback only because that NRE was caught - a degraded but working boot. It now reports and calls the fallback directly. If your project subclasses `ParseOneCoreAppData` and assigns `AppData` *after* calling `base`, you will now get a refusal where you previously booted: assign before returning, or override the whole method. `idContent` is a **position** in the `modules` array, not an id - worth checking if you see the new error. Non-OneCore projects are unaffected; the same check exists in `ParseAppData` but is defensive there. |
| v0.38.1 | One shared `internal StringUtil.FormatElapsed`; the two "not from the server" signals stay separate, by decision (M83) | **Nothing to do** - no public API moved and no behaviour changed. Two private duplicates became one `internal` helper inside `EnigmaVirtual`, invisible to `App/` either way. Worth knowing if you were about to unify `APIResponseModel.fromCache` with `AppDataController.failedRemoteJson`: **do not**. The second gates the C2 recursive delete, and they diverge in both directions - see M83. Known gap: M42's cache marking does not reach Android or WebGL, where `LoadLocalData` routes through `LoadRemoteData`. |
| v0.38.0 | A response served from a cache is marked and dated, and says so in the log (M42) | **Expect a new red line on any boot that falls back to the local appData** - `appData is being served from the local file [...], last written 3 day(s) ago`. It is not a new failure; that boot already fell back, silently. `APIResponseModel` gains two `[NonSerialized]` public fields, `fromCache` and `TimeSpan? cacheAge`: **additive and non-breaking**, and a project can now show a staleness banner with `if (response.fromCache)` in one line of `App/`. `ApiResponseResult` deliberately still returns `Success` for a cache hit - a new enum member would break every branch's `switch`, and the caller genuinely does have content to render. Note `fromCache` means "read from a file rather than received from the server"; the narrower "the keep-list cannot be trusted" signal is still `AppDataController.failedRemoteJson` (M83). |
| v0.37.0 | The asset download refuses to write outside the configured asset root (H22) | **Check your appData for absolute media paths before deploying this.** Any value that resolves outside `AppData.*.assetsPath` - a rooted `C:\…` or `//host/share`, or a relative one with `../` - is now refused per asset with `Asset download REFUSED for [...]`, and is **not** downloaded. Reads are unchanged: the five read callers of `GetAssetPath` still resolve a rooted value verbatim, so media that genuinely lives on another drive still displays; only the download is contained. The config folder under persistentDataPath is refused **regardless** of what `assetsPath` resolves to, which matters if your project leaves both `assetsPath` values empty - that collapses the root onto the config folder's parent. Refusals are counted separately in the end-of-chunk summary. Also: `IsUnder` now compares case-sensitively off Windows, which tightens the asset **cleanup** on Android and Linux as well - the two equality checks beside it stay case-insensitive, so they over-refuse there rather than under-refuse. |
| v0.36.1 | Every exit from the asset and texture walks now releases the boot chain, and logs the exception that caused it (M80, M81) | **Two new always-compiled `LogError`s on boot failure paths, plus a `LogException` before each.** If your project greps the console, the strings `The walk over the content model threw` and `Sorting the walked assets, or a handler it notified, threw` are new. Behaviour: a walk that throws no longer hangs the boot - it reports and continues with whatever media is already on disk, **without** running the asset cleanup, because a walk that did not finish leaves no keep-list to trust. No API change; the new `RaiseCleaningComplete`/`RaiseVerificationComplete` helpers are private. The null-`AppData` branch this adds became reachable in v0.38.2, when M82 gave `MainController` a guard that logs and continues instead of dereferencing. |
| v0.36.0 | `TextureCacheManager.GetOrSetTexture2D` now invokes its callback **exactly once**; a cache clear completes what it cancels, with `null` (M79) | **A behaviour change on a live path, with no signature change to flag it.** Before this, a `ClearCache()` during an in-flight download killed the coroutine and the callback never arrived - so a consumer that treated "callback arrived" as "texture arrived" simply never ran. It now runs, with `null`. Check any project callback for a null-texture branch: the framework's own `TextureCacheLoader` has one and blanks the image, but a project handler that dereferences the texture will now throw where it used to be silent. **Cancelled requests always get `null`, never the error placeholder**, even with `showErrorImage: true` - a cancellation must not be indistinguishable from a loaded broken-image icon. Also: an empty url and an inactive manager now report at `LogError` instead of saying nothing on the shipped config. |
| v0.35.1 | The texture pre-cache reports why it cached nothing, and a null appData no longer hangs the boot (M75) | **One behaviour change on a dormant path, worth knowing before you enable it.** `TextureCacheManager.ValidateAssets_IE` used to log `Presented Data null or empty!` and then fall out of the coroutine without raising `onAssetVerificationCompleteEvent` - the event that calls `LoadNextScene`. A null appData therefore hung on the preloader until the C3 watchdog latched `_bootFailed`. It now raises the event and the boot continues without a texture cache. If your project relied on that stall as a stop-the-line signal, the `LogError` is still there and still says why. Also: the empty-list case now logs, and it distinguishes text-only, model-blind, video-only and unrecognised-format - so expect a new **Warning** on a video-only content set, which is normal and not a fault. |
| v0.35.0 | `Assets/04_Prefabs/Core.prefab`, `Assets/04_Prefabs/Core/SystemUI.prefab` and `Assets/Resources/Gema/Core/SystemUI.prefab` **deleted**; the preloader now shows the asset total before the first file, and says so when a content set has no media (M74, M71, M72) | **Check whether your project's scenes reference any of the three prefabs.** Nothing in the base project did - verified by GUID across every file under `Assets`, `ProjectSettings` and `Packages` - and `CoreChecker` loads the `CoreSetup` **scene**, not a prefab. But a branched project that dragged one into a scene will get a missing reference. It was already getting a broken `Core`: all three were pre-2018 prefab format and still serialized `HiddenEvent` and a `LongTouxhZone` typo, while `Core.prefab` bound none of `loginPanel`, `assetManager`, `textureCacheManager` or `assetManagerOutputText`. Rebuild from `CoreSetup.unity` instead, which is what actually boots. `git revert` restores them if a branch genuinely needs one. |
| v0.34.2 | The preloader reads `DOWNLOADING <file>` / `RETRYING (n)` during downloads instead of holding the last `VERIFYING` line; the per-attempt download error names the attempt number (M36) | **No new log lines and no API change** - `Download failed [x.jpg], attempt 1 of 4: …` replaces `x.jpg Error: …` on a line that already fired unconditionally, and the `doLogs`-gated `Retrying n/3` line beside it is gone. A project parsing the console for the old string needs the new one. Two things to know: a branched project that authored its own preloader off a **prefab** rather than `CoreSetup.unity` sees none of this, because no prefab binds `assetManagerOutputText` (M74); and **do not "tidy" the two asset events into one count** (M70), which reads as an obvious cleanup and causes a false `BOOT STALLED`. |
| v0.18.3 | Unity Ads and the mediation SDK removed from `Assets/Plugins/Android`, along with their EDM4U declaration and install record (M58) | Nothing to do unless a branched project actually shows ads - none do. If one needs them, **`git revert` this commit** - that restores the declaration and a Force Resolve re-downloads the tree. Do not assume Package Manager can put it back: `com.unity3d.mediation` was Unity Mediation, since sunset in favour of LevelPlay, so the registry may no longer serve it. The same declaration also carried three iOS pods, moot until iOS is built. Note the `androidx.startup.InitializationProvider` element went from `AndroidManifest.xml` with it, since the aar supplying that class is gone. **Android-only** - a Windows-targeting project sees no difference. |
| v0.27.0 | `Timer`: `interval` becomes a serialized field, the `IsRunning` setter is removed, `Update()` becomes `protected`, `SetDuration` is `[Obsolete]`, `GetProgress()` becomes `public` (M16) | **Three of these can break through scene data rather than at compile time**, because `set_interval`, `set_IsRunning` and `Update` were all legal `UnityEvent` persistent-call targets and a broken binding is a silent no-op. Find them: `Get-ChildItem Assets -Recurse -Include *.unity,*.prefab \| Select-String -Pattern 636141d60ee340d47bdd2a6ace68b423` (PowerShell; `grep -rn "636141d60ee340d47bdd2a6ace68b423" Assets/` on a POSIX shell) for objects carrying a `Timer`, then look for `m_MethodName: set_interval`, `set_IsRunning` or `Update` in those files. Both of this repo's own `Timer`s are clean. **One behaviour change has no compile error and no log**: `interval` defaulted to 0, so a bare `StartTimer()` used to complete on the first frame - it now waits 1s, or the value you author in the Inspector. And a non-positive interval is now honoured as "no timeout" - the timer warns and does not start, rather than completing immediately. |
| v0.34.0 | `Core.LanguageChangedEvent` is an `event`, not a field; a duplicate `Core` no longer clears the real one's `IsSet` (M7, M68) | **`CS0070` if your project assigns the event.** `+=`/`-=` are unaffected; `= null`, `= handler`, or calling `.SafeInvoke()` on it from outside `Core` will not compile, which is the point - as a field, one assignment anywhere silenced every localized label in the app. Also **check your `OnDestroy` methods**: if a subscriber detaches behind `if (!Core.IsSet) return;`, remove the guard. `-=` needs nothing from Core, and the event is static, so a skipped detach outlives the scene. |
| v0.33.0 | `StringUtil`: path helpers handle both separators, `ToClock` and `CalculateMD5Hash` are `[Obsolete]`, MD5 switches ASCII to UTF8 (M5) | **`CalculateMD5Hash` returns a different hash for non-ASCII input.** It was `Encoding.ASCII`, which collapses every byte above 0x7F to `?`, so accented strings collided; a project comparing against stored hashes of accented text will see them all miss once. They were wrong before, not now. `RemoveFileNameFromPath` and `GetFileNameFromUrl` now split on `\` as well as `/`, which is a **fix** on Windows - the old versions returned empty for a backslash-terminated path, so the download folder was never created - but if a project depended on the old empty return, that is where to look. Both `[Obsolete]`s are warnings, not errors. |
| v0.32.0 | Cached textures are requested `nonReadable: true`; `TextureExtensions.ReadableClone` rewritten; three unreferenced `public static` methods removed from `StringUtil` (M63, M67, L1) | **Reading pixels off a cached texture now throws.** `GetPixels32`, `GetRawTextureData` and `Apply` raise `UnityException` on any texture from `TextureCacheManager` - which is louder than before, when it worked until a download failed and the non-readable Resources placeholder was served. If a project needs those pixels, use `TextureExtensions.ReadableClone` and `Destroy` the result; do **not** revert the flag, which pays doubled memory on every image to read one. `ReadableClone` itself never worked before this version, so a project relying on it was already getting noise. `StringUtil.GetIP_Identifier`, `.Serialize` and `.IsAnyFieldEmpty` are gone - `CS0117` if you call them, and each was four lines to reinstate locally. The commented-out alternate BestHTTP `Api` at the foot of `Api.cs` is deleted too (142 lines); it was commented, so nothing compiled against it, and `HTTPMethods` is a locally-declared enum that stays. |
| v0.31.0 | `OnDataLoadFailedEvent` no longer fires after `OnDataLoadedEvent` has fired for the same load, and every raise of it goes through one private method (M65) | **A behaviour change to a `public event` whose signature did not move**, so nothing will flag it at compile time. If your project subscribes to `OnDataLoadFailedEvent`, it stops being called for a failure that happens *after* the appdata parse already announced success - which is the point: one boot cannot both succeed and fail. Nothing in the base project subscribes (M48), so this only affects branches that added a handler. Also: a response whose `code` is not `Success` is no longer written into the offline appdata cache, so it can no longer be served back as content on a later boot. |
| v0.30.0 | `cleanUnlistedAssets` set back to `false` in the shipped `settings.json`; `APIResponseModel.GetValueAsString` reimplemented on Newtonsoft (H21, M11) | **Check your own `settings.json` for `cleanUnlistedAssets`.** Any project branched between v0.15.0 and v0.29.2 inherited it as `true`, which makes the app recursively delete every file under the asset root that the current `appData` does not reference. If your installation wants that, set it in the **deployed** config under persistentDataPath, not in the one that ships inside the player. `GetValueAsString` now returns `null` with a log instead of throwing `ArgumentOutOfRangeException` when the body has no `values` member, and its output is `Formatting.None` rather than the server's original whitespace. |
| v0.29.0 | A missing translation now renders the **key** instead of leaving the previous language's text on screen; `TryGetLocalized` treats a `null` value as absent (M6, M64) | **Two behaviour changes, both visible.** A key with no value for the current language used to leave whatever was on screen - `Translations.GetTranslation` returned `null` against its own documented contract, and `MultiLangText` then skipped the assignment entirely. It returns the key now, and the assignment happens either way, so a stale label becomes a visible one. **An empty string is still a value**: `"en": ""` renders blank, not the key, so a label deliberately blanked for one language keeps working. Only `null` falls through. If a project relied on the old staleness to hide untranslated content, it will see keys - the fix is to supply `""` rather than omitting the language. |
| v0.28.0 | `TranslatableField<T>` renamed to `LocalizedField<T>`, with its members: `HasTranslation`→`HasLocalized`, `HasAnyTranslation`→`HasAnyLocalized`, `TryGetTranslation`→`TryGetLocalized`, `TryGetValue`→`TryGet`. The `[Obsolete] GetTranslation` overloads are **deleted** | **A find-and-replace on the project's own C#, and nothing else.** No content changes: this type derives from `Dictionary`, so Unity never serialized it into a scene or prefab, and Newtonsoft matches `appData.json` by *field* name rather than type name — verified, including that nothing anywhere sets `TypeNameHandling`, so no `$type` was ever written. Every consequence is a compile error. In practice the only symbol a branched project can even reach is the **type name**, since `GetLocalized` aside, every member is `internal` and has been unreachable from `App/` since v0.24.0 — unless the project added its own files under `EnigmaVirtual/`, which share the assembly and see the rest. Run: `Get-ChildItem Assets -Recurse -Include *.cs \| Select-String -Pattern "TranslatableField\|TryGetTranslation\|HasAnyTranslation"` (PowerShell; `grep -rn "TranslatableField\|TryGetTranslation\|HasAnyTranslation" Assets/ --include=*.cs` on a POSIX shell) |
| v0.26.0 | `SpriteCreateLayerJob` and `ImageLoaderThread.ExecuteSpriteCreateJob` **deleted**; `DownloadableImage` now destroys the texture and sprite it created for the previous load (M1) | Both deleted members were `public`, so a project referencing either gets `CS0246`/`CS1061`. Neither could ever have run - an `IJobParallelFor` struct cannot hold managed `UnityEngine.Object` references and `Sprite.Create` is main-thread-only - so there is nothing to replace them with; delete the call. **The lifetime change needs a look**: a texture or sprite that `DownloadableImage` downloaded is now destroyed when the next load replaces it and on `OnDestroy`, so a project that kept a reference to `_rawImage.texture` or `_image.sprite` and used it elsewhere will find it destroyed. The public `SetImageSprite` is unaffected - it deliberately does not take ownership, so a `UnityEvent`-bound sprite asset is safe. |
| v0.25.0 | `TextureCacheLoader` and `DownloadableImage` stop rewriting the serialized `fitType`, stop mutating the shared cached texture, and resolve `Both`/`Fill` from the container's `rect.size` rather than `sizeDelta` (M2) | **Where a `DownloadableImage` or `TextureCacheLoader` loads more than one image into the same instance, or sits on a stretch-anchored rect, images will lay out differently - and that is the fix.** Nothing changes for a one-image-per-instance, fixed-size cell, which is what this repo's own `ImageGalleryItem.prefab` is. `Both` used to survive one image - the first load rewrote the field, so every later image reused the first one's orientation - and on a stretch-anchored rect the comparison read `(0,0)` and always chose `Height`. Re-check any layout that was tuned around the old behaviour; `Fill` now resizes instead of doing nothing. `generateMipMaps` is gone from `TextureCacheLoader` (it was a no-op) and `filterMode` is deprecated there - it applies nothing and logs an error at startup if set to anything but `Bilinear`, so a project relying on `Point` hears about it rather than going quietly blurry. A failed load now clears the image instead of leaving the previous one on screen. |
| v0.24.1 | `string.GetTranslation()` **deleted** from `StringExtensions` (M62) | **A project calling it will not compile** - `CS1061`. Delete the `.GetTranslation()` suffix; it was an identity function (`=> message`), so every call is behaviour-identical without it. There is no silent-break path: an extension method cannot be bound in a `UnityEvent` persistent call, serialized, or reached by reflection over instance methods, so the compiler finds every site. Unrelated to `LocalizedField`'s own `GetTranslation`, which was a different method and was deleted separately in v0.28.0. |
| v0.24.0 | Assembly definitions added: `EnigmaVirtual` (framework + `External/`), `App`, `LeanTween` (M20) | Nothing to do unless the project has its own scripts outside `Assets/01_Scripts/App/` — those stay in `Assembly-CSharp`, which references the others automatically, so they keep compiling. What *does* break is a project calling an `internal` framework member from its own code: that is now `CS0122`, and the fix is to make the member `public` in the framework. Two already were — `TranslatableField.GetLocalized` and `AppDataSettings.GetAssetPath`. Also `ApplicationDescription.cs` moved from `Assets/Resources/` into the framework; the GUID is unchanged so `Descriptor.asset` still resolves, and only the *asset* ever needed to be in `Resources/`. |
| v0.23.0 | `AppDataController` deserializes into `AppDataBase`/`AppSettingsBase`, told what to build by `protected virtual Type AppDataType`/`AppSettingsType` (M20) | **Do this or the project silently has no content.** Keep `App/Data/ProjectAppDataController.cs` (it names your `AppData` and `AppSettings` and needs no edit) and **author it on a GameObject under `Core`** in your scene. `MainController.SetupAppData` discovers it; unauthored, the framework deserializes into the base types, your model's members are never populated, and nothing fails to compile because `MediaWalker` is reflective and every consumer takes `object`. With `cleanUnlistedAssets` enabled the empty keep-list clears the media cache too. Also fixed here: `SetupAppData` used to `return` right after discovering a controller, before subscribing `OnDataLoadedEvent` and calling `Init()`, so a scene-authored one was found and abandoned and the boot chain stalled. |
| v0.22.0 | The four `MediaVO` overloads removed from `VideoPlayerControllerBase`, `DownloadableImage` and `TextureCacheLoader` (M20) | They are extension methods in `App/Extensions/MediaVOExtensions.cs` now, in the global namespace, so ordinary calls like `image.Load(media, true)` keep compiling untouched. Two cases need action. **A project that `override`s `Play(MediaVO)`** gets `CS0115` - override `Play(string)` instead, which is the right seam. **A project that wired `Play(MediaVO)` to a `UnityEvent` in the Inspector** would break *silently*, because a persistent call resolves by reflection over instance methods and `UnityEvent` skips what it cannot resolve without logging - so App's `VideoPlayerController` now carries instance `Play(MediaVO)`/`Prepare(MediaVO)` shims, and a project that merges keeps its binding. New framework `Load(string url, string fallbackUrl)` on the two loaders keeps the thumb→url fallback shared rather than copied per project. |
| v0.21.1 | `ScreenType` moved from `App/UI/` to `EnigmaVirtual/Screens/` (M20) | Nothing to do. The enum already declared `namespace EnigmaVirtual.Screens`, so this is a file move with no namespace, API or GUID change - a branched project sees the file in a new folder and nothing else. |
| v0.21.0 | `TranslatableField<T>` is now case-insensitive as a dictionary (H11) - the type is `LocalizedField<T>` as of v0.28.0, and the member names below are its pre-v0.28.0 ones | Nothing to do for translation lookups - `GetLocalized`, `GetTranslation`, `GetValue` and `HasTranslation` behaved case-insensitively before and still do. **What changed is the inherited surface:** `ContainsKey`, `this[key]`, `Remove`, `Add` and enumeration now treat `"EN"` and `"en"` as one key. A project that stored both cases separately will find one entry where it expected two, and `Add` on the second now throws - use the indexer. Nothing in this repo did either. |
| v0.20.0 | `SocketConnection.ReadSocket()` **deleted**; `OneCoreSocketConnection` **deleted**; `keepAliveTime`/`reconnectTime` gain an upper bound (C6, M17) | **A project calling `ReadSocket` will not compile** - that is intended. Replace it with `while (socket.TryGetMessage(out string msg))`, which hands over one complete framed message per call, so the caller no longer splits a raw buffer. A project referencing `OneCoreSocketConnection` retypes to `SocketConnection`: `OneCoreClient.Settings` already derives from `SocketsSettings`, so one class always served both. Neither type is serialized in any scene here - both components are added at runtime - so no scene edit is needed. `keepAliveTime` and `reconnectTime` are capped at 300&#160;s, because the keep-alive ping is the only detector of a half-open socket and an unbounded value silently disabled recovery. |
| v0.19.1 | The four Phidgets rows deleted from `CoreSetup.unity`'s settings panel, and both commented-out `SettingsGroup.Phidgets` branches removed from `SettingInput` (H18) | Nothing to do: the rows were on an inactive `PhidgetsPanel` and were already skipped when saving. A branched project that **enabled** that panel loses the rows on merge - and they never worked, so nothing it relied on is lost. Per-board Phidgets config is JSON-only (`Phidgets.phidgets[]`); `boardIp`, `boardPort` and `reconnectTime` are read by no code at all. `PhidgetSettings.SetData` is kept as the extension point if per-board editing is ever built - it needs a board selector feeding an index, which `SettingInput` has no concept of. |
| v0.19.0 | Settings-panel password moves to a PBKDF2 hash; `EditorSettings` gains `passwordHash`, `maxTries`, `lockoutSeconds`, and initializers on `password`, `touchTime` and `autoHideTime` (H14) | **Nothing breaks on update - the plaintext `password` field still works.** To migrate: *Gema → Set Panel Password*, paste the result into `EditorSettings.passwordHash`, set `password` to `""` **by hand** (the tool does not edit settings.json), in the shipped template **and** in any deployed config under `persistentDataPath`. **Then clear Player Prefs** - `Core` prefers a `PlayerPrefs["Settings"]` snapshot over the file, and that snapshot has no `passwordHash` key, so on a machine that ever saved from the panel the old plaintext keeps working and the migration silently does nothing. A **release** build now fails on a non-empty `password`, and on a `passwordHash` of the default `"gema"`. Two behaviour changes for a config with those keys absent: `touchTime` and `autoHideTime` were `0` and are now `5`/`30`, so the long-press is no longer instant and the panel auto-hides. |
| v0.18.4 | `AnaliticsSender.sendAnaliticsTest` (public field) and its `Update`/`Awake` removed (M8) | Nothing to do unless a project referenced that field in code, which would now fail to compile - the test it triggered is a right-click **Send a test analytics event** on the component instead. A serialized value on a scene or prefab instance is dropped silently, which matters to nobody: it was a one-shot debug trigger, and the component is in no scene or prefab in this repo. |
| v0.18.2 | `Phidget22.NET.dll` `Any Platform` → `Editor` + `Standalone: Win64` (M14) | Nothing to do on 64-bit Windows. **A project building 32-bit Windows silently loses the DLL** - add `Standalone: Win` in the Plugin inspector. The two Phidget SDK controllers are now behind `PLATFORM_STANDALONE_WIN \|\| UNITY_EDITOR_WIN`, so a non-Windows target no longer compiles them; `PhidgetsManager`'s public surface is unchanged and `SetState` reports instead of acting. |
| v0.18.0 | Serial-port code gated behind `PLATFORM_STANDALONE_WIN \|\| UNITY_EDITOR_WIN` (M21) | Nothing to do on Windows. On any other target `UnitySerialPort` and `ArduinoController` no longer compile, so a project that referenced them from `App/` needs the same gate on its own call sites. |
| v0.16.4 | `SystemSettings.Android` → `SystemSettings.AndroidSettings` (M54) | Rename the reference. The only in-framework reader is under `#if UNITY_ANDROID`, so a Windows-targeting project sees no compile error and silently inherits the fix. **On a device that used the settings panel's PlayerPrefs save, clear Player Prefs** — that snapshot holds the old field name and `Core.Awake` prefers it over the file. |
| v0.16.3 | `SettingInput.Save(bool)` → `Save()`; `public SaveIntoJson(object, string)` deleted (M50) | A project with its own settings panel calls `Core.file.SaveCoreSettings()` **once** after its field loop, instead of passing a flag per field. |
| v0.16.2 | — | No API change. |
| v0.16.0 | `ConfigManagerHelper.CopyFile` (public static) deleted (H6) | No replacement needed: `ConfigManager` now seeds every player except WebGL by itself. Only breaks a Windows-player or `DEBUG_MODE` compile, since `CopyFile` sat inside the old Windows `#if`. |
| v0.15.0 | `Api.SaveAsJsonFile(rawData, folderPath, filename)` → `(rawData, filePath)`, and `protected` → `private`; `LoadJsonFile` `internal` → `private` (H8) | The old third argument was unused and the method wrote to an instance field. If a project calls it, pass the full path — or use `FileManager`/`ConfigManager.WriteAtomic`. |

## 6. Platform matrix

Honest current state, not the aspiration:

| Target | Status |
|---|---|
| Windows Standalone | Supported. The most exercised target, and the only one with a long history. |
| Windows Editor | Supported. `DEBUG_MODE`, when a developer sets it locally, also enables the debug config paths in-editor. |
| Android | **First built and run on a device 2026-08-14** - booted to `Core :: Boot complete.` on an SM-T510 (Android 11, IL2CPP, armeabi-v7a), and CMS media downloads (235 files) and loads there; see the note under this table for what that run did and did not cover. Paths fixed (C4, v0.3.0); config seeding **now runs** (H6, v0.16.0), and with it the `file://` scheme fix and `SaveAppSettings`. The seeded copy applies from the *second* launch (M45), and M48 is still open here; the `file://` scheme is one helper as of v0.42.0 (M47) (M46 is editor-only). Verbose logging on (`DO_LOGS`). Serial/Phidgets/RawInput non-functional. |
| iOS / macOS / Linux | Paths fixed (C4). Config seeding **now runs** (H6, v0.16.0) and, unlike Android, synchronously — the shipped file is a real file on all three, so it needs none of the fetch path. Error logging **now works** — `LogError`/`LogException` are unconditional as of C7 (v0.5.0); verbose logging is off, which is intended. |
| WebGL | Not viable: `System.IO.Ports` unavailable, StreamingAssets not readable via `File.*`. |

**Android was built and run for the first time on 2026-08-14**, on a Samsung SM-T510 (Android 11,
IL2CPP, armeabi-v7a, development build), and booted all the way through: settings parsed,
`ConfigManager` patched the deployed config, the live CMS answered, appData parsed, assets
validated, start scene loaded, `Core :: Boot complete.` One run is not a soak test and the media
paths M47 fixed were not individually exercised, but the boot chain is no longer theoretical
there. That run also found M90 within its first log line, which is the argument for doing it
sooner.

iOS and WebGL (`DO_LOGS` covers `Android` and `Standalone`, and Standalone includes macOS and Linux) have still never been **run**. C4 is compile-verified and
the Windows paths are runtime-verified; their path behaviour remains untested.

Windows-only subsystems, gated by `#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN` at the
**call site** in `MainController`, and since v0.18.0/v0.18.2 in the implementation too: the
Phidgets DLL is Editor + Win64 only (`Phidget22.NET.dll.meta` has `Any: enabled: 0` — M14 closed),
and the serial source is gated (M21 closed). Still call-site-only: `RawKeyInput` (global keyboard
hook — H15),
`WindowUtils`, `ForegroundEnforcer`, process kill/restore.

`scriptingDefineSymbols` (committed): `Android: DO_LOGS`, `Standalone: DO_LOGS`. `DO_LOGS` now
gates **verbose logging only** — errors survive without it (C7). `DEBUG_MODE` is *not* committed;
it is a per-developer working-tree toggle, and `BuildValidator` fails any build while it is
defined, which is its purpose (M18 corrected).

## 7. Threading model

Main-thread-only by intent, with **one** exception, and it is contained by design rather than
by accident.

`SocketConnection` runs its read loop on a background `Thread` — but since v0.20.0 that thread
touches only the socket, a `ConcurrentQueue` and `volatile` fields. The main thread drains the queue
in `Update` and raises every event, log and notification from there (C6). That shape is the worked
example to copy: **do not marshal a stream item at a time**. The old code marshalled one notification
correctly and then invoked a `UnityEvent` on the very next line, which is what made it unfixable in
place.

`ActionExtensions.InvokeLater` used to be the second exception — it scheduled on a
`System.Timers.Timer`, so any Unity call inside the callback threw (C5). It was **deleted in
v0.9.0**; `ActionExtensions.cs:39` is the tombstone. Use a coroutine.

`MainThreadDispatcher.RunOnMainThread(Action)` is the marshalling primitive for anything that does
need it; its drain loop was fixed in v0.14.1 (H7). `ImageLoaderThread` does **not** thread despite
the name.

## 8. Conventions

Established and worth keeping:

- Settings DTOs are `[System.Serializable]` classes of **public fields** with defaults,
  nested to mirror the JSON. Never properties — `JsonUtility` ignores those.
- Every module has `isActive` and `doLogs` in its settings block; `MainController.Init()`
  only creates a module when `isActive`.
- Editor-only code is either under `Assets/Editor/` or wrapped in `#if UNITY_EDITOR`.
  The `Attributes/` folder does this correctly in all 18 files — follow that pattern.
- Framework base classes are `abstract`/`virtual` and extended in `App/`
  (`AppDataBase`→`AppData`, `Api`→`ApiControllerBase`→`ApiController`,
  `ScreenControllerBase`→`ScreenController`).

Conventions to stop following:

- `Development.LogIf(cond, msg)` logs when `cond` is **true**, at Log level - it was called `Assert` until v0.45.0 (M9). Its `LogType` overload stopped defaulting that argument in v0.41.0 (M25) - pass it, or use the two-argument form and get Log level. It is a
  `LogIf`, not an assert (M9).
- `Invoke("MethodName", delay)` — string-based, refactor-hostile, and a private target is a
  stripping candidate under IL2CPP. Use `MonoBehaviour.CallLater(delay, Action)` (M10, v0.46.0).
  It runs on a persistent host rather than on the caller, so it survives the caller being
  deactivated exactly as `Invoke` did — `AppInfo`'s boot-critical delayed `Hide` needs that.
- `if (!Core.IsSet) return;` in `Start` — use `enabled = false` instead (H13).

## 9. Known gaps

Assembly definitions landed in v0.24.0; `Assembly-CSharp` holds 13 scripts now, not 260. Before that, every edit recompiled everything and `internal` provided
no real encapsulation. No test assembly, so `com.unity.test-framework` is present but
unused. The three stray URP `.asset` files that used to sit here were deleted in v0.18.0, and
`m_CustomRenderPipeline` is unset (M19).

Full register with severities and locations: [known-issues.md](known-issues.md).



### Timeline project identity
This project's Unity company name is Museu Salvador Caetano and product name is Timeline. The Windows config helper opens/seeds %USERPROFILE%/AppData/LocalLow/Museu Salvador Caetano/Timeline/StreamingAssets/config, matching that identity. Descriptor metadata names the museum and 2026.
