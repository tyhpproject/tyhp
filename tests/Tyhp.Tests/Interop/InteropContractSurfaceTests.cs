using System.Text.Json;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Interop;

namespace Tyhp.Tests.Interop;

[Trait("Category", "Conformance")]
[Trait("Category", "Interop")]
public class InteropContractSurfaceTests
{
    [Fact]
    public void CurrentVersion_StaysOne_Story216DidNotBump()
    {
        InteropContract.CurrentVersion.Should().Be(
            1,
            "Story 21.6 is type-system only and must not bump interopContractVersion");
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
}
