namespace Engine.Core.Abstractions;

public interface IExecutableNode
{
    string NodeId { get; }
    void OnInit(ExecutionContext context);
    void OnUpdate(float deltaTime);
    void OnDraw(IGraphicsService graphics);
    void OnUnload() { }
}
