using System.Reflection;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DiagnosticBagTests
{
    [Fact]
    public void Add_SingleDiagnostic_AppearsInAll()
    {
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, Array.Empty<object>()));
        bag.All.Should().ContainSingle();
        bag.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void AddErrorWarningInfo_SetSeverityCounts()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddWarning(MessageCode.ParserUnknownError, "a.tyhp", 2, 0);
        bag.AddInfo(MessageCode.ParserUnknownError, "a.tyhp", 3, 0);

        bag.HasErrors.Should().BeTrue();
        bag.HasWarnings.Should().BeTrue();
        bag.ErrorCount.Should().Be(1);
        bag.WarningCount.Should().Be(1);
        bag.InfoCount.Should().Be(1);
    }

    [Fact]
    public void ErrorsAndWarnings_FilterBySeverity()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddWarning(MessageCode.ParserUnknownError, "b.tyhp", 1, 0);

        bag.Errors.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Error);
        bag.Warnings.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void AddRange_MergesDiagnostics()
    {
        var bag = new DiagnosticBag();
        var other = new DiagnosticBag();
        other.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddRange(other);
        bag.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void All_OrdersByFileThenLine()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "b.tyhp", 2, 0);
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);

        bag.All.Select(d => d.FileName).Should().ContainInOrder("a.tyhp", "b.tyhp");
    }

    [Fact]
    public void Add_FromMultipleThreads_IsThreadSafe()
    {
        var bag = new DiagnosticBag();
        const int threads = 4;
        const int perThread = 100;

        Parallel.For(0, threads, thread =>
        {
            for (var i = 0; i < perThread; i++)
            {
                // Unique file+line so de-duplication does not collapse concurrent inserts.
                bag.AddError(MessageCode.ParserUnknownError, $"file_{thread}_{i}.tyhp", i + 1, 0);
            }
        });

        bag.ErrorCount.Should().Be(threads * perThread);
    }

    [Fact]
    public void Add_IdenticalDiagnostics_AreDeduplicated()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 3, 9, ";", 9);
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 3, 9, ";", 9);
        bag.AddError(MessageCode.VisitorUnexpectedAlternative, "a.tyhp", 3, 4, "statementRequiringTerminal", "StatementRequiringTerminalContext");

        bag.ErrorCount.Should().Be(2);
        bag.Errors.Should().ContainSingle(d => d.Code == MessageCode.ParserUnexpectedError);
        bag.Errors.Should().ContainSingle(d => d.Code == MessageCode.VisitorUnexpectedAlternative);
    }

    [Fact]
    public void Add_SameCodeDifferentFormatParams_AreKept()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 1, 0, ";", 0);
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 1, 0, "}", 0);

        bag.ErrorCount.Should().Be(2);
    }

    [Fact]
    public void AddWarning_SuppressedCode_IsDropped()
    {
        var bag = new DiagnosticBag(new HashSet<MessageCode> { MessageCode.TyhpdefRuntimePackageNotFound });
        bag.AddWarning(MessageCode.TyhpdefRuntimePackageNotFound, "a.tyhp", 1, 0, "core");
        bag.AddWarning(MessageCode.TyhpdefPhpExtensionPackageNotFound, "a.tyhp", 2, 0, "widget");

        bag.HasWarnings.Should().BeTrue();
        bag.WarningCount.Should().Be(1);
        bag.Warnings.Should().ContainSingle(d => d.Code == MessageCode.TyhpdefPhpExtensionPackageNotFound);
    }

    [Fact]
    public void AddError_SuppressedWarningCode_IsKept()
    {
        var bag = new DiagnosticBag(new HashSet<MessageCode> { MessageCode.ParserUnknownError });
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddWarning(MessageCode.ParserUnknownError, "a.tyhp", 2, 0);

        bag.ErrorCount.Should().Be(1);
        bag.WarningCount.Should().Be(0);
        bag.HasErrors.Should().BeTrue();
        bag.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void AddInfo_SuppressedWarningCode_IsKept()
    {
        var bag = new DiagnosticBag(new HashSet<MessageCode> { MessageCode.ParserUnknownError });
        bag.AddInfo(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);

        bag.InfoCount.Should().Be(1);
    }

    [Fact]
    public void Project_DoesNotAssignDefaultSuppressedWarnings()
    {
        var before = DiagnosticBag.DefaultSuppressedWarnings;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["suppressWarnings:0"] = "TYHP4204",
            })
            .Build();

        var project = new Project(configuration);

        DiagnosticBag.DefaultSuppressedWarnings.Should().BeSameAs(before);
        project.SuppressedWarnings.Should().Contain(MessageCode.CheckerConditionAlwaysTrueFalse);
    }

    [Fact]
    public void ProjectSingleton_IsNotVisibleOnAnotherThread()
    {
        Project? seen = new Project(new ConfigurationBuilder().Build());
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var reader = new Thread(() =>
        {
            started.Set();
            release.Wait();
            seen = Project.Singleton;
        });
        reader.Start();
        started.Wait();

        var previous = Project.Singleton;
        Project.Singleton = new Project(new ConfigurationBuilder().Build());
        try
        {
            release.Set();
            reader.Join();
            seen.Should().BeNull();
        }
        finally
        {
            Project.Singleton = previous;
        }
    }

    [Fact]
    public void SilentDiagnosticSinks_AreNotProcessWide()
    {
        AssertFreshBag(typeof(TypeComparer), "SilentDiagnostics");
        AssertFreshBag(typeof(UtilityTypeResolver), "ExpandDiagnostics");
        AssertFreshBag(typeof(MagicUtilityTypeResolver), "SilentDiagnostics");
        AssertFreshBag(typeof(SymbolNameExistenceVerifier), "SilentDiagnostics");
        AssertFreshBag(typeof(ClosureProducerInference), "SilentBag");
    }

    [Fact]
    public void IsolatedCompilation_DoesNotKeepASharedCompilationService()
    {
        typeof(IsolatedCompilation)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Should()
            .NotContain(field => field.FieldType == typeof(CompilationService));
    }

    private static void AssertFreshBag(Type owner, string name)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        FieldInfo? field = owner.GetField(name, flags);
        field.Should().BeNull($"{owner.Name}.{name} must not be one shared DiagnosticBag");

        PropertyInfo? property = owner.GetProperty(name, flags);
        property.Should().NotBeNull();
        object? first = property!.GetValue(null);
        object? second = property.GetValue(null);
        first.Should().BeAssignableTo<DiagnosticBag>();
        second.Should().BeAssignableTo<DiagnosticBag>();
        ReferenceEquals(first, second).Should().BeFalse();
    }
}

