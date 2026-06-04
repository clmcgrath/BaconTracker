# BaconTracker 🥓

BaconTracker is a zero-cost, high-performance C# overlay tracker for Hearthstone Battlegrounds. Built using Silk.NET (OpenGL), ImGui, and GTK4, it offers a hardware-accelerated overlay window with low system overhead.

---

## Features

- **Lobby & Minion Browser:** Quickly browse and filter all Hearthstone Battlegrounds minions. Includes search queries, Tavern Tier filters, and toggles for **Golden** and **Duos-only** cards.
- **Lua Plugin Engine:** Extend the HUD and tracker display panels using declarative Lua plugin scripts.
- **Dynamic Database Synchronization:** Automatically downloads and minifies the latest Hearthstone card database from HearthstoneJSON at startup.
- **Cross-Platform:** Native support for both Linux (Wayland and X11/XWayland via GTK4) and Windows (Win32 API).
- **CI/CD Integrated:** Automated multi-platform packages (zips, tarballs, and Debian `.deb` installers) built via Cake Frosting.

---

## Directory Structure

- **`BaconTracker.App/`**: Main entry point, OpenGL initialization, ImGui controller, and native GTK4/Win32 window managers.
- **`BaconTracker.Core/`**: Game state tracking, Hearthstone log parsing, local settings management, card database loader, and wine-prefix detectors.
- **`BaconTracker.Build/`**: Cake Frosting automated build system defining compiler stages and native installer generation.
- **`BaconTracker.PatchGenerator/`**: Developer tool used to scrape, filter, and compile raw Hearthstone card data into minified versions.
- **`plugins/`**: Dynamic folder where customizable Lua UI layout panels are registered.

---

## Development Setup

### Prerequisites

- **.NET 10.0 SDK** (or newer)
- **Linux Dependencies (Ubuntu/Debian):**
  ```bash
  sudo apt install libgtk-4-dev libgtk-4-1
  ```

### Build & Run Locally

1. **Clone the repository:**
   ```bash
   git clone https://github.com/clmcgrath/BaconTracker.git
   cd BaconTracker
   ```

2. **Compile the solution:**
   ```bash
   dotnet build
   ```

3. **Run the tracker:**
   ```bash
   dotnet run --project BaconTracker.App
   ```

---

## Build & Packaging (Cake Pipeline)

We use Cake Frosting to compile and package production releases. You can build all targets by running:

```bash
dotnet run --project BaconTracker.Build --target Default
```

### Available Build Targets:
- `Clean`: Cleans build outputs (`bin`/`obj`) in all subprojects and empty the artifacts directory.
- `Publish-Windows`: Compiles and packages a self-contained Windows `win-x64` zip file.
- `Publish-Linux`: Compiles a self-contained Linux `linux-x64` tarball and structures/builds a Debian installer package (`.deb`).
- `Publish-Mac`: Compiles and packages a self-contained macOS `osx-x64` tarball.
- `Default`: Cleans and runs all three platform publish tasks sequentially.

*Outputs are saved under the `./artifacts/` folder.*

---

## Command Line Options

### BaconTracker Application (`bacontracker` or `BaconTracker.App`)

On Linux, the application automatically attempts to detect layer-shell compatibility. You can override or bypass this behavior using:

| Switch | Description |
| :--- | :--- |
| `--wayland` / `-wayland` / `--native` | Forces the Wayland native backend (`GDK_BACKEND=wayland`) and bypasses checking for the `libgtk4-layer-shell.so.0` library. |

*Note: If layer-shell is not present, the app will automatically relaunch utilizing XWayland stay-above mode.*

### Metadata Scraper (`BaconTracker.PatchGenerator`)

If you want to manually regenerate card list exports:

```bash
dotnet run --project BaconTracker.PatchGenerator [outputPath] [switches]
```

| Parameter/Switch | Default | Description |
| :--- | :--- | :--- |
| `[outputPath]` | `bg_cards.json` | The file location where the minified JSON results will be saved. |
| `-s` / `--sync-art` | Disabled | Downloads and synchronizes card art images locally from the Hearthstone JSON CDN. |
| `--art-dir <dir>` | `art_cache` | Specifies the local folder path to store downloaded card images. |
