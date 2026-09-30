using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefOverlayLoadTests
{
    [Fact]
    public void OverlayArrayOrder_LaterFileWinsForSameTyhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/a_first.tyhpdef", """
            <?tyhpdef
            function overlay_target(): int;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/z_last.tyhpdef", """
            <?tyhpdef
            function overlay_target(): string;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var fn = FindFunction(global, "overlay_target");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("string");
    }

    [Fact]
    public void StubsListedBeforeHandWritten_HandWrittenWins()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/overlay_target.tyhpdef", """
            <?tyhpdef
            function overlay_target(): int;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/overlay_target.tyhpdef", """
            <?tyhpdef
            function overlay_target(): string;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var fn = FindFunction(global, "overlay_target");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("string");
    }

    [Fact]
    public void FunctionAlias_AddsTyhpNameAndLeavesPhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/alias.tyhpdef", """
            <?tyhpdef
            function \call_user_func as call_user_func_unsafe(callable $callback): mixed;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        FindFunction(global, "call_user_func").Should().NotBeNull();
        FindFunction(global, "call_user_func_unsafe").Should().NotBeNull();
    }

    [Fact]
    public void FunctionAlias_MatchingStampAgainstPhpOriginal_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/alias.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function call_user_func(callable $callback): mixed
            function \call_user_func as call_user_func_unsafe(callable $callback): mixed;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void FunctionAlias_VariadicDotsInStamp_MatchLayer1WithoutDots()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/variadic.tyhpdef", """
            <?tyhpdef
            function \keep_alias_src(mixed ...$args): mixed;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/alias_variadic.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function keep_alias_src(mixed ...$args): mixed
            function \keep_alias_src as keep_alias_unsafe(mixed ...$args): mixed;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void NullableType_StampKeepsQuestionMark()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/nullable.tyhpdef", """
            <?tyhpdef
            function take(?string $value): ?\PhpParser\Node;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/nullable.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function take(?string $value): ?\PhpParser\Node
            function take(?string $value): ?\PhpParser\Node;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void FunctionAlias_StaleStamp_StillWarns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/alias.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function call_user_func(): void
            function \call_user_func as call_user_func_unsafe(callable $callback): mixed;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void MemberStamp_MatchingCompactConstructor_DoesNotWarnWhenAnotherTypeSharesTheName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/ctors.tyhpdef", """
            <?tyhpdef
            final class Closure {
                private function __construct(): void;
                public function __invoke(): mixed;
            }
            class Error {
                public function __construct(string $message = ""): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/closure.tyhpdef", """
            <?tyhpdef
            // @overlay-against: class Closure
            final partial class Closure {
                // @overlay-against: function __construct(): void
                private function __construct(): void;
                // @overlay-against: function __invoke(): mixed
                public function __invoke(mixed ...$args): mixed;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void MemberStamp_StaleConstructorStamp_StillWarns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/ctors.tyhpdef", """
            <?tyhpdef
            final class Closure {
                private function __construct(): void;
            }
            class Error {
                public function __construct(string $message = ""): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/closure.tyhpdef", """
            <?tyhpdef
            final partial class Closure {
                // @overlay-against: function __construct(string $message): void
                private function __construct(): void;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void MemberStamp_MethodMatchingConstName_UsesMethodStampNotConst()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/logger.tyhpdef", """
            <?tyhpdef
            class Logger {
                public const int DEBUG ?? 100;
                public function debug(string $message, array $context = []): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/logger.tyhpdef", """
            <?tyhpdef
            partial class Logger {
                // @overlay-against: function debug(string $message, array $context): void
                public function debug(string $message, array $context = []): void;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void TypeStamp_ClassMatchingGlobalConstName_UsesClassStampNotConst()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/phar.tyhpdef", """
            <?tyhpdef
            const int PHAR ?? 1;
            class Phar {
                public function running(): string;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/phar.tyhpdef", """
            <?tyhpdef
            // @overlay-against: class Phar
            partial class Phar {
                // @overlay-against: function running(): string
                public function running(): string;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void FunctionStamp_NullableParam_OmittingQuestionMark_Warns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/nullable.tyhpdef", """
            <?tyhpdef
            function set_loader(?callable $resolver): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/nullable.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function set_loader(callable $resolver): bool
            function set_loader(?callable $resolver): bool;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void NullableUnion_StampUsesNullArm()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/nullable.tyhpdef", """
            <?tyhpdef
            function take(?(string|int) $value): void;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/nullable.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function take(string|int|null $value): void
            function take(?(string|int) $value): void;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void NullableIntersection_StampKeepsAmpersandThenNull()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/nullable.tyhpdef", """
            <?tyhpdef
            interface Alpha {}
            interface Beta {}
            function take(?(Alpha&Beta) $value): void;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/nullable.tyhpdef", """
            <?tyhpdef
            interface Alpha {}
            interface Beta {}
            // @overlay-against: function take(Alpha&Beta|null $value): void
            function take(?(Alpha&Beta) $value): void;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void NullableNameContainingNull_StampKeepsQuestionMark()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/nullable.tyhpdef", """
            <?tyhpdef
            function take(?NullLogger $value): void;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/nullable.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function take(?NullLogger $value): void
            function take(?NullLogger $value): void;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void OmitInInclude_IsError()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/omit_include.tyhpdef", """
            <?tyhpdef
            omit function \call_user_func();
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefOmitOutsideOverlay);
        FindFunction(global, "call_user_func").Should().NotBeNull();
    }

    [Fact]
    public void IncludePartial_StaysAdditive()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/partial_extra.tyhpdef", """
            <?tyhpdef
            partial class \DomainException {
                public function extraMember(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOmitOutsideOverlay);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefIllegalKeywordCombination);

        var type = FindObject(global, "DomainException");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("getMessage");
        type.Members.Should().ContainKey("extraMember");
    }

    [Fact]
    public void OverlayPartial_OmitsListedMemberOnly()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/domain.tyhpdef", """
            <?tyhpdef
            partial class \DomainException {
                omit public function getPrevious();
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var type = FindObject(global, "DomainException");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("getMessage");
        type.Members.Should().NotContainKey("getPrevious");
    }

    [Fact]
    public void OmitFunction_RemovesSymbol()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/omit_map.tyhpdef", """
            <?tyhpdef
            omit function \array_map();
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        FindFunction(global, "array_map").Should().BeNull();
        FindFunction(global, "call_user_func").Should().NotBeNull();
    }

    [Fact]
    public void StampMismatch_WarnsAndStillApplies()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/mismatch.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function array_map(): void
            function \array_map(): string;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var fn = FindFunction(global, "array_map");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("string");
    }

    [Fact]
    public void StampMismatch_Strict_IsErrorAndStillApplies()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/mismatch.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function array_map(): void
            function \array_map(): string;
            """);

        var (global, diagnostics) = Bind(builder, strict: true);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var fn = FindFunction(global, "array_map");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("string");
    }

    [Fact]
    public void OmitMissingSymbol_Warns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/omit_missing.tyhpdef", """
            <?tyhpdef
            omit function \definitely_not_a_real_function();
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOmitMissingSymbol);
    }

    [Fact]
    public void OverlayPartialFunction_MergesAttributesAndKeepsSignature()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/pure_map.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial function \array_map;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefPartialFunctionTargetNotFound);

        var fn = FindFunction(global, "array_map");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("array");
        fn.Parameters.Should().HaveCount(2);
        AttributeNames(fn.DeclaringAstNode).Should().Contain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void OverlayPartialFunction_AfterGenericReplace_KeepsLayer2Types()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/generic_map.tyhpdef", """
            <?tyhpdef
            function \array_map<T>(callable $callback, array $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/pure_map.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial function \array_map;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var fn = FindFunction(global, "array_map");
        fn.Should().NotBeNull();
        fn!.GenericParameters.Should().NotBeEmpty();
        TypeText(fn.ReturnType).Should().Contain("array");
        AttributeNames(fn.DeclaringAstNode).Should().Contain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void OverlayPartialFunction_MissingName_WarnsAndSkips()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/missing_partial_fn.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial function \definitely_not_a_real_function;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialFunctionTargetNotFound);
        FindFunction(global, "array_map").Should().NotBeNull();
        AttributeNames(FindFunction(global, "array_map")!.DeclaringAstNode)
            .Should().NotContain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void PartialFunctionInInclude_IsError()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/partial_fn_include.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial function \array_map;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialFunctionOutsideOverlay);
        AttributeNames(FindFunction(global, "array_map")!.DeclaringAstNode)
            .Should().NotContain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void OverlayPartialFunction_MethodInsidePartialType_MergesAttributes()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/domain_pure.tyhpdef", """
            <?tyhpdef
            partial class \DomainException {
                #[\Tyhp\Optimize\Pure]
                partial function getMessage;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var type = FindObject(global, "DomainException");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("getMessage");
        type.Members.Should().ContainKey("getPrevious");
        var method = type.Members["getMessage"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        TypeText(method.ReturnType).Should().Contain("string");
        AttributeNames(method.DeclaringAstNode).Should().Contain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void OverlayPartialFunction_MemberInsideFullClass_IsError()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/domain_full.tyhpdef", """
            <?tyhpdef
            class \DomainException {
                #[\Tyhp\Optimize\Pure]
                partial function getMessage;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialFunctionMemberOutsidePartialType);
    }

    [Fact]
    public void OverlayPartialFunction_AppliesToEntireOverloadSet()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            function \array_map(callable $callback, array $array, array $arr2): array;
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/pure_map.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial function \array_map;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var fn = FindFunction(global, "array_map");
        fn.Should().NotBeNull();
        fn!.Overloads.Should().NotBeEmpty();
        AttributeNames(fn.DeclaringAstNode).Should().Contain("Tyhp\\Optimize\\Pure");
        foreach (var overload in fn.Overloads)
        {
            AttributeNames(overload.DeclaringAstNode).Should().Contain("Tyhp\\Optimize\\Pure");
        }
    }

    [Fact]
    public void OverlayPartialFunction_NameOnlyStamp_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/pure_map.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            // @overlay-against: function array_map
            partial function \array_map;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void AttributedClassMethod_StampAfterPhpAttribute_DoesNotWarn8022()
    {
        using var builder = OverlayFixture(phpVersion: "8.3");
        builder.WithTyhpFile("pkg/_tyhpdef/node_stub.tyhpdef", """
            <?tyhpdef
            class NodeStub {
                public function getRootNode(?array $options): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/node_stub.tyhpdef", """
            <?tyhpdef
            partial class NodeStub {
                #[\Tyhp\Php(">=8.3")]
                // @overlay-against: function getRootNode(?array $options): void
                public function getRootNode<TOptions extends array>(?TOptions $options): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);

        var type = FindObject(global, "NodeStub");
        type.Should().NotBeNull();
        var method = type!.Members["getRootNode"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        method.GenericParameters.Should().NotBeEmpty();
    }

    [Fact]
    public void UnstampedReplace_GenericParamBoundedByArray_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items<TKey = mixed, TValue = mixed, TArray extends array<TKey, TValue>>(TArray $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_ByRefGenericParamBoundedByArray_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/shuffle_items.tyhpdef", """
            <?tyhpdef
            function \shuffle_items(array &$array): true;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/shuffle_items.tyhpdef", """
            <?tyhpdef
            function \shuffle_items<T, TArray extends array<T> = array<T>>(TArray &$array): true;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_GenericParamBoundedByString_AgainstArray_Warns8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items<T extends string>(T $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_UnconstrainedGenericParam_AgainstArray_Warns8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items<T>(T $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_EmptyBaselineParam_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items($array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_MixedBaselineParam_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(mixed $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_ExactSpelledMatch_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_CyclicGenericConstraints_DoesNotStackOverflowAndWarns8022()
    {
        // Pathological `T extends U, U extends T` constraint cycle: neither substitution
        // ever reaches `array`, so this must terminate (not stack-overflow) and still warn,
        // rather than being accidentally treated as compatible once a cycle is detected.
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items(array $array): array;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/count_items.tyhpdef", """
            <?tyhpdef
            function \count_items<T extends U, U extends T>(T $array): array;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_StaticTrueLiteral_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/flag.tyhpdef", """
            <?tyhpdef
            function \flag(bool $v): void;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/flag.tyhpdef", """
            <?tyhpdef
            function \flag(true $v): void;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void StampedFirstOverload_UnstampedSecondSameName_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/check_kind.tyhpdef", """
            <?tyhpdef
            function \check_kind(mixed $object_or_class, string $class, bool $allow_string = false): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/check_kind.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function check_kind(mixed $object_or_class, string $class, bool $allow_string): bool
            function \check_kind(object $object_or_class, string $class, false $allow_string = false): bool;
            function \check_kind(object|string $object_or_class, string $class, true $allow_string): bool;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);

        var fn = FindFunction(global, "check_kind");
        fn.Should().NotBeNull();
        fn!.Overloads.Should().HaveCount(1);
    }

    [Fact]
    public void UnstampedFirstOverlayReplace_Incompatible_StillWarns8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/check_kind.tyhpdef", """
            <?tyhpdef
            function \check_kind(array $object_or_class, string $class): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/check_kind.tyhpdef", """
            <?tyhpdef
            function \check_kind<T extends string>(T $object_or_class, string $class): bool;
            function \check_kind<T extends string>(T $object_or_class, string $class, true $allow_string): bool;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Warnings.Count(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace)
            .Should().Be(1, "8022 is the replace check; the unstamped overload append must not add a second 8022");
    }

    [Fact]
    public void TwoStampedOverloads_MismatchOnSecond_StillWarns8021()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/check_kind.tyhpdef", """
            <?tyhpdef
            function \check_kind(mixed $object_or_class, string $class, bool $allow_string = false): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/check_kind.tyhpdef", """
            <?tyhpdef
            // @overlay-against: function check_kind(mixed $object_or_class, string $class, bool $allow_string): bool
            function \check_kind(object $object_or_class, string $class, false $allow_string = false): bool;
            // @overlay-against: function check_kind(): void
            function \check_kind(object|string $object_or_class, string $class, true $allow_string): bool;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void StampedMethodOverloads_EachMatchingALayer1Overload_DoesNotWarn8021()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/period.tyhpdef", """
            <?tyhpdef
            class Period {
                public function __construct(int $start, int $interval, int $recurrences, int $options = 0): void;
                public function __construct(int $start, int $interval, string $end, int $options = 0): void;
                public function __construct(string $isostr, int $options = 0): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/period.tyhpdef", """
            <?tyhpdef
            partial class Period {
                // @overlay-against: function __construct(int $start, int $interval, string $end, int $options): void
                public function __construct(mixed $start, int $interval, mixed $end, int $options = 0): void;
                // @overlay-against: function __construct(int $start, int $interval, int $recurrences, int $options): void
                public function __construct(mixed $start, int $interval, int $recurrences, int $options = 0): void;
                // @overlay-against: function __construct(string $isostr, int $options): void
                public function __construct(string $isostr, int $options = 0): void;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void StampedMethodOverload_MatchingNoneOfLayer1Overloads_Warns8021()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/period.tyhpdef", """
            <?tyhpdef
            class Period {
                public function __construct(int $start, int $interval, int $recurrences, int $options = 0): void;
                public function __construct(string $isostr, int $options = 0): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/period.tyhpdef", """
            <?tyhpdef
            partial class Period {
                // @overlay-against: function __construct(): void
                public function __construct(string $isostr, int $options = 0): void;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void UnstampedReplace_NativeTypeTestOnly_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/is_thing.tyhpdef", """
            <?tyhpdef
            function \is_thing(mixed $value): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/is_thing.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\NativeTypeTest]
            function \is_thing(mixed $value): $value is string;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
    }

    [Fact]
    public void UnstampedReplace_AfterStubOverlayParamNarrow_RestoringLayer1_DoesNotWarn8022()
    {
        // Layer 2 stub harvest may tighten a Layer 1 `mixed` parameter (is_callable's
        // `$callable_name`). A later hand overlay that restores mixed, adds a static
        // `false` literal, and a compile-only NativeTypeTest must still be a compatible
        // rewrite of Layer 1 — 8022 compares to Reflection, not to the live Layer 2 symbol.
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/is_callable_like.tyhpdef", """
            <?tyhpdef
            function \is_callable_like(mixed $value, bool $syntax_only = false, mixed &$callable_name = null): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/is_callable_like.tyhpdef", """
            <?tyhpdef
            function \is_callable_like(mixed $value, bool $syntax_only = false, ?string &$callable_name = null): bool;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/is_callable_like.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\NativeTypeTest]
            function \is_callable_like(mixed $value, false $syntax_only = false, mixed &$callable_name = null): $value is callable;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void StampedFirstMethodOverload_UnstampedSecondSameName_DoesNotWarn8022()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/bound.tyhpdef", """
            <?tyhpdef
            final class Bound {
                public function bindTo(object $newThis, string $newScope): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/bound.tyhpdef", """
            <?tyhpdef
            final partial class Bound {
                // @overlay-against: function bindTo(object $newThis, string $newScope): void
                public function bindTo(object $newThis, object $newScope): void;
                public function bindTo(array $newThis, object $newScope): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);

        var type = FindObject(global, "Bound");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("bindTo");
        var bindTo = type.Members["bindTo"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        bindTo.Overloads.Should().HaveCount(1);
    }

    [Fact]
    public void OverlayPartialFunction_FullStampMismatch_StillWarnsAndApplies()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/pure_map.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            // @overlay-against: function array_map(): void
            partial function \array_map;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        AttributeNames(FindFunction(global, "array_map")!.DeclaringAstNode)
            .Should().Contain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void OverlayPartialFunction_AliasOnly_ReplacesTyhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \strtolower(string $string): string;
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/rename.tyhpdef", """
            <?tyhpdef
            partial function \strtolower as str2LC;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        FindFunction(global, "strtolower").Should().BeNull();
        var renamed = FindFunction(global, "str2LC");
        renamed.Should().NotBeNull();
        renamed!.OriginalPhpName.Should().Be("strtolower");
        TypeText(renamed.ReturnType).Should().Contain("string");
        renamed.Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void OverlayPartialFunction_KeepAndAlias_ExposesBothTyhpNames()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \strtoupper(string $string): string;
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/keep_and_alias.tyhpdef", """
            <?tyhpdef
            partial function \strtoupper;
            partial function \strtoupper as str2UC;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        var original = FindFunction(global, "strtoupper");
        var alias = FindFunction(global, "str2UC");
        original.Should().NotBeNull();
        alias.Should().NotBeNull();
        alias!.OriginalPhpName.Should().Be("strtoupper");
        TypeText(original!.ReturnType).Should().Contain("string");
        TypeText(alias.ReturnType).Should().Contain("string");
    }

    [Fact]
    public void OverlayPartialFunction_LaterLayerMatchesCurrentAliasNotPhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \strtolower(string $string): string;
            function \strtoupper(string $string): string;
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/a_layer2.tyhpdef", """
            <?tyhpdef
            partial function \strtolower as str2LC;
            partial function \strtoupper;
            partial function \strtoupper as str2UC;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/z_layer3.tyhpdef", """
            <?tyhpdef
            partial function str2LC as string_to_lower_case;
            partial function str2UC as string_to_upper_case;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefPartialFunctionTargetNotFound);

        FindFunction(global, "string_to_lower_case").Should().NotBeNull();
        FindFunction(global, "string_to_upper_case").Should().NotBeNull();
        FindFunction(global, "strtoupper").Should().NotBeNull();

        FindFunction(global, "strtolower").Should().BeNull();
        FindFunction(global, "str2LC").Should().BeNull();
        FindFunction(global, "str2UC").Should().BeNull();

        FindFunction(global, "string_to_lower_case")!.OriginalPhpName.Should().Be("strtolower");
        FindFunction(global, "string_to_upper_case")!.OriginalPhpName.Should().Be("strtoupper");
    }

    [Fact]
    public void OverlayPartialFunction_LaterLayerMatchingPhpOriginalAfterRename_Warns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \strtolower(string $string): string;
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/a_layer2.tyhpdef", """
            <?tyhpdef
            partial function \strtolower as str2LC;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/z_layer3.tyhpdef", """
            <?tyhpdef
            partial function \strtolower as string_to_lower_case;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialFunctionTargetNotFound);
        FindFunction(global, "str2LC").Should().NotBeNull();
        FindFunction(global, "string_to_lower_case").Should().BeNull();
        FindFunction(global, "strtolower").Should().BeNull();
    }

    [Fact]
    public void OverlayPartialFunction_MethodKeepAndAlias_ExposesBothNames()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/domain_alias.tyhpdef", """
            <?tyhpdef
            partial class \DomainException {
                partial function getMessage;
                partial function getMessage as messageText;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        var type = FindObject(global, "DomainException");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("getMessage");
        type.Members.Should().ContainKey("messageText");
        var alias = type.Members["messageText"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        alias.OriginalPhpName.Should().Be("getMessage");
        TypeText(alias.ReturnType).Should().Contain("string");
    }

    [Fact]
    public void OverlayPartialFunction_MethodAliasOnly_ReplacesTyhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/domain_rename.tyhpdef", """
            <?tyhpdef
            partial class \DomainException {
                partial function getMessage as messageText;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        var type = FindObject(global, "DomainException");
        type.Should().NotBeNull();
        type!.Members.Should().NotContainKey("getMessage");
        type.Members.Should().ContainKey("messageText");
        var alias = type.Members["messageText"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        alias.OriginalPhpName.Should().Be("getMessage");
    }

    [Fact]
    public void OverlayPartialTypeHeader_ReplacesGenericsAndImplementsKeepsMembers()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/storage.tyhpdef", """
            <?tyhpdef
            class Storage implements \Countable {
                public function get(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/storage.tyhpdef", """
            <?tyhpdef
            partial class Storage<TObject extends object = object, TData = mixed> implements \Countable, \Iterator;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        var type = FindObject(global, "Storage");
        type.Should().NotBeNull();
        type!.GenericParameters.Select(g => g.Name).Should().Equal("TObject", "TData");
        type.Members.Should().ContainKey("get");
        type.ImplementsTypes.Should().HaveCount(2);
    }

    [Fact]
    public void OverlayPartialTypeHeader_ShrinksGenericsLeavingOldParamUnresolvedOnSymbol()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/box.tyhpdef", """
            <?tyhpdef
            class Box<TValue> {
                public function get(): TValue;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/box.tyhpdef", """
            <?tyhpdef
            partial class Box<TObject, TData>;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        var type = FindObject(global, "Box");
        type.Should().NotBeNull();
        type!.GenericParameters.Select(g => g.Name).Should().Equal("TObject", "TData");
        type.GenericParameters.Should().NotContain(g => g.Name == "TValue");
    }

    [Fact]
    public void OverlayPartialTypeHeader_KeepAndAlias_ExposesBothTyhpNames()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/named_storage.tyhpdef", """
            <?tyhpdef
            class Storage {
                public function get(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/named_storage.tyhpdef", """
            <?tyhpdef
            partial class Storage;
            partial class Storage as ObjectStorage;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefPartialTypeHeaderNoEffect);
        var original = ResolveObject(global, "Storage");
        var alias = ResolveObject(global, "ObjectStorage");
        original.Should().NotBeNull();
        alias.Should().NotBeNull();
        ReferenceEquals(original, alias).Should().BeTrue();
        original!.Members.Should().ContainKey("get");
    }

    [Fact]
    public void OverlayPartialTypeHeader_AliasOnly_ReplacesTyhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/rename_storage.tyhpdef", """
            <?tyhpdef
            class Storage {
                public function get(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/rename_storage.tyhpdef", """
            <?tyhpdef
            partial class Storage as ObjectStorage;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        ResolveObject(global, "Storage").Should().BeNull();
        var type = ResolveObject(global, "ObjectStorage");
        type.Should().NotBeNull();
        type!.Name.Should().Be("ObjectStorage");
        type.OriginalPhpName.Should().Be("Storage");
        type.Members.Should().ContainKey("get");
    }

    [Fact]
    public void OverlayPartialTypeHeader_BareNoOp_Warns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/bare_storage.tyhpdef", """
            <?tyhpdef
            class Storage {
                public function get(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/bare_storage.tyhpdef", """
            <?tyhpdef
            partial class Storage;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialTypeHeaderNoEffect);
    }

    [Fact]
    public void PartialTypeHeaderInInclude_IsError()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/include_header.tyhpdef", """
            <?tyhpdef
            class Storage {}
            partial class Storage<T>;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialTypeHeaderOutsideOverlay);
    }

    [Fact]
    public void OverlayPartialTypeHeader_ThenMemberPartial_SameFile()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/combo_storage.tyhpdef", """
            <?tyhpdef
            class Storage {
                public function get(): mixed;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/combo_storage.tyhpdef", """
            <?tyhpdef
            partial class Storage<TData>;
            partial class Storage {
                public function get(): TData;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        var type = FindObject(global, "Storage");
        type.Should().NotBeNull();
        type!.GenericParameters.Select(g => g.Name).Should().Equal("TData");
        MethodReturnTypeText(type, "get").Should().Contain("TData");
    }

    [Fact]
    public void OverlayPartial_MethodOverloads_ReplaceHarvestThenAppend()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/bound.tyhpdef", """
            <?tyhpdef
            final class Bound {
                public function bindTo(?object $newThis, object|string|null $newScope = 'static'): ?\Bound;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/bound.tyhpdef", """
            <?tyhpdef
            final partial class Bound {
                public function bindTo(object $newThis, object $newScope): ?\Bound;
                public function bindTo(object $newThis, 'static' $newScope = 'static'): ?\Bound;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderDuplicateSymbolDeclaration
            && d.Message.Contains("bindTo", StringComparison.OrdinalIgnoreCase));

        var type = FindObject(global, "Bound");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("bindTo");
        var bindTo = type.Members["bindTo"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        bindTo.Overloads.Should().HaveCount(1);
        bindTo.Parameters.Should().HaveCount(2);
        bindTo.Overloads[0].Parameters.Should().HaveCount(2);
        TypeText(bindTo.Overloads[0].Parameters[1].DeclaredType).Should().Contain("static");
    }

    [Fact]
    public void OverlayPartialMissingType_WarnsAndSkips()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/missing_partial.tyhpdef", """
            <?tyhpdef
            partial class \DefinitelyNotDefined {
                public function extra(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);
        FindObject(global, "DefinitelyNotDefined").Should().BeNull();
    }

    [Fact]
    public void OverlayPartial_NamespacedClass_MatchesLayer1AcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_bar.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Bar {
                    public function a(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_bar.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                partial class Bar {
                    public function a(): int;
                    omit public function z();
                    public function b(): void;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var type = FindObjectInNamespace(global, "Foo", "Bar");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("a");
        type.Members.Should().ContainKey("b");
        type.Members.Should().NotContainKey("z");
        MethodReturnTypeText(type, "a").Should().Contain("int");
    }

    [Fact]
    public void OverlayPartial_NamespacedClass_MatchingStamp_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_stamp.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Bar {
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_stamp.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                // @overlay-against: class Bar
                partial class Bar {
                }
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void OverlayPartial_NamespacedClassAlias_MatchingStamp_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_alias_stamp.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Bar {
                    public function get(): mixed;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_alias_stamp.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                // @overlay-against: class Bar
                partial class Bar as Baz {
                }
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void FunctionAlias_NamespacedPhpOriginal_MatchingStamp_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/ns_fn_alias.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                function bar(int $x): int;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/ns_fn_alias.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                // @overlay-against: function bar(int $x): int
                function \Foo\bar as baz(int $x): int;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void FunctionAlias_NamespacedUnqualifiedPhpOriginal_MatchingStamp_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/ns_fn_alias2.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                function bar(int $x): int;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/ns_fn_alias2.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                // @overlay-against: function bar(int $x): int
                function bar as baz(int $x): int;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void ClassAlias_NamespacedUnqualifiedPhpOriginal_MatchingStamp_DoesNotWarn()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/ns_class_alias.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Bar {
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/ns_class_alias.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                // @overlay-against: class Bar
                class Bar as Baz {
                }
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void OverlayPartial_WrongNamespace_DoesNotMergeAndWarns()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_bar2.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Bar {
                    public function a(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/wrong_namespace.tyhpdef", """
            <?tyhpdef
            namespace Baz {
                partial class Bar {
                    public function b(): void;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var fooBar = FindObjectInNamespace(global, "Foo", "Bar");
        fooBar.Should().NotBeNull();
        fooBar!.Members.Should().ContainKey("a");
        fooBar.Members.Should().NotContainKey("b");

        var bazBar = FindObjectInNamespace(global, "Baz", "Bar");
        bazBar.Should().BeNull();
    }

    [Fact]
    public void OverlayPartial_FlatDottedName_MatchesBlockSyntaxLayer1()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_flat.tyhpdef", """
            <?tyhpdef
            namespace Flat {
                class Item {
                    public function a(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_flat.tyhpdef", """
            <?tyhpdef
            partial class \Flat\Item {
                public function a(): int;
                public function b(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var type = FindObjectInNamespace(global, "Flat", "Item");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("a");
        type.Members.Should().ContainKey("b");
        MethodReturnTypeText(type, "a").Should().Contain("int");
    }

    [Fact]
    public void OverlayPartial_NestedNamespace_MatchesLayer1AcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/nested_bar.tyhpdef", """
            <?tyhpdef
            namespace Foo\Sub {
                class Bar {
                    public function a(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/nested_bar.tyhpdef", """
            <?tyhpdef
            namespace Foo\Sub {
                partial class Bar {
                    public function a(): int;
                    public function b(): void;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var type = FindObjectInNamespace(global, "Foo\\Sub", "Bar");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("a");
        type.Members.Should().ContainKey("b");
        MethodReturnTypeText(type, "a").Should().Contain("int");
    }

    [Fact]
    public void OverlayOmitNamespacedFunction_RemovesAcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_fn.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                function baz(): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_fn_omit.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                omit function baz();
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOmitMissingSymbol);
        FindFunction(global, "baz").Should().BeNull();
    }

    [Fact]
    public void OverlayReplaceNamespacedFunction_LastWinsAcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_fn2.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                function qux(): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_fn_replace.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                function qux(): int;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);

        var fn = FindFunction(global, "qux");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("int");
    }

    [Fact]
    public void OverlayPartial_VersionGatedNamespacedClass_SkipsWhenNotVisible()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/widgetdb_gated.tyhpdef", """
            <?tyhpdef
            namespace WidgetdbGated {
                #[\Tyhp\Php(">=8.4")]
                class Store {
                    public function query(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/widgetdb_gated.tyhpdef", """
            <?tyhpdef
            namespace WidgetdbGated {
                partial class Store {
                    public function query(): int;
                    public function getWarningCount(): int;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var type = FindObjectInNamespace(global, "WidgetdbGated", "Store");
        type.Should().BeNull();
    }

    [Fact]
    public void StubsListedBeforeHandWritten_NamespacedPartialClass_HandWrittenWins()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_stub_target.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class StubTarget {
                    public function a(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/namespaced_stub_target.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                partial class StubTarget {
                    public function a(): int;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_stub_target.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                partial class StubTarget {
                    public function a(): string;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var type = FindObjectInNamespace(global, "Foo", "StubTarget");
        type.Should().NotBeNull();
        MethodReturnTypeText(type!, "a").Should().Contain("string");
    }

    [Fact]
    public void OverlayFullReplace_NamespacedClass_LastWinsAcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/namespaced_replace.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Replaced {
                    public function a(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/namespaced_replace.tyhpdef", """
            <?tyhpdef
            namespace Foo {
                class Replaced {
                    public function a(): int;
                    public function b(): void;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);

        var type = FindObjectInNamespace(global, "Foo", "Replaced");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("a");
        type.Members.Should().ContainKey("b");
        MethodReturnTypeText(type, "a").Should().Contain("int");
    }

    [Fact]
    public void OverlayOmitConstant_IsCaseSensitiveAcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/case_constants.tyhpdef", """
            <?tyhpdef
            const int FOO_BAR ?? 1;
            const int foo_bar ?? 2;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/omit_lowercase_constant.tyhpdef", """
            <?tyhpdef
            omit const int foo_bar;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOmitMissingSymbol);

        FindConstant(global, "FOO_BAR").Should().NotBeNull(
            "omitting the lower-case constant must not remove the differently-cased one");
        FindConstant(global, "foo_bar").Should().BeNull();
    }

    [Fact]
    public void OverlayPartial_FileScopeClass_StillMatchesAcrossFiles()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/filescope_qux.tyhpdef", """
            <?tyhpdef
            class Qux {
                public function a(): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/filescope_qux.tyhpdef", """
            <?tyhpdef
            partial class Qux {
                public function a(): int;
                public function b(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var type = FindObject(global, "Qux");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("a");
        type.Members.Should().ContainKey("b");
        MethodReturnTypeText(type, "a").Should().Contain("int");
    }

    [Fact]
    public void OverlayPartial_VersionGatedNamespacedClass_AppliesWhenVisible()
    {
        using var builder = OverlayFixture(phpVersion: "8.4");
        builder.WithTyhpFile("pkg/_tyhpdef/widgetdb.tyhpdef", """
            <?tyhpdef
            namespace Widgetdb {
                #[\Tyhp\Php(">=8.4")]
                class Store {
                    public function query(): void;
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/widgetdb.tyhpdef", """
            <?tyhpdef
            namespace Widgetdb {
                partial class Store {
                    public function query(): int;
                    public function getWarningCount(): int;
                }
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);

        var type = FindObjectInNamespace(global, "Widgetdb", "Store");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("query");
        type.Members.Should().ContainKey("getWarningCount");
        MethodReturnTypeText(type, "query").Should().Contain("int");
    }

    [Fact]
    public void OverlayPartial_GatedMemberReplace_DoesNotReport4303()
    {
        using var builder = OverlayFixture(phpVersion: "8.4");
        builder.WithTyhpFile("pkg/_tyhpdef/frame.tyhpdef", """
            <?tyhpdef
            class FrameProcessor {
                #[\Tyhp\Php(">=8.4")]
                public bool $cloneDocument;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/frame.tyhpdef", """
            <?tyhpdef
            partial class FrameProcessor {
                #[\Tyhp\Php(">=8.4")]
                public bool $cloneDocument;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);

        var type = FindObject(global, "FrameProcessor");
        type.Should().NotBeNull();
        type!.Members.Keys.Should().Contain(k => k.TrimStart('$') == "cloneDocument");
    }

    /// <summary>
    /// Layer 1's gate does not match the compile target (<c>&gt;=8.4</c> vs. an 8.2 target), so
    /// the baseline method is never registered as a live member — an overlay replacement must
    /// still evict the lingering gated-declaration record for it (regression for the gap left by
    /// <see cref="OverlayPartial_GatedMemberReplace_DoesNotReport4303"/>, which only covers a
    /// gate the target satisfies).
    /// </summary>
    [Fact]
    public void OverlayPartial_UnsatisfiedGatedLayer1Member_UngatedOverlayReplaceDoesNotReport4303()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/date.tyhpdef", """
            <?tyhpdef
            class DateTimeStub {
                #[\Tyhp\Php(">=8.4")]
                public function getMicrosecond(): int;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/date.tyhpdef", """
            <?tyhpdef
            partial class DateTimeStub {
                public function getMicrosecond(): int;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);

        var type = FindObject(global, "DateTimeStub");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("getMicrosecond");
    }

    /// <summary>
    /// Same as <see cref="OverlayPartial_UnsatisfiedGatedLayer1Member_UngatedOverlayReplaceDoesNotReport4303"/>
    /// but for a magic method. Magic methods are tracked under their own dedicated
    /// <c>SymbolType</c> values (not a generic method kind), so eviction of the lingering
    /// gated-declaration record must resolve the same magic-method symbol type Layer 1 used.
    /// </summary>
    [Fact]
    public void OverlayPartial_UnsatisfiedGatedLayer1MagicMethod_UngatedOverlayReplaceDoesNotReport4303()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/hash.tyhpdef", """
            <?tyhpdef
            final class HashContextStub {
                #[\Tyhp\Php(">=8.4")]
                public function __debugInfo(): array;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/stubs/hash.tyhpdef", """
            <?tyhpdef
            final partial class HashContextStub {
                public function __debugInfo(): array;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);

        var type = FindObject(global, "HashContextStub");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("__debugInfo");
    }

    /// <summary>
    /// An overlay <c>partial enum</c> can write both a brace-form body (cases) and a
    /// backing-type header in the same declaration (grammar allows the two together, unlike
    /// the header-only semicolon form). The backing type must still reach the merged symbol —
    /// not just the header-only-no-body path — since compiled-library `package.tyhpdef` regeneration
    /// reads <see cref="ObjectDeclarationSymbol.BackingType"/> off that symbol.
    /// </summary>
    [Fact]
    public void OverlayPartialEnum_BraceFormWithBackingType_AppliesBackingType()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/suit.tyhpdef", """
            <?tyhpdef
            enum Suit {
                case Hearts;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/suit.tyhpdef", """
            <?tyhpdef
            partial enum Suit: string {
                case Spades = 'spades';
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);

        var type = FindObject(global, "Suit");
        type.Should().NotBeNull();
        type!.BackingType.Should().NotBeNull();
        TypeText(type.BackingType).ToLowerInvariant().Should().Be("string");
        // Enum cases are `ObjectConstantSymbol`s registered in `Constants` (case-sensitive
        // namespace), not `Members` (see `RegisterObjectMember`).
        type.Constants.Should().ContainKey("Hearts");
        type.Constants.Should().ContainKey("Spades");
    }

    /// <summary>
    /// Same gap as <see cref="OverlayPartialEnum_BraceFormWithBackingType_AppliesBackingType"/>
    /// but for an include-load (non-overlay) <c>partial enum</c> fragment. Header-only partials
    /// are overlay-only (<c>TyhpdefPartialTypeHeaderOutsideOverlay</c>), so every include
    /// fragment is brace-form — the backing type must be applied unconditionally here.
    /// </summary>
    [Fact]
    public void IncludePartialEnum_DeclaresBackingType_AppliesToSymbol()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/color_base.tyhpdef", """
            <?tyhpdef
            enum Color {
                case Red;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/color_partial.tyhpdef", """
            <?tyhpdef
            partial enum Color: int {
                case Blue = 2;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoOverlayErrors(diagnostics);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefPartialTargetNotFound);

        var type = FindObject(global, "Color");
        type.Should().NotBeNull();
        type!.BackingType.Should().NotBeNull();
        TypeText(type.BackingType).ToLowerInvariant().Should().Be("int");
        type.Constants.Should().ContainKey("Red");
        type.Constants.Should().ContainKey("Blue");
    }

    [Fact]
    public void HandWrittenOverlayPath_IsNotStubPath()
    {
        TyhpdefSnapshotStore.IsHandOverlayPath("/tmp/pkg/_tyhpdef/overlays/Iterator.tyhpdef").Should().BeTrue();
        TyhpdefSnapshotStore.IsHandOverlayPath("/tmp/pkg/_tyhpdef/overlays/stubs/Iterator.tyhpdef").Should().BeFalse();
    }

    private static TestProjectBuilder OverlayFixture(string phpVersion = "8.2")
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson($$"""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "{{phpVersion}}" }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"],
                "overlay": [
                    "./_tyhpdef/overlays/stubs/*.tyhpdef",
                    "./_tyhpdef/overlays/*.tyhpdef"
                ]
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            class \DomainException {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        return builder;
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(
        TestProjectBuilder builder,
        bool strict = false)
    {
        var project = builder.BuildProject();
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.EnableAstCache = false;
            o.SkipChecking = true;
            o.StrictMode = strict;
        });
        var result = compilationService.ParseFiles([userFile], options);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static void AssertNoOverlayErrors(DiagnosticBag diagnostics)
    {
        var overlayCodes = new HashSet<MessageCode>
        {
            MessageCode.TyhpdefOmitOutsideOverlay,
            MessageCode.TyhpdefIllegalKeywordCombination,
            MessageCode.TyhpdefOverlayStampMismatch,
            MessageCode.TyhpdefPartialFunctionOutsideOverlay,
            MessageCode.TyhpdefPartialFunctionMemberOutsidePartialType,
            MessageCode.TyhpdefPartialTypeHeaderOutsideOverlay,
            MessageCode.TyhpdefBindError,
            MessageCode.TyhpdefParseError,
        };
        diagnostics.Errors.Where(d => overlayCodes.Contains(d.Code)).Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol func
                && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = func;
            }
        });
        return found;
    }

    private static ConstantSymbol? FindConstant(GlobalScope global, string name)
    {
        ConstantSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ConstantSymbol constant
                && string.Equals(constant.Name, name, StringComparison.Ordinal))
            {
                found = constant;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? ResolveObject(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is ObjectDeclarationSymbol direct
            && !direct.IsExtension)
        {
            return direct;
        }

        foreach (var child in ((IBaseScope)global).GetAllChildScopes())
        {
            if (child.FindChildSymbolByName(name) is ObjectDeclarationSymbol hit
                && !hit.IsExtension)
            {
                return hit;
            }
        }

        return FindObject(global, name);
    }

    private static ObjectDeclarationSymbol? FindObjectInNamespace(
        GlobalScope global,
        string namespaceName,
        string typeName)
    {
        var nsScope = global.FindNamespaceScope(namespaceName);
        if (nsScope == null)
        {
            return null;
        }

        ObjectDeclarationSymbol? found = null;
        Walk(nsScope, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && !obj.IsExtension
                && string.Equals(obj.Name, typeName, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static string MethodReturnTypeText(ObjectDeclarationSymbol type, string methodName)
    {
        type.Members.TryGetValue(methodName, out var member).Should().BeTrue();
        var method = member.Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        return TypeText(method.ReturnType);
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }

    private static string TypeText(Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast? node)
    {
        if (node == null)
        {
            return "";
        }

        if (!string.IsNullOrEmpty(node.ValueString) && node is not Tyhp.TyhpLang.Ast.PhpTypeExpressionAst)
        {
            return node.ValueString;
        }

        if (!string.IsNullOrEmpty(node.Identifier))
        {
            return node.Identifier;
        }

        foreach (var child in node.AstChildren)
        {
            var nestedText = TypeText(child);
            if (!string.IsNullOrEmpty(nestedText))
            {
                return nestedText;
            }
        }

        return "";
    }

    private static List<string> AttributeNames(IBase2Ast? node)
    {
        if (node == null)
        {
            return [];
        }

        return node.AstAttributes
            .OfType<PhpAttributeAst>()
            .Select(attribute =>
            {
                var written = attribute.Name?.ValueString ?? attribute.Name?.Identifier ?? "";
                return written.Trim().TrimStart('\\');
            })
            .Where(name => name.Length > 0)
            .ToList();
    }
}
