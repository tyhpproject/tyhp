using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20")]
public class TyhpdefValidateServiceTests
{
    [Fact]
    public void Validate_File_ReportsPass()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-val-ok-").FullName;
        try
        {
            var path = Path.Combine(dir, "ok.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                function ping(): void;
                """);
            var result = new TyhpdefGenerationResult();
            new TyhpdefValidateService().Validate(path, result, quiet: true);

            result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.ValidatedFileCount.Should().Be(1);
            result.ValidatedPassedCount.Should().Be(1);
            result.ValidatedFailedCount.Should().Be(0);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Validate_Directory_ReportsFailWithLineErrors()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-val-bad-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "ok.tyhpdef"), "<?tyhpdef\nfunction ping(): void;\n");
            File.WriteAllText(Path.Combine(dir, "bad.tyhpdef"), "<?tyhpdef\nfunction (\n");
            var result = new TyhpdefGenerationResult();
            new TyhpdefValidateService().Validate(dir, result, quiet: true);

            result.Success.Should().BeFalse();
            result.ValidatedFileCount.Should().Be(2);
            result.ValidatedPassedCount.Should().Be(1);
            result.ValidatedFailedCount.Should().Be(1);
            result.ValidatedErrorCount.Should().BeGreaterThan(0);
            result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefParseError);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Validate_MissingPath_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tyhpdef-missing-" + Guid.NewGuid().ToString("N"));
        var result = new TyhpdefGenerationResult();
        new TyhpdefValidateService().Validate(missing, result, quiet: true);
        result.Success.Should().BeFalse();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefGenerationError);
    }

    [Fact]
    public void Discover_FindsNestedTyhpdefs()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-val-nest-").FullName;
        try
        {
            var nested = Path.Combine(dir, "sub");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "a.tyhpdef"), "<?tyhpdef\n");
            File.WriteAllText(Path.Combine(dir, "skip.txt"), "nope");
            TyhpdefValidateService.Discover(dir).Should().ContainSingle(p => p.EndsWith("a.tyhpdef", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
