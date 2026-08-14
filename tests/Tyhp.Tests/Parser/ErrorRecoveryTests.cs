using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class ErrorRecoveryTests
{
    [Fact]
    public void Parse_MissingSemicolon_ReportsErrorAndPartialAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n$x = 1\n$y = 2;\n");
        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_UnclosedBrace_ReportsError()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "InvalidTyhp/unclosed_brace.tyhp");
        var result = ParserTestHelper.ParseFile(path);
        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_InvalidType_ParsesButLeavesTypeResolutionToChecker()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "InvalidTyhp/invalid_type.tyhp");
        var result = ParserTestHelper.ParseFile(path);
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_DuplicateClassDeclarations_ParseSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Dup {}
            class Dup {}
            """);
        result.Diagnostics.Errors.Should().BeEmpty("duplicate detection is the binder's responsibility");
        result.Ast.Should().NotBeNull();
    }

    [Theory]
    [InlineData("class Struct { public int $x = 1; }")]
    [InlineData("final class Struct {}")]
    [InlineData("abstract class Struct {}")]
    [InlineData("trait Struct {}")]
    [InlineData("interface Struct {}")]
    [InlineData("enum Struct {}")]
    public void Parse_ReservedKeywordAsTypeName_DoesNotAbortWithNullReference(string declaration)
    {
        // generic-structs #3: reserved keyword as a type name must yield parse/visitor
        // diagnostics, never TYHP1003 NullReferenceException abort.
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n{declaration}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "reserved-keyword type names must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Fact]
    public void CompilationService_ReservedKeywordAsTypeName_DoesNotAbortWithNullReference()
    {
        // Same recovery path as CLI lint / CompilationService.ParseFile (not only Tyhpdef.ParseContent).
        var path = Path.Combine(Path.GetTempPath(), $"tyhp_reserved_struct_{Guid.NewGuid():N}.tyhp");
        File.WriteAllText(path, "<?tyhp\nclass Struct { public int $x = 1; }\n");
        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([path], new CompilationOptions
            {
                EnableAstCache = false,
                SkipChecking = true,
            });

            result.Diagnostics.HasErrors.Should().BeTrue();
            result.Diagnostics.Errors.Should().NotContain(
                d => d.Code == MessageCode.ParserCompileAborted,
                "CompilationService must not convert reserved-keyword recovery into TYHP1003");
            result.ParsedFiles.Should().NotBeEmpty("partial AST should still be collected");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("type Foo")]
    [InlineData("type Struct")]
    [InlineData("extension Foo")]
    [InlineData("extension Struct")]
    [InlineData("struct Foo")]
    public void Parse_TruncatedTypeExtensionOrStructDecl_DoesNotAbortWithNullReference(string declaration)
    {
        // Truncated decls leave required trailing children null after ANTLR recovery;
        // visitors must report VisitorMissingRequiredNode (or other parse diagnostics),
        // never TYHP1003 NullReferenceException abort.
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n{declaration}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated type/extension/struct decls must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Fact]
    public void Parse_TruncatedClassBodyTypeAlias_DoesNotAbortWithNullReference()
    {
        // Same truncated-`type` NRE pattern as the top-level case, but reached through
        // VisitTyhpClassTypeAlias's delegation to VisitTyhpTypeAlias for a class-body alias.
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\nclass Foo { type Bar }\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated class-body type alias must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Fact]
    public void Parse_TruncatedTyhpdefTypeAlias_DoesNotAbortWithNullReference()
    {
        // Same truncated-`type` NRE pattern as the top-level .tyhp case, but reached through
        // the tyhpdef top-statement dispatcher (VisitTyhpdefTypeAliasDecl).
        var result = ParserTestHelper.ParseTyhpdefContent("<?tyhpdef\ntype Foo\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated tyhpdef type alias must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Theory]
    [InlineData("use extension")]
    [InlineData("use extension Foo")]
    [InlineData("function overloadSig(): int")]
    [InlineData("fn shortName(): int =>")]
    [InlineData("type Foo<")]
    [InlineData("struct Foo<")]
    [InlineData("function foo<")]
    [InlineData("int $")]
    [InlineData("$x = new struct")]
    [InlineData("class C { operator }")]
    public void Parse_TruncatedTyhpDeclarationSites_DoesNotAbortWithNullReference(string declaration)
    {
        // Broader unguarded-mandatory-field audit: truncated use-extension / overload /
        // generic-param / typed-var / anon-struct inputs must not escape as TYHP1003.
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n{declaration}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated Tyhp declaration sites must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Theory]
    [InlineData("use extension")]
    [InlineData("use extension Foo")]
    [InlineData("use")]
    [InlineData("use function")]
    [InlineData("class C { const }")]
    [InlineData("class C { public const }")]
    [InlineData("class C { use }")]
    [InlineData("class C { use extension }")]
    [InlineData("class C { extension function }")]
    [InlineData("class C { extension fn }")]
    [InlineData("class C { extension operator }")]
    [InlineData("class C { operator }")]
    [InlineData("type Foo<")]
    public void Parse_TruncatedTyhpdefDeclarationSites_DoesNotAbortWithNullReference(string declaration)
    {
        // Tyhpdef-side siblings of the truncated declaration NRE audit (import extension,
        // class const, trait/extension use, inline extension function/operator, generics).
        var result = ParserTestHelper.ParseTyhpdefContent($"<?tyhpdef\n{declaration}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated tyhpdef declaration sites must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Fact]
    public void Parse_EmptyAssignmentRhs_ReportsOnlyParserUnexpectedToken()
    {
        // Story 14 Phase 5 #1: `$x = ;` used to also emit two TYHP2002 diagnostics naming
        // StatementRequiringTerminalContext from ANTLR recovery stubs. The real finding is
        // the syntax error alone (TYHP1002 via CompilationService / TyhpdefParseError via
        // Tyhpdef.ParseContent).
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(): void {
                $x = ;
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.ErrorCount.Should().Be(1, "exactly one syntax diagnostic — no TYHP2002 leak");
        result.Diagnostics.Errors.Should().ContainSingle(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError,
            "the unexpected `;` must surface as the real parse diagnostic");
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.VisitorUnexpectedAlternative,
            "must not leak TYHP2002 with ANTLR context class names");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Fact]
    public void Parse_IncompleteBinaryExpr_DoesNotLeakVisitorUnexpectedAlternative()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(): void {
                $x = 1 +;
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.VisitorUnexpectedAlternative,
            "recovery stubs for `$x = 1 +;` must not emit TYHP2002");
        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError);
        result.Diagnostics.Errors
            .Count(d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError)
            .Should().Be(1, "duplicate syntax diagnostics from recovery must be de-duplicated");
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_TopLevelEmptyAssignmentRhs_ReportsOnlyParserUnexpectedToken()
    {
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n$x = ;\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.ErrorCount.Should().Be(1);
        result.Diagnostics.Errors.Should().ContainSingle(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError);
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.VisitorUnexpectedAlternative);
        result.Ast.Should().NotBeNull();
    }

    [Theory]
    [InlineData("$x->;")]
    [InlineData("$x?->;")]
    public void Parse_ObjectOperatorWithoutPropertyName_DoesNotStickInPropertyLookupMode(string stmt)
    {
        // Incomplete `->` / `?->` used to leave the lexer permanently in ST_LOOKING_FOR_PROPERTY,
        // cascading TYHP1001: Unknown parser error: 0x0 for the rest of the file. Catch-all
        // ST_LOOKING_FOR_PROPERTY_INVALID pops the mode so recovery stays local.
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\nfunction demo(): void {\n    " + stmt + "\n}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserUnknownError,
            "must not cascade TYHP1001 0x0 from a stuck ST_LOOKING_FOR_PROPERTY mode");
        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError,
            "incomplete object operator must surface a real syntax diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Theory]
    [InlineData("$x->;")]
    [InlineData("$x?->;")]
    public void Parse_ObjectOperatorWithoutPropertyName_DoesNotGlueIntoNextStatement(string stmt)
    {
        // ST_LOOKING_FOR_PROPERTY_INVALID must keep T_ERROR on the default channel, not
        // ErrorLexemChannel: hiding the token would let the parser splice `$y = 1;`'s tokens
        // straight into this member-access expression, silently reinterpreting the malformed
        // `$x->;` as a valid `$x->$y = 1;` assignment with zero diagnostics.
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\nfunction demo(): void {\n    " + stmt + "\n    $y = 1;\n}\n");

        result.Diagnostics.HasErrors.Should().BeTrue(
            "a stray `->;` must never silently glue into the following statement");
        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError,
            "incomplete object operator must surface a real syntax diagnostic even when followed by more code");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Theory]
    [InlineData("$z = $x->y->;")]
    [InlineData("$z = $x->y?->;")]
    [InlineData("$z = $x->y::;")]
    [InlineData("$z = Foo::BAR::;")]
    public void Parse_ChainedMalformedMemberAccess_DoesNotLeakVisitorUnexpectedAlternative(string stmt)
    {
        // Chained malformed member/constant access used to reach VisitMemberNameAlt /
        // VisitMemberConstantNameAlt / VisitMemberInstanceNameAlt on ANTLR recovery stubs and
        // emit TYHP2002 naming MemberNameContext (etc.) alongside the real TYHP1002.
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\nfunction demo(): void {\n    " + stmt + "\n}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.VisitorUnexpectedAlternative,
            "chained malformed member access must not leak TYHP2002 with ANTLR context class names");
        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError,
            "recovery must still surface the real syntax diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Theory]
    [InlineData("class X { use T { Foo::bar as ; } }")]
    [InlineData("class X { use T { Foo::bar insteadof ; } }")]
    public void Parse_MalformedTraitAdaptation_DoesNotLeakVisitorUnexpectedAlternative(string decl)
    {
        // Malformed trait adaptations used to reach VisitTraitAdaptation on an ANTLR recovery
        // stub and emit TYHP2002 naming TraitAdaptationContext alongside the real TYHP1002.
        // Same recovery-stub guard pattern as the memberName / statement Alts.
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n" + decl + "\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.VisitorUnexpectedAlternative,
            "malformed trait adaptation must not leak TYHP2002 with ANTLR context class names");
        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError,
            "recovery must still surface the real syntax diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Fact]
    public void Parse_TruncatedUnionReturnType_DoesNotAbortWithNullReference()
    {
        // generic-structs #3 follow-up: `function demo(): Foo| {}` used to throw
        // NoViableAltException mid-`functionDeclarationStatement`, leaving a wholly empty
        // recovery stub (no Identifier/ParameterList/ReturnType/StatementList). Visiting those
        // null mandatory fields aborted with TYHP1003 (NullReferenceException).
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\nfunction demo(): Foo| {}\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated union return type must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.TyhpdefParseError || d.Code == MessageCode.ParserUnexpectedError,
            "recovery should still surface a real parse diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }

    [Theory]
    [InlineData("function demo(): Foo| {}")]
    [InlineData("function demo(): Foo& {}")]
    [InlineData("function demo(): & {}")]
    [InlineData("function demo(): &Foo {}")]
    [InlineData("function demo(): ? {}")]
    [InlineData("function demo(?): void {}")]
    [InlineData("function demo(? $x): void {}")]
    [InlineData("function demo(Foo| $x): void {}")]
    [InlineData("fn(): Foo| => 1;")]
    [InlineData("fn(?): void => 1;")]
    [InlineData("class C { function m(?): void {} }")]
    [InlineData("class C { function m(): Foo| {} }")]
    public void Parse_TruncatedTypeOrReturnTypeShapes_DoesNotAbortWithNullReference(string declaration)
    {
        // Broader blast radius of the truncated typeExpr / return-type NRE: nullable `?` alone
        // (VisitTypeWithoutStatic → null GrammarAddon), unary `&` recovered as PhpExprAmpersand
        // with null Op/R, and truncated `|` recovered as BinaryOr with a null child that used to
        // hit Antlr Visit(null) → TYHP1003.
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n" + declaration + "\n");

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.ParserCompileAborted,
            "truncated type/return-type shapes must not escape as NullReferenceException / TYHP1003");
        result.Diagnostics.Errors.Select(d => d.Code).Should().Contain(
            code => code == MessageCode.TyhpdefParseError
                || code == MessageCode.ParserUnexpectedError
                || code == MessageCode.VisitorMissingRequiredNode,
            "recovery should still surface a real parse/visitor diagnostic");
        result.Ast.Should().NotBeNull("ANTLR recovery should still produce a partial AST");
    }
}
