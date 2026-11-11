namespace Engine.Core.Abstractions;

public sealed record WorkspaceFileEntry(
    string Name,
    string FullPath,
    string RelativePath,
    string Extension,
    long SizeBytes,
    DateTime LastWriteTimeUtc);

public sealed record WorkspaceDirectoryNode(
    string Name,
    string FullPath,
    string RelativePath,
    IReadOnlyList<WorkspaceDirectoryNode> Subdirectories,
    IReadOnlyList<WorkspaceFileEntry> Files);

public interface IWorkspaceService
{
    string ProjectRoot { get; }
    void SetProjectRoot(string projectRoot);
    DirectoryInfo GetRootDirectory();
    WorkspaceDirectoryNode ScanDirectory(string? path = null);
    IReadOnlyList<string> EnumerateFiles(string? relativeSubfolder = null, string searchPattern = "*", bool recursive = true);
    string ResolvePath(string path);
    string ToRelativePath(string fullPath);
}
