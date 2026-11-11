# WorkbenchEngine (Minimal 2D Workbench & Runtime)

> **Language:** English | [Русская версия (README.ru.md)](README.ru.md)

**WorkbenchEngine** is a lightweight, open-source, domain-agnostic 2D workbench and runtime built on **.NET 8 / C# 12**, **Raylib-cs**, **ImGui.NET**, and **Roslyn**.

Its core contains **zero game-domain knowledge** (no built-in concepts of genres, characters, dialogs, quests, or physics rules). Instead, it provides low-level OS/GPU primitives, file hot-reloading, a modular plugin UI, interactive 2D layer composition, timeline evaluation, shaders/offscreen surfaces, and dynamic C# script execution orchestrated cleanly via an event bus.

---

## Key Architectural Principles

1. **Zero Game-Domain Knowledge (`Engine.Core`)**: The core only manages windows, input, audio buffers, 2D textures (`TextureHandle` fast cache), hardware 2D camera (`Camera2D`), blend modes, offscreen surfaces (`FBO`s), fragment shaders, settings, command menus, workspaces, and plugin lifecycles.
2. **Plugin-First UI (`Engine.Plugins`)**: Every editor panel (`Project`, `Layers`, `Inspector`, `Viewport`, `Timeline`, `Execution`, `Settings`) is an independent `IEnginePlugin` that communicates over contracts and `IEventBus` events, and can be toggled on/off at runtime.
3. **Data-Driven & Code-Agnostic**: Scenes are plain JSON (`scenes/*.scene.json`), assets are workspace files, and behaviors are external C# scripts (`scripts/**/*.cs`) implementing `IExecutableNode`.
4. **VS Code First Workflow**: There is no built-in text editor. Edit C# scripts, JSON documents, or GLSL shaders in VS Code and the workbench automatically hot-reloads modified assets, shaders, scenes, and compiled script assemblies.

---

## Tech Stack

* **Target Runtime:** .NET 8 / C# 12 (`RollForward: Major`, Apple Silicon ARM64 & desktop)
* **Graphics & Audio:** `Raylib-cs` 7.0.1 (OpenGL 4.1 / Metal backend on macOS, GLSL `#version 330` shaders, Core Audio / miniaudio)
* **Workbench UI:** `ImGui.NET` 1.91.6.1 + `rlImGui-cs` 3.2.0 (docking workspace)
* **Dynamic Scripting:** `Microsoft.CodeAnalysis.CSharp` 4.12.0 (Roslyn in-memory compilation + collectible `AssemblyLoadContext`)
* **macOS Integration:** Native top `NSMenu` bar (`MacOsMenuBridge`) and resilient Cocoa modifier/mouse state recovery (`MacOsInputBridge`)

---

## Quick Start

### Build

```bash
dotnet build WorkbenchEngine.sln
```

### Run Showcase / Sample Projects

```bash
dotnet run --project src/Engine.App -- --project SampleProject
dotnet run --project src/Engine.App -- --project HandshakeShowcase
```

### CLI Arguments

| Argument | Description | Default |
| :--- | :--- | :--- |
| `--project <path>` | Path to the project root directory containing `project.json` | `./SampleProject` |
| `--no-splash` | Skip the animated startup splash screen and open the workbench immediately | `false` |
| `--smoke-frames <N>` | Run `N` frames and exit cleanly (useful for automated checks) | Unlimited (interactive) |

---

## Solution Structure

```text
WorkbenchEngine/
├── WorkbenchEngine.sln
├── SampleProject/                  # Isolated starter workspace
│   ├── project.json                # Resolution, active C# nodes, timeline tracks
│   ├── scenes/
│   │   └── main.scene.json         # Composable 2D layer hierarchy
│   ├── scripts/
│   │   └── MovingSpriteNode.cs     # Sample IExecutableNode script
│   └── assets/
│       ├── sprites/                # .png / .jpg / .jpeg textures
│       ├── shaders/                # .fs / .vs / .glsl shaders (e.g., cctv_scanlines.fs)
│       └── audio/                  # .wav / .ogg / .mp3 sounds
├── HandshakeShowcase/              # Showcase demo: procedural characters & logo reveal
└── src/
    ├── Engine.Core/                # Domain-agnostic core & service contracts
    │   ├── Abstractions/           # Interfaces (IEnginePlugin, IGraphicsService, TextureHandle, etc.)
    │   ├── Audio/                  # RaylibAudioService
    │   ├── Events/                 # EventBus & standard EngineEvents
    │   ├── Graphics/               # RaylibGraphicsService (textures, Camera2D, BlendMode, surfaces, shaders)
    │   └── Plugins/                # PluginHost, PluginContext, Settings, MenuRegistry, Input
    ├── Engine.Plugins/             # Default workbench plugins
    │   ├── Workspace/              # WorkspaceExplorerPlugin (file tree + FileSystemWatcher)
    │   ├── Viewport/               # ViewportPlugin (RenderTexture display & 2D gizmos)
    │   ├── Scene/                  # SceneLayersPlugin (Layers + Inspector + C# export)
    │   ├── Timeline/               # UniversalTimelinePlugin (property curves & triggers)
    │   ├── Execution/              # ExecutionPlugin (Roslyn compiler + runtime host)
    │   └── Settings/               # WorkbenchSettingsPlugin & WorkbenchTheme (Studio Graphite / Cobalt)
    └── Engine.App/                 # Desktop host entry point
        ├── Program.cs              # Window init, docking layout, splash state machine, render/UI loop
        ├── StartupSplashController.cs # Async warmup & procedural Verstak logo reveal
        ├── MacOsMenuBridge.cs      # Native macOS NSMenu bar synchronization
        └── ImGuiInputBridge.cs     # Frame input bridge preventing stuck modifiers/mouse
```

---

## Workbench Features & Controls

### Menus (`File`, `View`, `Plugins`, `Settings`)
* On **macOS**, menus live in the native top system bar (`NSMenu`). On other platforms, an ImGui main menu bar fallback is shown automatically.
* **Plugins (`Plugins -> Manage Plugins...`) & Preferences (`Settings -> Workbench Settings...`, `Cmd+,`)**:
  * Enable/disable optional plugins live without restarting.
  * Show/hide individual panels (closing any tab via `x` syncs automatically with `View`).
  * Configure UI scale, Viewport grid & snap size, clear background color, auto-save scenes, or reset dock layout (`View -> Reset Layout`).

### Viewport (`ViewportPlugin`)
* **Modes:** Switch between `Edit` (interactive selection and gizmos visible) and `Play` (clean output preview without editor overlays).
* **2D Gizmos:** Click any layer in the canvas to select and manipulate it directly:
  * `W` / **Move:** Drag the layer body (`Shift` locks X/Y axis; `Alt` temporarily bypasses grid snap).
  * `E` / **Rot:** Drag the top green rotation handle (`Shift` snaps rotation to 15° steps).
  * `R` / **Scale:** Drag corner handles for proportional scaling (`Shift` unlocks non-uniform X/Y stretch) or edge midpoints in `Scale` mode.
  * `G`: Toggle grid overlay.
* **Resolution Presets:** `1280x720`, `960x540`, `640x360`, plus `Fit` scaling (synced with `project.json`).

### Layers & Inspector (`SceneLayersPlugin`)
* Edit `scenes/*.scene.json` documents with **Hierarchy (`ParentId`)**, `ZOrder`, visibility (`V`), lock (`L`), and `Undo`/`Redo` (`Cmd+Z` / `Cmd+Shift+Z`), `Save` (`Cmd+S`).
* Supported layer kinds:
  * `Sprite`: Workspace image with tint, origin presets (`TopLeft`, `Center`, `Bottom`), and optional shader.
  * `Rect`: Colored solid/tinted rectangle (supports rotation & shaders).
  * `Text`: Rendered text label with font size and tint.
  * `Group`: Transform-only parent container (children inherit position, rotation, and scale).
  * `Surface`: Draws a named offscreen render buffer (`BeginSurface("key", w, h)`) created by C# scripts, with full transform + shader post-processing!
* **Workflow Bridges:**
  * `Key Transform at Playhead`: Inserts/updates keyframes in `Timeline` / `project.json` at the current playhead time.
  * `Copy Layer C# Draw`: Copies ready-to-use `IGraphicsService` draw code to clipboard for VS Code.
  * `Export Scene C#...`: Generates `scripts/Generated<SceneName>Scene.cs` containing `DrawAll(IGraphicsService graphics, float time = 0f)`.

### Timeline (`UniversalTimelinePlugin`)
* Data-driven curves (`Linear`, `SmoothStep`, `Step`) and event triggers (`sound`, `method`) stored in `project.json`.
* Scrubbing or playing updates `ExecutionContext` properties and any scene layer carrying a matching `tags.bindPrefix` (e.g. `"bindPrefix": "sprite"`) or `layer.<id>.<prop>` track.

### Project Explorer & Hot-Reload (`WorkspaceExplorerPlugin`)
* Filters: `All`, `Sprites`, `Audio`, `Code` (`.cs`, `.json`, `.fs`, `.vs`, `.glsl`).
* Saving files in VS Code triggers `AssetModifiedEvent`:
  * `.png` / `.jpg` -> invalidates GPU texture cache.
  * `.wav` / `.ogg` / `.mp3` -> invalidates audio buffer cache.
  * `.fs` / `.vs` / `.glsl` -> recompiles shader cache.
  * `.cs` / `project.json` -> recompiles all `scripts/**/*.cs` together and re-initializes active `IExecutableNode`s.

---

## Creating Games & Interactive Projects

### 1. Project Layout

Create a folder anywhere (or copy `SampleProject/`) with:

```text
MyProject/
├── project.json
├── scenes/
│   └── main.scene.json
├── scripts/
│   └── GameControllerNode.cs
└── assets/
    ├── sprites/
    ├── shaders/
    └── audio/
```

Launch it with:

```bash
dotnet run --project src/Engine.App -- --project /path/to/MyProject
```

### 2. Configuring `project.json`

```json
{
  "name": "MyProject",
  "virtualWidth": 1280,
  "virtualHeight": 720,
  "nodes": [
    {
      "nodeId": "main_controller",
      "scriptPath": "scripts/GameControllerNode.cs",
      "enabled": true,
      "properties": {
        "sprite.path": "assets/sprites/hero.png"
      }
    }
  ],
  "timeline": {
    "maxDuration": 4.0,
    "loop": true,
    "propertyTracks": [
      {
        "id": "track_x",
        "displayName": "Hero X",
        "targetProperty": "sprite.x",
        "keyframes": [
          { "time": 0.0, "value": 320.0, "easing": "SmoothStep" },
          { "time": 2.0, "value": 960.0, "easing": "Linear" }
        ]
      }
    ],
    "triggerTracks": [
      {
        "id": "track_methods",
        "displayName": "Signals",
        "triggers": [
          { "time": 2.0, "actionType": "method", "target": "main_controller.OnBeat", "argument": "cue" }
        ]
      }
    ]
  }
}
```

> Note: All `.cs` files under `scripts/` are compiled together into one dynamic assembly so helper classes, generators, and multiple `IExecutableNode` types can reference each other freely. Set `"enabled": false` on a `nodeId` in `project.json` to temporarily disable a node.

### 3. Writing `IExecutableNode` Scripts (Surfaces, Shaders, Camera2D, Blend Modes & `TextureHandle`)

`IExecutableNode` provides four lifecycle hooks (`OnUnload` has a default empty implementation for backward compatibility):
* `OnInit(ExecutionContext context)` — acquire `TextureHandle`s, subscribe to `context.EventBus`, initialize state.
* `OnUpdate(float deltaTime)` — advance simulation and update offscreen surfaces (`BeginSurface` / `EndSurface`).
* `OnDraw(IGraphicsService graphics)` — render with `BeginCamera2D` / `EndCamera2D`, `BeginBlendMode` / `EndBlendMode`, shaders, and zero-alloc `TextureHandle` draws.
* `OnUnload()` *(optional)* — called right before hot-reload / unload (`ScriptAssemblyLoadContext.Unload()`) so nodes can unsubscribe events and release offscreen surfaces (`graphics.ReleaseSurface(...)`).

```csharp
using System.Numerics;
using Engine.Core.Abstractions;
using Raylib_cs;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace MyProject.Scripts;

public sealed class MonitorDemoNode : IExecutableNode
{
    private ExecutionContext? _ctx;
    private TextureHandle _tileset;
    private float _pulse;

    public string NodeId => "monitor_demo";

    public void OnInit(ExecutionContext context)
    {
        _ctx = context;
        _tileset = context.Graphics.GetTextureHandle("assets/sprites/tileset.png");
    }

    // Callable from Timeline triggerTracks: actionType = "method", target = "monitor_demo.OnCue"
    public void OnCue(string? arg)
    {
        _pulse = 1.0f;
    }

    public void OnUpdate(float deltaTime)
    {
        _pulse = MathF.Max(0f, _pulse - deltaTime * 2f);

        // Prepare a procedural offscreen surface that any "Surface" layer in scenes/*.scene.json can display!
        if (_ctx != null)
        {
            _ctx.Graphics.BeginSurface("cctv_feed", 320, 180);
            _ctx.Graphics.Clear(new Color((byte)15, (byte)24, (byte)20, (byte)255));
            float x = 160f + MathF.Sin(_ctx.CurrentTime * 2f) * 90f;
            _ctx.Graphics.DrawCircle(new Vector2(x, 90f), 26f + _pulse * 8f, Color.Lime);
            _ctx.Graphics.EndSurface();
        }
    }

    public void OnDraw(IGraphicsService graphics)
    {
        if (_ctx == null) return;

        // Hardware 2D Camera + O(1) zero-alloc TextureHandle tile drawing
        graphics.BeginCamera2D(target: new Vector2(128f, 128f), offset: new Vector2(480f, 270f), zoom: 2.0f);
        if (_tileset.IsValid)
        {
            graphics.DrawTexture(_tileset, Vector2.Zero, srcRect: new Rectangle(0f, 0f, 16f, 16f), tint: Color.White);
        }

        // Additive lighting pass via BlendMode
        graphics.BeginBlendMode(BlendMode.Additive);
        graphics.DrawCircle(new Vector2(128f, 128f), 48f, new Color((byte)255, (byte)190, (byte)100, (byte)120));
        graphics.EndBlendMode();
        graphics.EndCamera2D();

        // Screen-space draw of a surface with a workspace fragment shader
        graphics.BeginShader("assets/shaders/cctv_scanlines.fs");
        graphics.SetShaderFloat("assets/shaders/cctv_scanlines.fs", "uTime", _ctx.CurrentTime);
        graphics.SetShaderFloat("assets/shaders/cctv_scanlines.fs", "uIntensity", 1.0f);
        graphics.SetShaderVec2("assets/shaders/cctv_scanlines.fs", "uResolution", new Vector2(320f, 180f));
        graphics.DrawSurface("cctv_feed", new Vector2(180f, 110f), new Vector2(320f, 180f), Color.White, origin: Vector2.Zero);
        graphics.EndShader();
    }

    public void OnUnload()
    {
        _ctx?.Graphics.ReleaseSurface("cctv_feed");
    }
}
```

### 4. Writing Custom Fragment Shaders (`.fs`)

Raylib 5.5 uses GLSL `#version 330` fragment shaders with standard inputs (`fragTexCoord`, `fragColor`, `texture0`, `colDiffuse`):

```glsl
#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

uniform float uTime = 0.0;
uniform float uIntensity = 1.0;
uniform vec2 uResolution = vec2(960.0, 540.0);

out vec4 finalColor;

void main()
{
    vec4 texel = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
    finalColor = vec4(texel.rgb * (1.0 - 0.1 * uIntensity), texel.a);
}
```

Drop `.fs` files into `assets/shaders/` and select them directly inside **Inspector -> Shader (.fs)** or bind them via `IGraphicsService.BeginShader(...)`.

### 5. Performance Guidance for Large Simulations
If you simulate thousands of autonomous entities (e.g. 10,000+ agents in a single `IExecutableNode`):
* Store state in **flat arrays of primitives / blittable `struct`s** (Structure of Arrays) with `int` IDs instead of managed `class` references to eliminate GC pauses.
* Use **Round-Robin time-slicing** for slow AI/economic layers and a pre-allocated transaction ring buffer instead of calling `EventBus.Publish<T>()` for every micro-interaction.

---

## Creating Custom Workbench Plugins (`IEnginePlugin`)

Plugins extend the workbench itself (custom inspectors, generators, asset importers, profilers, telemetry windows) without touching `Engine.Core`.

### 1. Core Contracts (`PluginContext`)

Every plugin receives a [`PluginContext`](src/Engine.Core/Plugins/PluginContext.cs) in `OnLoaded`:

| Property | Interface | Purpose |
| :--- | :--- | :--- |
| `Graphics` | [`IGraphicsService`](src/Engine.Core/Abstractions/IGraphicsService.cs) | 2D draw calls, `TextureHandle` O(1) cache, `Camera2D`, `BlendMode`, surfaces, shaders |
| `Audio` | [`IAudioService`](src/Engine.Core/Abstractions/IAudioService.cs) | Sound playback & cache invalidation |
| `Input` | [`IInputService`](src/Engine.Core/Abstractions/IInputService.cs) | Keyboard, mouse, virtual viewport mouse coordinates |
| `Workspace` | [`IWorkspaceService`](src/Engine.Core/Abstractions/IWorkspaceService.cs) | Project root, file enumeration, relative/absolute path conversion |
| `EventBus` | [`IEventBus`](src/Engine.Core/Abstractions/IEventBus.cs) | Typed event communication between plugins and core |
| `Settings` | [`ISettingsService`](src/Engine.Core/Abstractions/ISettingsService.cs) | Persistent JSON user preferences |
| `Menu` | [`IMenuService`](src/Engine.Core/Abstractions/IMenuService.cs) | Register macOS top menu / fallback menu commands & shortcuts |
| `Plugins` | [`IPluginManager`](src/Engine.Core/Abstractions/IPluginManager.cs) | Query/toggle plugin enabled state and panel window visibility |
| `Runtime` | [`ExecutionContext`](src/Engine.Core/Abstractions/ExecutionContext.cs) | Shared runtime properties & active simulation state |

### 2. Example Plugin Implementation

Create a new class in `src/Engine.Plugins/<YourPlugin>/MyToolPlugin.cs`:

```csharp
using System.Numerics;
using Engine.Core.Abstractions;
using Engine.Core.Events;
using Engine.Core.Plugins;
using ImGuiNET;

namespace Engine.Plugins.Custom;

public sealed class TelemetryLogPlugin : IEnginePlugin
{
    private PluginContext? _context;
    private readonly List<string> _logs = new();

    public string Id => "custom.telemetry_log";
    public string DisplayName => "Telemetry Log";

    public void OnLoaded(PluginContext context)
    {
        _context = context;
        _context.EventBus.Subscribe<EngineLogEvent>(OnEngineLog);

        // Register a toggle entry in the View menu
        _context.Menu.Register(new MenuItemDefinition(
            Id: "telemetry.toggle_window",
            Path: "View/Telemetry Log",
            Callback: () =>
            {
                bool cur = _context.Plugins?.IsWindowVisible(Id) ?? true;
                _context.Plugins?.SetWindowVisible(Id, !cur);
            },
            IsChecked: () => _context.Plugins?.IsWindowVisible(Id) ?? true,
            Order: 70));
    }

    public void OnUpdate(float deltaTime)
    {
    }

    public void OnRenderUI()
    {
        if (_context == null) return;

        bool visible = _context.Plugins?.IsWindowVisible(Id) ?? true;
        if (!visible) return;

        bool keepOpen = visible;
        if (ImGui.Begin(DisplayName, ref keepOpen))
        {
            if (ImGui.Button("Clear")) _logs.Clear();
            ImGui.Separator();

            ImGui.BeginChild("TelemetryScroll", Vector2.Zero, ImGuiChildFlags.None);
            for (int i = _logs.Count - 1; i >= 0; i--)
            {
                ImGui.TextWrapped(_logs[i]);
            }
            ImGui.EndChild();
        }
        ImGui.End();

        if (!keepOpen)
        {
            _context.Plugins?.SetWindowVisible(Id, false);
        }
    }

    public void OnUnloaded()
    {
        if (_context != null)
        {
            _context.Menu.UnregisterByPrefix("telemetry.");
            _context.EventBus.Unsubscribe<EngineLogEvent>(OnEngineLog);
        }
    }

    private void OnEngineLog(EngineLogEvent evt)
    {
        _logs.Add($"[{evt.Level}] {evt.Source}: {evt.Message}");
        if (_logs.Count > 200) _logs.RemoveAt(0);
    }
}
```

### 3. Registering Your Plugin

Register it in [`src/Engine.App/Program.cs`](src/Engine.App/Program.cs) before `pluginHost.LoadAll()`:

```csharp
pluginHost.RegisterPlugin(new TelemetryLogPlugin(), canDisable: true, defaultWindowVisible: false);
```

Your plugin immediately appears in **Settings -> Plugins**, respects `Enable/Disable` lifecycle hooks (`OnLoaded` / `OnUnloaded`), persists its state across launches, and integrates cleanly into the macOS menu bar and ImGui docking layout.
