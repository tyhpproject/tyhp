using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Keeps the result of <see cref="Tyhpdef.GetSourceFiles"/> between binds of one project so a
    /// long-running host (the language server) does not re-read, re-hash, and re-deserialize every
    /// tyhpdef AST on each analysis cycle.
    /// </summary>
    /// <remarks>
    /// The cached sources are reloaded when the tyhpdef-relevant options change, when any loaded
    /// tyhpdef file's size or write time changes, or after <see cref="Invalidate"/>. New packages
    /// or files under a tyhpdef glob are only picked up after <see cref="Invalidate"/>, which the
    /// language server calls when <c>tyhp.json</c>, <c>composer.json</c>, <c>composer.lock</c>, or a
    /// <c>.tyhpdef</c> file under the project changes. Each reuse clears <c>BoundSymbol</c>,
    /// <c>SelectedOverload</c>, and <c>OwningFile</c> on every node so the next bind starts from the
    /// same state as a freshly deserialized tree. Callers must not bind two compilations against one
    /// cache at the same time.
    /// </remarks>
    public sealed class TyhpdefSourceCache : ITyhpdefSourceProvider
    {
        private readonly object _gate = new();
        private Entry? _entry;

        /// <summary>Drops the cached sources; the next call reloads them from disk.</summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _entry = null;
            }
        }

        /// <summary>Number of times sources were loaded from disk (for tests and logging).</summary>
        public int LoadCount { get; private set; }

        /// <inheritdoc />
        public IReadOnlyList<TyhpdefSourceFile> GetSourceFiles(DiagnosticBag diagnostics, CompilationOptions? options)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);

            Entry entry;
            lock (_gate)
            {
                var key = OptionsKey(options);
                if (_entry is { } cached && cached.Key == key && !cached.HasChangedOnDisk())
                {
                    foreach (var source in cached.Sources)
                    {
                        AstBindState.Clear(source.Ast);
                    }

                    entry = cached;
                }
                else
                {
                    var loadDiagnostics = new DiagnosticBag(new HashSet<MessageCode>());
                    var sources = Tyhpdef.GetSourceFiles(loadDiagnostics, options).ToList();
                    entry = new Entry(key, sources, loadDiagnostics.All, Fingerprint(sources));
                    _entry = entry;
                    LoadCount++;
                }
            }

            diagnostics.AddRange(entry.Diagnostics);
            return entry.Sources;
        }

        private static string OptionsKey(CompilationOptions? options)
        {
            if (options is null)
            {
                return "<default>";
            }

            return string.Join(
                "\u001f",
                options.PhpVersion,
                options.ProjectPath,
                options.Tagless ? "tagless" : "classic",
                options.ApplyTyhpdefOverlays ? "overlays" : "no-overlays",
                string.Join("\u001e", options.TyhpdefIncludePaths),
                string.Join("\u001e", options.TyhpdefOverlayPaths),
                string.Join("\u001e", options.TyhpdefExcludePaths));
        }

        private static List<FileStamp> Fingerprint(IEnumerable<TyhpdefSourceFile> sources)
        {
            var stamps = new List<FileStamp>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                var path = DiskPath(source.Ast.Identifier) ?? DiskPath(source.Ast.FileName);
                if (path is null || !seen.Add(path))
                {
                    continue;
                }

                stamps.Add(FileStamp.Read(path));
            }

            return stamps;
        }

        /// <summary>
        /// <paramref name="candidate"/> when it is an absolute path to a file on disk rather than an
        /// embedded <c>&lt;tyhpdef:…&gt;</c> name. Cached tyhpdef ASTs keep a relative
        /// <c>FileName</c> and the absolute path in <c>Identifier</c>.
        /// </summary>
        private static string? DiskPath(string? candidate)
        {
            if (string.IsNullOrEmpty(candidate)
                || !Path.IsPathRooted(candidate)
                || candidate.Contains("<tyhpdef:", StringComparison.Ordinal))
            {
                return null;
            }

            return candidate;
        }

        private sealed record FileStamp(string Path, long Length, long LastWriteTicks)
        {
            public static FileStamp Read(string path)
            {
                try
                {
                    var info = new FileInfo(path);
                    return info.Exists
                        ? new FileStamp(path, info.Length, info.LastWriteTimeUtc.Ticks)
                        : new FileStamp(path, -1, 0);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new FileStamp(path, -1, 0);
                }
            }
        }

        private sealed class Entry(
            string key,
            IReadOnlyList<TyhpdefSourceFile> sources,
            IReadOnlyList<IDiagnostic> diagnostics,
            IReadOnlyList<FileStamp> stamps)
        {
            public string Key { get; } = key;

            public IReadOnlyList<TyhpdefSourceFile> Sources { get; } = sources;

            public IReadOnlyList<IDiagnostic> Diagnostics { get; } = diagnostics;

            public bool HasChangedOnDisk()
            {
                foreach (var stamp in stamps)
                {
                    if (FileStamp.Read(stamp.Path) != stamp)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
