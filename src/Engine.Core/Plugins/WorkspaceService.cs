using Engine.Core.Abstractions;
using Engine.Core.Events;

namespace Engine.Core.Plugins;

public sealed class WorkspaceService : IWorkspaceService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        "bin",
        "obj"
    };

    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store"
    };

    private readonly IEventBus _eventBus;

    public WorkspaceService(string projectRoot, IEventBus eventBus)
    {
        _eventBus = eventBus;
        ProjectRoot = Path.GetFullPath(projectRoot);
    }

    public string ProjectRoot { get; private set; }

    public void SetProjectRoot(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ProjectRoot = Path.GetFullPath(projectRoot);
        _eventBus.Publish(new ProjectLoadedEvent(ProjectRoot));
    }

    public DirectoryInfo GetRootDirectory()
    {
        return new DirectoryInfo(ProjectRoot);
    }

    public WorkspaceDirectoryNode ScanDirectory(string? path = null)
    {
        string targetPath = string.IsNullOrWhiteSpace(path) ? ProjectRoot : ResolvePath(path);
        DirectoryInfo dir = new(targetPath);
        if (!dir.Exists)
        {
            return new WorkspaceDirectoryNode(dir.Name, dir.FullName, ToRelativePath(dir.FullName), [], []);
        }

        return ScanDirectoryRecursive(dir);
    }

    public IReadOnlyList<string> EnumerateFiles(string? relativeSubfolder = null, string searchPattern = "*", bool recursive = true)
    {
        string baseDir = string.IsNullOrWhiteSpace(relativeSubfolder)
            ? ProjectRoot
            : ResolvePath(relativeSubfolder);

        DirectoryInfo dir = new(baseDir);
        if (!dir.Exists)
        {
            return Array.Empty<string>();
        }

        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return dir.EnumerateFiles(searchPattern, option)
            .Where(f => !IgnoredFiles.Contains(f.Name) && !IsInsideIgnoredDirectory(f.Directory))
            .Select(f => f.FullName)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProjectRoot;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ProjectRoot, path));
    }

    public string ToRelativePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return ".";
        }

        return Path.GetRelativePath(ProjectRoot, Path.GetFullPath(fullPath)).Replace('\\', '/');
    }

    private WorkspaceDirectoryNode ScanDirectoryRecursive(DirectoryInfo directory)
    {
        var subdirs = new List<WorkspaceDirectoryNode>();
        var files = new List<WorkspaceFileEntry>();

        try
        {
            foreach (DirectoryInfo sub in directory.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (IgnoredDirectories.Contains(sub.Name))
                {
                    continue;
                }

                subdirs.Add(ScanDirectoryRecursive(sub));
            }

            foreach (FileInfo file in directory.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (IgnoredFiles.Contains(file.Name))
                {
                    continue;
                }

                files.Add(new WorkspaceFileEntry(
                    file.Name,
                    file.FullName,
                    ToRelativePath(file.FullName),
                    file.Extension.ToLowerInvariant(),
                    file.Length,
                    file.LastWriteTimeUtc));
            }
        }
        catch (IOException)
        {
            // Ignore transient IO access errors while external editor writes files.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new WorkspaceDirectoryNode(
            directory.Name,
            directory.FullName,
            ToRelativePath(directory.FullName),
            subdirs,
            files);
    }

    private bool IsInsideIgnoredDirectory(DirectoryInfo? dir)
    {
        DirectoryInfo? current = dir;
        while (current != null && !string.Equals(current.FullName, ProjectRoot, StringComparison.Ordinal))
        {
            if (IgnoredDirectories.Contains(current.Name))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}
