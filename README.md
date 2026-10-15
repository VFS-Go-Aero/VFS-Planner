# Custom Mission Planner (VFS Planner)

A customized fork of [Mission Planner](https://github.com/ArduPilot/MissionPlanner) for ArduPilot vehicles. It keeps upstream compatibility while adding map overlays, preflight checks, and UI improvements.

**Full fork changelog:** [CHANGES.md](CHANGES.md)

---

## Table of contents

- [Quick start](#quick-start)
- [Main screens](#main-screens)
- [Map overlays (Flight Data)](#map-overlays-flight-data)
- [Preflight & arming](#preflight--arming)
- [Installation](#installation)
- [Platform support](#platform-support)
- [Upstream & license](#upstream--license)

---

## Quick start

1. **Install or build** the app (see [Installation](#installation)).
2. **Connect** the vehicle (USB, radio, or TCP/UDP — same as Mission Planner).
3. Open **Flight Data** to fly and monitor telemetry; open **Flight Planner** to edit waypoints.
4. Before arming, open **Mission Checklist** and complete items; use map overlays if you need TFR/airspace context.

---

## Main screens

| Tab / area | Use it for |
|------------|------------|
| **Flight Data** | Live map, HUD, telemetry, map overlay toggles |
| **Flight Planner** | Mission waypoints, geofence, rally points |
| **Mission Checklist** | Preflight summary and checklist (arming blockers) |
| **Initial Setup / Config** | Parameters, firmware, frame setup (same workflow as upstream) |

Other tabs match upstream Mission Planner unless noted in [CHANGES.md](CHANGES.md).

---

## Map overlays (Flight Data)

At the **bottom of the map** on Flight Data, use the checkboxes to turn layers on or off. Layers load from the network when enabled; allow a few seconds for polygons or tiles to appear.

| Toggle | What it shows |
|--------|----------------|
| **Weather** | RainViewer radar tiles on the map |
| **TFRs** | FAA temporary flight restrictions (polygons) |
| **Airspace** | OpenAIP airspace (WFS; URL configurable) |
| **LAANC grid** | FAA UAS facility map grid |
| **NOTAMs** | Markers derived from TFR locations |
| **Special use** | Special-use airspace (OpenAIP + DoD layer where available) |
| **Custom NoFly** | Your configured no-fly zones |

**NOTAM / TFR briefing:** With TFR-related data loaded, a panel on the right side of the map lists items and offers zoom and FAA detail links.

Settings for these layers are stored in planner settings (same keys as the checkboxes, e.g. `showweather`, `showtfr`, `showairspace`).

---

## Preflight & arming

**Mission Checklist** includes a **preflight summary** at the top (link, GPS, PreArm, TFR conflicts, etc.) and checklist items from `Stage2Checklist.xml` / `Stage3Checklist.xml` / your saved mission checklist.

Arming can be blocked when:

- Required checklist items marked as arming blockers are not complete
- The link is not connected (when applicable)
- PreArm checks fail (default: required; setting `armguard_require_prearm`)
- Home or mission path intersects loaded TFRs (default: blocked; setting `armguard_block_tfr`)

For TFR-based arming checks, enable **TFRs** on the map so polygons can load before you arm.

Run **PreArm** from the checklist or summary when the vehicle is connected and configured.

---

## Installation

### Windows (typical)

Use the build provided by your team, or build from source below.

### Build from source

- **Requirements:** Visual Studio 2022, .NET tooling as required by the solution

```bash
git clone <repository-url>
cd CustomMissionPlanner
git submodule update --init
```

Open `MissionPlanner.sln` in Visual Studio 2022 and build **MissionPlanner**.

### Linux (Mono, experimental)

```bash
sudo apt install mono-complete mono-runtime libmono-system-windows-forms4.0-cil \
    libmono-system-core4.0-cil libmono-winforms4.0-cil libmono-corlib4.0-cil \
    libmono-system-management4.0-cil libmono-system-xml-linq4.0-cil

mono MissionPlanner.exe
```

---

## Platform support

| Platform | Status |
|----------|--------|
| Windows | Supported |
| Linux (Mono) | Work in progress |
| macOS | Work in progress |
| Android / iOS | Work in progress |

---

## Upstream & license

- Upstream: https://github.com/ArduPilot/MissionPlanner  
- ArduPilot docs: https://ardupilot.org/planner/  
- Community: https://discuss.ardupilot.org/c/ground-control-software/mission-planner  

License: [COPYING.txt](COPYING.txt)
