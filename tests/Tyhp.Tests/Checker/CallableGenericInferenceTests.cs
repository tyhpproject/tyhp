using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Argument-driven generic binding in <c>CallableGenericInference.CollectGenericBindings</c>,
/// including a union-typed declared parameter matched against a concrete (non-union) actual.
/// </summary>
[Trait("Category", "Checker")]
public class CallableGenericInferenceTests
{
    [Fact]
    public void UnionPattern_NonUnionIteratorActual_InfersTKeyTValue()
    {
        // iterator_to_array-shaped: pattern is Traversable<TKey,TValue>|array<TKey,TValue>,
        // actual is a single Iterator<string, User>. Inference must try each union arm.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class User {}
            function iterator_to_array<TKey extends int|string, TValue>(
                \Traversable<TKey, TValue>|array<TKey, TValue> $iterator,
                bool $preserve_keys
            ): array<TKey, TValue> {
                return [];
            }
            function demo(\Iterator<string, User> $it): void {
                array<string, User> $keep = iterator_to_array($it, true);
            }
            """);

        errors.Should().BeEmpty(
            "iterator_to_array($it, true) must infer TKey/TValue from Iterator<string, User>: "
            + Describe(errors));
    }

    [Fact]
    public void UnionPattern_NonUnionArrayActual_InfersTKeyTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class User {}
            function iterator_to_array<TKey extends int|string, TValue>(
                \Traversable<TKey, TValue>|array<TKey, TValue> $iterator,
                bool $preserve_keys
            ): array<TKey, TValue> {
                return [];
            }
            function demo(array<string, User> $arr): void {
                array<string, User> $keep = iterator_to_array($arr, true);
            }
            """);

        errors.Should().BeEmpty(
            "iterator_to_array($arr, true) must infer TKey/TValue from array<string, User>: "
            + Describe(errors));
    }

    [Fact]
    public void UnionPattern_UnequalMemberCountActualUnion_InfersTKeyTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class User {}
            function iterator_to_array<TKey extends int|string, TValue>(
                \Traversable<TKey, TValue>|array<TKey, TValue> $iterator,
                bool $preserve_keys
            ): array<TKey, TValue> {
                return [];
            }
            function demo(\Iterator<string, User>|\Generator<string, User> $it): void {
                array<string, User> $keep = iterator_to_array($it, true);
            }
            """);

        errors.Should().BeEmpty(
            "unequal union member counts must still bind TKey/TValue from each actual arm: "
            + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var result = IsolatedCompilation.ParseSnippet(
            content,
            skipChecking: true,
            fileName: fileName);
        result.GlobalScope.Should().NotBeNull("bind should succeed: " + Describe(result.Diagnostics.Errors.ToList()));
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);

        return result.Diagnostics.Errors
            .Where(e => e.FileName is not null
                && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
            .ToList();
    }
}
