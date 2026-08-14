using Tyhp.CLI.IntegrityChecks;
using Tyhp.Config;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class CacheCheckSymlinkPathTests
{
    [Fact]
    public void TryResolveProjectSource_AcceptsAbsoluteKeyThroughProjectSymlink()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        // Project root is the physical directory (matches FileInfo / GetCurrentDirectory after
        // entering a symlinked tree). Cache keys may still store the symlink spelling.
        var projectRoot = PathCanonicalizer.GetCanonicalFullPath(layout.RealRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var ok = CacheCheck.TryResolveProjectSource(
            layout.LinkSourceFile,
            projectRoot,
            out var absolutePath);

        ok.Should().BeTrue();
        absolutePath.Should().Be(PathCanonicalizer.GetCanonicalFullPath(layout.RealSourceFile));
    }

    [Fact]
    public void TryResolveProjectSource_StillRejectsKeyOutsideProject()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var projectRoot = PathCanonicalizer.GetCanonicalFullPath(layout.RealRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var outside = Path.Combine(Path.GetTempPath(), "tyhp-outside-" + Guid.NewGuid().ToString("N") + ".tyhp");
        File.WriteAllText(outside, "<?tyhp\n");
        try
        {
            CacheCheck.TryResolveProjectSource(outside, projectRoot, out _)
                .Should().BeFalse();
        }
        finally
        {
            try { File.Delete(outside); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void TryResolveProjectSource_AcceptsRelativeKeyUnderCanonicalRoot()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var projectRoot = PathCanonicalizer.GetCanonicalFullPath(layout.RealRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        CacheCheck.TryResolveProjectSource("src/index.tyhp", projectRoot, out var absolutePath)
            .Should().BeTrue();
        absolutePath.Should().Be(PathCanonicalizer.GetCanonicalFullPath(layout.RealSourceFile));
    }

    /// <summary>
    /// Physical project under a temp directory, plus a sibling directory symlink that points at it.
    /// Returns null when the host cannot create symlinks (common on locked-down Windows).
    /// </summary>
    private sealed class SymlinkProjectLayout : IDisposable
    {
        private SymlinkProjectLayout(
            string realRoot,
            string linkRoot,
            string realSourceFile,
            string linkSourceFile)
        {
            RealRoot = realRoot;
            LinkRoot = linkRoot;
            RealSourceFile = realSourceFile;
            LinkSourceFile = linkSourceFile;
        }

        public string RealRoot { get; }
        public string LinkRoot { get; }
        public string RealSourceFile { get; }
        public string LinkSourceFile { get; }

        public static SymlinkProjectLayout? TryCreate()
        {
            var id = Guid.NewGuid().ToString("N");
            var parent = Path.Combine(Path.GetTempPath(), "tyhp-symlink-cache-" + id);
            var realRoot = Path.Combine(parent, "real");
            var linkRoot = Path.Combine(parent, "link");
            var srcDir = Path.Combine(realRoot, "src");

            Directory.CreateDirectory(srcDir);
            var realSourceFile = Path.Combine(srcDir, "index.tyhp");
            File.WriteAllText(Path.Combine(realRoot, "tyhp.json"), """{"include":["**/*.tyhp"]}""");
            File.WriteAllText(realSourceFile, "<?tyhp\n");

            try
            {
                Directory.CreateSymbolicLink(linkRoot, realRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
                return null;
            }

            if (!Directory.Exists(linkRoot))
            {
                try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
                return null;
            }

            return new SymlinkProjectLayout(
                realRoot,
                linkRoot,
                realSourceFile,
                Path.Combine(linkRoot, "src", "index.tyhp"));
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(RealRoot);
            if (String.IsNullOrEmpty(parent))
            {
                return;
            }

            try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
        }
    }
}
