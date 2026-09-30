using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpRuntimeDetectorTests
{
    [Fact]
    public void Inspect_NonexistentPath_ReportsTyhpdefPhpNotFound()
    {
        var detector = new PhpRuntimeDetector();
        var diagnostics = new DiagnosticBag();
        var missing = Path.Combine(Path.GetTempPath(), "tyhp-missing-php", Guid.NewGuid().ToString("N"), "php");

        var info = detector.Inspect(missing, diagnostics);

        info.Should().BeNull();
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPhpNotFound);
    }

    [Fact]
    public void Inspect_DoesNotSearchPathOrHomebrew()
    {
        var detector = new PhpRuntimeDetector();
        var diagnostics = new DiagnosticBag();
        var missing = Path.Combine(Path.GetTempPath(), "definitely-not-on-path-" + Guid.NewGuid().ToString("N"), "php");

        var info = detector.Inspect(missing, diagnostics);

        info.Should().BeNull("Inspect must use the explicit path only, never PATH php");
        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.TyhpdefPhpNotFound);
    }

    [Fact]
    public void ParsePhpVersion_ReadsFirstPhpLine()
    {
        var version = PhpRuntimeDetector.ParsePhpVersion(
            "PHP 8.3.11 (cli) (built: Aug 1 2024)\nCopyright (c) The PHP Group\n");

        version.Should().Be("8.3.11");
    }

    [Fact]
    public void ParseModules_ReadsZendAndPhpSections()
    {
        var modules = PhpRuntimeDetector.ParseModules(
            """
            [PHP Modules]
            Core
            json
            standard

            [Zend Modules]
            Zend OPcache
            """);

        modules.Should().Contain("json");
        modules.Should().Contain("Core");
        modules.Should().Contain("Zend OPcache");
    }
}
