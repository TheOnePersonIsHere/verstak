using Engine.Core.Abstractions;
using ExecutionContext = Engine.Core.Abstractions.ExecutionContext;

namespace Engine.Core.Plugins;

public sealed class PluginContext
{
    public PluginContext(
        IGraphicsService graphics,
        IAudioService audio,
        IEventBus eventBus,
        IWorkspaceService workspace,
        IInputService input,
        ISettingsService? settings = null,
        IMenuService? menu = null,
        ExecutionContext? runtime = null,
        IQuadBatchService? batcher = null,
        IVirtualDisplayService? display = null,
        ISurfaceCompositor? compositor = null)
    {
        Graphics = graphics;
        EventBus = eventBus;
        Workspace = workspace;
        Input = input;
        Settings = settings ?? new JsonSettingsService(eventBus);
        Menu = menu ?? new MenuRegistry();
        Runtime = runtime ?? new ExecutionContext(graphics, audio, eventBus, workspace, input, batcher, display, compositor);
        Audio = Runtime.Audio;
        Batcher = batcher ?? Runtime.Batcher;
        Display = display ?? Runtime.Display;
        Compositor = compositor ?? Runtime.Compositor;
    }

    public IGraphicsService Graphics { get; }
    public IAudioMixerService Audio { get; }
    public IEventBus EventBus { get; }
    public IWorkspaceService Workspace { get; }
    public IInputService Input { get; }
    public ISettingsService Settings { get; }
    public IMenuService Menu { get; }
    public IQuadBatchService Batcher { get; }
    public IVirtualDisplayService Display { get; }
    public ISurfaceCompositor Compositor { get; }
    public ExecutionContext Runtime { get; }
    public IPluginManager? Plugins { get; internal set; }
}
