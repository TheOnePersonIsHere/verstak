using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Engine.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Raylib_cs;

namespace Engine.Plugins.Execution;

public sealed class ScriptAssemblyLoadContext : AssemblyLoadContext
{
    public ScriptAssemblyLoadContext() : base(isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName) => null;
}

public sealed record ScriptCompileResult(
    bool Success,
    ScriptAssemblyLoadContext? LoadContext,
    IReadOnlyList<IExecutableNode> Nodes,
    IReadOnlyList<string> Errors);

public static class ScriptCompiler
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> BaseReferences = new(BuildMetadataReferences);

    public static ScriptCompileResult CompileNodesFromFiles(IEnumerable<string> scriptPaths)
    {
        var paths = scriptPaths.Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
        {
            return new ScriptCompileResult(true, null, Array.Empty<IExecutableNode>(), Array.Empty<string>());
        }

        var syntaxTrees = new List<SyntaxTree>(paths.Length + 1)
        {
            CSharpSyntaxTree.ParseText(
                """
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Numerics;
                """,
                path: "__GlobalUsings__.g.cs",
                encoding: Encoding.UTF8)
        };
        foreach (string path in paths)
        {
            string source = File.ReadAllText(path, Encoding.UTF8);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(source, path: path, encoding: Encoding.UTF8));
        }

        string assemblyName = $"DynamicNodes_{Guid.NewGuid():N}";
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            BaseReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug));

        using var peStream = new MemoryStream();
        EmitResult emitResult = compilation.Emit(peStream);

        if (!emitResult.Success)
        {
            string[] errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToArray();

            return new ScriptCompileResult(false, null, Array.Empty<IExecutableNode>(), errors);
        }

        peStream.Position = 0;
        var loadContext = new ScriptAssemblyLoadContext();
        try
        {
            Assembly assembly = loadContext.LoadFromStream(peStream);
            var nodes = new List<IExecutableNode>();

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IExecutableNode).IsAssignableFrom(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IExecutableNode node)
                {
                    nodes.Add(node);
                }
            }

            return new ScriptCompileResult(true, loadContext, nodes, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            loadContext.Unload();
            return new ScriptCompileResult(false, null, Array.Empty<IExecutableNode>(), [ex.Message]);
        }
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var refs = new List<MetadataReference>();

        void AddAssemblyPath(string? location)
        {
            if (string.IsNullOrWhiteSpace(location) || !File.Exists(location) || !seenPaths.Add(location))
            {
                return;
            }

            refs.Add(MetadataReference.CreateFromFile(location));
        }

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddAssemblyPath(path);
            }
        }

        AddAssemblyPath(typeof(object).Assembly.Location);
        AddAssemblyPath(typeof(IExecutableNode).Assembly.Location);
        AddAssemblyPath(typeof(Raylib).Assembly.Location);
        AddAssemblyPath(typeof(System.Numerics.Vector2).Assembly.Location);

        return refs;
    }
}
