using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Build")]
public class PublishContentServiceTests
{
    [Fact]
    public void ExpandEntry_PreservesRecursiveDirUnderDst()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "docs/guide/intro.md", "intro");
        Write(layout.Root, "docs/drafts/wip.md", "wip");

        var entry = new PublishContentEntry
        {
            Src = ["docs/**/*.md"],
            Dst = "docs",
            Exclude = ["docs/drafts/**"],
        };

        var matches = PublishContentService.ExpandEntry(layout.Root, entry);

        matches.Should().ContainSingle();
        matches[0].RelativeDestPath.Should().Be("guide/intro.md");
        Path.GetFileName(matches[0].SourceFullPath).Should().Be("intro.md");
    }

    [Fact]
    public void ExpandEntry_FlatDropsMatchedFolders()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "docs/guide/intro.md", "intro");

        var entry = new PublishContentEntry
        {
            Src = ["docs/**/*.md"],
            Dst = "docs",
            Flat = true,
        };

        var matches = PublishContentService.ExpandEntry(layout.Root, entry);

        matches.Should().ContainSingle();
        matches[0].RelativeDestPath.Should().Be("intro.md");
    }

    [Fact]
    public void ExpandEntry_DirectorySrcKeepsTree()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "assets/img/logo.png", "png");
        Write(layout.Root, "assets/readme.txt", "txt");

        var entry = new PublishContentEntry { Src = ["assets"] };
        var matches = PublishContentService.ExpandEntry(layout.Root, entry);

        matches.Select(m => m.RelativeDestPath).Should().BeEquivalentTo("img/logo.png", "readme.txt");
    }

    [Fact]
    public void TryCopy_CopiesSingleFileToPublishRoot()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "README.md", "hello");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "README.md";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        diagnostics.HasErrors.Should().BeFalse();
        File.ReadAllText(Path.Combine(layout.Publish, "README.md")).Should().Be("hello");
    }

    [Fact]
    public void TryCopy_RenamesSingleFileWhenDstHasExtension()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "LICENSE", "mit");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "LICENSE";
            values["output:publishContent:0:dst"] = "LICENSE.txt";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        File.Exists(Path.Combine(layout.Publish, "LICENSE.txt")).Should().BeTrue();
        File.Exists(Path.Combine(layout.Publish, "LICENSE")).Should().BeFalse();
    }

    [Fact]
    public void TryCopy_FileStemDestination_IsUniquePerMatch()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "docs/guide/intro.md", "intro");
        Write(layout.Root, "docs/other.md", "other");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "docs/**/*.md";
            values["output:publishContent:0:dst"] = "{fileStem}.txt";
        });
        var diagnostics = new DiagnosticBag();

        project.Output.PublishContent[0].Dst.Should().Be("{fileStem}.txt");
        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        diagnostics.HasErrors.Should().BeFalse();
        File.ReadAllText(Path.Combine(layout.Publish, "intro.txt")).Should().Be("intro");
        File.ReadAllText(Path.Combine(layout.Publish, "other.txt")).Should().Be("other");
    }

    [Fact]
    public void TryCopy_RecursiveDirFileName_KeepsMatchedFolders()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "docs/guide/intro.md", "intro");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "docs/**/*.md";
            values["output:publishContent:0:dst"] = "{recursiveDir}/{fileName}";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        diagnostics.HasErrors.Should().BeFalse();
        File.ReadAllText(Path.Combine(layout.Publish, "guide", "intro.md")).Should().Be("intro");
    }

    [Fact]
    public void TryCopy_RejectsDestinationOutsidePublishRoot()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "README.md", "hello");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "README.md";
            values["output:publishContent:0:dst"] = "../escape.md";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildPublishContentFailed);
        File.Exists(Path.Combine(layout.Root, "escape.md")).Should().BeFalse();
    }

    [Fact]
    public void TryCopy_RejectsGlobWithFileDestination()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "a.txt", "a");
        Write(layout.Root, "b.txt", "b");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "*.txt";
            values["output:publishContent:0:dst"] = "out.txt";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildPublishContentFailed);
    }

    [Fact]
    public void TryCopy_WarnsWhenOptionalSrcMatchesNothing()
    {
        using var layout = CreateLayout();
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "missing/**/*.md";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildPublishContentUnmatched);
        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void TryCopy_ErrorsWhenRequiredSrcMatchesNothing()
    {
        using var layout = CreateLayout();
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "missing.md";
            values["output:publishContent:0:required"] = "true";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildPublishContentFailed);
    }

    [Fact]
    public void TryCopy_PreserveNeverDoesNotOverwrite()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "README.md", "new");
        Directory.CreateDirectory(layout.Publish);
        File.WriteAllText(Path.Combine(layout.Publish, "README.md"), "old");

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "README.md";
            values["output:publishContent:0:preserve"] = "never";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        File.ReadAllText(Path.Combine(layout.Publish, "README.md")).Should().Be("old");
    }

    [Fact]
    public void TryCopy_DryRunDoesNotWrite()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "README.md", "hello");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "README.md";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: true).Should().BeTrue();
        File.Exists(Path.Combine(layout.Publish, "README.md")).Should().BeFalse();
    }

    [Fact]
    public void TryCopy_SkipUnchangedLeavesIdenticalFile()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "README.md", "hello");
        Directory.CreateDirectory(layout.Publish);
        var dest = Path.Combine(layout.Publish, "README.md");
        File.Copy(Path.Combine(layout.Root, "README.md"), dest);
        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(Path.Combine(layout.Root, "README.md")));
        var before = File.GetLastWriteTimeUtc(dest);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "README.md";
            values["output:publishContent:0:preserve"] = "always";
            values["output:publishContent:0:skipUnchanged"] = "true";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeTrue();
        File.GetLastWriteTimeUtc(dest).Should().Be(before);
    }

    [Fact]
    public void TryCopy_ErrorsWhenFlatCollides()
    {
        using var layout = CreateLayout();
        Write(layout.Root, "a/intro.md", "a");
        Write(layout.Root, "b/intro.md", "b");
        Directory.CreateDirectory(layout.Publish);

        var project = CreateProject(layout.Root, values =>
        {
            values["output:publishContent:0:src"] = "**/*.md";
            values["output:publishContent:0:dst"] = "docs";
            values["output:publishContent:0:flat"] = "true";
        });
        var diagnostics = new DiagnosticBag();

        PublishContentService.TryCopy(project, diagnostics, dryRun: false).Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildPublishContentFailed);
    }

    [Fact]
    public void Build_CopiesPublishContentIntoPublishPath()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": {
                        "path": "publish/src",
                        "publishPath": "publish",
                        "publishContent": [
                            { "src": "README.md" },
                            { "src": "docs/**/*.md", "dst": "docs" }
                        ]
                    }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                class App {
                    public function ping(): int {
                        return 1;
                    }
                }
                """)
            .WithTyhpFile("README.md", "hello-readme")
            .WithTyhpFile("docs/guide/intro.md", "intro");

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "publish", "README.md"))
            .Should().Be("hello-readme");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "publish", "docs", "guide", "intro.md"))
            .Should().Be("intro");
    }

    [Fact]
    public void Build_PublishCleanWipesStaleFilesThenCopies()
    {
        using var project = new TestProjectBuilder();
        var stale = Path.Combine(project.ProjectDirectory, "publish", "stale.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "stale");

        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": {
                        "path": "publish/src",
                        "publishPath": "publish",
                        "publishClean": true,
                        "publishContent": [
                            { "src": "README.md" }
                        ]
                    }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                class App {
                    public function ping(): int {
                        return 1;
                    }
                }
                """)
            .WithTyhpFile("README.md", "fresh");

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.Exists(stale).Should().BeFalse();
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "publish", "README.md"))
            .Should().Be("fresh");
    }

    private static Project CreateProject(string projectRoot, Action<Dictionary<string, string?>> configure)
    {
        var projectFile = Path.Combine(projectRoot, "tyhp.json");
        if (!File.Exists(projectFile))
        {
            File.WriteAllText(projectFile, "{}");
        }

        var values = new Dictionary<string, string?>
        {
            ["*project_file_path"] = projectFile,
            ["output:publishPath"] = "publish",
        };
        configure(values);

        return new Project(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private static void Write(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static TempLayout CreateLayout() => new();

    private sealed class TempLayout : IDisposable
    {
        public TempLayout()
        {
            Root = Path.Combine(Path.GetTempPath(), "tyhp-publish-content", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "tyhp.json"), "{}");
            Publish = Path.Combine(Root, "publish");
        }

        public string Root { get; }

        public string Publish { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }
}
