# DHH_AGENTS.md

Fork-specific instructions for coding agents (Claude Code, Codex, ...) working on **DHHPlanner**,
DHH RC Lab's product built on TitanPlanner (Titan Dynamics), which is a fork of ArduPilot Mission Planner
(chain: ArduPilot -> Titan -> DHHPlanner). Read `AGENTS.md` first (it is Titan's, inherited) for the codebase
(build, architecture, the `#if !LIB` trap, WinForms triplets, localisation). This file is
DHH-owned: neither Titan nor upstream touches it, so keep fork knowledge here, not in `CLAUDE.md`.

## Product identity

| Item | Value | Where |
|---|---|---|
| Product name | `DHHPlanner` | `ExtLibs/Utilities/Settings.cs` (`AppConfigName`, also names the user data folder `Documents\DHHPlanner`), `Program.cs` (`name`, drives splash + window title) |
| Company | DHH RC Lab | `Properties/AssemblyInfo.cs`, `MissionPlanner.csproj` metadata, `wix/Program.cs` Manufacturer |
| MSI UpgradeCode | own GUID, never reuse upstream's | `wix/Program.cs` |
| Exe name | still `MissionPlanner.exe` (on purpose: fewer upstream conflicts; Updater depends on it) | — |
| Default theme | `DHH.mpsystheme` (palette is a placeholder until the official brand colours arrive) | repo root, `MainV2.cs`, `Utilities/ThemeManager.cs` |
| App self-update | GitHub Releases of `mrhim08/DHHPlanner` | `app.config` `UpdateLocation*` keys, `Utilities/Update.cs` |
| First-run defaults | `HandleDhhEditionUpgrade()` runs once (setting `dhh_edition`): DHH theme and the Params/Video/Tuning/Inspector tabs shown | `Program.cs` |
| Splash | `dhh_logo.png` (1920x1200 placeholder, embedded in `Splash.resx` as `dhhLogo.Image`); "Version: DHH x.y" | `Splash.cs`, `Splash.Designer.cs`, `Splash.resx` |
| Network identity | ADS-B User-Agent `DHHPlanner` plus placeholder contact `dhhplanner@users.noreply.github.com` (adsb.lol wants contact info; replace with a monitored address) | `ExtLibs/Utilities/adsb.cs` |
| Offline map pack | writes tag `DHHPlanner.OfflineMapPack`; the reader does not check the tag, so Titan packs load | `Utilities/OfflineMapPack.cs` |
| Credits | Michael Oborne copyright kept; no Titan personal credits in assembly metadata (`HUD.cs` header still says "Modified by Mohammad Adib": GPL notice, keep) | `Properties/AssemblyInfo.cs` |

DHHPlanner keeps its settings separate from stock Mission Planner on purpose, so both can be
installed side by side. Do not add a settings migration from "Mission Planner" or TitanPlanner:
`Settings.PreviousAppConfigNames` (Titan's `DataFolderMigration` mechanism) stays empty. The MSI only
upgrades/removes DHHPlanner (own UpgradeCode); Titan's `KeepStockMissionPlanner` / old-Titan-removal
upgrade logic was removed from `wix/Program.cs` on purpose.

Vehicle-firmware and parameter-metadata downloads must keep pointing at ArduPilot servers. Only
the application self-update keys point at DHH.

## Fork features and where they live

| Feature | Files |
|---|---|
| DHH panel (FlightData tab "DHH"): VTOL state, operator mode buttons, preflight checklist gating ARM, parameter-vs-preset check | `DHH/DHHPanel.cs` (compiled into the exe, desktop only) |
| Aircraft profiles (first: eCobra VTOL QuadPlane) | `DHH/dhh-aircraft/<id>/profile.json`, `checklist.json`, `params/*.param`; copied to the output as `dhh-aircraft\`; a folder of the same name in the user data dir overrides it |

### DHH panel safety rules (do not weaken)
- ARM is enabled only with a live link (packet < 2 s old), disarmed, every auto check passing,
  every manual item ticked, and the built-in prearm/GPS/EKF checks passing even if a profile's
  checklist omits them. Unknown/no-data counts as fail. Never force-arm.
- "Apply preset" refuses while armed, needs a firmware match and an explicitly matched or chosen
  profile, and lists every `NAME: old -> new` before writing.
- No blocking MAVLink call on the UI thread or in `Loop()`; every handler is wrapped so the
  plugin can never crash the app.

## Upstream sync
- Remotes: `titan` = `Titan-Dynamics/TitanPlanner`, `upstream` = `ArduPilot/MissionPlanner`; work on `development`.
- Normal flow: merge `titan/development` into `development` (Titan merges ArduPilot regularly, so this brings
  both). Merge `upstream/master` directly only when an ArduPilot fix is needed before Titan has it.
- Conflict rule: **DHH identity, Titan UI, upstream fixes.** Keep DHH's name/GUIDs/URLs/theme/logos, keep
  Titan's UI and features, take upstream's fixes. Re-check every row of "Product identity" afterwards, and
  re-run `git grep -n -i -E "titan|titanuas|titandynamics"` for identity that crept back in (Titan's logos,
  `titandynamics.aero` links, `TitanPlanner-*` release names, Tornado/Proton icons, its contact address).
- Keep DHH changes in new files (`DHH/`) and hook into Titan/upstream files with as few lines as possible;
  `GCSViews/FlightData.cs` and `MainV2.cs` are the main conflict hot spots.
- Preserve each file's existing line endings and BOM; never reformat whole upstream files.

## Known gaps (to do before the first release)
- Release packaging: the CI zip (`7z a ... *`) and the MSI (`wix/Program.cs`) now include
  `dhh-aircraft/**/params/*.param`; the legacy `create.bat` 7z line re-adds `dhh-aircraft` after its `*.param`
  exclusion. Not yet exercised on a real CI run.
- `.github/workflows/main.yml` publishes `DHHPlanner.zip`, `DHHPlanner-Installer.msi`, `version.txt`, `checksums.txt`
  to `development-build` (push to `development`) and `DHHPlanner-<version>` (push to `master`) of
  `mrhim08/DHHPlanner`; untested until the first run. Release notes list all non-ArduPilot commits (including
  Titan's) as DHHPlanner's; add a `titan` split in `.github/scripts/release-notes.sh` if that gets noisy.
  Linux/mac/android workflows only publish in the ArduPilot repository.
- `ExtLibs/Installer` (web installer project) still downloads and installs stock Mission Planner;
  do not ship it until it is re-pointed.
- `MainV2.cs` Windows XP branch still sets update URLs to `firmware.ardupilot.org/MissionPlanner/xp/`.
- The tlog thumbnail shell handler (CLSID in `wix/Program.cs`, cache in
  `ProgramData\Mission Planner\tlogimagecache`) is shared with stock Mission Planner.
- Branding is still placeholder: app icon (`mpdesktop.ico`, `mpdesktop44/150.png`) is upstream's ArduPilot icon,
  main-menu logo is upstream's, splash is a generated text PNG, theme colours are placeholders. Plane/copter
  map icons are upstream's generic images (`Resources/planetracker.png`, `quad2.png`) drawn by Titan's marker code;
  check the size looks right. Titan's tab icons and `Resources/acc_*.png` calibration pictures are Titan-made art
  (the PNGs carry a Titan developer's local file path in their metadata); replace them when DHH has its own.
- ADS-B: `API_CONTACT` in `ExtLibs/Utilities/adsb.cs` is a placeholder (`dhhplanner@users.noreply.github.com`); set a monitored address.
- Splash form has no window icon (`$this.Icon` resource dropped with the logo swap; harmless). Main-menu logo click still opens ardupilot.org.
- File associations `.tlog`/`.log`/`.dfbin`: whichever of DHHPlanner / Mission Planner / TitanPlanner is installed last owns them.
- Android/linux/mac workflows only run or publish in the ArduPilot repository.

## CHANGES.md
`CHANGES.md` is the user-facing summary of what DHHPlanner adds over upstream, describing how it
behaves today (not a changelog). Update it, and `README.md` when user-facing, whenever a fork
feature lands. `ChangeLog.txt` is upstream's and is not maintained here.
