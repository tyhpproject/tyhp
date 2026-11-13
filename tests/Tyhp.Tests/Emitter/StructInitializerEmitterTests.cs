using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class StructInitializerEmitterTests
{
    private static (string Php, IReadOnlyList<PHPOutputFile> Files) Compile(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "structs.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project: null,
                expressionTypes: result.ExpressionTypes);
            var files = new TyhpEmitter(context).Emit(result.ParsedFiles!).ToList();
            var php = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
            return (php, files);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string CompilePhp(string tyhp) => Compile(tyhp).Php;

    private static string ReturnLine(string php, string functionName)
    {
        var start = php.IndexOf("function " + functionName + "(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"function {functionName} should be emitted");
        var ret = php.IndexOf("return ", start, StringComparison.Ordinal);
        ret.Should().BeGreaterThanOrEqualTo(0, $"function {functionName} should return");
        var end = php.IndexOf(';', ret);
        return php[ret..(end + 1)];
    }

    [Fact]
    public void Emit_BracketForm_MatchesWithForm_ForNamedGenericAnonymousAliasAndExtends()
    {
        AssertSamePhp(
            """
            <?tyhp
            type Point = struct {
                float $x = 0.0;
                float $y = 0.0;
            };
            function make(): array {
                return new Point() with [x => 1.0, y => 2.0];
            }
            """,
            """
            <?tyhp
            type Point = struct {
                float $x = 0.0;
                float $y = 0.0;
            };
            function make(): array {
                return new Point[x => 1.0, y => 2.0];
            }
            """);

        AssertSamePhp(
            """
            <?tyhp
            type Box<T> = struct {
                T $value;
                int $n = 0;
            };
            function make(): array {
                return new Box<int>() with [value => 1];
            }
            """,
            """
            <?tyhp
            type Box<T> = struct {
                T $value;
                int $n = 0;
            };
            function make(): array {
                return new Box<int>[value => 1];
            }
            """);

        AssertSamePhp(
            """
            <?tyhp
            function make(): array {
                return new struct {
                    string $name;
                    int $age = 1;
                } with [name => 'Alice', age => 30];
            }
            """,
            """
            <?tyhp
            function make(): array {
                return new struct {
                    string $name;
                    int $age = 1;
                }[name => 'Alice', age => 30];
            }
            """);

        AssertSamePhp(
            """
            <?tyhp
            type Row = struct {
                string 'String Value' as $strVal = "";
                int $n = 1;
            };
            function make(): array {
                return new Row() with [strVal => 'new'];
            }
            """,
            """
            <?tyhp
            type Row = struct {
                string 'String Value' as $strVal = "";
                int $n = 1;
            };
            function make(): array {
                return new Row[strVal => 'new'];
            }
            """);

        AssertSamePhp(
            """
            <?tyhp
            type Base = struct {
                int $x = 0;
            };
            type Child = struct extends Base {
                int $y = 1;
            };
            function make(): array {
                return new Child() with [x => 2];
            }
            """,
            """
            <?tyhp
            type Base = struct {
                int $x = 0;
            };
            type Child = struct extends Base {
                int $y = 1;
            };
            function make(): array {
                return new Child[x => 2];
            }
            """);
    }

    [Fact]
    public void Emit_EmptyBrackets_MatchEmptyConstructor()
    {
        AssertSamePhp(
            """
            <?tyhp
            type Point = struct {
                int $x = 1;
                int $y = 2;
            };
            function make(): array {
                return new Point();
            }
            """,
            """
            <?tyhp
            type Point = struct {
                int $x = 1;
                int $y = 2;
            };
            function make(): array {
                return new Point[];
            }
            """);
    }

    [Fact]
    public void Emit_BracketFormWithoutSpread_FoldsToOneArrayLiteral()
    {
        var php = CompilePhp("""
            <?tyhp
            type Opts = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
            };
            function makeBase(): array {
                return new Opts[name => 'ann'];
            }
            """);

        var made = ReturnLine(php, "makeBase");
        made.Should().Be("return ['retries' => 3, 'name' => 'ann'];");
        made.Should().NotContain("nick");
        made.Should().NotContain(@"\array_replace(");
        php.Should().NotContain("...");
    }

    [Fact]
    public void Emit_Spread_PrunesGuaranteedDefaults_AndKeepsNullableGaps()
    {
        var php = CompilePhp("""
            <?tyhp
            type Opts = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
            };
            type Loose = struct {
                string $name;
                ?int $retries;
            };
            type Dest = struct {
                string $name;
                ?int $retries = 3;
            };
            type Base = struct {
                string $name;
            };
            type Child = struct extends Base {
                int $retries = 3;
            };
            function makeCopy(Opts $base): array {
                return new Opts[...$base, nick => 'a'];
            }
            function fromLoose(Loose $src): array {
                return new Dest[...$src];
            }
            function fromBase(Base $base): array {
                return new Child[...$base, name => 'z'];
            }
            function merged(Opts $base): array {
                return new Opts[...$base, name => 'n', retries => 9];
            }
            """);

        ReturnLine(php, "makeCopy").Should().Be(@"return \array_replace($base, ['nick' => 'a']);");
        ReturnLine(php, "fromLoose").Should().Be(@"return \array_replace(['retries' => 3], $src);");
        ReturnLine(php, "fromBase").Should().Be(@"return \array_replace(['retries' => 3], $base, ['name' => 'z']);");
        ReturnLine(php, "merged").Should().Be(@"return \array_replace($base, ['name' => 'n', 'retries' => 9]);");
        php.Should().NotContain("...");
    }

    [Fact]
    public void Emit_IntegerAliasSpread_KeepsKeysZeroAndOne()
    {
        var php = CompilePhp("""
            <?tyhp
            type Args = struct {
                mixed 0 as $arg1;
                mixed 1 as $arg2;
            };
            function shift(Args $src): array {
                return new Args[...$src, arg2 => 'z'];
            }
            """);

        var line = ReturnLine(php, "shift");
        line.Should().Be(@"return \array_replace($src, [1 => 'z']);");
        line.Should().NotContain("...");
        line.Should().NotContain("'0'");
        line.Should().NotContain("'1'");
    }

    [Fact]
    public void Emit_AliasSpread_UsesArrayKey()
    {
        var php = CompilePhp("""
            <?tyhp
            type Row = struct {
                string 'String Value' as $strVal = "";
                int $n = 1;
            };
            function copy(Row $r): array {
                return new Row[...$r, strVal => 'new'];
            }
            """);

        ReturnLine(php, "copy").Should().Be(@"return \array_replace($r, ['String Value' => 'new']);");
        php.Should().NotContain("...");
        php.Should().NotContain("'strVal'");
    }

    [Fact]
    public void Emit_CloneAndInPlaceWithSpread_UseArrayReplace()
    {
        var php = CompilePhp("""
            <?tyhp
            type Opts = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
            };
            function cloned(Opts $p, Opts $q): array {
                return clone $p with [...$q, nick => 'a'];
            }
            function inplace(Opts $p, Opts $q): array {
                return $p with [...$q];
            }
            """);

        ReturnLine(php, "cloned").Should().Be(@"return \array_replace($p, $q, ['nick' => 'a']);");
        ReturnLine(php, "inplace").Should().Be(@"return \array_replace($p, $q);");
        php.Should().NotContain("clone ");
        php.Should().NotContain(" with ");
        php.Should().NotContain("...");
    }

    [Fact]
    public void Emit_AnonymousAndGenericSpread_PruneFromTheSourceShape()
    {
        var php = CompilePhp("""
            <?tyhp
            type Src<T> = struct {
                T $value;
            };
            type Dest = struct {
                ?string $value = 'x';
            };
            function copyAnon(struct { string $name; int $retries = 3; ?string $nick; } $src): array {
                return new struct {
                    string $name;
                    int $retries = 3;
                    ?string $nick;
                }[...$src, nick => 'a'];
            }
            function keep(Src<?string> $src): array {
                return new Dest[...$src];
            }
            function drop(Src<string> $src): array {
                return new Dest[...$src];
            }
            """);

        ReturnLine(php, "copyAnon").Should().Be(@"return \array_replace($src, ['nick' => 'a']);");
        ReturnLine(php, "keep").Should().Be(@"return \array_replace(['value' => 'x'], $src);");
        ReturnLine(php, "drop").Should().Be(@"return \array_replace($src);");
        php.Should().NotContain("...");
    }

    [Fact]
    public void Emit_ObjectWith_IsUnchanged()
    {
        var php = CompilePhp("""
            <?tyhp
            class Point {
                public int $x = 0;
                public int $y = 0;
            }
            type Src = struct {
                int $x = 0;
                int $y = 0;
            };
            function copy(Point $p, Src $src): Point {
                return clone $p with [...$src, x => 1];
            }
            function make(Src $src): Point {
                return new Point() with [...$src];
            }
            function plain(): Point {
                return new Point() with [x => 1];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $p, [...$src, 'x' => 1])");
        php.Should().Contain(@"\Tyhp\ObjectHelper::with(new Point(), [...$src])");
        php.Should().Contain(@"\Tyhp\ObjectHelper::with(new Point(), ['x' => 1])");
        php.Should().NotContain(@"\array_replace(");
    }

    [Fact]
    public void Run_SpreadOverrideOrder_IntegerKeys_AndSingleEvaluation()
    {
        var (php, files) = Compile("""
            <?tyhp
            type Opts = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
            };
            type Args = struct {
                mixed 0 as $arg1;
                mixed 1 as $arg2;
            };
            class Counter {
                public static int $hits = 0;
            }
            function order(Opts $base): array {
                return new Opts[name => 'first', ...$base];
            }
            function finish(Opts $base): array {
                return new Opts[...$base, name => 'last'];
            }
            function layers(Opts $a, Opts $b): array {
                return new Opts[...$a, name => 'mid', ...$b];
            }
            function shift(Args $src): array {
                return new Args[...$src, arg2 => 'z'];
            }
            function once(Opts $base): array {
                return new Opts[...bump($base), nick => 'a'];
            }
            function bump(Opts $o): Opts {
                Counter::$hits = Counter::$hits + 1;
                return $o;
            }
            """);

        ReturnLine(php, "order").Should().Be(
            @"return \array_replace(['name' => 'first'], $base);");
        ReturnLine(php, "finish").Should().Be(
            @"return \array_replace($base, ['name' => 'last']);");
        ReturnLine(php, "layers").Should().Be(
            @"return \array_replace($a, ['name' => 'mid'], $b);");
        ReturnLine(php, "shift").Should().Be(@"return \array_replace($src, [1 => 'z']);");
        ReturnLine(php, "once").Should().Be(@"return \array_replace(bump($base), ['nick' => 'a']);");
        php.Should().NotContain("...");

        var output = EmittedPhpRunner.Run(files, """
            $base = ['retries' => 3, 'name' => 'ann'];
            $order = \order($base);
            echo $order['name'], ',', $order['retries'], "\n";
            $finish = \finish($base);
            echo $finish['name'], ',', $finish['retries'], "\n";
            $a = ['retries' => 1, 'name' => 'ann'];
            $b = ['retries' => 9, 'name' => 'bob'];
            $layers = \layers($a, $b);
            echo $layers['name'], ',', $layers['retries'], "\n";
            $src = [0 => 'a', 1 => 'b'];
            $shift = \shift($src);
            echo $shift[0], ',', $shift[1], "\n";
            echo implode(',', array_map(static fn ($k) => (string) $k, array_keys($shift))), "\n";
            $once = \once($base);
            echo $once['nick'], ',', $once['name'], ',', $once['retries'], ',', \Counter::$hits, "\n";
            """);

        output.Should().Be("ann,3\nlast,3\nbob,9\na,z\n0,1\na,ann,3,1\n");
    }

    private static void AssertSamePhp(string withForm, string bracketForm)
    {
        var withPhp = CompilePhp(withForm);
        var bracketPhp = CompilePhp(bracketForm);
        bracketPhp.Should().Be(withPhp);
        bracketPhp.Should().NotContain(@"\array_replace(");
        bracketPhp.Should().NotContain("...");
    }
}
