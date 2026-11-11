using Engine.Core.Plugins;

namespace Engine.Core.Abstractions;

public interface IEnginePlugin
{
    string Id { get; }
    string DisplayName { get; }
    void OnLoaded(PluginContext context);
    void OnUpdate(float deltaTime);
    void OnRenderUI();
    void OnUnloaded();
}
