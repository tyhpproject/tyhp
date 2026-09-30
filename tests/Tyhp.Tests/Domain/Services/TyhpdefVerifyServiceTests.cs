using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20")]
public class TyhpdefVerifyServiceTests
{
    [Fact]
    public void Compare_CompatibleGenericRefinement_Passes()
    {
        var golden = IndexWithFunction("json_encode", "mixed", "string|false");
        var final = IndexWithFunction("json_encode", "array<string, mixed>|string", "string|false");
        var bag = new DiagnosticBag();
        TyhpdefVerifyService.Compare(golden, final, bag);
        bag.HasErrors.Should().BeFalse(string.Join("; ", bag.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Compare_MissingGoldenWithoutOmit_Reports7506()
    {
        var golden = IndexWithFunction("json_encode", "mixed", "string|false");
        var final = new TyhpdefApiIndex();
        var bag = new DiagnosticBag();
        TyhpdefVerifyService.Compare(golden, final, bag);
        bag.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefVerifyIncompatible);
    }

    [Fact]
    public void Compare_OmittedGolden_DoesNotFail()
    {
        var golden = IndexWithFunction("hidden", "mixed", "void");
        var final = new TyhpdefApiIndex();
        final.Omit(@"\hidden");
        var bag = new DiagnosticBag();
        TyhpdefVerifyService.Compare(golden, final, bag);
        bag.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Compare_KindMismatch_Fails()
    {
        var golden = new TyhpdefApiIndex();
        golden.AddOrReplace(new TyhpdefApiSymbol { Fqn = @"\Box", Kind = "class" });
        var final = new TyhpdefApiIndex();
        final.AddOrReplace(new TyhpdefApiSymbol { Fqn = @"\Box", Kind = "function", ReturnType = "void" });
        var bag = new DiagnosticBag();
        TyhpdefVerifyService.Compare(golden, final, bag);
        bag.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefVerifyIncompatible);
    }

    [Fact]
    public void Compare_IllegalParamNarrowing_Fails()
    {
        var golden = IndexWithFunction("accepts", "mixed", "void");
        var final = IndexWithFunction("accepts", "int", "void");
        var bag = new DiagnosticBag();
        TyhpdefVerifyService.Compare(golden, final, bag);
        bag.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefVerifyIncompatible);
    }

    [Fact]
    public void Compare_ExtraOverlayMember_Allowed()
    {
        var golden = IndexWithFunction("json_encode", "mixed", "string|false");
        var final = IndexWithFunction("json_encode", "mixed", "string|false");
        final.AddOrReplace(new TyhpdefApiSymbol { Fqn = @"\JsonException::operator +", Kind = "operator", ReturnType = "self" });
        var bag = new DiagnosticBag();
        TyhpdefVerifyService.Compare(golden, final, bag);
        bag.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void OverlayOmit_HidesGoldenFunction()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-verify-omit-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "ExtDemo.tyhpdef"), """
                <?tyhpdef
                function keep(): void;
                function gone(): void;
                """);
            var overlayDir = Path.Combine(dir, "overlays");
            Directory.CreateDirectory(overlayDir);
            File.WriteAllText(Path.Combine(overlayDir, "hand.tyhpdef"), """
                <?tyhpdef
                omit function gone();
                function extra(): int;
                """);

            var golden = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction { Name = "keep", ReturnType = "void" },
                    new TyhpdefFunction { Name = "gone", ReturnType = "void" },
                ],
            };
            var result = new TyhpdefGenerationResult();
            new TyhpdefVerifyService().Verify(
                new TyhpdefGenerationOptions { OutputDirectory = dir, Verify = true },
                result,
                golden);

            result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void OverlayLastWins_AndCliStubsFirst()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-verify-order-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "ExtDemo.tyhpdef"), """
                <?tyhpdef
                function mapped(array $x): array;
                """);
            Directory.CreateDirectory(Path.Combine(dir, "overlays", "stubs"));
            File.WriteAllText(Path.Combine(dir, "overlays", "stubs", "stubs.tyhpdef"), """
                <?tyhpdef
                function mapped(array<string, mixed> $x): array<string, mixed>;
                """);
            Directory.CreateDirectory(Path.Combine(dir, "overlays"));
            File.WriteAllText(Path.Combine(dir, "overlays", "hand.tyhpdef"), """
                <?tyhpdef
                function mapped(array<int, string> $x): array<int, string>;
                """);

            var golden = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction
                    {
                        Name = "mapped",
                        ReturnType = "array",
                        Parameters = [new TyhpdefParameter { Name = "x", Type = "array" }],
                    },
                ],
            };
            var result = new TyhpdefGenerationResult();
            new TyhpdefVerifyService().Verify(
                new TyhpdefGenerationOptions { OutputDirectory = dir, Verify = true },
                result,
                golden);

            result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void PackageManifest_UsesOverlayArray()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-verify-pkg-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.tyhpdef"), """
                <?tyhpdef
                function base(): mixed;
                """);
            var overlayPath = Path.Combine(dir, "custom-overlay.tyhpdef");
            File.WriteAllText(overlayPath, """
                <?tyhpdef
                omit function base();
                """);
            File.WriteAllText(Path.Combine(dir, "composer.json"), """
                {
                  "extra": {
                    "tyhp": {
                      "package": {
                        "include": ["./package.tyhpdef"],
                        "exclude": [],
                        "overlay": ["./custom-overlay.tyhpdef"]
                      }
                    }
                  }
                }
                """);

            var golden = new TyhpdefFile
            {
                GlobalFunctions = [new TyhpdefFunction { Name = "base", ReturnType = "mixed" }],
            };
            var result = new TyhpdefGenerationResult();
            new TyhpdefVerifyService().Verify(
                new TyhpdefGenerationOptions { OutputDirectory = dir, Verify = true },
                result,
                golden);

            result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void ExtractOmits_MemberInsidePartialClass()
    {
        var omits = TyhpdefAstApiReader.ExtractOmits("""
            <?tyhpdef
            partial class Box {
                omit public function secret();
                function keep(): void;
            }
            omit function gone();
            """);
        omits.Should().Contain(s => s.EndsWith("::secret", StringComparison.OrdinalIgnoreCase));
        omits.Should().Contain(s => s.Equals(@"\gone", StringComparison.OrdinalIgnoreCase));
    }

    private static TyhpdefApiIndex IndexWithFunction(string name, string paramType, string returnType)
    {
        var index = new TyhpdefApiIndex();
        index.AddOrReplace(new TyhpdefApiSymbol
        {
            Fqn = TyhpdefApiIndex.Qualify("", name),
            Kind = "function",
            ReturnType = returnType,
            Parameters = [new TyhpdefApiParameter { Name = "value", Type = paramType }],
        });
        return index;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
