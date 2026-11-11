namespace Engine.Plugins.Workspace;

public sealed record WorkspaceChangedEvent(string ProjectRoot);

public sealed record FileWatcherSelfSaveEvent(string FilePath, bool IsSaving);
