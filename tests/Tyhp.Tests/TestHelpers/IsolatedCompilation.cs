using System.Collections.Frozen;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.TestHelpers;

/// <summary>
/// Compilation helpers for language-rule tests. Runtime-package overlay contracts are
/// covered by <c>runtime/packages/test-all-tyhpdef.sh</c>, not this suite.
/// </summary>
public static class IsolatedCompilation
{
    /// <summary>
    /// Isolated compiles do not load bundled php/core packages, so the binder warns that
    /// those packages are missing. Language-rule tests are not asserting package presence.
    /// </summary>
    public static FrozenSet<MessageCode> MissingPackageWarnings { get; } =
        FrozenSet.ToFrozenSet(
        [
            MessageCode.TyhpdefPhpExtensionPackageNotFound,
            MessageCode.TyhpdefRuntimePackageNotFound,
        ]);

    private static readonly object SharedStubGate = new();
    private static string? _sharedStubPath;
    private static readonly CompilationService SharedCompilation = new();

    /// <param name="includeMinimalPhpStubs">
    /// When true (default), prepends <see cref="SyntheticPhpStubs.MinimalPhp"/> even if
    /// <paramref name="tyhpdefIncludePaths"/> is non-empty. Pass false only for an empty
    /// environment or harvest/overlay fixtures that redeclare Exception/Closure.
    /// </param>
    public static CompilationOptions CreateOptions(
        string projectPath,
        string phpVersion = "8.2",
        bool skipChecking = false,
        IReadOnlyList<string>? tyhpdefIncludePaths = null,
        Action<CompilationOptions>? configure = null,
        bool includeMinimalPhpStubs = true)
    {
        var includes = tyhpdefIncludePaths?.ToList() ?? [];
        if (includeMinimalPhpStubs)
        {
            includes.Insert(0, SharedMinimalPhpStubPath);
        }

        Directory.CreateDirectory(projectPath);

        var options = new CompilationOptions
        {
            EnableAstCache = false,
            MaxThreads = 1,
            PhpVersion = phpVersion,
            ProjectPath = projectPath,
            SkipChecking = skipChecking,
            TyhpdefIncludePaths = includes,
            SuppressedWarnings = MissingPackageWarnings,
        };
        configure?.Invoke(options);
        options.EnableAstCache = false;
        options.MaxThreads = 1;
        options.SuppressedWarnings = MergeMissingPackageWarnings(options.SuppressedWarnings);
        return options;
    }

    /// <summary>
    /// Parses a Tyhp snippet in a throwaway directory, optionally with extra tyhpdef text
    /// written beside it. Does not set <see cref="CompilationOptions.ProjectPath"/> to the
    /// repo root.
    /// </summary>
    public static CompilationResult ParseSnippet(
        string tyhp,
        string? tyhpdef = null,
        string phpVersion = "8.2",
        bool skipChecking = false,
        string fileName = "snippet.tyhp",
        Action<CompilationOptions>? configure = null,
        bool includeMinimalPhpStubs = true)
    {
        return ParseSnippet(
            tyhp,
            tyhpdef is null ? [] : [tyhpdef],
            phpVersion,
            skipChecking,
            fileName,
            configure,
            includeMinimalPhpStubs);
    }

    public static CompilationResult ParseSnippet(
        string tyhp,
        IReadOnlyList<string> tyhpdefs,
        string phpVersion = "8.2",
        bool skipChecking = false,
        string fileName = "snippet.tyhp",
        Action<CompilationOptions>? configure = null,
        bool includeMinimalPhpStubs = true)
    {
        string? extraDir = null;
        var includes = new List<string>();
        if (tyhpdefs.Count > 0)
        {
            extraDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extraDir);
            for (var i = 0; i < tyhpdefs.Count; i++)
            {
                var tyhpdefPath = Path.Combine(extraDir, $"stubs{i}.tyhpdef");
                File.WriteAllText(tyhpdefPath, tyhpdefs[i]);
                includes.Add(tyhpdefPath);
            }
        }

        try
        {
            var projectPath = extraDir ?? Path.GetDirectoryName(SharedMinimalPhpStubPath)!;
            return SharedCompilation.ParseFiles(
                new Dictionary<string, string>(StringComparer.Ordinal) { [fileName] = tyhp },
                CreateOptions(
                    projectPath,
                    phpVersion,
                    skipChecking,
                    includes,
                    configure,
                    includeMinimalPhpStubs));
        }
        finally
        {
            if (extraDir != null)
            {
                try { Directory.Delete(extraDir, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Compiles existing files with <see cref="SyntheticPhpStubs.MinimalPhp"/> stubs in a
    /// throwaway project directory. Used by fixture snapshot / conformance lint tests.
    /// </summary>
    public static CompilationResult ParseExistingFiles(
        IReadOnlyList<string> files,
        string phpVersion = "8.2",
        bool skipChecking = false,
        Action<CompilationOptions>? configure = null,
        bool includeMinimalPhpStubs = true)
    {
        return SharedCompilation.ParseFiles(
            files,
            CreateOptions(
                Path.GetDirectoryName(SharedMinimalPhpStubPath)!,
                phpVersion,
                skipChecking,
                configure: configure,
                includeMinimalPhpStubs: includeMinimalPhpStubs));
    }

    public static TestProjectBuilder CreateOverlayProject(
        string phpVersion = "8.2",
        string? includeTyhpdef = null,
        string? overlayTyhpdef = null,
        string userTyhp = """
            <?tyhp
            function overlay_probe(): void {}
            """)
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson($$"""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "{{phpVersion}}" }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithTyhpFile("src/app.tyhp", userTyhp);
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"],
                "overlay": [
                    "./_tyhpdef/overlays/stubs/*.tyhpdef",
                    "./_tyhpdef/overlays/*.tyhpdef"
                ]
            }
            """);
        if (includeTyhpdef != null)
        {
            builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", includeTyhpdef);
        }

        if (overlayTyhpdef != null)
        {
            builder.WithTyhpFile("pkg/_tyhpdef/overlays/overlay.tyhpdef", overlayTyhpdef);
        }

        return builder;
    }

    public static CompilationResult BindProject(
        TestProjectBuilder builder,
        bool skipChecking = true,
        bool strict = false)
    {
        var project = builder.BuildProject();
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.EnableAstCache = false;
            o.MaxThreads = 1;
            o.SkipChecking = skipChecking;
            o.StrictMode = strict;
            o.SuppressedWarnings = MissingPackageWarnings;
        });
        options.SuppressedWarnings = MergeMissingPackageWarnings(options.SuppressedWarnings);
        return SharedCompilation.ParseFiles([userFile], options);
    }

    private static string SharedMinimalPhpStubPath
    {
        get
        {
            if (_sharedStubPath != null)
            {
                return _sharedStubPath;
            }

            lock (SharedStubGate)
            {
                if (_sharedStubPath != null)
                {
                    return _sharedStubPath;
                }

                var dir = Path.Combine(Path.GetTempPath(), "tyhp-tests", "shared-fixtures");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "__minimal_php.tyhpdef");
                File.WriteAllText(path, SyntheticPhpStubs.MinimalPhp);
                _sharedStubPath = path;
                return path;
            }
        }
    }

    private static FrozenSet<MessageCode> MergeMissingPackageWarnings(IReadOnlySet<MessageCode>? existing)
    {
        if (existing is null || existing.Count == 0)
        {
            return MissingPackageWarnings;
        }

        if (MissingPackageWarnings.IsSubsetOf(existing))
        {
            return existing as FrozenSet<MessageCode> ?? existing.ToFrozenSet();
        }

        return existing.Concat(MissingPackageWarnings).ToFrozenSet();
    }
}
