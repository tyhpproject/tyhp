using FluentAssertions;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Lint;
using Tyhp.TyhpLang.Lint.Fixes;

namespace Tyhp.Tests.Lint;

public class LintFixEngineTests
{
    [Fact]
    public void CreateDefault_RegistersFourPlaceholderFixes()
    {
        var engine = LintFixEngine.CreateDefault();

        engine.RegisteredCodes.Should().HaveCount(4);
        engine.RegisteredCodes.Should().Contain(MessageCode.CheckerVariableTypeRequired);
        engine.RegisteredCodes.Should().Contain(MessageCode.BinderSymbolNotFound);
        engine.RegisteredCodes.Should().Contain(MessageCode.CheckerUnusedImport);
        engine.RegisteredCodes.Should().Contain(MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void PlaceholderFixes_ReturnNotYetImplemented()
    {
        ILintFix[] stubs =
        [
            new AddMissingTypeAnnotationFix(),
            new AddMissingImportFix(),
            new RemoveUnusedImportFix(),
            new SortImportsFix(),
        ];

        var diagnostic = CreateDiagnostic(MessageCode.CheckerUnusedImport);

        foreach (var stub in stubs)
        {
            var result = stub.Apply("<?tyhp\n", diagnostic);
            result.Success.Should().BeFalse();
            result.FailureReason.Should().Be("Not yet implemented");
            result.ModifiedSourceText.Should().BeNull();
        }
    }

    [Fact]
    public void ApplyFixes_WithNoMatchingDiagnostics_ReturnsEmpty()
    {
        var engine = LintFixEngine.CreateDefault();
        var result = new CompilationResult();
        result.Diagnostics.AddError(MessageCode.CheckerTypeMismatch, "a.tyhp", 1, 0);

        var pass = engine.ApplyFixes(result);

        pass.Applications.Should().BeEmpty();
        pass.LoopDetected.Should().BeFalse();
    }

    [Fact]
    public void ApplyFixes_MatchingStub_ReportsFailureWithoutWriting()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"tyhp-lint-fix-{Guid.NewGuid():N}.tyhp");
        const string original = "<?tyhp\nuse Foo\\Bar;\n";
        File.WriteAllText(tempFile, original);

        try
        {
            var engine = LintFixEngine.CreateDefault();
            var result = new CompilationResult();
            result.Diagnostics.AddWarning(MessageCode.CheckerUnusedImport, tempFile, 2, 0);

            var pass = engine.ApplyFixes(result);

            pass.Applications.Should().ContainSingle();
            pass.Applications[0].Result.Success.Should().BeFalse();
            pass.Applications[0].Result.FailureReason.Should().Be("Not yet implemented");
            pass.Applications[0].BackupPath.Should().BeNull();
            File.ReadAllText(tempFile).Should().Be(original);
            Directory.GetFiles(Path.GetDirectoryName(tempFile)!, Path.GetFileName(tempFile) + ".bak.*")
                .Should().BeEmpty();
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ApplyFixes_DetectsLoop_WhenPreviouslyAppliedLocationReappears()
    {
        var engine = LintFixEngine.CreateDefault();
        var result = new CompilationResult();
        result.Diagnostics.AddWarning(MessageCode.CheckerUnusedImport, "a.tyhp", 3, 4);

        var previously = new HashSet<LintFixLocationKey>
        {
            new("a.tyhp", MessageCode.CheckerUnusedImport, 3, 4),
        };

        var pass = engine.ApplyFixes(result, previously);

        pass.LoopDetected.Should().BeTrue();
        pass.LoopLocation.Should().Be(new LintFixLocationKey("a.tyhp", MessageCode.CheckerUnusedImport, 3, 4));
        pass.Applications.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_DuplicateTargetCodes_Throws()
    {
        var act = () => new LintFixEngine(
        [
            new RemoveUnusedImportFix(),
            new StubFix(MessageCode.CheckerUnusedImport, _ => LintFixResult.Failed("x")),
        ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{MessageCode.CheckerUnusedImport}*");
    }

    [Fact]
    public void ApplyFixes_SuccessfulFix_WritesFileAndBacksUpOriginalOnce()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tyhp-lint-fix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "a.tyhp");
        const string original = "<?tyhp\nline2\n";
        File.WriteAllText(file, original);

        try
        {
            // Two diagnostics in one file so the second write must reuse the first backup.
            var engine = new LintFixEngine(
            [
                new StubFix(
                    MessageCode.CheckerUnusedImport,
                    source => LintFixResult.Succeeded(source + "fix1\n")),
                new StubFix(
                    MessageCode.CheckerDuplicateImport,
                    source => LintFixResult.Succeeded(source + "fix2\n")),
            ]);

            var result = new CompilationResult();
            result.Diagnostics.AddWarning(MessageCode.CheckerUnusedImport, file, 1, 0);
            result.Diagnostics.AddWarning(MessageCode.CheckerDuplicateImport, file, 2, 0);

            var pass = engine.ApplyFixes(result);

            pass.Applications.Should().HaveCount(2);
            pass.Applications.Should().OnlyContain(a => a.Result.Success);

            // Both edits land, so the second fix saw the first fix's output.
            File.ReadAllText(file).Should().Be(original + "fix1\n" + "fix2\n");

            var backups = Directory.GetFiles(tempDir, "a.tyhp.bak.*");
            backups.Should().HaveCount(1, "each file is backed up once per engine run");
            File.ReadAllText(backups[0]).Should().Be(original, "the backup holds the pre-fix original");
            pass.Applications.Select(a => a.BackupPath).Should().AllBe(backups[0]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void CreateBackup_WhenPathTaken_DoesNotOverwriteExistingBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tyhp-lint-fix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "b.tyhp");
        File.WriteAllText(file, "first\n");

        try
        {
            var firstBackup = LintFixEngine.CreateBackup(file);
            File.WriteAllText(file, "second\n");

            // Same second → same timestamp; the original backup must survive.
            var secondBackup = LintFixEngine.CreateBackup(file);

            secondBackup.Should().NotBe(firstBackup);
            File.ReadAllText(firstBackup).Should().Be("first\n");
            File.ReadAllText(secondBackup).Should().Be("second\n");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IDiagnostic CreateDiagnostic(MessageCode code)
    {
        var bag = new DiagnosticBag();
        bag.AddWarning(code, "stub.tyhp", 1, 0);
        return bag.All.First();
    }

    private sealed class StubFix(MessageCode targetCode, Func<string, LintFixResult> apply) : ILintFix
    {
        public MessageCode TargetCode => targetCode;

        public string Description => $"Stub fix for {targetCode}";

        public LintFixResult Apply(string sourceText, IDiagnostic diagnostic) => apply(sourceText);
    }
}
