# VerstakEngine (Минималистичный 2D-верстак и рантайм)

> **Язык:** [English (README.md)](README.md) | Русский

**VerstakEngine** — лёгкий, открытый и доменно-независимый 2D-верстак и рантайм-движок на **.NET 8 / C# 12**, **Raylib-cs**, **ImGui.NET** и **Roslyn**.

Ядро движка полностью изолировано от игровой предметной области: оно **ничего не знает** о персонажах, диалогах, жанрах или правилах конкретной игры. Вместо этого движок даёт низкоуровневые примитивы ОС и GPU, горячую перезагрузку ассетов и C#-скриптов, плагинный интерфейс, редактор 2D-слоёв во вьюпорте, шейдеры, офскрин-поверхности и универсальную временную шкалу.

---

## Архитектурные принципы

1. **Немое ядро (`Engine.Core`):** в ядре строго запрещены понятия игрового домена (сущности, координаты сетки, тайлы, инвентари, коллизии, физические тела, персонажи). `Engine.Core` оперирует только аппаратно-системными абстракциями: окном, вводом, GPU-батчингом плоской геометрии (`IQuadBatchService`), 2D-текстурами (`TextureHandle`), виртуальной проекцией и пиксельным снэппингом (`IVirtualDisplayService`), многопроходной композицией FBO (`ISurfaceCompositor`), аппаратной `Camera2D` и `BlendMode`, GLSL-шейдерами, полифоническим аудиомикшером по шинам (`IAudioMixerService`), скоупами выгрузки сборок (`IScriptScope`), настройками, меню, рабочей областью и шиной событий `EventBus`.
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
    │   ├── Abstractions/           # Интерфейсы сервисов (IQuadBatchService, IVirtualDisplayService, ISurfaceCompositor, IAudioMixerService, IScriptScope, IExecutableNode)
    │   ├── Audio/                  # RaylibAudioMixerService и RaylibAudioService
    │   ├── Events/                 # EventBus и базовые события
    │   ├── Graphics/               # RaylibGraphicsService, RaylibQuadBatchService, VirtualDisplayService, SurfaceCompositor
    │   └── Plugins/                # PluginHost, PluginContext, ScriptAssemblyScope, Settings, MenuRegistry, Input
    ├── Engine.Plugins/             # Стандартный набор плагинов верстака
    │   ├── Workspace/              # WorkspaceExplorerPlugin (дерево файлов + FileSystemWatcher)
    │   ├── Viewport/               # ViewportPlugin (матрица виртуального дисплея, Point/Bilinear, метрики Draw Calls/Quads, 2D-гизмо)
    │   ├── Scene/                  # SceneLayersPlugin (Layers + Inspector + экспорт в C#)
    │   ├── Timeline/               # UniversalTimelinePlugin (ключевые кадры и триггеры)
    │   ├── Execution/              # ExecutionPlugin (Roslyn emit/load, ScriptAssemblyScope teardown, замер Hot-Reload)
    │   └── Settings/               # WorkbenchSettingsPlugin и WorkbenchTheme (Studio Graphite / Cobalt)
    └── Engine.App/                 # Точка входа
        ├── Assets/VerstakAppIcon.png  # Встроенная иконка 512x512 для macOS Dock и окна приложения
        ├── AppDockIconBridge.cs       # Привязка иконки к NSApplication Dock и Raylib-окну
        ├── Program.cs                 # Инициализация окна, докинг-лейаут, сплеш-машина и цикл
        ├── StartupSplashController.cs # Фоновый прогрев и процедурная анимация логотипа Verstak
        ├── MacOsMenuBridge.cs         # Нативное верхнее меню macOS NSMenu
        └── ImGuiInputBridge.cs        # Мост ввода с защитой от залипания клавиш
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
* **Виртуальный дисплей и масштабирование (`IVirtualDisplayService`):**
  * Режимы скейлинга (`1:1 None`, `Stretch`, `Fit`, `Integer`) на базе `GetPresentationMatrix` и обратного маппинга мыши `ScreenToVirtual`. Фильтр автоматически переключается на `TextureFilter.Point` в режимах `IntegerScale`/`None` и на `TextureFilter.Bilinear` для `FitLetterbox`/`Stretch`.
  * Режимы пиксельного снэппинга (`Snap Off`, `Floor`, `Round`) для координат `Camera2D` и `Display.SnapCoordinates(...)`.
  * Пресеты разрешения: `480x270`, `640x360`, `960x540`, `1280x720`, `1920x1080` и `Native` (синхронизируется с `project.json`).
  * В статус-баре и `Overlay`-панели отображается статистика батчера: `Draw Calls` и `Quads` за кадр.

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

`project.json` поддерживает как базовые `virtualWidth` / `virtualHeight`, так и опциональные секции `display` и `audioBuses` (при их отсутствии сохраняются дефолты для полной обратной совместимости):

```json
{
  "name": "MyGame",
  "virtualWidth": 1280,
  "virtualHeight": 720,
  "display": {
    "virtualWidth": 480,
    "virtualHeight": 270,
    "scalingMode": "IntegerScale",
    "snapping": "Round"
  },
  "audioBuses": {
    "Master": 1.0,
    "Music": 0.85,
    "Atmosphere": 0.70,
    "Effects": 1.0,
    "Interface": 1.0
  },
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

### 3. Скрипты `IExecutableNode` (примитивы Core v2 и детерминированный Hot-Reload)

Интерфейс `IExecutableNode` предоставляет 4 метода жизненного цикла (`OnUnload` имеет пустую реализацию по умолчанию):
* `OnInit(ExecutionContext context)` — получение `TextureHandle`, создание поверхностей `context.Compositor.EnsureSurface(...)`, регистрация ресурсов в `context.Scope.TrackSurface(...)` и `context.Scope.Subscribe<T>(...)`, запуск фоновых потоков аудио.
* `OnUpdate(float deltaTime)` — шаг симуляции (на плоских `SoA`-массивах) и обновление состояния.
* `OnDraw(IGraphicsService graphics)` — батчинг десятков тысяч спрайтов через `context.Batcher` (`0 B` аллокаций), работа с `Camera2D` и многопроходным композитингом `context.Compositor`.
* `OnUnload()` *(опционально)* — вызывается при очистке [`ScriptAssemblyScope.Dispose`](src/Engine.Core/Plugins/ScriptAssemblyScope.cs) до `AssemblyLoadContext.Unload()` (подписки скоупа отписываются автоматически, а tracked FBO детерминированно освобождаются).

#### Подсистемы `ExecutionContext`

| Свойство | Интерфейс | Возможности |
| :--- | :--- | :--- |
| `Graphics` | [`IGraphicsService`](src/Engine.Core/Abstractions/IGraphicsService.cs) | Базовый 2D-рендер, `TextureHandle` (`O(1)`), `Camera2D`, `BlendMode`, `SetShaderSurface`, `SetShaderTexture` |
| `Batcher` | [`IQuadBatchService`](src/Engine.Core/Abstractions/IQuadBatchService.cs) | Аппаратный VAO/VBO/EBO батчер на 16 384 квада за draw call (`Begin`, `DrawQuad`, `DrawQuadRotated`, `Flush`, `End`, `0 B` аллокаций) |
| `Display` | [`IVirtualDisplayService`](src/Engine.Core/Abstractions/IVirtualDisplayService.cs) | `VirtualResolution`, `ScalingMode`, `Snapping`, `SnapCoordinates`, `ScreenToVirtual`, `GetPresentationMatrix` |
| `Compositor` | [`ISurfaceCompositor`](src/Engine.Core/Abstractions/ISurfaceCompositor.cs) | Кэш именованных FBO (`EnsureSurface`, `BeginTarget`, `EndTarget`, `ExecutePass(PassDescriptor)`, `PresentToScreen`) |
| `Audio` | [`IAudioMixerService`](src/Engine.Core/Abstractions/IAudioMixerService.cs) | Шины `AudioBus` (`Master`, `Music`, `Atmosphere`, `Effects`, `Interface`), полифонический `PlayOneShot(..., pitchVariation)`, `PlayStream`/`StopStream`, `CrossfadeMusic` |
| `Scope` / `Events` | [`IScriptScope`](src/Engine.Core/Abstractions/IScriptScope.cs) / [`IScopedEventBus`](src/Engine.Core/Abstractions/IScriptScope.cs) | `Scope.Subscribe<T>` и `Scope.TrackSurface` с автоматической очисткой при Hot-Reload |

```csharp
using System.Numerics;
using Engine.Core.Abstractions;
using Engine.Core.Events;
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

        // Создаём переиспользуемые FBO-поверхности и привязываем к скоупу для автоочистки
        context.Compositor.EnsureSurface("diffuse", 480, 270);
        context.Compositor.EnsureSurface("lightmap", 480, 270);
        context.Scope.TrackSurface("diffuse");
        context.Scope.TrackSurface("lightmap");

        // Скоуп-подписка автоматически снимается перед AssemblyLoadContext.Unload()
        context.Scope.Subscribe<ViewportResolutionChangedEvent>(evt =>
        {
            context.Compositor.EnsureSurface("diffuse", evt.VirtualWidth, evt.VirtualHeight);
            context.Compositor.EnsureSurface("lightmap", evt.VirtualWidth, evt.VirtualHeight);
        });

        context.Audio.PlayStream("rain", "assets/audio/rain.wav", AudioBus.Atmosphere, targetVolume: 0.6f, fadeInSec: 1.5f);
    }

    // Вызывается триггером таймлайна: actionType = "method", target = "monitor_demo.OnCue"
    public void OnCue(string? arg)
    {
        _pulse = 1.0f;
        _ctx?.Audio.PlayOneShot("assets/audio/chime.wav", AudioBus.Effects, volume: 0.8f, pitchVariation: 0.08f);
    }

    public void OnUpdate(float deltaTime)
    {
        _pulse = MathF.Max(0f, _pulse - deltaTime * 2f);
    }

    public void OnDraw(IGraphicsService graphics)
    {
        if (_ctx == null) return;

        // Проход 1: батчинг геометрии в "diffuse" с пиксельным снэппингом Camera2D (0 B аллокаций)
        _ctx.Compositor.BeginTarget("diffuse", new Color((byte)14, (byte)16, (byte)22, (byte)255));
        graphics.BeginCamera2D(target: new Vector2(128.37f, 128.81f), offset: new Vector2(240f, 135f));

        _ctx.Batcher.Begin(_tileset);
        for (int i = 0; i < 400; i++)
        {
            var pos = new Vector2((i % 20) * 16f, (i / 20) * 16f);
            _ctx.Batcher.DrawQuad(pos, new Vector2(16f, 16f), new Rectangle(0f, 0f, 16f, 16f), Color.White);
        }
        _ctx.Batcher.End();

        graphics.EndCamera2D();
        _ctx.Compositor.EndTarget();

        // Проход 2: карта освещения и финальный шейдерный композитинг в основной буфер
        _ctx.Compositor.BeginTarget("lightmap", new Color((byte)45, (byte)50, (byte)75, (byte)255));
        graphics.DrawCircle(new Vector2(240f, 135f), 84f + _pulse * 24f, new Color((byte)255, (byte)210, (byte)150, (byte)255));
        _ctx.Compositor.EndTarget();

        _ctx.Compositor.ExecutePass(new PassDescriptor
        {
            SourceSurface = "diffuse",
            TargetSurface = null,
            ShaderPath = "assets/shaders/night_light.fs",
            BlendMode = BlendMode.Alpha,
            SetupUniforms = gfx =>
            {
                gfx.SetShaderSurface("assets/shaders/night_light.fs", "uLightmap", "lightmap");
                gfx.SetShaderFloat("assets/shaders/night_light.fs", "uTime", _ctx.CurrentTime);
            }
        });
    }

    public void OnUnload()
    {
        _ctx?.Audio.StopStream("rain", fadeOutSec: 0.2f);
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
При моделировании десятков тысяч автономных элементов или частиц внутри `IExecutableNode`:
* Используйте плоские массивы примитивов и blittable-`struct` (Structure of Arrays) с целочисленными ID вместо графа ссылочных объектов `class` — это обеспечивает строго **`0 B` аллокаций GC за кадр**.
* Подавайте геометрию через `context.Batcher` (`IQuadBatchService`) — до **16 384 квадов на один draw call** (на Apple M4 подтверждены **65 000 квадов при ~370 FPS / 0 B GC alloc**).
* Распределяйте тяжелые расчёты по кадрам (Round-Robin Time-Slicing), а внутренние транзакции держите в предвыделенном кольцевом буфере вместо публикации каждого микрособытия в `EventBus`.

---

## Создание плагинов верстака (`IEnginePlugin`)

Плагины расширяют сам верстак (генераторы, кастомные инспекторы, панели профилирования, импортеры ассетов), не меняя `Engine.Core`.

### 1. Контракты [`PluginContext`](src/Engine.Core/Plugins/PluginContext.cs)

| Свойство | Интерфейс | Назначение |
| :--- | :--- | :--- |
| `Graphics` | [`IGraphicsService`](src/Engine.Core/Abstractions/IGraphicsService.cs) | Отрисовка 2D-примитивов, `TextureHandle` (O(1)), `Camera2D`, `BlendMode`, поверхности, шейдеры |
| `Batcher` | [`IQuadBatchService`](src/Engine.Core/Abstractions/IQuadBatchService.cs) | Аппаратный квад-батчер (до 16 384 квадов на flush, автосброс при смене состояния) |
| `Display` | [`IVirtualDisplayService`](src/Engine.Core/Abstractions/IVirtualDisplayService.cs) | Виртуальное разрешение, режимы масштабирования, пиксельный снэппинг, `ScreenToVirtual` |
| `Compositor` | [`ISurfaceCompositor`](src/Engine.Core/Abstractions/ISurfaceCompositor.cs) | Многопроходный композитор FBO-поверхностей (`BeginTarget`, `ExecutePass`, `PresentToScreen`) |
| `Audio` | [`IAudioMixerService`](src/Engine.Core/Abstractions/IAudioMixerService.cs) | Шины `AudioBus`, полифонический `PlayOneShot`, `PlayStream`/`CrossfadeMusic`, инвалидация |
| `Input` | [`IInputService`](src/Engine.Core/Abstractions/IInputService.cs) | Клавиатура, мышь, координаты во вьюпорте |
| `Workspace` | [`IWorkspaceService`](src/Engine.Core/Abstractions/IWorkspaceService.cs) | Путь к проекту, сканирование файлов, разрешение путей |
| `EventBus` | [`IEventBus`](src/Engine.Core/Abstractions/IEventBus.cs) | Типизированная публикация и подписка на события |
| `Settings` | [`ISettingsService`](src/Engine.Core/Abstractions/ISettingsService.cs) | Сохранение настроек в JSON |
| `Menu` | [`IMenuService`](src/Engine.Core/Abstractions/IMenuService.cs) | Регистрация пунктов меню и хоткеев в верхней панели macOS / ImGui |
| `Plugins` | [`IPluginManager`](src/Engine.Core/Abstractions/IPluginManager.cs) | Управление активностью плагинов и видимостью окон |
| `Runtime` | [`ExecutionContext`](src/Engine.Core/Abstractions/ExecutionContext.cs) | Общие runtime-свойства и сервисы Core v2 |

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
