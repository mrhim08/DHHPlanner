# DHHPlanner - DHH RC Lab edition of Mission Planner

DHHPlanner is the ground control station of DHH RC Lab. It is based on [TitanPlanner](https://github.com/Titan-Dynamics/TitanPlanner) (Titan Dynamics), which is itself based on [ArduPilot Mission Planner](https://github.com/ArduPilot/MissionPlanner) by Michael Oborne and contributors. All three are licensed under the GNU GPLv3. DHHPlanner inherits TitanPlanner's reworked UI and adds the built-in DHH flight panel and aircraft profiles, a DHH default theme, and its own settings folder so it can be installed side by side with stock Mission Planner.

---

## Enhancements

Inherited from TitanPlanner: considerable UI/UX upgrades, a reworked HUD, a real-time 3D map with vehicle rendering (and ADSB), an overhauled parameter editor and tab layout, a Betaflight-style motor setup, USB auto-connect, offline map packs for air-gapped planning, and dozens of large improvements throughout the app.

**See [CHANGES.md](CHANGES.md) for the full list of features and fixes added in this fork.**

---

## Installation

### Windows (Recommended)

Grab the latest installer from [Releases](https://github.com/mrhim08/DHHPlanner/releases/latest). The app will notify you on updates from then on.

### Building from Source

Requires Visual Studio 2022.

```bash
git clone https://github.com/mrhim08/DHHPlanner.git
cd DHHPlanner
git submodule update --init
```

Open `MissionPlanner.sln` in Visual Studio 2022 and Build.

### Linux (Mono)

```bash
sudo apt install mono-complete mono-runtime libmono-system-windows-forms4.0-cil \
    libmono-system-core4.0-cil libmono-winforms4.0-cil libmono-corlib4.0-cil \
    libmono-system-management4.0-cil libmono-system-xml-linq4.0-cil

mono MissionPlanner.exe
```

See [Linux/README.md](Linux/README.md) for the upstream build (`./Linux/build.sh`) and launch (`./Linux/run.sh`) scripts. The prebuilt Linux and macOS downloads that document links to are published by upstream only; this fork ships Windows releases.

---

## Platform Support

| Platform | Status |
|----------|--------|
| Windows | ✅ Full Support |
| Linux (Mono) | ⚠️ WIP |
| macOS | ⚠️ WIP |
| Android | ⚠️ WIP |
| iOS | ⚠️ WIP |

---

## Data Sources

- Live ADS-B traffic: [adsb.fi](https://adsb.fi) open data (personal, non-commercial use)
- Motor frame diagrams: [ArduPilot documentation](https://ardupilot.org/copter/docs/connect-escs-and-motors.html) (CC BY-SA 3.0), shipped in `MotorFrames/`

## Upstream

- Upstream repository: https://github.com/ArduPilot/MissionPlanner
- ArduPilot website: http://ardupilot.org/planner/
- Forum: http://discuss.ardupilot.org/c/ground-control-software/mission-planner

## License

See [COPYING.txt](COPYING.txt).
