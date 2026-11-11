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
        ExecutionContext? runtime = null)
    {
        Graphics = graphics;
        Audio = audio;
        EventBus = eventBus;
        Workspace = workspace;
        Input = input;
        Settings = settings ?? new JsonSettingsService(eventBus);
        Menu = menu ?? new MenuRegistry();
        Runtime = runtime ?? new ExecutionContext(graphics, audio, eventBus, workspace, input);
    }

    public IGraphicsService Graphics { get; }
    public IAudioService Audio { get; }
    public IEventBus EventBus { get; }
    public IWorkspaceService Workspace { get; }
    public IInputService Input { get; }
    public ISettingsService Settings { get; }
    public IMenuService Menu { get; }
    public ExecutionContext Runtime { get; }
    public IPluginManager? Plugins { get; internal set; }
}
