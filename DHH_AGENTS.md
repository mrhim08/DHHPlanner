# DHH_AGENTS.md

Fork-specific instructions for coding agents (Claude Code, Codex, ...) working on **DHHPlanner**,
DHH RC Lab's fork of ArduPilot Mission Planner. Read `CLAUDE.md` first for the upstream codebase
(build, architecture, the `#if !LIB` trap, WinForms triplets, localisation). This file is
fork-owned: upstream never touches it, so keep fork knowledge here, not in `CLAUDE.md`.

## Product identity

| Item | Value | Where |
|---|---|---|
| Product name | `DHHPlanner` | `ExtLibs/Utilities/Settings.cs` (`AppConfigName`, also names the user data folder `Documents\DHHPlanner`), `Program.cs` (`name`, drives splash + window title) |
| Company | DHH RC Lab | `Properties/AssemblyInfo.cs`, `MissionPlanner.csproj` metadata, `wix/Program.cs` Manufacturer |
| MSI UpgradeCode | own GUID, never reuse upstream's | `wix/Program.cs` |
| Exe name | still `MissionPlanner.exe` (on purpose: fewer upstream conflicts; Updater depends on it) | — |
| Default theme | `DHH.mpsystheme` (palette is a placeholder until the official brand colours arrive) | repo root, `MainV2.cs`, `Utilities/ThemeManager.cs` |
| App self-update | GitHub Releases of `dhh-rclab/DHHPlanner` (org name is a placeholder) | `app.config` `UpdateLocation*` keys, `Utilities/Update.cs` |

DHHPlanner keeps its settings separate from stock Mission Planner on purpose, so both can be
installed side by side. Do not add a settings migration from "Mission Planner".

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
- Remote `upstream` = `ArduPilot/MissionPlanner`; work on `development`.
- Merge `upstream/master` into `development` regularly. On conflict keep the fork's identity and
  UI changes, take upstream's fixes. Re-check every row of "Product identity" afterwards.
- Keep fork changes in new files (`DHH/`) and hook into upstream files with as few lines as
  possible; `GCSViews/FlightData.cs` and `MainV2.cs` are the main conflict hot spots (in
  TitanPlanner's history they were touched by 110 and 40 fork commits).
- Preserve each file's existing line endings and BOM; never reformat whole upstream files.

## Known gaps (to do before the first release)
- Release zip: the 7z command in `wix/Program.cs` excludes `*.param`, so `dhh-aircraft/**/params/*.param`
  would be dropped from the zip (the MSI includes it). Fix when the CI packaging is reworked.
- CI `.github/workflows/main.yml` still produces upstream artifact names (`MissionPlannerBeta.zip`);
  it must publish `DHHPlanner.zip`, `version.txt` and `checksums.txt` to the GitHub release the
  `app.config` update keys point at.
- `ExtLibs/Installer` (web installer project) still downloads and installs stock Mission Planner;
  do not ship it until it is re-pointed.
- `MainV2.cs` Windows XP branch still sets update URLs to `firmware.ardupilot.org/MissionPlanner/xp/`.
- The tlog thumbnail shell handler (CLSID in `wix/Program.cs`, cache in
  `ProgramData\Mission Planner\tlogimagecache`) is shared with stock Mission Planner.
- Icons, splash image and brand colours are still upstream/placeholder; GitHub org `dhh-rclab` is a placeholder.

## CHANGES.md
`CHANGES.md` is the user-facing summary of what DHHPlanner adds over upstream, describing how it
behaves today (not a changelog). Update it, and `README.md` when user-facing, whenever a fork
feature lands. `ChangeLog.txt` is upstream's and is not maintained here.
