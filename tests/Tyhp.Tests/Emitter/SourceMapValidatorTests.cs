using System.Text.Json;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter.SourceMap;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class SourceMapValidatorTests
{
    [Fact]
    public void Validate_WellFormedMap_IsValidAndDecodesMappings()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("echo 1;", Provider(1, 0));
        collector.AddNewLine();
        collector.AddContent("echo 2;", Provider(2, 0));
        string json = new SourceMapGenerator("App.php", "src/").Generate(collector);
        const string php = "echo 1;\necho 2;\n";

        var diagnostics = new DiagnosticBag();
        var result = SourceMapValidator.Validate(json, php, diagnostics);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.TotalMappings.Should().Be(2);
        result.GeneratedLineCount.Should().Be(3);
        result.MappedLineCount.Should().Be(2);
        result.DecodedMappings.Should().HaveCount(2);
        result.DecodedMappings[0].Should().Be(new SourceMapping(0, 0, 0, 0, 0));
        result.DecodedMappings[1].Should().Be(new SourceMapping(1, 0, 0, 1, 0));
        diagnostics.HasWarnings.Should().BeFalse();
        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Validate_UnpaddedTrailingGeneratedLines_AreCoverageNotErrors()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        string json = new SourceMapGenerator("App.php").Generate(collector);
        // mappings covers generated line 0 only; two extra PHP lines are trailing/unmapped.
        const string php = "x\n\n";

        var result = SourceMapValidator.Validate(json, php, new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.GeneratedLineCount.Should().Be(3);
        result.MappedLineCount.Should().Be(1);
        JsonDocument.Parse(json).RootElement.GetProperty("mappings").GetString()
            .Should().Be("AAAA");
    }

    [Fact]
    public void Validate_SourceMappingUrlCommentLine_IsIgnoredForLineCount()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("echo 1;", Provider(1, 0));
        string json = new SourceMapGenerator("App.php").Generate(collector);
        string php = SourceMapWriter.AppendSourceMappingUrl("echo 1;\n", "App.php.map");

        php.Should().Contain("sourceMappingURL=");
        var result = SourceMapValidator.Validate(json, php, new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.GeneratedLineCount.Should().Be(SourceMapValidator.CountLines("echo 1;\n"));
        result.Errors.Should().NotContain(e => e.Contains("line groups", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyGeneratedLinesBetweenMappings_AreAccepted()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        collector.AddNewLine();
        collector.AddNewLine();
        collector.AddContent("y", Provider(2, 0));
        string json = new SourceMapGenerator("App.php").Generate(collector);
        const string php = "x\n\ny\n";

        var result = SourceMapValidator.Validate(json, php, new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        JsonDocument.Parse(json).RootElement.GetProperty("mappings").GetString()
            .Should().Be("AAAA;;AACA");
        result.MappedLineCount.Should().Be(2);
    }

    [Fact]
    public void Validate_MalformedJson_DoesNotThrowAndReportsError()
    {
        var diagnostics = new DiagnosticBag();
        var result = SourceMapValidator.Validate("{not json", "<?php\n", diagnostics);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not valid", StringComparison.OrdinalIgnoreCase));
        diagnostics.ToList().Should().Contain(d =>
            d.Code == MessageCode.EmitterSourceMapInvalidMapping
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Validate_EmptyJson_DoesNotThrow()
    {
        var result = SourceMapValidator.Validate("   ", "<?php\n", new DiagnosticBag());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingVersion_IsInvalid()
    {
        const string json = """{"file":"App.php","sources":["App.tyhp"],"mappings":"AAAA"}""";
        var result = SourceMapValidator.Validate(json, "x\n", new DiagnosticBag());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("version", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TruncatedVlq_IsCaught()
    {
        // Continuation bit set with no further characters.
        const string json = """{"version":3,"file":"App.php","sources":["App.tyhp"],"mappings":"g"}""";
        var diagnostics = new DiagnosticBag();
        var result = SourceMapValidator.Validate(json, "x\n", diagnostics);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("VLQ", StringComparison.OrdinalIgnoreCase));
        diagnostics.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Validate_IllegalFieldCount_IsCaught()
    {
        // Two VLQ zeros: 2 fields (not 1, 4, or 5).
        const string json = """{"version":3,"file":"App.php","sources":["App.tyhp"],"mappings":"AA"}""";
        var result = SourceMapValidator.Validate(json, "x\n", new DiagnosticBag());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("fields", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_OneFieldSegment_IsAccepted()
    {
        const string json = """{"version":3,"file":"App.php","sources":["App.tyhp"],"mappings":"A"}""";
        var result = SourceMapValidator.Validate(json, "x\n", new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.TotalMappings.Should().Be(1);
        result.MappedLineCount.Should().Be(1);
        result.DecodedMappings.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SourceIndexOutOfRange_IsCaught()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        string mappings = MappingsOf(collector);
        const string json = """{"version":3,"file":"App.php","sources":[],"mappings":"MAPPINGS"}""";

        var result = SourceMapValidator.Validate(
            json.Replace("MAPPINGS", mappings, StringComparison.Ordinal),
            "x\n",
            new DiagnosticBag(),
            coverageThreshold: 0);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Source index", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NameIndexOutOfRange_IsCaught()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("class", Provider(1, 0), name: "MyClass");
        string mappings = MappingsOf(collector);
        string json = $$"""{"version":3,"file":"App.php","sources":["App.tyhp"],"names":[],"mappings":"{{mappings}}"}""";

        var result = SourceMapValidator.Validate(json, "class\n", new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Name index", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeOriginalPosition_IsCaught()
    {
        // Generated col 0, source 0, original line -1, original col 0 → VLQ 0,0,-1,0 = AADA
        const string json = """{"version":3,"file":"App.php","sources":["App.tyhp"],"mappings":"AADA"}""";
        var result = SourceMapValidator.Validate(json, "x\n", new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("negative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MappingsPastGeneratedFile_IsCaught()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        collector.AddNewLine();
        collector.AddContent("y", Provider(2, 0));
        string json = new SourceMapGenerator("App.php").Generate(collector);

        var result = SourceMapValidator.Validate(json, "x", new DiagnosticBag(), coverageThreshold: 0);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("line groups", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CoverageBelowThreshold_WarnsButStaysValid()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        string json = new SourceMapGenerator("App.php").Generate(collector);
        const string php = "x\n\n\n\n";

        var result = SourceMapValidator.Validate(json, php, new DiagnosticBag(), coverageThreshold: 50);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.CoveragePercentage.Should().BeLessThan(50);
        result.Warnings.Should().Contain(w => w.Contains("coverage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SourcesContentMismatch_IsCaughtWhenProviderIsAvailable()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        string json = new SourceMapGenerator("App.php").Generate(
            collector,
            includeSourcesContent: true,
            sourceContentProvider: _ => "<?tyhp original\n");

        var result = SourceMapValidator.Validate(
            json,
            "x\n",
            new DiagnosticBag(),
            coverageThreshold: 0,
            sourceContentProvider: _ => "<?tyhp different\n");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("sourcesContent", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SourcesContentMatch_Passes()
    {
        const string source = "<?tyhp class App {}\n";
        var collector = new SourceMapCollector("src/App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        string json = new SourceMapGenerator("App.php", "src/").Generate(
            collector,
            includeSourcesContent: true,
            sourceContentProvider: path => path == "src/App.tyhp" ? source : null);

        var result = SourceMapValidator.Validate(
            json,
            "x\n",
            new DiagnosticBag(),
            coverageThreshold: 0,
            sourceContentProvider: path => path is "src/App.tyhp" or "App.tyhp" ? source : null);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validate_NullArguments_Throw()
    {
        var diagnostics = new DiagnosticBag();
        Action nullJson = () => SourceMapValidator.Validate(null!, "x", diagnostics);
        Action nullPhp = () => SourceMapValidator.Validate("{}", null!, diagnostics);
        Action nullBag = () => SourceMapValidator.Validate("{}", "x", null!);

        nullJson.Should().Throw<ArgumentNullException>();
        nullPhp.Should().Throw<ArgumentNullException>();
        nullBag.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CountLines_MatchesNewlinePlusOne()
    {
        SourceMapValidator.CountLines("").Should().Be(0);
        SourceMapValidator.CountLines("x").Should().Be(1);
        SourceMapValidator.CountLines("x\n").Should().Be(2);
        SourceMapValidator.CountLines("x\ny").Should().Be(2);
        SourceMapValidator.CountLines("x\ny\n").Should().Be(3);
    }

    private static string MappingsOf(SourceMapCollector collector)
    {
        string json = new SourceMapGenerator("App.php").Generate(collector);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("mappings").GetString()!;
    }

    private static IBase2Ast Provider(int line, int column) => new TestAst(line, column);

    private sealed class TestAst : Base2Ast
    {
        public TestAst(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }
}
