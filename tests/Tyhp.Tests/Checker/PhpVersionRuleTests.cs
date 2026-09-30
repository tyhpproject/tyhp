using Microsoft.Extensions.Configuration;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story20.5")]
public class PhpVersionRuleTests
{
    [Fact]
    public void HappyPath_ActiveDeclareAndAttribute_No43xx()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4") {
                function gated_block(): void {}
            }

            #[\Tyhp\Php(">=8.4")]
            function gated_attr(): void {}

            function always(): void {}
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().NotContain(
            d => (int)d.Code >= 4300 && (int)d.Code <= 4306,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDefaulted);
    }

    [Fact]
    public void AlternateVariants_AreNotUnreachable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4") {
                function example(): string { return "8.4"; }
            }
            declare(php="<8.4") {
                function example(): string { return "8.2"; }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
    }

    [Fact]
    public void AlternateAttributeVariants_AreNotUnreachable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            function example(string $v): mixed { return $v; }
            #[\Tyhp\Php("<8.4")]
            function example(string $v): mixed { return $v; }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
    }

    [Fact]
    public void InactiveFileLevel_DoesNotTypeCheckSkippedExtensionBody()
    {
        var source = """
            <?tyhp
            declare(php=">=8.4");
            extension GatedOps extends string {
                function length(): int {
                    return \strlen($this);
                }
            }
            """;

        var skipped = CompileAndCheck(source, phpVersion: "8.2");
        skipped.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerThisInStaticContext);
        skipped.Errors.Should().NotContain(d => (int)d.Code >= 4300 && (int)d.Code <= 4306);

        var active = CompileAndCheck(source, phpVersion: "8.4");
        active.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerThisInStaticContext);
        active.Errors.Should().NotContain(
            d => (int)d.Code >= 4300 && (int)d.Code <= 4306,
            string.Join("; ", active.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void NestedUnsatisfiedForThisTarget_IsNotUnreachable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.3") {
                function outer_ok(): void {}
                declare(php=">=8.4") {
                    function inner_need_84(): void {}
                }
            }
            """, phpVersion: "8.3");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void InvalidDeclareConstraint_Reports4300()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php="not-a-constraint!!");
            function should_be_absent(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint).Should().Be(1);
    }

    [Fact]
    public void InvalidAttributeConstraint_Reports4300()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php("not-a-constraint!!")]
            function bad_gate(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint).Should().Be(1);
    }

    [Fact]
    public void InvalidBlockDeclareConstraint_Reports4300()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function always_present(): void {}
            declare(php="not-a-constraint!!") {
                function bad_constraint_fn(): void {}
            }
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint).Should().Be(1);
    }

    [Fact]
    public void MixedDeclare_Reports4301Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4", strict_types=1);
            function still_bound(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionDeclareNotAlone).Should().Be(1);
    }

    [Fact]
    public void NestedUnsatisfiableDeclare_Reports4302()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4") {
                function outer(): void {}
                declare(php="<8.3") {
                    function never(): void {}
                }
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionUnreachable).Should().Be(1);
    }

    [Fact]
    public void NestedUnsatisfiableAttribute_Reports4302()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4") {
                #[\Tyhp\Php("<8.3")]
                function never(): void {}
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionUnreachable).Should().Be(1);
    }

    [Fact]
    public void InactiveDeclareBody_DoesNotTypeCheckOmittedCalls()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4") {
                function only_on_84(): void {
                    this_function_does_not_exist_anywhere();
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
    }

    [Fact]
    public void InactiveCompoundDeclareBody_DoesNotTypeCheckOmittedExtension()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4 <8.5") {
                extension ArrayExtensionsVersioned extends array {
                    function findKey(): mixed {
                        return \this_function_does_not_exist_anywhere($this);
                    }
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerThisInStaticContext);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
    }

    [Fact]
    public void ActiveCompoundDeclareBody_StillTypeChecksCalls()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4 <8.5") {
                function only_on_84(): void {
                    this_function_does_not_exist_anywhere();
                }
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void NestedUnsatisfiableDeclare_WhenOuterInactiveForTarget_StillReports4302()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.4") {
                function outer(): void {}
                declare(php="<8.3") {
                    function never(): void {}
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
    }

    [Fact]
    public void OverlappingSameName_Reports4303Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.2")]
            function foo(): void {}
            #[\Tyhp\Php(">=8.3")]
            function foo(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration).Should().Be(1);
    }

    [Fact]
    public void DistinctSignaturesSameGate_DoesNotReport4303()
    {
        var diagnostics = CompileAndCheckFiles(
            phpVersion: "8.3",
            requireNoBindErrors: true,
            ("test.tyhpdef", """
                <?tyhpdef
                #[\Tyhp\Php(">=8.3")]
                function foo(int $a): int;
                #[\Tyhp\Php(">=8.3")]
                function foo(string $a): string;
                """));

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
    }

    [Fact]
    public void OverlappingStaticAndInstanceMethods_DistinctShapes_Reports4303()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                #[\Tyhp\Php(">=8.0")]
                public function bar(int $a): void {}
                #[\Tyhp\Php(">=8.0")]
                public static function bar(string $a, int $b): void {}
            }
            """, phpVersion: "8.3");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration).Should().Be(1);
    }

    [Fact]
    public void OverlappingStaticMethods_DistinctShapes_DoesNotReport4303()
    {
        var diagnostics = CompileAndCheckFiles(
            phpVersion: "8.3",
            requireNoBindErrors: true,
            ("test.tyhpdef", """
                <?tyhpdef
                class Host {
                    #[\Tyhp\Php(">=8.3")]
                    public static function example(int $a): void;
                    #[\Tyhp\Php(">=8.3")]
                    public static function example(string $a, int $b): void;
                }
                """));

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
    }

    [Fact]
    public void DeprecatedBoolArgOverload_CallSiteWarnsOnlyOnBoolArg()
    {
        var diagnostics = CompileAndCheckFiles(
            phpVersion: "8.5",
            requireNoBindErrors: true,
            ("listed.tyhpdef", """
                <?tyhpdef
                declare(php=">=8.5") {
                    function listed(): array;
                    deprecated function listed(bool $exclude_disabled): array;
                }
                """),
            ("use.tyhp", """
                <?tyhp
                function demo(): void {
                    listed();
                    listed(true);
                }
                """));

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerDeprecatedUsage)
            .Should().Be(1, string.Join("; ", diagnostics.Warnings.Select(w => $"{w.Code}: {w.Message}")));
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void DifferentExtensions_SameMemberName_DoNotReport4303(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Tyhp;

            extension StringOps extends string {
                function first(int $length = 1): string {
                    return $this;
                }
            }

            declare(php=">=8.4") {
                extension ArrayOps extends array {
                    function first(): mixed {
                        return null;
                    }
                }
            }
            """, phpVersion: phpVersion);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
    }

    [Fact]
    public void SameExtension_OverlappingMemberGates_Reports4303()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Tyhp;

            extension StringOps extends string {
                #[\Tyhp\Php(">=8.2")]
                function first(): string {
                    return $this;
                }

                #[\Tyhp\Php(">=8.3")]
                function first(): string {
                    return $this;
                }
            }
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
    }

    [Fact]
    public void AttributeOnStruct_Reports4304Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            type Point = struct { int $x = 0; };
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget).Should().Be(1);
    }

    [Fact]
    public void AttributeOnExtension_Reports4304Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            extension StringOps extends string {
                function length(): int {
                    return \strlen($this);
                }
            }
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget).Should().Be(1);
    }

    [Fact]
    public void AttributeOnTyhpdefExtension_Reports4304Once()
    {
        var diagnostics = CompileAndCheckFiles(
            phpVersion: "8.2",
            requireNoBindErrors: false,
            ("test.tyhpdef", """
                <?tyhpdef
                #[\Tyhp\Php(">=8.4")]
                extension UriStringExtensions extends string {
                    fn parse(): string => \parse_url($this, \PHP_URL_PATH) ?? '';
                }
                """));

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget).Should().Be(1);
    }

    [Fact]
    public void MissingVersionArgument_Reports4305Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php]
            function still_bound(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument).Should().Be(1);
    }

    [Fact]
    public void NonStringVersionArgument_Reports4305Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(84)]
            function still_bound(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument).Should().Be(1);
    }

    [Fact]
    public void NonLiteralVersionArgument_Reports4305Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function version_of(): string { return ">=8.4"; }

            #[\Tyhp\Php(version_of())]
            function still_bound(): void {}
            """, phpVersion: "8.2", requireNoBindErrors: false);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument).Should().Be(1);
    }

    [Fact]
    public void UnsetPhpVersion_Warns4306OnceAcrossFiles()
    {
        var diagnostics = CompileAndCheckFiles(
            phpVersion: "",
            requireNoBindErrors: true,
            ("a.tyhp", """
                <?tyhp
                function one(): void {}
                """),
            ("b.tyhp", """
                <?tyhp
                function two(): void {}
                """));

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerPhpVersionDefaulted).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDefaulted);
    }

    [Fact]
    public void ExplicitPhpVersion_DoesNotWarn4306()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function ok(): void {}
            """, phpVersion: "8.2");

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDefaulted);
    }

    [Fact]
    public void MissingProjectOutputPhpVersion_DefaultsTo82AndFlagsChecker()
    {
        // A real `tyhp.json` with no `output.phpVersion` key must default to 8.2 (not 8.4) and
        // flow the "defaulted" flag through to the checker options so 4306 fires (Story 20.5
        // Phase 5/7 boundary: the default itself belongs to Phase 5, not just the warning).
        var project = new Tyhp.Config.Project(new ConfigurationBuilder().Build());
        var compilationOptions = CompilationOptions.FromProject(project);

        compilationOptions.PhpVersion.Should().Be("8.2");
        compilationOptions.PhpVersionWasDefaulted.Should().BeTrue();
        compilationOptions.Checker.PhpVersion.Should().Be("8.2");
        compilationOptions.Checker.PhpVersionWasDefaulted.Should().BeTrue();
    }

    [Fact]
    public void ActiveGateBlockInsideFunction_AssignmentsFlowOut()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function pick(): string {
                ?string $value = null;
                declare(ext="!intl") {
                    $value = 'plain';
                }
                declare(php=">=8.0") {
                    $value = $value . '!';
                }
                return $value;
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void GatedTopLevelConstant_ActiveVariant_DoesNotReportAttributeTargetMismatch()
    {
        // `\Tyhp\Php`'s #[\Attribute] meta must allow TARGET_CONSTANT (top-level/namespace
        // `const`, distinct from TARGET_CLASS_CONSTANT) — otherwise an active gate on a
        // top-level constant is wrongly rejected by AttributeRule's TARGET_* check (4127).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.2")]
            const FOO = 1;

            function use_it(): int { return FOO; }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    private static DiagnosticBag CompileAndCheck(
        string content,
        string phpVersion = "8.2",
        bool requireNoBindErrors = true)
        => CompileAndCheckFiles(phpVersion, requireNoBindErrors, ("test.tyhp", content));

    private static DiagnosticBag CompileAndCheckFiles(
        string phpVersion,
        bool requireNoBindErrors,
        params (string FileName, string Content)[] files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePaths = new List<string>();
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: phpVersion,
                skipChecking: true,
                configure: o => o.Checker = new CheckerOptions
                {
                    PhpVersion = string.IsNullOrWhiteSpace(phpVersion)
                        ? CompilationOptions.DefaultPhpVersionWhenUnset
                        : phpVersion,
                });
            var result = compilationService.ParseFiles(filePaths, options);
            if (requireNoBindErrors)
            {
                var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
                bindErrors.Should().BeEmpty(
                    $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");
            }

            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics,
                symbolTree,
                result.GlobalScope!,
                options.Checker);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
