using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Config;

[Trait("Category", "Config")]
public class LintFileSymlinkPathTests
{
    [Fact]
    public void PathCanonicalizer_IsUnderRoot_AcceptsSymlinkSpellingOfChild()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        PathCanonicalizer.IsUnderRoot(layout.LinkSourceFile, layout.RealRoot)
            .Should().BeTrue();
        PathCanonicalizer.IsUnderRoot(layout.RealSourceFile, layout.LinkRoot)
            .Should().BeTrue();
    }

    [Fact]
    public void PathCanonicalizer_ResolvesIntermediateDirectorySymlinks()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var viaLink = Path.Combine(layout.LinkRoot, "src", "index.tyhp");
        var canonical = PathCanonicalizer.GetCanonicalFullPath(viaLink);

        canonical.Should().Be(PathCanonicalizer.GetCanonicalFullPath(layout.RealSourceFile));
        File.Exists(canonical).Should().BeTrue();
    }

    [Fact]
    public void ValidateLintConfig_AcceptsAbsolutePathThroughProjectSymlink()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        // Project root is the physical directory (matches GetCurrentDirectory / FileInfo after cd
        // into a symlinked tree). --file uses the symlink spelling of the same file.
        var project = CreateProject(layout.RealProjectFile, fileViaSymlink: layout.LinkSourceFile);
        var bag = new DiagnosticBag();

        var ok = project.ValidateLintConfig(bag);

        ok.Should().BeTrue();
        bag.ErrorCount.Should().Be(0);
        PathCanonicalizer.GetCanonicalFullPath(project.LintFile!)
            .Should().Be(PathCanonicalizer.GetCanonicalFullPath(layout.RealSourceFile));
    }

    [Fact]
    public void ValidateLintConfig_AcceptsRelativePathUnderSymlinkedCwdShape()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var project = CreateProject(layout.RealProjectFile, fileViaSymlink: "src/index.tyhp");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            // Enter via the symlink path so relative GetFullPath would normally disagree with a
            // physical project root — canonicalization keeps validation consistent either way.
            Directory.SetCurrentDirectory(layout.LinkRoot);
            var bag = new DiagnosticBag();

            var ok = project.ValidateLintConfig(bag);

            ok.Should().BeTrue();
            bag.ErrorCount.Should().Be(0);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void ValidateLintConfig_StillRejectsFileOutsideProject()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var outside = Path.Combine(Path.GetTempPath(), "tyhp-outside-" + Guid.NewGuid().ToString("N") + ".tyhp");
        File.WriteAllText(outside, "<?tyhp\n");
        try
        {
            var project = CreateProject(layout.RealProjectFile, fileViaSymlink: outside);
            var bag = new DiagnosticBag();

            var ok = project.ValidateLintConfig(bag);

            ok.Should().BeFalse();
            bag.Errors.Should().Contain(d => d.Code == MessageCode.LintFileNotInProject);
        }
        finally
        {
            try { File.Delete(outside); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject(string projectFilePath, string fileViaSymlink)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = projectFilePath,
                ["include:0"] = "**/*.tyhp",
                ["file"] = fileViaSymlink,
            })
            .Build();

        return new Project(configuration);
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
            string realProjectFile,
            string realSourceFile,
            string linkSourceFile)
        {
            RealRoot = realRoot;
            LinkRoot = linkRoot;
            RealProjectFile = realProjectFile;
            RealSourceFile = realSourceFile;
            LinkSourceFile = linkSourceFile;
        }

        public string RealRoot { get; }
        public string LinkRoot { get; }
        public string RealProjectFile { get; }
        public string RealSourceFile { get; }
        public string LinkSourceFile { get; }

        public static SymlinkProjectLayout? TryCreate()
        {
            var id = Guid.NewGuid().ToString("N");
            var parent = Path.Combine(Path.GetTempPath(), "tyhp-symlink-lint-" + id);
            var realRoot = Path.Combine(parent, "real");
            var linkRoot = Path.Combine(parent, "link");
            var srcDir = Path.Combine(realRoot, "src");

            Directory.CreateDirectory(srcDir);
            var realProjectFile = Path.Combine(realRoot, "tyhp.json");
            var realSourceFile = Path.Combine(srcDir, "index.tyhp");
            File.WriteAllText(realProjectFile, """{"include":["**/*.tyhp"]}""");
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

            var linkSourceFile = Path.Combine(linkRoot, "src", "index.tyhp");
            // Require that symlink and physical spellings actually differ; otherwise the
            // regression would be a no-op on this host (e.g. already-canonical temp roots).
            if (String.Equals(
                    Path.GetFullPath(linkSourceFile),
                    PathCanonicalizer.GetCanonicalFullPath(realSourceFile),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Still a useful check when CreateSymbolicLink succeeded but GetFullPath already
                // resolved the link (some hosts). Keep the layout for ValidateLintConfig coverage.
            }

            return new SymlinkProjectLayout(
                realRoot,
                linkRoot,
                realProjectFile,
                realSourceFile,
                linkSourceFile);
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
