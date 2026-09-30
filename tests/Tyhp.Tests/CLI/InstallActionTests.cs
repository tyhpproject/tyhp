using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class InstallActionTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];
    private readonly int _previousExitCode = Environment.ExitCode;

    [Fact]
    public void OfficialInstallerUrls_AreTheDocumentedGetComposerLocations()
    {
        ComposerInstallerChecksum.InstallerUrl.Should().Be("https://getcomposer.org/installer");
        ComposerInstallerChecksum.SignatureUrl.Should().Be("https://composer.github.io/installer.sig");
    }

    [Fact]
    public void InstallHelp_DocumentsComposerTargetAndLocalDefault()
    {
        Message.LocalizeRaw("CLI_InstallHelpDescription")
            .Should().Contain("tyhp composer install");
        Message.LocalizeRaw("CLI_InstallHelpOptionLocal")
            .Should().Contain("composer.phar");
        Message.LocalizeRaw("CLI_InstallHelpOptionGlobal")
            .Should().Contain(".local/bin/composer");
        Message.LocalizeRaw("CLI_InstallHelpTargetComposer")
            .Should().Contain("Composer");

        DisplayHelp.InstallHelp();
    }

    [Fact]
    public void NoTarget_PrintsHelpAndSucceeds()
    {
        var (action, _) = this.CreateAction([]);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        action.LastError.Should().BeNull();
    }

    [Fact]
    public void UnknownTarget_FailsAndListsComposer()
    {
        var transport = FakeTransport.Matching();
        transport.ThrowIfCalled = true;
        var (action, _) = this.CreateAction(["php"], transport: transport);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallUnknownTarget);
        transport.InstallerCalls.Should().Be(0);
    }

    [Fact]
    public void MissingPhp_FailsWithoutDownloading()
    {
        var transport = FakeTransport.Matching();
        var runner = new FakeSetupRunner { ThrowIfCalled = true };
        var (action, projectDir) = this.CreateAction(
            ["composer"],
            transport,
            runner,
            findPhp: () => null);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallPhpNotFound);
        transport.InstallerCalls.Should().Be(0);
        File.Exists(Path.Combine(projectDir, "composer.phar")).Should().BeFalse();
    }

    [Fact]
    public void AlreadyInstalled_IsNoOpWithoutForce()
    {
        var (action, projectDir) = this.CreateAction(
            ["composer"],
            transport: new FakeTransport { ThrowIfCalled = true },
            runner: new FakeSetupRunner { ThrowIfCalled = true });
        var dest = Path.Combine(projectDir, "composer.phar");
        File.WriteAllText(dest, "existing-composer");

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        action.LastError.Should().BeNull();
        File.ReadAllText(dest).Should().Be("existing-composer");
    }

    [Fact]
    public void Force_ReplacesExistingBinary()
    {
        var transport = FakeTransport.Matching();
        var runner = new FakeSetupRunner { Payload = "replaced-composer" };
        var (action, projectDir) = this.CreateAction(["composer", "--force"], transport, runner);
        var dest = Path.Combine(projectDir, "composer.phar");
        File.WriteAllText(dest, "old-composer");

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        action.LastError.Should().BeNull();
        File.ReadAllText(dest).Should().Be("replaced-composer");
        runner.CallCount.Should().Be(1);
        transport.InstallerCalls.Should().Be(1);
    }

    [Fact]
    public void ShaMismatch_FailsAndLeavesNoBinary()
    {
        var transport = FakeTransport.Matching();
        transport.Signature = "deadbeef";
        var runner = new FakeSetupRunner { ThrowIfCalled = true };
        var (action, projectDir) = this.CreateAction(["composer"], transport, runner);
        var dest = Path.Combine(projectDir, "composer.phar");

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallComposerChecksumMismatch);
        File.Exists(dest).Should().BeFalse();
        runner.CallCount.Should().Be(0);
        File.Exists(Path.Combine(projectDir, "composer-setup.php")).Should().BeFalse();
    }

    [Fact]
    public void ShaMismatch_WithForce_LeavesExistingBinary()
    {
        var transport = FakeTransport.Matching();
        transport.Signature = "00";
        var runner = new FakeSetupRunner { ThrowIfCalled = true };
        var (action, projectDir) = this.CreateAction(["composer", "--force"], transport, runner);
        var dest = Path.Combine(projectDir, "composer.phar");
        File.WriteAllText(dest, "keep-me");

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallComposerChecksumMismatch);
        File.ReadAllText(dest).Should().Be("keep-me");
        runner.CallCount.Should().Be(0);
    }

    [Fact]
    public void LocalInstall_WritesComposerPharInProjectDirectory()
    {
        var transport = FakeTransport.Matching();
        var runner = new FakeSetupRunner { Payload = "local-phar" };
        var (action, projectDir) = this.CreateAction(["composer", "--local"], transport, runner);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        File.ReadAllText(Path.Combine(projectDir, "composer.phar")).Should().Be("local-phar");
        runner.LastFileName.Should().Be("composer.phar");
    }

    [Fact]
    public void GlobalInstall_WritesComposerUnderLocalBin()
    {
        var globalDir = this.CreateTempDirectory();
        var transport = FakeTransport.Matching();
        var runner = new FakeSetupRunner { Payload = "global-composer" };
        var (action, projectDir) = this.CreateAction(
            ["composer", "--global"],
            transport,
            runner,
            globalInstallDirectory: globalDir);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        File.ReadAllText(Path.Combine(globalDir, "composer")).Should().Be("global-composer");
        File.Exists(Path.Combine(projectDir, "composer.phar")).Should().BeFalse();
        runner.LastFileName.Should().Be("composer");
    }

    [Fact]
    public void LocalAndGlobalTogether_FailsWithoutDownloading()
    {
        var transport = FakeTransport.Matching();
        transport.ThrowIfCalled = true;
        var (action, _) = this.CreateAction(["composer", "--local", "--global"], transport: transport);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallConflictingLocationFlags);
    }

    [Fact]
    public void SetupFailure_LeavesNoBinaryBehind()
    {
        var transport = FakeTransport.Matching();
        var runner = new FakeSetupRunner { ExitCode = 2 };
        var (action, projectDir) = this.CreateAction(["composer"], transport, runner);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallComposerSetupFailed);
        File.Exists(Path.Combine(projectDir, "composer.phar")).Should().BeFalse();
    }

    [Fact]
    public void DownloadFailure_LeavesNoBinaryBehind()
    {
        var transport = new FakeTransport
        {
            Installer = [],
            Signature = "abc",
        };
        var runner = new FakeSetupRunner { ThrowIfCalled = true };
        var (action, projectDir) = this.CreateAction(["composer"], transport, runner);

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InstallComposerDownloadFailed);
        File.Exists(Path.Combine(projectDir, "composer.phar")).Should().BeFalse();
    }

    [Fact]
    public void IntegrityHint_PointsAtInstallComposer()
    {
        Message.LocalizeRaw("CLI_IntegrityEnvComposerMissing")
            .Should().Contain("tyhp install composer");
        Message.LocalizeRaw("CLI_ComposerActionNotFound")
            .Should().Contain("tyhp install composer");
    }

    [Fact]
    public void RewriteHelpAlias_InstallHelp_BecomesHelpSubjectInstall()
    {
        var rewritten = ActionConfigProvider.RewriteHelpAlias(
            ActionConfigProvider.ExpandBareBooleanFlags(["install", "--help"]));

        rewritten.Should().Equal("help", "--subject=install");
    }

    [Fact]
    public void BareBooleanFlags_IncludeInstallFlags()
    {
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--force");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--global");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--local");
    }

    [Fact]
    public void TryParseAction_AcceptsInstall()
    {
        ActionConfigProvider.TryParseAction("install", out var action).Should().BeTrue();
        action.Should().Be(Tyhp.Config.Action.install);
    }

    [Fact]
    public void Checksum_DoesNotMatchEmptyOrWhitespaceSignature()
    {
        var bytes = "installer"u8.ToArray();
        ComposerInstallerChecksum.Matches(bytes, "").Should().BeFalse();
        ComposerInstallerChecksum.Matches(bytes, "   ").Should().BeFalse();
        ComposerInstallerChecksum.Matches([], ComposerInstallerChecksum.Sha384Hex(bytes)).Should().BeFalse();
    }

    private (InstallAction Action, string ProjectDir) CreateAction(
        string[] args,
        FakeTransport? transport = null,
        FakeSetupRunner? runner = null,
        Func<string?>? findPhp = null,
        string? globalInstallDirectory = null)
    {
        var projectDir = this.CreateTempDirectory();
        var configPath = Path.Combine(projectDir, "tyhp.json");
        File.WriteAllText(configPath, """{"include":["src/**/*.tyhp"]}""");

        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = configPath,
                ["quiet"] = "true",
            })
            .Build());

        Environment.ExitCode = (int)ExitCode.Success;
        var action = new InstallAction(project)
        {
            Args = args,
            Transport = transport,
            SetupRunner = runner,
            FindPhp = findPhp ?? (() => "/usr/bin/php"),
            ProbeComposerExecutable = File.Exists,
            GlobalInstallDirectory = globalInstallDirectory,
        };
        return (action, projectDir);
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-install-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        this._tempDirectories.Add(tempDir);
        return tempDir;
    }

    public void Dispose()
    {
        Environment.ExitCode = this._previousExitCode;
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

    private sealed class FakeTransport : IComposerInstallerTransport
    {
        public byte[] Installer { get; set; } = "fake-composer-installer"u8.ToArray();

        public string? Signature { get; set; }

        public bool ThrowIfCalled { get; set; }

        public int InstallerCalls { get; private set; }

        public int SignatureCalls { get; private set; }

        public static FakeTransport Matching()
        {
            var installer = "fake-composer-installer"u8.ToArray();
            return new FakeTransport
            {
                Installer = installer,
                Signature = ComposerInstallerChecksum.Sha384Hex(installer),
            };
        }

        public Task<byte[]?> DownloadInstallerAsync(CancellationToken cancellationToken)
        {
            if (this.ThrowIfCalled)
            {
                throw new InvalidOperationException("HTTP must not be used in this test");
            }

            this.InstallerCalls++;
            return Task.FromResult<byte[]?>(this.Installer.Length == 0 ? null : this.Installer);
        }

        public Task<string?> DownloadInstallerSignatureAsync(CancellationToken cancellationToken)
        {
            if (this.ThrowIfCalled)
            {
                throw new InvalidOperationException("HTTP must not be used in this test");
            }

            this.SignatureCalls++;
            return Task.FromResult(this.Signature);
        }
    }

    private sealed class FakeSetupRunner : IComposerSetupRunner
    {
        public int ExitCode { get; set; }

        public int CallCount { get; private set; }

        public string? LastFileName { get; private set; }

        public string Payload { get; set; } = "FAKE_COMPOSER";

        public bool ThrowIfCalled { get; set; }

        public int Run(
            string phpPath,
            string setupScriptPath,
            string installDir,
            string fileName,
            CancellationToken cancellationToken)
        {
            if (this.ThrowIfCalled)
            {
                throw new InvalidOperationException("composer-setup.php must not be run in this test");
            }

            this.CallCount++;
            this.LastFileName = fileName;
            if (this.ExitCode != 0)
            {
                return this.ExitCode;
            }

            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, fileName), this.Payload);
            return 0;
        }
    }
}
