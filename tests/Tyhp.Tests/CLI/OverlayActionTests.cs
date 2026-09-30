using System.Text.Json;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Trait("Category", "Tyhpdef")]
[Collection("ProcessGlobalState")]
public class OverlayActionTests
{
    [Fact]
    public void Create_KnownFqn_WritesHandWrittenOverlayAndAppendsGlob()
    {
        using var builder = FixtureWithStubsGlobOnly();
        var project = builder.BuildProject();
        var action = new OverlayAction(project) { Args = ["create", "\\DomainException"] };
        var result = action.Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var overlayPath = Path.Combine(project.GetProjectPath(), "pkg", "_tyhpdef", "overlays", "DomainException.tyhpdef");
        File.Exists(overlayPath).Should().BeTrue();
        var overlayText = File.ReadAllText(overlayPath);
        overlayText.Should().Contain("class");
        overlayText.Should().Contain("DomainException");
        overlayText.Should().NotContain("/overlays/stubs/");

        var overlayGlobs = OverlayGlobs(project.GetProjectPath());
        overlayGlobs.Should().Equal(
            "./_tyhpdef/overlays/stubs/*.tyhpdef",
            "./_tyhpdef/overlays/*.tyhpdef");
    }

    [Fact]
    public void Create_DoesNotDuplicateHandWrittenGlob()
    {
        using var builder = Fixture();
        var project = builder.BuildProject();
        var action = new OverlayAction(project) { Args = ["create", "\\DomainException"] };
        action.Start(CancellationToken.None);
        var again = new OverlayAction(project) { Args = ["create", "\\DomainException"] };
        again.Start(CancellationToken.None);

        var overlayGlobs = OverlayGlobs(project.GetProjectPath());
        overlayGlobs.Should().Equal(
            "./_tyhpdef/overlays/stubs/*.tyhpdef",
            "./_tyhpdef/overlays/*.tyhpdef");
    }

    [Fact]
    public void Create_DoesNotInsertHandWrittenGlobBeforeStubs()
    {
        using var builder = FixtureWithStubsGlobOnly();
        var project = builder.BuildProject();
        new OverlayAction(project) { Args = ["create", "\\DomainException"] }.Start(CancellationToken.None);

        var overlayGlobs = OverlayGlobs(project.GetProjectPath())
            .Select(e => TyhpdefOverlayManifestEditor.NormalizeGlob(e))
            .ToList();
        overlayGlobs[0].Should().Be("_tyhpdef/overlays/stubs/*.tyhpdef");
        overlayGlobs[^1].Should().Be("_tyhpdef/overlays/*.tyhpdef");
    }

    [Fact]
    public void Stamp_WritesOverlayAgainstForKnownFqn()
    {
        using var builder = Fixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/DomainException.tyhpdef", """
            <?tyhpdef
            class \DomainException {
                public function getMessage(): string;
            }
            """);
        var project = builder.BuildProject();
        var action = new OverlayAction(project, new ScriptedPhpRuntimeManager()) { Args = ["stamp", "\\DomainException"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var overlayPath = Path.Combine(project.GetProjectPath(), "pkg", "_tyhpdef", "overlays", "DomainException.tyhpdef");
        File.ReadAllText(overlayPath).Should().Contain("@overlay-against:");
    }

    [Fact]
    public void Stamp_WritesOverlayAgainstForStubOverlay()
    {
        using var builder = Fixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/DomainException.tyhpdef", """
            <?tyhpdef
            partial class \DomainException {
                public function getMessage(): string;
            }
            """);
        var project = builder.BuildProject();
        var action = new OverlayAction(project, new ScriptedPhpRuntimeManager()) { Args = ["stamp", "\\DomainException"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var overlayPath = Path.Combine(
            project.GetProjectPath(),
            "pkg",
            "_tyhpdef",
            "overlays",
            "stubs",
            "DomainException.tyhpdef");
        File.ReadAllText(overlayPath).Should().Contain("@overlay-against:");
    }

    [Fact]
    public void Create_UnknownFqn_ReportsOverlayTargetNotFound()
    {
        using var builder = Fixture();
        var project = builder.BuildProject();
        var action = new OverlayAction(project) { Args = ["create", "\\DoesNotExist"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.OverlayTargetNotFound);
    }

    [Fact]
    public void Create_ExternType_ReportsOverlayCreateOnExternAndDoesNotWriteFile()
    {
        using var builder = ExternFixture();
        var project = builder.BuildProject();
        var action = new OverlayAction(project) { Args = ["create", "\\ExternNs\\Peer"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.OverlayCreateOnExtern);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.OverlayTargetNotFound);

        var overlayDir = Path.Combine(project.GetProjectPath(), "pkg", "_tyhpdef", "overlays");
        var overlayPath = Path.Combine(overlayDir, "Peer.tyhpdef");
        File.Exists(overlayPath).Should().BeFalse();
        if (Directory.Exists(overlayDir))
        {
            Directory.GetFiles(overlayDir, "*.tyhpdef", SearchOption.AllDirectories)
                .Should()
                .BeEmpty("create must not copy an extern into a hollow class overlay");
        }
    }

    [Fact]
    public void Stamp_ExternDeclaration_IsNoOp()
    {
        using var builder = ExternFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/peer.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\Peer;
            """);
        var project = builder.BuildProject();
        var action = new OverlayAction(project, new ScriptedPhpRuntimeManager()) { Args = ["stamp", "\\ExternNs\\Peer"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var overlayPath = Path.Combine(
            project.GetProjectPath(),
            "pkg",
            "_tyhpdef",
            "overlays",
            "peer.tyhpdef");
        File.ReadAllText(overlayPath).Should().NotContain("@overlay-against:");
    }

    [Fact]
    public void InvalidSubcommand_ReportsOverlayInvalidArguments()
    {
        using var builder = Fixture();
        var project = builder.BuildProject();
        var action = new OverlayAction(project) { Args = ["explode"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.OverlayInvalidArguments);
    }

    [Fact]
    public void Stamp_HonorsPhpGatesPerManagedRuntime()
    {
        using var builder = Fixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", GatedBaseline);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/gated.tyhpdef", GatedOverlay);
        var project = builder.BuildProject();
        var runtimes = new ScriptedPhpRuntimeManager(new Dictionary<string, string>
        {
            ["8.2"] = "8.2.15",
            ["8.3"] = "8.3.20",
            ["8.4"] = "8.4.99",
            ["8.5"] = "8.5.8",
        });
        var action = new OverlayAction(project, runtimes) { Args = ["stamp"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        runtimes.Requested.Should().Equal(PhpRuntimeVersion.SupportedMinors);

        var text = File.ReadAllText(Path.Combine(
            project.GetProjectPath(),
            "pkg",
            "_tyhpdef",
            "overlays",
            "gated.tyhpdef"));

        text.Should().Contain("// @overlay-against: function stampfix_always(): void");
        text.Should().Contain("// @overlay-against: function stampfix_gated(): int");
        text.Should().Contain("// @overlay-against: function stampfix_gated(): string");
        text.Should().Contain("// @overlay-against: function stampfix_exact(): bool");
        text.Should().Contain("// @overlay-against: function stampfix_runtime(): float");
        text.Should().Contain("// @overlay-against: function stampfix_runtime(): bool");
        text.Should().NotContain("// @overlay-against: function stampfix_runtime(): int");
        text.Should().Contain("// @overlay-against: function stampfix_attr_exact(): int");
        text.Should().Contain("// @overlay-against: function stampfix_attr_min(): string");
        text.Should().Contain("// @overlay-against: function attrGated(): bool");
        text.Should().Contain("// @overlay-against: function attrGated(): string");
        text.Should().NotContain("WRONG");
    }

    [Fact]
    public void Stamp_MissingRuntime_DoesNotStampThatVersionFromAnotherPhp()
    {
        using var builder = Fixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function stampfix_always(): void;
            declare(php="<8.5") {
                function stampfix_future(): int;
            }
            declare(php=">=8.5") {
                function stampfix_future(): string;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/gated.tyhpdef", """
            <?tyhpdef
            function stampfix_always(): void;
            declare(php="<8.5") {
                function stampfix_future(): int;
            }
            declare(php=">=8.5") {
                // @overlay-against: function stampfix_future(): KEEPME
                function stampfix_future(): string;
            }
            """);
        var project = builder.BuildProject();
        var runtimes = new ScriptedPhpRuntimeManager(unavailable: ["8.5"]);
        var action = new OverlayAction(project, runtimes) { Args = ["stamp"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.OverlayPhpRuntimeUnavailable
            && d.FormatParams.Contains("8.5"));
        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.OverlayPhpRuntimeUnavailable
            && d.FormatParams.Contains("8.2"));
        runtimes.Requested.Should().Equal(PhpRuntimeVersion.SupportedMinors);

        var text = File.ReadAllText(Path.Combine(
            project.GetProjectPath(),
            "pkg",
            "_tyhpdef",
            "overlays",
            "gated.tyhpdef"));
        text.Should().Contain("// @overlay-against: function stampfix_always(): void");
        text.Should().Contain("// @overlay-against: function stampfix_future(): int");
        text.Should().Contain("// @overlay-against: function stampfix_future(): KEEPME");
        text.Should().NotContain("// @overlay-against: function stampfix_future(): string");
    }

    [Fact]
    public void Stamp_RuntimeReportingAnotherMinor_FailsThatPass()
    {
        using var builder = Fixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            declare(php="8.4.99") {
                function stampfix_mismatch(): float;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/gated.tyhpdef", """
            <?tyhpdef
            declare(php="8.4.99") {
                // @overlay-against: function stampfix_mismatch(): sentinel
                function stampfix_mismatch(): float;
            }
            """);
        var project = builder.BuildProject();
        var runtimes = new ScriptedPhpRuntimeManager(new Dictionary<string, string>
        {
            ["8.4"] = "8.3.9",
        });
        var action = new OverlayAction(project, runtimes) { Args = ["stamp"] };
        var result = action.Start(CancellationToken.None);

        result!.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.OverlayPhpRuntimeUnavailable
            && d.FormatParams.Contains("8.4"));
        var text = File.ReadAllText(Path.Combine(
            project.GetProjectPath(),
            "pkg",
            "_tyhpdef",
            "overlays",
            "gated.tyhpdef"));
        text.Should().Contain("// @overlay-against: function stampfix_mismatch(): sentinel");
        text.Should().NotContain("// @overlay-against: function stampfix_mismatch(): float");
    }

    private const string GatedBaseline = """
        <?tyhpdef
        function stampfix_always(): void;

        declare(php="<8.4") {
            function stampfix_gated(): int;
        }
        declare(php=">=8.4") {
            function stampfix_gated(): string;
        }

        declare(php="8.3") {
            function stampfix_exact(): bool;
        }

        declare(php="8.4.0") {
            function stampfix_runtime(): int;
        }
        declare(php="8.4.99") {
            function stampfix_runtime(): float;
        }

        #[\Tyhp\Php("8.3")]
        function stampfix_attr_exact(): int;

        #[\Tyhp\Php(">=8.4")]
        function stampfix_attr_min(): string;

        class StampFixBox {
            #[\Tyhp\Php("8.3")]
            public function attrGated(): bool;
            #[\Tyhp\Php(">=8.4")]
            public function attrGated(): string;
        }
        """;

    private const string GatedOverlay = """
        <?tyhpdef
        function stampfix_always(): void;

        declare(php="<8.4") {
            // @overlay-against: function stampfix_gated(): WRONG
            function stampfix_gated(): int;
        }
        declare(php=">=8.4") {
            // @overlay-against: function stampfix_gated(): WRONG
            function stampfix_gated(): string;
        }

        declare(php="8.3") {
            // @overlay-against: function stampfix_exact(): WRONG
            function stampfix_exact(): bool;
        }

        declare(php="8.4.0") {
            // @overlay-against: function stampfix_runtime(): bool
            function stampfix_runtime(): int;
        }
        declare(php="8.4.99") {
            function stampfix_runtime(): float;
        }

        #[\Tyhp\Php("8.3")]
        // @overlay-against: function stampfix_attr_exact(): WRONG
        function stampfix_attr_exact(): int;

        #[\Tyhp\Php(">=8.4")]
        // @overlay-against: function stampfix_attr_min(): WRONG
        function stampfix_attr_min(): string;

        class StampFixBox {
            #[\Tyhp\Php("8.3")]
            // @overlay-against: function attrGated(): WRONG
            public function attrGated(): bool;
            #[\Tyhp\Php(">=8.4")]
            // @overlay-against: function attrGated(): WRONG
            public function attrGated(): string;
        }
        """;

    private sealed class ScriptedPhpRuntimeManager : PhpRuntimeManager
    {
        private readonly Dictionary<string, string> _versions;
        private readonly HashSet<string> _unavailable;

        public ScriptedPhpRuntimeManager(
            IReadOnlyDictionary<string, string>? versions = null,
            IEnumerable<string>? unavailable = null)
        {
            this._versions = versions == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(versions, StringComparer.Ordinal);
            this._unavailable = new HashSet<string>(unavailable ?? [], StringComparer.Ordinal);
        }

        public List<string> Requested { get; } = [];

        public override PhpRuntimeInfo? Ensure(
            string minor,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken = default)
        {
            this.Requested.Add(minor);
            if (this._unavailable.Contains(minor))
            {
                return null;
            }

            var version = this._versions.TryGetValue(minor, out var configured)
                ? configured
                : minor + ".1";
            return new PhpRuntimeInfo
            {
                Path = "/tyhp-managed-php/" + minor + "/php",
                Version = version,
                IniPath = "/tyhp-managed-php/" + minor + "/tyhp.ini",
                IsManaged = true,
                Provider = "test",
            };
        }
    }

    private static TestProjectBuilder Fixture()
    {
        var builder = FixtureWithStubsGlobOnly();
        builder.WithTyhpFile("pkg/composer.json", """
            {
                "name": "acme/pkg",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./_tyhpdef/*.tyhpdef"],
                            "overlay": [
                                "./_tyhpdef/overlays/stubs/*.tyhpdef",
                                "./_tyhpdef/overlays/*.tyhpdef"
                            ]
                        }
                    }
                }
            }
            """);
        return builder;
    }

    private static TestProjectBuilder FixtureWithStubsGlobOnly()
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpFile("pkg/composer.json", """
            {
                "name": "acme/pkg",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./_tyhpdef/*.tyhpdef"],
                            "overlay": [
                                "./_tyhpdef/overlays/stubs/*.tyhpdef"
                            ]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        return builder;
    }

    private static TestProjectBuilder ExternFixture()
    {
        var builder = Fixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\Peer;
            """);
        return builder;
    }

    private static List<string?> OverlayGlobs(string projectPath)
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(projectPath, "pkg", "composer.json")));
        return manifest.RootElement
            .GetProperty("extra")
            .GetProperty("tyhp")
            .GetProperty("package")
            .GetProperty("overlay")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
    }
}
