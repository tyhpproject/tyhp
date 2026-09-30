using System.IO.Compression;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Locates (and optionally fetches) Layer 2 stub corpora into gitignored
    /// <c>tools/stub-cache/</c>. URLs match <c>runtime/README.md</c>.
    /// </summary>
    public sealed class StubCorpusCache
    {
        public const string PsalmId = "psalm";
        public const string PhpStanId = "phpstan";
        public const string PhanId = "phan";
        public const string PhpStormId = "phpstorm";

        internal const string EnvCacheDirectory = "TYHP_STUB_CACHE";

        private static readonly HashSet<string> StubExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".php",
            ".stub",
            ".phpstub",
        };

        // Verbatim MIT permission and warranty text from the upstream LICENSE files.
        private const string MitTerms =
            """
            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.
            """;

        private const string PhpStormApacheNotice =
            """
            Copyright 2010-2023 JetBrains s.r.o.

            Licensed under the Apache License, Version 2.0 (the "License");
            you may not use this file except in compliance with the License.
            You may obtain a copy of the License at

                http://www.apache.org/licenses/LICENSE-2.0

            Unless required by applicable law or agreed to in writing, software
            distributed under the License is distributed on an "AS IS" BASIS,
            WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
            See the License for the specific language governing permissions and
            limitations under the License.
            """;

        private static readonly StubCorpusDescriptor[] Corpora =
        [
            new(
                PsalmId,
                "Psalm stubs",
                "MIT",
                "https://github.com/vimeo/psalm/tree/6.x/stubs",
                "https://github.com/vimeo/psalm/archive/refs/heads/6.x.zip",
                ["stubs"],
                "MIT License\n\nCopyright (c) 2016 Vimeo\n\n" + MitTerms),
            new(
                PhpStanId,
                "PHPStan stubs",
                "MIT",
                "https://github.com/phpstan/phpstan-src/tree/2.2.x/stubs",
                "https://github.com/phpstan/phpstan-src/archive/refs/heads/2.2.x.zip",
                ["stubs"],
                "MIT License\n\nCopyright (c) 2016 Ondřej Mirtes\nCopyright (c) 2025 PHPStan s.r.o.\n\n" + MitTerms),
            new(
                PhanId,
                "Phan stubs",
                "MIT",
                "https://github.com/phan/phan/tree/v6/internal/stubs",
                "https://github.com/phan/phan/archive/refs/heads/v6.zip",
                ["internal", "stubs"],
                "The MIT License (MIT)\n\nCopyright (c) 2015 Rasmus Lerdorf\nCopyright (c) 2015 Andrew Morrison\n\n" + MitTerms),
            new(
                PhpStormId,
                "PhpStorm stubs",
                "Apache-2.0",
                "https://github.com/jetbrains/phpstorm-stubs",
                "https://github.com/jetbrains/phpstorm-stubs/archive/refs/heads/master.zip",
                [],
                PhpStormApacheNotice),
        ];

        private readonly IPhpRuntimeTransport _transport;

        public StubCorpusCache()
            : this(new PhpRuntimeTransport())
        {
        }

        internal StubCorpusCache(IPhpRuntimeTransport transport)
        {
            this._transport = transport;
        }

        public static IReadOnlyList<StubCorpusDescriptor> AllCorpora => Corpora;

        /// <summary>
        /// Resolves a populated cache: explicit options, <c>TYHP_STUB_CACHE</c>, then
        /// <c>tools/stub-cache/</c>. Optionally fetches zipballs when missing.
        /// </summary>
        public string? Resolve(
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(diagnostics);

            var cacheDir = ResolveDirectory(options);
            if (IsPopulated(cacheDir))
            {
                return cacheDir;
            }

            if (options.FetchStubCache && !string.IsNullOrWhiteSpace(cacheDir))
            {
                Message.Info("CLI_TyhpdefFetchingStubs", cacheDir);
                if (this.TryFetch(cacheDir, cancellationToken) && IsPopulated(cacheDir))
                {
                    return cacheDir;
                }
            }

            if (options.RequireStubs)
            {
                diagnostics.AddError(
                    MessageCode.TyhpdefStubCacheRequired,
                    "generate_tyhpdef",
                    0,
                    0,
                    cacheDir ?? "tools/stub-cache/");
            }
            else
            {
                diagnostics.AddWarning(
                    MessageCode.TyhpdefStubCacheMissing,
                    "generate_tyhpdef",
                    0,
                    0,
                    cacheDir ?? "tools/stub-cache/");
            }

            return null;
        }

        public static string? ResolveDirectory(TyhpdefGenerationOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.StubCacheDirectory))
            {
                return Path.GetFullPath(options.StubCacheDirectory);
            }

            var env = Environment.GetEnvironmentVariable(EnvCacheDirectory);
            if (!string.IsNullOrWhiteSpace(env))
            {
                return Path.GetFullPath(env);
            }

            var repoRoot = FindRepoRoot();
            if (repoRoot is not null)
            {
                return Path.Combine(repoRoot, "tools", "stub-cache");
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "tools", "stub-cache");
        }

        public static bool IsPopulated(string? cacheDirectory)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
            {
                return false;
            }

            foreach (var corpus in Corpora)
            {
                var corpusDir = Path.Combine(cacheDirectory, corpus.Id);
                if (Directory.Exists(corpusDir) && EnumerateStubFiles(corpusDir).Any())
                {
                    return true;
                }
            }

            // Vendored test snapshots may be a flat tree of corpus folders *or* the cache root
            // itself may contain stub files (single-corpus excerpt).
            return EnumerateStubFiles(cacheDirectory).Any();
        }

        public static IEnumerable<string> EnumerateStubFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                yield break;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                yield break;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (StubExtensions.Contains(ext)
                    || file.EndsWith(".php.stub", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }

        public static IReadOnlyDictionary<string, string> CorpusDirectories(string cacheDirectory)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var corpus in Corpora)
            {
                var dir = Path.Combine(cacheDirectory, corpus.Id);
                if (Directory.Exists(dir) && EnumerateStubFiles(dir).Any())
                {
                    map[corpus.Id] = dir;
                }
            }

            if (map.Count == 0 && EnumerateStubFiles(cacheDirectory).Any())
            {
                map[PhpStormId] = cacheDirectory;
            }

            return map;
        }

        internal bool TryFetch(string cacheDirectory, CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(cacheDirectory);
            }
            catch (IOException)
            {
                return false;
            }

            var any = false;
            foreach (var corpus in Corpora)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dest = Path.Combine(cacheDirectory, corpus.Id);
                if (Directory.Exists(dest) && EnumerateStubFiles(dest).Any())
                {
                    any = true;
                    continue;
                }

                if (this.TryFetchCorpus(corpus, dest, cancellationToken))
                {
                    any = true;
                }
            }

            if (any)
            {
                TryWriteCacheAttribution(cacheDirectory);
            }

            return any;
        }

        private bool TryFetchCorpus(
            StubCorpusDescriptor corpus,
            string destination,
            CancellationToken cancellationToken)
        {
            var zipPath = destination + ".zip";
            var extractDir = destination + ".extract";
            try
            {
                var ok = this._transport.DownloadToFileAsync(
                    new Uri(corpus.ZipUrl),
                    zipPath,
                    expectedSha256: null,
                    cancellationToken).GetAwaiter().GetResult();
                if (!ok || !File.Exists(zipPath))
                {
                    return false;
                }

                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, recursive: true);
                }

                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
                var source = FindExtractedCorpusRoot(extractDir, corpus.ZipInnerSegments);
                if (source is null)
                {
                    return false;
                }

                CopyDirectory(source, destination);
                return EnumerateStubFiles(destination).Any();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException
                or UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
            finally
            {
                TryDelete(zipPath);
                TryDeleteDirectory(extractDir);
            }
        }

        private static string? FindExtractedCorpusRoot(string extractDir, IReadOnlyList<string> innerSegments)
        {
            var roots = Directory.GetDirectories(extractDir);
            var searchRoots = roots.Length > 0 ? roots : [extractDir];
            foreach (var root in searchRoots)
            {
                if (innerSegments.Count == 0)
                {
                    return root;
                }

                var current = root;
                var found = true;
                foreach (var segment in innerSegments)
                {
                    var next = Path.Combine(current, segment);
                    if (!Directory.Exists(next))
                    {
                        found = false;
                        break;
                    }

                    current = next;
                }

                if (found)
                {
                    return current;
                }
            }

            // Fallback: search a few levels for a directory named the last segment.
            if (innerSegments.Count > 0)
            {
                var last = innerSegments[^1];
                try
                {
                    return Directory.EnumerateDirectories(extractDir, last, SearchOption.AllDirectories)
                        .FirstOrDefault();
                }
                catch (IOException)
                {
                    return null;
                }
            }

            return searchRoots.FirstOrDefault();
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destFile = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, overwrite: true);
            }
        }

        private static void TryWriteCacheAttribution(string cacheDirectory)
        {
            try
            {
                var lines = new List<string>
                {
                    "Tyhp Layer 2 stub cache",
                    "These trees are fetched for tyhpdef harvest; they are not Tyhp source of truth.",
                    "",
                };
                foreach (var corpus in Corpora)
                {
                    lines.Add($"- {corpus.Title} ({corpus.License}): {corpus.PageUrl}");
                }

                File.WriteAllText(Path.Combine(cacheDirectory, "ATTRIBUTION.txt"), string.Join(Environment.NewLine, lines));
            }
            catch (IOException)
            {
            }
        }

        internal static string? FindRepoRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var found = WalkForRepoRoot(start);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string? WalkForRepoRoot(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                return null;
            }

            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
            }
            catch (Exception)
            {
                return null;
            }

            var depth = 0;
            while (directory != null && depth++ < 12)
            {
                if (File.Exists(Path.Combine(directory.FullName, "tyhp.csproj"))
                    && Directory.Exists(Path.Combine(directory.FullName, "runtime"))
                    && Directory.Exists(Path.Combine(directory.FullName, "tools")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// One stub corpus listed in <c>runtime/README.md</c>.
    /// <paramref name="LicenseNotice"/> is the upstream copyright and permission text that
    /// must accompany material adapted from the corpus; it is written to <c>NOTICE</c>.
    /// </summary>
    public sealed record StubCorpusDescriptor(
        string Id,
        string Title,
        string License,
        string PageUrl,
        string ZipUrl,
        IReadOnlyList<string> ZipInnerSegments,
        string LicenseNotice);
}
