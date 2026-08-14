using Tyhp.CLI;
using Tyhp.Domain.Enums;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class CliStartupTests
{
    [Theory]
    [InlineData("-d=x")]
    [InlineData("-q=true")]
    [InlineData("-v=1")]
    public void TryValidateArgs_RejectsShortSwitchWithValue(string badFlag)
    {
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            CliStartup.TryValidateArgs(new[] { "build", badFlag }).Should().BeFalse();
            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public void TryValidateArgs_AllowsLongOptionsAndBareShortFlags()
    {
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            CliStartup.TryValidateArgs(new[] { "build", "--dry-run=true", "-q", "--verbose" })
                .Should().BeTrue();
            Environment.ExitCode.Should().Be(0);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public void TryValidateProjectFile_RejectsMissingExplicitPath()
    {
        var previous = Environment.ExitCode;
        var missing = Path.Combine(Path.GetTempPath(), $"tyhp-missing-{Guid.NewGuid():N}.json");
        try
        {
            Environment.ExitCode = 0;
            CliStartup.TryValidateProjectFile(new[] { "lint", $"--tyhp-project={missing}" })
                .Should().BeFalse();
            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public void TryValidateProjectFile_AllowsExistingExplicitPath()
    {
        var previous = Environment.ExitCode;
        var path = Path.Combine(Path.GetTempPath(), $"tyhp-present-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}\n");
        try
        {
            Environment.ExitCode = 0;
            CliStartup.TryValidateProjectFile(new[] { "version", "--tyhp-project", path })
                .Should().BeTrue();
            Environment.ExitCode.Should().Be(0);
        }
        finally
        {
            File.Delete(path);
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public void IsConfigurationFailure_RecognizesWrappedFormatException()
    {
        var wrapped = new AggregateException(
            new InvalidOperationException("outer", new FormatException("bad json")));

        CliStartup.IsConfigurationFailure(wrapped).Should().BeTrue();
        CliStartup.UnwrapConfigurationFailure(wrapped).Should().BeOfType<FormatException>();
    }
}
