using System.Text.Json;
using System.Text.RegularExpressions;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Interop;

namespace Tyhp.Tests.Interop;

[Trait("Category", "Conformance")]
[Trait("Category", "Interop")]
public class InteropContractSurfaceTests
{
    private static readonly Regex NamespacePattern = new(
        @"^namespace\s+(?<ns>[A-Za-z_][A-Za-z0-9_\\]*)\s*[{;]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DeclarationPattern = new(
        @"^(?:(?:final|abstract|readonly)\s+)*(?<kind>class|interface|trait)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEnumerable<object[]> RequiredSymbols()
        => InteropContractSurface.RequiredSymbols.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(RequiredSymbols))]
    public void RequiredSymbol_ExistsInPackageTyhpdef(InteropContractSurface.RequiredSymbol symbol)
    {
        var tyhpdefPath = ResolvePackageTyhpdef(symbol.Package);
        File.Exists(tyhpdefPath).Should().BeTrue($"missing tyhpdef for {symbol.Package}: {tyhpdefPath}");

        var declarations = ParseDeclarations(File.ReadAllText(tyhpdefPath));

        declarations.Should().Contain(
            (symbol.FullyQualifiedName, symbol.DeclarationKeyword),
            $"expected {symbol.DeclarationKeyword} `{symbol.FullyQualifiedName}` in {tyhpdefPath}");
    }

    /// <summary>
    /// Collects (fully-qualified name, keyword) pairs declared in a tyhpdef, tracking the
    /// enclosing <c>namespace</c> so the check verifies the FQN rather than a bare type name.
    /// </summary>
    private static HashSet<(string Fqn, string Keyword)> ParseDeclarations(string text)
    {
        var declarations = new HashSet<(string, string)>();
        var currentNamespace = string.Empty;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0
                || line.StartsWith('*')
                || line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("/*", StringComparison.Ordinal)
                || line.StartsWith('#'))
            {
                continue;
            }

            var namespaceMatch = NamespacePattern.Match(line);
            if (namespaceMatch.Success)
            {
                currentNamespace = namespaceMatch.Groups["ns"].Value.Trim('\\');
                continue;
            }

            var declarationMatch = DeclarationPattern.Match(line);
            if (!declarationMatch.Success)
            {
                continue;
            }

            var name = declarationMatch.Groups["name"].Value;
            var fqn = currentNamespace.Length == 0 ? name : $"{currentNamespace}\\{name}";
            declarations.Add((fqn, declarationMatch.Groups["kind"].Value.ToLowerInvariant()));
        }

        return declarations;
    }

    [Fact]
    public void AllRuntimePackages_StampCurrentInteropContractVersion()
    {
        var packagesRoot = TestFileManager.GetRuntimePackagesDirectory();
        foreach (var packageName in InteropContract.RuntimePackageNames)
        {
            var shortName = packageName.Split('/')[^1];
            var composerPath = Path.Combine(packagesRoot, shortName, "composer.json");
            File.Exists(composerPath).Should().BeTrue(composerPath);

            InteropContract.TryReadVersionFromComposerJson(composerPath, out var version)
                .Should().BeTrue($"missing interopContractVersion on {packageName}");
            version.Should().Be(InteropContract.CurrentVersion, packageName);
        }
    }

    [Fact]
    public void DistBuildScript_DerivesContractStampFromSourceManifest()
    {
        var scriptPath = Path.Combine(
            TestFileManager.GetRuntimePackagesDirectory(),
            "build-common.sh");
        File.Exists(scriptPath).Should().BeTrue(scriptPath);

        var script = File.ReadAllText(scriptPath);
        script.Should().Contain(
            "read_package_interop_contract_version",
            "dist manifests must read the stamp from the source composer.json");
        Regex.IsMatch(script, @"""interopContractVersion""\s*:\s*\d")
            .Should().BeFalse("a hard-coded dist stamp can drift from InteropContract.CurrentVersion");
    }

    [Fact]
    public void ValidateInteropContractVersions_ReportsMismatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "composer.json"), """
                {
                  "name": "tyhp/core",
                  "extra": { "tyhp": { "interopContractVersion": 0 } }
                }
                """);

            var diagnostics = new DiagnosticBag();
            var service = new TyhpLibDistributionService(diagnostics);
            var pathMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tyhp/core"] = tempDir,
            };

            service.ValidateInteropContractVersions(
                ["tyhp/core"],
                outputDirectory: null,
                runtimePackagePathMap: pathMap);

            diagnostics.Errors.Should().ContainSingle(d =>
                d.Code == MessageCode.EmitterInteropContractMismatch);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryReadVersionFromComposerJson_ReadsExtraStamp()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "tyhp-interop-read-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new
            {
                name = "tyhp/core",
                extra = new { tyhp = new { interopContractVersion = InteropContract.CurrentVersion } },
            }));

            InteropContract.TryReadVersionFromComposerJson(tempPath, out var version).Should().BeTrue();
            version.Should().Be(InteropContract.CurrentVersion);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private static string ResolvePackageTyhpdef(string packageName)
    {
        var shortName = packageName.Split('/')[^1];
        return Path.Combine(
            TestFileManager.GetRuntimePackagesDirectory(),
            shortName,
            "package.tyhpdef");
    }
}
