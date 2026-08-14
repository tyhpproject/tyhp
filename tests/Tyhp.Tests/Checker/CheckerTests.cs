using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class CheckerStateTests
{
    [Fact]
    public void SnapShot_CreatesIndependentCopy()
    {
        var state = new CheckerState();
        state.Variables["x"] = new VariableState { DeclaredType = CheckedTypes.Int };

        var snapshot = state.SnapShot();
        snapshot.IsLocked.Should().BeTrue();
        state.Variables["x"].DeclaredType = CheckedTypes.String;

        snapshot.Variables["x"].DeclaredType.Should().Be(CheckedTypes.Int);
        snapshot.Variables.Should().NotBeSameAs(state.Variables);

        var branchState = snapshot.Split(ScopeType.CodeBlock);
        branchState.Should().NotBeNull();
        branchState.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Split_FunctionScope_ClearsVariables()
    {
        var state = new CheckerState();
        state.Variables["x"] = new VariableState { DeclaredType = CheckedTypes.Int };

        var functionState = state.Split(ScopeType.FunctionDeclaration);

        functionState.Parent.Should().BeSameAs(state);
        functionState.Variables.Should().BeEmpty();
        state.Variables.Should().ContainKey("x");
    }

    [Fact]
    public void Split_CodeBlock_CopiesVariables()
    {
        var state = new CheckerState();
        state.Variables["x"] = new VariableState { DeclaredType = CheckedTypes.Int, IsDefinitelyAssigned = true };

        var blockState = state.Split(ScopeType.CodeBlock);

        blockState.Variables.Should().ContainKey("x");
        blockState.Variables["x"].IsDefinitelyAssigned.Should().BeTrue();
        blockState.Variables["x"].DeclaredType = CheckedTypes.String;
        state.Variables["x"].DeclaredType.Should().Be(CheckedTypes.Int);
    }

    [Fact]
    public void Merge_UnionsTypesAndTracksPossibleUndefined()
    {
        var state = new CheckerState();
        state.Variables["always"] = new VariableState
        {
            DeclaredType = CheckedTypes.Int,
            IsDefinitelyAssigned = true,
        };
        state.Variables["leftOnly"] = new VariableState
        {
            DeclaredType = CheckedTypes.String,
            IsDefinitelyAssigned = true,
        };

        var branch = state.SnapShot();
        branch.Variables.Remove("leftOnly");
        branch.Variables["rightOnly"] = new VariableState
        {
            DeclaredType = CheckedTypes.Float,
            IsDefinitelyAssigned = true,
        };
        branch.Variables["always"] = new VariableState
        {
            DeclaredType = CheckedTypes.String,
            IsDefinitelyAssigned = false,
            IsPossiblyNull = true,
        };

        state.Merge(branch);

        state.Variables["always"].DeclaredType.Should().BeOfType<UnionCheckedType>();
        state.Variables["always"].IsDefinitelyAssigned.Should().BeFalse();
        state.Variables["always"].IsPossiblyNull.Should().BeTrue();
        state.Variables["leftOnly"].IsPossiblyUndefined.Should().BeTrue();
        state.Variables.Should().ContainKey("rightOnly");
        state.Variables["rightOnly"].IsPossiblyUndefined.Should().BeTrue();
    }

    [Fact]
    public void Merge_IfWithoutElse_AssignedInThen_StaysPossiblyUndefined()
    {
        var state = new CheckerState();
        state.Variables["x"] = VariableState.ForDeclaration(
            new Tyhp.TyhpLang.Binder.Symbols.VariableSymbol("x"),
            CheckedTypes.Int,
            isAssigned: false);

        var before = state.SnapShot();
        var thenState = before.Split(ScopeType.CodeBlock);
        thenState.AssignVariable("x", CheckedTypes.Int, new DiagnosticBag());

        state.Merge(thenState);

        state.Variables["x"].IsDefinitelyAssigned.Should().BeFalse();
        state.Variables["x"].IsPossiblyUndefined.Should().BeTrue();
    }
}

[Trait("Category", "Checker")]
public class TyhpCheckerTests
{
    [Fact]
    public void Check_EmptyFile_DoesNotThrow()
    {
        var diagnostics = new DiagnosticBag();
        var symbolTree = new SymbolTree(new SymbolIdentifier([]));
        var checker = new TyhpChecker(diagnostics, symbolTree, symbolTree.GlobalScope);
        var file = TyhpSrcFileAst.Create("empty.tyhp", "hash");

        var act = () => checker.Check([file]);

        act.Should().NotThrow();
        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Check_SkipsErrorAstNodes()
    {
        var diagnostics = new DiagnosticBag();
        var symbolTree = new SymbolTree(new SymbolIdentifier([]));
        var checker = new TyhpChecker(diagnostics, symbolTree, symbolTree.GlobalScope);
        var file = TyhpSrcFileAst.Create(
            "errors.tyhp",
            "hash",
            codeBlocks: [ErrorAst.Create("bad", Tyhp.Domain.Exceptions.MessageCode.VisitorUnknownError, 1, 0)]);

        var act = () => checker.Check([file]);

        act.Should().NotThrow();
    }
}
