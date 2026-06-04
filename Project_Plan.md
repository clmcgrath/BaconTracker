This is the perfect point to package our entire high-level system architecture into a formal **System Context File**. You can drop this markdown file directly into your IDE workspace (e.g., as README.md or SYSTEM\_CONTEXT.md) for your AI coding assistant (like Cursor, Claude Engineer, or a custom agent) to digest.

It contains every single technical constraint, architectural choice, zero-cost networking strategy, and code blueprint we have established.

## ---

**SYSTEM\_CONTEXT.md**

## **1\. Project Vision & Core Value Proposition**

* **Target Audience:** Competitive Hearthstone Battlegrounds players on **Windows**, **Linux (Wayland)**, and **macOS**.  
* **Primary Key Performance Indicator (KPI):** Absolute zero-cost server infrastructure at scale while achieving immediate mid-session content delivery.  
* **Core Product Advantage (USP):** Native support for Linux Wayland via Layer Shell. Highly performant code-first architecture allowing on-the-fly content updates without forcing application restarts.

## **2\. Technology Stack & Architectural Core**

* **Language/Platform:** .NET 8/9 C\# Unified Platform Engine.  
* **UI Layer:** Code-only UI framework with **0% XAML/Markup**. Built utilizing an immediate-mode paradigm via **ImGui.NET** paired with an **SDL2 / GLFW** native windowing backend wrapper.  
* **Linux Window Subsystem:** Native Wayland Layer Shell Protocol (wlr-layer-shell-unstable-v1) using low-level C\# platform bindings (e.g., WaylandDotnet or custom P/Invoke over libgtk-layer-shell.so).  
* **Windows Window Subsystem:** Win32 API Extended Window Styles (WS\_EX\_TOPMOST | WS\_EX\_TRANSPARENT | WS\_EX\_LAYERED) applied via native P/Invoke.  
* **macOS Window Subsystem:** AppKit/Cocoa window properties (NSFloatingWindowLevel, ignoresMouseEvents \= true, canJoinAllSpaces) handled via C\# Interop.

## **3\. The Zero-Overhead Content Delivery Network (CDN) Matrix**

To maintain an operating overhead of exactly **$0.00/month** while serving infinite active client installations, the build utilizes a highly optimized hybrid architecture combining cloud compute and static asset serving:

`[ Scheduled CRON / Webhook Trigger ]`

                 `|`  
                 `v`  
      `[ GitHub Actions Runner ]  ======> Automated HearthstoneJSON scraping & Battlegrounds minification`  
                 `|`  
                 `+-------> 1. Compiles single-file native binaries (Win/Linux) to GitHub Releases.`  
                 `+-------> 2. Commits a compact static dataset asset 'bg_cards.json' to GitHub Pages.`  
                 `+-------> 3. Sends a Cache-Purge POST trigger request directly to Cloudflare.`

## **Serverless Signaling Layer (Azure Functions \+ Cloudflare)**

To check for version content mismatch flags without forcing the client app to waste bandwidth scraping large file headers, an update notification API is hosted on the **Azure Functions Consumption Tier**.

`[ Desktop App Startup / Periodic 5-Min Poll ]`

                 `|`  
                 `v`  
   `[ ://yourdomain.com ]`  
                 `|`  
                 `v`  
     `[ Cloudflare Edge Server ]`

                 `|`  
        `+--------+--------+`  
        `|                 |`  
   `(Cache Hit)       (Cache Miss)`

        `|                 |`  
        `v                 v`  
`[ Return Hash ]    [ Wake Up Azure Function ] ===> Read Portal App Setting Envs`  
`(Cost: $0.00)      (Max 12x/hour globally)         (Cost: $0.00 within free grants)`

## **4\. Production-Ready Deployment Pipeline & Code Repositories**

## **The GitHub Actions Workflow (.github/workflows/generate-patch.yml)**

`name: Global Card Patch Automation Pipeline`

`on:`  
  `schedule:`  
    `- cron: '0 */3 * * *'  # Automatically executes every 3 hours`  
  `workflow_dispatch:      # Manual force execution override`

`permissions:`  
  `contents: write`

`jobs:`  
  `run-patch-pipeline:`  
    `runs-on: ubuntu-latest`  
    `steps:`  
    `- name: Checkout Code Repository`  
      `uses: actions/checkout@v4`

    `- name: Setup .NET SDK Compiler`  
      `uses: actions/setup-dotnet@v4`  
      `with:`  
        `dotnet-version: '8.0.x'`

    `- name: Execute Data Mining and Minification Task`  
      `run: |`  
        `dotnet run --project ./PatchGenerator/PatchGenerator.csproj --configuration Release`

    `- name: Deploy Static Patch Asset to Free GitHub Pages CDN`  
      `uses: JamesIves/github-pages-deploy-action@v4`  
      `with:`  
        `folder: ./PatchGenerator/bin/Release/net8.0/dist`  
        `branch: gh-pages`  
        `clean: true`

    `- name: Purge Cloudflare Edge Cache Instantly`  
      `uses: jakejarvis/cloudflare-purge-action@v0.3.0`  
      `env:`  
        `CLOUDFLARE_ZONE: ${{ secrets.CLOUDFLARE_ZONE_ID }}`  
        `CLOUDFLARE_TOKEN: ${{ secrets.CLOUDFLARE_API_TOKEN }}`  
        `PURGE_URLS: '["https://yourdomain.com"]'`

## **The Desktop Client Live Polling Loop**

`using System;`  
`using System.IO;`  
`using System.Net.Http;`  
`using System.Text.Json;`  
`using System.Threading;`  
`using System.Threading.Tasks;`

`public class BackgroundUpdatePoller`  
`{`  
    `private const string VersionApiUrl = "https://yourdomain.com";`  
    `private const string StaticCdnUrl = "https://github.io";`  
    `private readonly HttpClient _client = new();`  
    `private readonly CancellationTokenSource _cts = new();`  
    `private string _currentLocalHash;`

    `public event Action<string>? OnPatchDownloadedReady;`

    `public BackgroundUpdatePoller(string initialHash) { _currentLocalHash = initialHash; _client.Timeout = TimeSpan.FromSeconds(3); }`

    `public void StartPollingLoop() => Task.Run(() => ExecPollingTimerAsync(_cts.Token));`  
    `public void StopPollingLoop() => _cts.Cancel();`

    `private async Task ExecPollingTimerAsync(CancellationToken ct)`  
    `{`  
        `using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));`  
        `while (await timer.WaitForNextTickAsync(ct))`  
        `{`  
            `try`  
            `{`  
                `string response = await _client.GetStringAsync(VersionApiUrl, ct);`  
                `using var jsonDoc = JsonDocument.Parse(response);`  
                `string remoteHash = jsonDoc.RootElement.GetProperty("VersionHash").GetString()!;`

                `if (remoteHash != _currentLocalHash)`  
                `{`  
                    `string freshData = await _client.GetStringAsync(StaticCdnUrl, ct);`  
                    `string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGTracker");`  
                    `string patchPath = Path.Combine(appFolder, "pending_patch.json");`  
                      
                    `await File.WriteAllTextAsync(patchPath, freshData, ct);`  
                    `_currentLocalHash = remoteHash;`  
                      
                    `OnPatchDownloadedReady?.Invoke(patchPath);`  
                `}`  
            `}`  
            `catch { /* Silent logging to maintain crash-free loop over flaky connections */ }`  
        `}`  
    `}`  
`}`

## **Atomic Memory Swapping (No Restart Lifecycle)**

`using System.Collections.Generic;`  
`using System.IO;`  
`using System.Threading;`

`public class DynamicCardRegistry`  
`{`  
    `private readonly ReaderWriterLockSlim _lock = new();`  
    `private Dictionary<string, BattlegroundsCardMap> _registry = new();`

    `public void HotSwapDatabase(string jsonFilePath)`  
    `{`  
        `var newMap = LoadFileToMap(jsonFilePath); // Custom parsing method`  
        `_lock.EnterWriteLock();`  
        `try { _registry = newMap; } // Atomic memory pointer adjustment`  
        `finally { _lock.ExitWriteLock(); }`  
    `}`

    `public bool TryLookupCard(string cardId, out BattlegroundsCardMap? card)`  
    `{`  
        `_lock.EnterReadLock();`  
        `try { return _registry.TryGetValue(cardId, out card); }`  
        `finally { _lock.ExitReadLock(); }`  
    `}`  
`}`

## **5\. In-Match Game Engine Logging Architecture**

* **0% Process Memory Injection, 0% P/Invoke Game Window Scraping, 0% OCR Overhead.**  
* **Security & Compliance Policy:** Data ingestion is driven strictly by tailing the local text files of Hearthstone's debug loop (Power.log) inside a non-blocking FileShare.ReadWrite pipeline. Completely safe and explicitly compliant with Blizzard Entertainment's standard Terms of Service (TOS) tracking constraints.  
* **Target Watcher Mappings:**  
  * **Linux (Wine/Proton Prefix Path):** \~/.local/share/Steam/steamapps/compatdata/214560/pfx/drive\_c/Users/steamuser/AppData/Local/Blizzard/Hearthstone/Logs/  
  * **Windows Native Path:** %USERPROFILE%\\AppData\\Local\\Blizzard\\Hearthstone\\Logs\\  
  * **macOS Native Path:** \~/Library/Logs/Hearthstone/

## **6\. Monitored Feature & Revenue Roadmap**

1. **Free Tier Panel:** Overlay tracks basic client lobby updates (banned/active tribes), records current turn gold, and prints cached warbands (attack values, health, placement position) upon entering active combat loops.  
2. **Premium Subscription Tier ($2-3/mo):** Lock simulation analysis capabilities behind a subscription verification tag. The instant a combat loop instantiates, the local background client spins a fast parallelized Monte Carlo multi-threaded simulator loop 5,000 times using the parsed warband records, rendering real-time win/loss/tie probability matrix components on the player’s overlay deck interface canvas before battle animations finish.

## **7. Future Development Timeline & UX Milestones**

To elevate BaconTracker from a basic concept to a premium-grade product, the following UX enhancements and milestone timelines are incorporated into the roadmap:

### **Milestone 1: Customization & Layout Editor (Current & Next Cycles)**
* **Visual Drag Boundaries:** Draw dashed borders around panels during "Edit Layout" mode so users clearly see bounds.
* **Magnetic Alignment / Grid Snapping:** Implement 10px grid snapping when dragging panels to make alignment clean and cohesive.
* **Central Control Panel:** Add a global Settings GUI allowing HUD scaling (0.5x - 2.0x), custom transparency sliders per-panel, and a "Reset to Defaults" button.

### **Milestone 2: Minion Browser Filtering & Response Speed (Upcoming Cycle)**
* **Golden Minion Toggle:** Add a dedicated golden button/toggle at the top of the browser to switch the entire list view between plain and golden variants, preventing visual clutter of double entries.
* **Hover Tooltip Delay:** Introduce a subtle 150ms hover delay before rendering the OpenGL card art to prevent flashing tooltips when sweeping the cursor across the table list.
* **Tribe Icon Navigation:** Replace the drop-down box with horizontal tribe icon button filters for faster one-click navigation.
* **Smart Off-Screen Tooltip Flipping:** Detect viewport edges and automatically flip tooltips (left/right/above/below) so card art never gets cropped or cut off by screen boundaries.
* **Official Client Asset Extraction:** Integrate a dynamic asset parser to scan local Hearthstone game installation paths (reading Unity `.unity3d` asset bundles under Wine/Steam directories) to extract official Gold icons, Tavern Tier shields, and borders, falling back seamlessly to CDN downloads.

### **Milestone 3: Lobby Analyst Data Visualization (Future Cycle)**
* **Health Bar Visuals:** Replace simple text health listings with color-coded HP bars (Green -> Yellow -> Red) and add special styling for dead/ghost players.
* **Turn History Timelines:** Render an interactive graph or visual history trail when hovering over a player's Tavern tier showing upgrades, triples, and previous combat results.
* **Combat Simulator Odds Gauge:** Add a gauge widget showing win/loss/draw percentages in real-time as soon as combat starts.

---

Once you hand this file off to your development agent, what would you like it to build next? We can focus on the Monte Carlo combat simulation library, layout settings persistence interfaces, or starting the Milestone 1 layout styling features. Which one shall we cue up?