using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20")]
public class TyhpdefMultiTargetMergerTests
{
    [Fact]
    public void IdenticalAcrossTargets_IsUngated()
    {
        var v82 = File("8.2", fn("shared", "void"));
        var v84 = File("8.4", fn("shared", "void"));

        var merged = TyhpdefMultiTargetMerger.Merge([("8.2", v82), ("8.4", v84)]);

        merged.GlobalFunctions.Should().ContainSingle(f => f.Name == "shared");
        merged.DeclareBlocks.Should().BeEmpty();
        merged.Header.Should().Contain("8.2");
        merged.Header.Should().Contain("8.4");
    }

    [Fact]
    public void AddedAtVersion_UsesDeclareBlock()
    {
        var v82 = File("8.2", fn("shared", "void"));
        var v84 = File("8.4", fn("shared", "void"), fn("added", "bool"));

        var merged = TyhpdefMultiTargetMerger.Merge([("8.2", v82), ("8.4", v84)]);

        merged.GlobalFunctions.Should().ContainSingle(f => f.Name == "shared");
        var block = merged.DeclareBlocks.Should().ContainSingle().Subject;
        block.Constraint.Should().Be(">=8.4");
        block.Functions.Should().ContainSingle(f => f.Name == "added");
    }

    [Fact]
    public void SignatureChange_EmitsDisjointDeclares()
    {
        var oldFn = new TyhpdefFunction
        {
            Name = "changed",
            Parameters = [new TyhpdefParameter { Name = "value", Type = "int" }],
            ReturnType = "int",
        };
        var newFn = new TyhpdefFunction
        {
            Name = "changed",
            Parameters =
            [
                new TyhpdefParameter { Name = "value", Type = "int" },
                new TyhpdefParameter { Name = "flags", Type = "int", DefaultValue = "0" },
            ],
            ReturnType = "int",
        };

        var merged = TyhpdefMultiTargetMerger.Merge([
            ("8.2", File("8.2", oldFn)),
            ("8.4", File("8.4", newFn)),
        ]);

        merged.GlobalFunctions.Should().BeEmpty();
        merged.DeclareBlocks.Should().HaveCount(2);
        var older = merged.DeclareBlocks.Single(b => b.Constraint == "<8.4");
        var newer = merged.DeclareBlocks.Single(b => b.Constraint == ">=8.4");
        older.Functions.Single().Parameters.Should().HaveCount(1);
        newer.Functions.Single().Parameters.Should().HaveCount(2);
        TyhpdefMultiTargetMerger.ConstraintsOverlap(older.Constraint, newer.Constraint).Should().BeFalse();
    }

    [Fact]
    public void RemovedAfterVersion_UsesUpperBound()
    {
        var v82 = File("8.2", fn("gone", "void"));
        var v84 = File("8.4");

        var merged = TyhpdefMultiTargetMerger.Merge([("8.2", v82), ("8.4", v84)]);

        var block = merged.DeclareBlocks.Should().ContainSingle().Subject;
        block.Constraint.Should().Be("<8.4");
        block.Functions.Should().ContainSingle(f => f.Name == "gone");
    }

    [Fact]
    public void SharedClass_MemberAddUsesAttribute()
    {
        var v82 = new TyhpdefFile
        {
            GlobalTypes =
            [
                new TyhpdefClassDeclaration
                {
                    Kind = "class",
                    Name = "Host",
                    Methods = [new TyhpdefMethod { Name = "always", ReturnType = "void", Modifiers = ["public"] }],
                },
            ],
        };
        var v84 = new TyhpdefFile
        {
            GlobalTypes =
            [
                new TyhpdefClassDeclaration
                {
                    Kind = "class",
                    Name = "Host",
                    Methods =
                    [
                        new TyhpdefMethod { Name = "always", ReturnType = "void", Modifiers = ["public"] },
                        new TyhpdefMethod { Name = "fresh", ReturnType = "string", Modifiers = ["public"] },
                    ],
                },
            ],
        };

        var merged = TyhpdefMultiTargetMerger.Merge([("8.2", v82), ("8.4", v84)]);

        var host = merged.GlobalTypes.Should().ContainSingle(t => t.Name == "Host").Subject;
        host.PhpGate.Should().BeNull();
        host.Methods.Should().ContainSingle(m => m.Name == "always" && m.PhpGate == null);
        host.Methods.Should().ContainSingle(m => m.Name == "fresh" && m.PhpGate == ">=8.4");
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void SharedClass_StorageThenHooked_EmitsDisjointGatedProperties()
    {
        var storage = new TyhpdefProperty
        {
            Name = "name",
            Type = "string",
            Modifiers = ["public"],
        };
        var hooked = new TyhpdefProperty
        {
            Name = "name",
            Type = "string",
            Modifiers = ["public"],
            Hooks =
            [
                new TyhpdefPropertyHook { Name = "get" },
                new TyhpdefPropertyHook { Name = "set" },
            ],
        };

        var merged = TyhpdefMultiTargetMerger.Merge([
            ("8.2", ClassWithProperty(storage)),
            ("8.4", ClassWithProperty(hooked)),
        ]);

        var host = merged.GlobalTypes.Should().ContainSingle(t => t.Name == "Host").Subject;
        host.PhpGate.Should().BeNull();
        host.Properties.Should().HaveCount(2);
        var older = host.Properties.Should().ContainSingle(p => p.PhpGate == "<8.4").Subject;
        older.Hooks.Should().BeEmpty();
        var newer = host.Properties.Should().ContainSingle(p => p.PhpGate == ">=8.4").Subject;
        newer.Hooks.Select(h => h.Name).Should().Equal("get", "set");
        TyhpdefMultiTargetMerger.ConstraintsOverlap(older.PhpGate, newer.PhpGate).Should().BeFalse();

        var text = TyhpdefOutputWriter.Write(merged, includeDocComments: false);
        text.Should().Contain("#[\\Tyhp\\Php(\"<8.4\")]");
        text.Should().Contain("#[\\Tyhp\\Php(\">=8.4\")]");
        text.Should().Contain("public string $name;");
        text.Should().Contain("public string $name { get; set; }");
        AssertParses(text);
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void SharedClass_IdenticalHooksAcrossTargets_IsUngated()
    {
        var hooked = new TyhpdefProperty
        {
            Name = "name",
            Type = "string",
            Modifiers = ["public"],
            Hooks =
            [
                new TyhpdefPropertyHook { Name = "get" },
                new TyhpdefPropertyHook { Name = "set" },
            ],
        };

        var merged = TyhpdefMultiTargetMerger.Merge([
            ("8.2", ClassWithProperty(hooked)),
            ("8.4", ClassWithProperty(hooked)),
        ]);

        var host = merged.GlobalTypes.Should().ContainSingle(t => t.Name == "Host").Subject;
        var property = host.Properties.Should().ContainSingle().Subject;
        property.PhpGate.Should().BeNull();
        property.Hooks.Select(h => h.Name).Should().Equal("get", "set");
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void SharedClass_GetOnlyThenGetSet_EmitsDisjointGatedProperties()
    {
        var getOnly = new TyhpdefProperty
        {
            Name = "name",
            Type = "string",
            Modifiers = ["public"],
            Hooks = [new TyhpdefPropertyHook { Name = "get" }],
        };
        var getSet = new TyhpdefProperty
        {
            Name = "name",
            Type = "string",
            Modifiers = ["public"],
            Hooks =
            [
                new TyhpdefPropertyHook { Name = "get" },
                new TyhpdefPropertyHook { Name = "set" },
            ],
        };

        var merged = TyhpdefMultiTargetMerger.Merge([
            ("8.2", ClassWithProperty(getOnly)),
            ("8.3", ClassWithProperty(getOnly)),
            ("8.4", ClassWithProperty(getSet)),
        ]);

        var host = merged.GlobalTypes.Should().ContainSingle(t => t.Name == "Host").Subject;
        host.Properties.Should().HaveCount(2);
        var older = host.Properties.Should().ContainSingle(p => p.PhpGate == "<8.4").Subject;
        older.Hooks.Should().ContainSingle(h => h.Name == "get");
        older.Hooks.Should().NotContain(h => h.Name == "set");
        var newer = host.Properties.Should().ContainSingle(p => p.PhpGate == ">=8.4").Subject;
        newer.Hooks.Select(h => h.Name).Should().Equal("get", "set");
    }

    [Fact]
    public void Struct_UsesDeclareNotAttribute()
    {
        var v82 = new TyhpdefFile();
        var v84 = new TyhpdefFile
        {
            GlobalTypes = [new TyhpdefClassDeclaration { Kind = "struct", Name = "Point" }],
        };

        var merged = TyhpdefMultiTargetMerger.Merge([("8.2", v82), ("8.4", v84)]);

        merged.GlobalTypes.Should().BeEmpty();
        var block = merged.DeclareBlocks.Should().ContainSingle().Subject;
        block.Constraint.Should().Be(">=8.4");
        block.Classes.Should().ContainSingle(t => t.Kind == "struct" && t.Name == "Point" && t.PhpGate == null);
    }

    [Fact]
    public void ConstraintFor_ContiguousRanges()
    {
        TyhpdefMultiTargetMerger.ConstraintFor(["8.2", "8.3", "8.4"], ["8.2", "8.3", "8.4"]).Should().BeNull();
        TyhpdefMultiTargetMerger.ConstraintFor(["8.3", "8.4"], ["8.2", "8.3", "8.4"]).Should().Be(">=8.3");
        TyhpdefMultiTargetMerger.ConstraintFor(["8.2", "8.3"], ["8.2", "8.3", "8.4"]).Should().Be("<8.4");
        TyhpdefMultiTargetMerger.ConstraintFor(["8.3"], ["8.2", "8.3", "8.4"]).Should().Be(">=8.3 <8.4");
        TyhpdefMultiTargetMerger.ConstraintFor(["8.2"], ["8.2", "8.4"]).Should().Be("<8.4");
        TyhpdefMultiTargetMerger.ConstraintFor(["8.2", "8.4"], ["8.2", "8.4"]).Should().BeNull();
    }

    [Fact]
    public void FixtureSnapshots_MergeToGatedParsableTyhpdef()
    {
        var options = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = "mini",
            IncludeDocComments = false,
            IncludeDeprecated = true,
        };
        var runtime82 = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.2.26", IsManaged = true };
        var runtime84 = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.4.5", IsManaged = true };
        var v82 = PhpReflectionMapper.Map(System.IO.File.ReadAllText(TyhpdefGenFixtures.Snapshot82MiniPath), options, runtime82);
        var v84 = PhpReflectionMapper.Map(System.IO.File.ReadAllText(TyhpdefGenFixtures.Snapshot84MiniPath), options, runtime84);

        var merged = TyhpdefMultiTargetMerger.Merge([("8.2", v82), ("8.4", v84)]);
        var text = TyhpdefOutputWriter.Write(merged, includeDocComments: false);

        text.Should().Contain("function mini_always");
        text.Should().Contain("declare(php=\">=8.4\")");
        text.Should().Contain("function mini_added");
        text.Should().Contain("declare(php=\"<8.4\")");
        text.Should().Contain("function mini_removed");
        text.Should().Contain("#[\\Tyhp\\Php(\">=8.4\")]");
        text.Should().Contain("function fresh()");
        AssertParses(text);
    }

    private static TyhpdefFile File(string header, params TyhpdefFunction[] functions)
        => new() { Header = header, GlobalFunctions = [.. functions] };

    private static TyhpdefFile ClassWithProperty(TyhpdefProperty property)
        => new()
        {
            GlobalTypes =
            [
                new TyhpdefClassDeclaration
                {
                    Kind = "class",
                    Name = "Host",
                    Properties = [property],
                },
            ],
        };

    private static TyhpdefFunction fn(string name, string returnType)
        => new() { Name = name, ReturnType = returnType };

    private static void AssertParses(string tyhpdef)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
        result.Ast.Should().NotBeNull();
    }
}
