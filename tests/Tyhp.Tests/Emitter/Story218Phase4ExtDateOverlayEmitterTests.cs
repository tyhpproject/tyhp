using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// DateTime comparison operators emit as native PHP; arithmetic maps onto
/// <c>add</c> / <c>sub</c> / <c>diff</c> via extension-operator splices.
/// Uses a synthetic DateTime tyhpdef, not the live Ext.Date overlay.
/// </summary>
[Trait("Category", "Emitter")]
public class Story218Phase4ExtDateOverlayEmitterTests
{
    [Fact]
    public void DateTime_LessThanAndSpaceship_EmitAsNativeOperators()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function less(\DateTime $dt, \DateTimeInterface $other): bool {
                return $dt < $other;
            }
            function spaceship(\DateTime $dt, \DateTimeInterface $other): int {
                return $dt <=> $other;
            }
            """);

        php.Should().Contain("$dt < $other");
        php.Should().Contain("$dt <=> $other");
        php.Should().NotContain("__lt");
        php.Should().NotContain("__lessThan");
        php.Should().NotContain("__spaceship");
        php.Should().NotContain("__compare");
    }

    [Fact]
    public void DateTime_PlusInterval_EmitsAddNotPlus()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function add(\DateTime $dt, \DateInterval $interval): \DateTime {
                return $dt + $interval;
            }
            function addImmutable(\DateTimeImmutable $dt, \DateInterval $interval): \DateTimeImmutable {
                return $dt + $interval;
            }
            """);

        php.Should().Contain("$dt->add($interval)");
        php.Should().NotContain("$dt + $interval");
        php.Should().NotContain("__add");
    }

    [Fact]
    public void DateTime_MinusDateTime_EmitsDiff()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function diff(\DateTime $dt, \DateTimeInterface $otherDt): \DateInterval {
                return $dt - $otherDt;
            }
            """);

        php.Should().Contain("$dt->diff($otherDt)");
        php.Should().NotContain("$dt - $otherDt");
        php.Should().NotContain("__sub");
        php.Should().NotContain("__diff");
    }

    [Fact]
    public void DateTime_MinusInterval_EmitsSub()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function sub(\DateTime $dt, \DateInterval $interval): \DateTime {
                return $dt - $interval;
            }
            """);

        php.Should().Contain("$dt->sub($interval)");
        php.Should().NotContain("$dt - $interval");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(
            tyhp,
            SyntheticPhpStubs.DateTimeOperators,
            phpVersion: "8.4");

        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var unexpectedErrors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        unexpectedErrors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        var project = new Project(configuration);
        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }
}
