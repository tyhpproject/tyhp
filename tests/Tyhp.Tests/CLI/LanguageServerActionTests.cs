using Microsoft.Extensions.Configuration;
using Nerdbank.Streams;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Enums;
using Tyhp.LanguageServer;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Trait("Category", "LanguageServer")]
public class LanguageServerActionTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task Start_Stdio_ListensThenStopsOnCancel()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root);
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        using var action = new LanguageServerAction(project, serverStream, serverStream);
        using var cts = new CancellationTokenSource();
        await using var clientOwner = clientStream;

        try
        {
            Environment.ExitCode = 0;
            var run = Task.Run(() => action.Start(cts.Token), CancellationToken.None);
            try
            {
                await action.WhenListening.WaitAsync(TestTimeout);
            }
            finally
            {
                cts.Cancel();
                await run.WaitAsync(TestTimeout);
            }

            Environment.ExitCode.Should().Be((int)ExitCode.Success);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_TcpFlag_FailsWithoutListening()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root, new Dictionary<string, string?> { ["tcp"] = "2087" });
        using var action = new LanguageServerAction(project);

        try
        {
            Environment.ExitCode = 0;
            await action.RunAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
            action.WhenListening.IsCompletedSuccessfully.Should().BeFalse();
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public void LanguageServerHelp_DoesNotClaimActionIsUnimplemented()
    {
        Message.LocalizeRaw("CLI_LanguageServerHelpDescription")
            .Should().Contain("Language Server Protocol");
        Message.LocalizeRaw("CLI_LanguageServerHelpOptionStdio")
            .Should().Contain("stdin/stdout");
        Message.LocalizeRaw("CLI_LanguageServerHelpNotAvailable")
            .Should().Contain("not yet implemented");

        var previousOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            DisplayHelp.LanguageServerHelp();
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        string output = captured.ToString();
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        output.Should().Contain("--stdio");
        output.Should().Contain("--tcp");
        output.Should().Contain("--pipe");
        output.Should().Contain("--pid-file");
        output.Should().NotContain(Message.LocalizeRaw("CLI_LanguageServerHelpNotAvailable"));
    }

    private static Project CreateProject(string root, Dictionary<string, string?>? extra = null)
    {
        var configPath = Path.Combine(root, "tyhp.json");
        File.WriteAllText(configPath, "{}\n");

        var settings = new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["quiet"] = "true",
        };

        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build());
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-language-server-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        this._tempDirectories.Add(tempDir);
        return tempDir;
    }

    public void Dispose()
    {
        foreach (var directory in this._tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}
