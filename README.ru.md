# WorkbenchEngine (Минималистичный 2D-верстак и рантайм)

> **Язык:** [English (README.md)](README.md) | Русский

**WorkbenchEngine** — лёгкий, открытый и доменно-независимый 2D-верстак и рантайм-движок на **.NET 8 / C# 12**, **Raylib-cs**, **ImGui.NET** и **Roslyn**.

Ядро движка полностью изолировано от игровой предметной области: оно **ничего не знает** о персонажах, диалогах, жанрах или правилах конкретной игры. Вместо этого движок даёт низкоуровневые примитивы ОС и GPU, горячую перезагрузку ассетов и C#-скриптов, плагинный интерфейс, редактор 2D-слоёв во вьюпорте, шейдеры, офскрин-поверхности и универсальную временную шкалу.

---

## Архитектурные принципы

1. **Немое ядро (`Engine.Core`):** управляет только окном, вводом, звуковыми буферами, 2D-текстурами (`TextureHandle`), аппаратной 2D-камерой (`Camera2D`), режимами смешивания (`BlendMode`), офскрин-поверхностями (`FBO`), фрагментными шейдерами, настройками, командами меню, файловой системой проекта и шиной событий `EventBus`.
2. **Плагинный интерфейс (`Engine.Plugins`):** каждый инструмент верстака (`Project`, `Layers`, `Inspector`, `Viewport`, `Timeline`, `Execution`, `Settings`) реализован как независимый плагин `IEnginePlugin`, общающийся только через контракты ядра и события. Плагины можно включать и выключать на лету.
3. **Data-Driven & Code-Agnostic:** сцены хранятся в прозрачном JSON (`scenes/*.scene.json`), а любая игровая логика или симуляция — это внешний C#-скрипт (`scripts/**/*.cs`), реализующий интерфейс `IExecutableNode`.
4. **Связка с VS Code:** верстак не содержит встроенного текстового редактора. Код, JSON и GLSL-шейдеры правятся во внешнем VS Code, а верстак мгновенно подхватывает изменения при сохранении файла.

---

## Технологический стек

* **Рантайм:** .NET 8 / C# 12 (`RollForward: Major`, macOS Apple Silicon ARM64 и десктоп)
* **Графика и звук:** `Raylib-cs` 7.0.1 (OpenGL 4.1 / Metal на macOS, шейдеры GLSL `#version 330`, Core Audio / miniaudio)
* **Интерфейс верстака:** `ImGui.NET` 1.91.6.1 + `rlImGui-cs` 3.2.0 (докинг-окна)
* **Динамическая компиляция:** `Microsoft.CodeAnalysis.CSharp` 4.12.0 (Roslyn + выгружаемый `ScriptAssemblyLoadContext`)
* **Интеграция с macOS:** нативное верхнее меню `NSMenu` (`MacOsMenuBridge`) и устойчивый опрос модификаторов/мыши через AppKit (`MacOsInputBridge`)

---

## Быстрый старт

### Сборка

```bash
dotnet build WorkbenchEngine.sln
```

### Запуск демо-проектов

```bash
dotnet run --project src/Engine.App -- --project SampleProject
dotnet run --project src/Engine.App -- --project HandshakeShowcase
```

### Аргументы командной строки

| Аргумент | Описание | По умолчанию |
| :--- | :--- | :--- |
| `--project <path>` | Путь к директории проекта с файлом `project.json` | `./SampleProject` |
| `--no-splash` | Пропустить стартовую анимацию сплеш-экрана и сразу открыть верстак | `false` |
| `--smoke-frames <N>` | Отрисовать `N` кадров и корректно завершить процесс (для автопроверок) | Без ограничения |

---

## Структура решения

```text
WorkbenchEngine/
├── WorkbenchEngine.sln
├── SampleProject/                  # Изолированный тестовый проект
│   ├── project.json                # Разрешение, активные узлы, треки таймлайна
│   ├── scenes/
│   │   └── main.scene.json         # Иерархическая сцена 2D-слоёв
│   ├── scripts/
│   │   └── MovingSpriteNode.cs     # Пример скрипта IExecutableNode
│   └── assets/
│       ├── sprites/                # Текстуры (.png / .jpg / .jpeg)
│       ├── shaders/                # Шейдеры (.fs / .vs / .glsl, например cctv_scanlines.fs)
│       └── audio/                  # Звуки (.wav / .ogg / .mp3)
├── HandshakeShowcase/              # Демо-сцена с процедурными персонажами и логотипом
└── src/
    ├── Engine.Core/                # Немое ядро и контракты
    │   ├── Abstractions/           # Интерфейсы сервисов, TextureHandle и IExecutableNode
    │   ├── Audio/                  # RaylibAudioService
    │   ├── Events/                 # EventBus и базовые события
    │   ├── Graphics/               # RaylibGraphicsService (текстуры, Camera2D, BlendMode, поверхности, шейдеры)
    │   └── Plugins/                # PluginHost, PluginContext, Settings, MenuRegistry, Input
    ├── Engine.Plugins/             # Стандартный набор плагинов верстака
    │   ├── Workspace/              # WorkspaceExplorerPlugin (дерево файлов + FileSystemWatcher)
    │   ├── Viewport/               # ViewportPlugin (вывод RenderTexture + 2D-гизмо)
    │   ├── Scene/                  # SceneLayersPlugin (Layers + Inspector + экспорт в C#)
    │   ├── Timeline/               # UniversalTimelinePlugin (ключевые кадры и триггеры)
    │   ├── Execution/              # ExecutionPlugin (Roslyn-компилятор и хост узлов)
    │   └── Settings/               # WorkbenchSettingsPlugin и WorkbenchTheme (Studio Graphite / Cobalt)
    └── Engine.App/                 # Точка входа
        ├── Program.cs              # Инициализация окна, докинг-лейаут, сплеш-машина и цикл
        ├── StartupSplashController.cs # Фоновый прогрев и процедурная анимация логотипа Verstak
        ├── MacOsMenuBridge.cs      # Нативное верхнее меню macOS NSMenu
        └── ImGuiInputBridge.cs     # Мост ввода с защитой от залипания клавиш
```

---

## Работа в верстаке

### Системное меню (`File`, `View`, `Plugins`, `Settings`)
* На **macOS** меню верстака вынесено в верхнюю строку системы (`NSMenu`). На других ОС автоматически показывается меню ImGui.
* **Плагины (`Plugins -> Manage Plugins...`) и настройки (`Settings -> Workbench Settings...`, `Cmd+,`):**
  * Включение и отключение необязательных плагинов без перезапуска.
  * Показ/скрытие любых панелей (кнопка закрытия `x` на вкладке синхронизируется с меню `View`).
  * Масштаб интерфейса, шаг сетки и привязки (`Snap`), фоновый цвет холста, автосохранение сцен и сброс раскладки (`View -> Reset Layout`).

### Вьюпорт (`ViewportPlugin`)
* **Режимы:** `Edit` (интерактивное выделение и гизмо) и `Play` (чистый предпросмотр без рамок редактора).
* **Интерактивные 2D-гизмо:** клик по объекту в окне выделяет верхний видимый слой:
  * `W` / **Move:** перетаскивание за тело слоя (`Shift` — фиксация оси X/Y; `Alt` — временное отключение `Snap`).
  * `E` / **Rot:** вращение за верхнюю зелёную ручку (`Shift` — шаг 15°).
  * `R` / **Scale:** угловые маркеры масштабируют пропорционально (`Shift` — свободное растяжение по X/Y), боковые маркеры — по одной оси.
  * `G`: показать/скрыть сетку.
* **Пресеты разрешения:** `1280x720`, `960x540`, `640x360` и переключатель `Fit` (синхронизируется с `project.json`).

### Слои и Инспектор (`SceneLayersPlugin`)
* Работа с документами `scenes/*.scene.json`: иерархия **`ParentId`** (дочерние слои наследуют позицию, поворот и масштаб родителя), `ZOrder`, видимость (`V`), блокировка (`L`), `Undo`/`Redo` (`Cmd+Z` / `Cmd+Shift+Z`), `Save` (`Cmd+S`).
* Типы слоёв:
  * `Sprite`: спрайт из `assets/` с тинтом, пивотом (`TopLeft`, `Center`, `Bottom`) и шейдером.
  * `Rect`: цветной прямоугольник (поддерживает поворот и шейдеры).
  * `Text`: текстовая метка с размером шрифта и цветом.
  * `Group`: контейнер-трансформ без собственного рендера (удобен как корень для составных объектов).
  * `Surface`: выводит именованный офскрин-буфер (`BeginSurface("key", w, h)`), отрисованный C#-скриптом, с возможностью вращать, масштабировать и накладывать на него пост-шейдер!
* **Рутину — в код и на таймлайн:**
  * `Key Transform at Playhead` — записывает текущие координаты/поворот/масштаб слоя как ключевые кадры в `Timeline` / `project.json`.
  * `Copy Layer C# Draw` — копирует готовый C#-код вызова `IGraphicsService` для вставки в VS Code.
  * `Export Scene C#...` — генерирует класс `scripts/Generated<ИмяСцены>Scene.cs` со статическим методом `DrawAll(IGraphicsService graphics, float time = 0f)`.

### Таймлайн (`UniversalTimelinePlugin`)
* Кривые свойств (`Linear`, `SmoothStep`, `Step`) и триггеры (`sound`, `method`) из `project.json`.
* Проигрывание или скраббинг передают значения в `ExecutionContext` и автоматически анимируют слои сцены с совпадающим тегом `tags.bindPrefix` (например, `"bindPrefix": "sprite"`) или ключом `layer.<id>.<prop>`.

### Обозреватель проекта (`WorkspaceExplorerPlugin`)
* Фильтры: `All`, `Sprites`, `Audio`, `Code` (`.cs`, `.json`, `.fs`, `.vs`, `.glsl`).
* При сохранении файлов в VS Code срабатывает `AssetModifiedEvent`:
  * `.png` / `.jpg` -> сброс и перезагрузка текстуры в GPU.
  * `.wav` / `.ogg` / `.mp3` -> сброс кэша звука.
  * `.fs` / `.vs` / `.glsl` -> горячая перекомпиляция шейдера.
  * `.cs` / `project.json` -> перекомпиляция всех `scripts/**/*.cs` и перезапуск `IExecutableNode`.

---

## Создание игр и интерактивных проектов

### 1. Структура папки проекта

Создайте папку проекта (или скопируйте `SampleProject/`):

```text
MyGame/
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

Запуск с вашим проектом:

```bash
dotnet run --project src/Engine.App -- --project /path/to/MyGame
```

### 2. Конфигурация `project.json`

```json
{
  "name": "MyGame",
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
          { "time": 2.0, "actionType": "method", "target": "main_controller.OnCue", "argument": "tick" }
        ]
      }
    ]
  }
}
```

> Все `.cs`-файлы внутри `scripts/` компилируются Roslyn **в одну сборку** — модули, хелперы и генераторы могут свободно ссылаться друг на друга. Чтобы временно отключить узел, задайте `"enabled": false` для его `nodeId`.

### 3. Скрипты `IExecutableNode` (Surface, шейдеры, Camera2D, BlendMode и `TextureHandle`)

Интерфейс `IExecutableNode` предоставляет 4 метода жизненного цикла (`OnUnload` имеет пустую реализацию по умолчанию для обратной совместимости):
* `OnInit(ExecutionContext context)` — получение `TextureHandle`, подписка на `context.EventBus`, инициализация состояния.
* `OnUpdate(float deltaTime)` — шаг симуляции и обновление офскрин-поверхностей (`BeginSurface` / `EndSurface`).
* `OnDraw(IGraphicsService graphics)` — рендер с `BeginCamera2D` / `EndCamera2D`, `BeginBlendMode` / `EndBlendMode`, шейдерами и zero-alloc вызовами `DrawTexture(TextureHandle, ...)`.
* `OnUnload()` *(опционально)* — вызывается перед выгрузкой/горячей перезагрузкой (`ScriptAssemblyLoadContext.Unload()`), чтобы отписаться от событий и освободить поверхности (`graphics.ReleaseSurface(...)`).

```csharp
using System.Numerics;
using Engine.Core.Abstractions;
using Raylib_cs;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace MyGame.Scripts;

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

    // Вызывается триггером таймлайна: actionType = "method", target = "monitor_demo.OnCue"
    public void OnCue(string? arg)
    {
        _pulse = 1.0f;
    }

    public void OnUpdate(float deltaTime)
    {
        _pulse = MathF.Max(0f, _pulse - deltaTime * 2f);

        // Рендер во внеэкранный буфер "cctv_feed" — его можно привязать к слою типа Surface в редакторе
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

        // Аппаратная 2D-камера и быстрый zero-alloc рендер тайлов через TextureHandle
        graphics.BeginCamera2D(target: new Vector2(128f, 128f), offset: new Vector2(480f, 270f), zoom: 2.0f);
        if (_tileset.IsValid)
        {
            graphics.DrawTexture(_tileset, Vector2.Zero, srcRect: new Rectangle(0f, 0f, 16f, 16f), tint: Color.White);
        }

        // Проход освещения через BlendMode
        graphics.BeginBlendMode(BlendMode.Additive);
        graphics.DrawCircle(new Vector2(128f, 128f), 48f, new Color((byte)255, (byte)190, (byte)100, (byte)120));
        graphics.EndBlendMode();
        graphics.EndCamera2D();

        // Вывод буфера в экранных координатах через фрагментный шейдер
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

### 4. Фрагментные шейдеры (`.fs`)

Raylib 5.5 использует 2D-шейдеры GLSL `#version 330` со стандартными входами (`fragTexCoord`, `fragColor`, `texture0`, `colDiffuse`):

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

Положите `.fs`-файл в `assets/shaders/` и выберите его прямо в **Inspector -> Shader (.fs)** или вызывайте через `IGraphicsService.BeginShader(...)`.

### 5. Масштабирование симуляций
При моделировании тысяч автономных агентов внутри `IExecutableNode`:
* Используйте плоские массивы примитивов и blittable-`struct` (Structure of Arrays) с целочисленными ID вместо графа ссылочных объектов `class` — это полностью убирает паузы GC.
* Распределяйте тяжелые расчёты по кадрам (Round-Robin Time-Slicing), а внутренние транзакции держите в предвыделенном кольцевом буфере вместо публикации каждого микрособытия в `EventBus`.

---

## Создание плагинов верстака (`IEnginePlugin`)

Плагины расширяют сам верстак (генераторы, кастомные инспекторы, панели профилирования, импортеры ассетов), не меняя `Engine.Core`.

### 1. Контракты [`PluginContext`](src/Engine.Core/Plugins/PluginContext.cs)

| Свойство | Интерфейс | Назначение |
| :--- | :--- | :--- |
| `Graphics` | [`IGraphicsService`](src/Engine.Core/Abstractions/IGraphicsService.cs) | Отрисовка 2D-примитивов, `TextureHandle` (O(1)), `Camera2D`, `BlendMode`, поверхности, шейдеры |
| `Audio` | [`IAudioService`](src/Engine.Core/Abstractions/IAudioService.cs) | Воспроизведение и инвалидация звуков |
| `Input` | [`IInputService`](src/Engine.Core/Abstractions/IInputService.cs) | Клавиатура, мышь, координаты во вьюпорте |
| `Workspace` | [`IWorkspaceService`](src/Engine.Core/Abstractions/IWorkspaceService.cs) | Путь к проекту, сканирование файлов, разрешение путей |
| `EventBus` | [`IEventBus`](src/Engine.Core/Abstractions/IEventBus.cs) | Типизированная публикация и подписка на события |
| `Settings` | [`ISettingsService`](src/Engine.Core/Abstractions/ISettingsService.cs) | Сохранение настроек в JSON |
| `Menu` | [`IMenuService`](src/Engine.Core/Abstractions/IMenuService.cs) | Регистрация пунктов меню и хоткеев в верхней панели macOS / ImGui |
| `Plugins` | [`IPluginManager`](src/Engine.Core/Abstractions/IPluginManager.cs) | Управление активностью плагинов и видимостью окон |
| `Runtime` | [`ExecutionContext`](src/Engine.Core/Abstractions/ExecutionContext.cs) | Общие runtime-свойства и состояние исполнения |

### 2. Пример собственного плагина

Создайте класс в `src/Engine.Plugins/<YourPlugin>/TelemetryLogPlugin.cs`:

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

        // Регистрация переключателя окна в меню View
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

### 3. Регистрация плагина

Подключите его в [`src/Engine.App/Program.cs`](src/Engine.App/Program.cs) перед `pluginHost.LoadAll()`:

```csharp
pluginHost.RegisterPlugin(new TelemetryLogPlugin(), canDisable: true, defaultWindowVisible: false);
```

После сборки плагин автоматически появится в `Settings -> Plugins`, будет корректно выгружаться/загружаться (`OnLoaded`/`OnUnloaded`), сохранять состояние видимости между запусками и отображаться в системном меню macOS.

---

## Лицензия

Проект распространяется под лицензией [MIT License](LICENSE).
