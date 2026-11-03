using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// TYHP4198: property reads/writes, method calls, and struct/object indexing through a receiver
/// whose type admits null must be narrowed first. Narrowed receivers, <c>?-&gt;</c>, and
/// existence probes stay clean.
/// </summary>
[Trait("Category", "Checker")]
public class PossiblyNullReceiverTests
{
    private const string Declarations = """
        <?tyhp
        namespace App;

        type User = struct {
            int $id;
            string $name;
            ?string $email;
            ?Address $address;
        };

        type Address = struct {
            string $city;
        };

        class Account {
            public ?Account $parent = null;
            public static ?Account $current = null;
            public function __construct(public string $name) {}
            public function label(): string { return $this->name; }
            public function parentOrNull(): ?Account { return $this->parent; }

            public function parentName(): string {
                if ($this->parent !== null) {
                    return $this->parent->name;
                }
                return '';
            }

            public static function currentName(): string {
                if (self::$current !== null) {
                    return self::$current->name;
                }
                return '';
            }
        }

        function findUser(array<User> $users, int $id): ?User {
            foreach ($users as $u) {
                if ($u->id === $id) {
                    return $u;
                }
            }
            return null;
        }

        function findAccount(string $name): ?Account {
            if ($name === 'x') {
                return new Account($name);
            }
            return null;
        }

        function isAccount(mixed $value): $value is Account {
            return $value instanceof Account;
        }

        class Node {
            public function __construct(public Account $owner, public ?Node $next = null) {}
            public function nextOrNull(): ?Node { return $this->next; }
        }

        """;

    [Fact]
    public void StructPropertyRead_OnNullableStructLocal_Reports()
    {
        var errors = PossiblyNullErrors("""
            function structCase(array<User> $users): string {
                $user = findUser($users, 2);
                return $user->name;
            }
            """);

        errors.Should().ContainSingle()
            .Which.Message.Should().Contain("`->name`").And.Contain("`?\\App\\User`");
    }

    [Fact]
    public void ClassPropertyRead_OnNullableObjectLocal_Reports()
    {
        var errors = PossiblyNullErrors("""
            function classCase(): string {
                $account = findAccount('x');
                return $account->name;
            }
            """);

        errors.Should().ContainSingle()
            .Which.Message.Should().Contain("`->name`").And.Contain("`?\\App\\Account`");
    }

    [Fact]
    public void NullablePropertyReturnedAsNonNull_StillReportsReturnMismatchOnly()
    {
        var diagnostics = CompileAndCheck(Declarations + """
            function emailCase(User $u): string {
                return $u->email;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPossiblyNullReceiver);
    }

    [Theory]
    [InlineData("function f(): string { $a = findAccount('x'); return $a->label(); }", "->label()")]
    [InlineData("function f(): string { return findAccount('x')->name; }", "->name")]
    [InlineData("function f(): string { return findAccount('x')->label(); }", "->label()")]
    [InlineData("function f(Account $a): string { return $a->parent->name; }", "->name")]
    [InlineData("function f(Account $a): string { return $a->parentOrNull()->label(); }", "->label()")]
    [InlineData("function f(): string { return Account::$current->name; }", "->name")]
    [InlineData("function f(?Account $a): string { return $a->name; }", "->name")]
    [InlineData("function f(Account|null $a): string { return $a->name; }", "->name")]
    [InlineData("function f(User $u): string { return $u->address->city; }", "->city")]
    [InlineData("function f(): void { $a = findAccount('x'); $a->name = 'y'; }", "->name")]
    [InlineData("function f(?Account $a): void { $a->parent ??= new Account('p'); }", "->parent")]
    [InlineData("function f(?Account $a): string { return $a->label() ?? ''; }", "->label()")]
    public void MemberAccess_ThroughPossiblyNullReceiver_Reports(string body, string access)
    {
        var errors = PossiblyNullErrors(body);

        errors.Should().ContainSingle()
            .Which.Message.Should().Contain("`" + access + "`");
    }

    [Fact]
    public void Index_OnNullableArrayAccessObject_Reports()
    {
        var errors = PossiblyNullErrors("""
            /** @implements \ArrayAccess<string, int> */
            class Bag implements \ArrayAccess<string, int> {
                public function offsetExists(mixed $offset): bool { return false; }
                public function offsetGet(mixed $offset): int { return 0; }
                public function offsetSet(mixed $offset, mixed $value): void {}
                public function offsetUnset(mixed $offset): void {}
            }
            function f(?Bag $bag): int {
                return $bag['k'];
            }
            """);

        errors.Should().ContainSingle()
            .Which.Message.Should().Contain("`[]`");
    }

    [Fact]
    public void Index_OnNullableArray_DoesNotReport()
    {
        var errors = PossiblyNullErrors("""
            function f(?array<string, int> $map): ?int {
                return $map['k'] ?? null;
            }
            function g(?array<string, int> $map): void {
                $map['k'] = 1;
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ExtensionWithNonNullTarget_OnNullableReceiver_Reports()
    {
        var errors = PossiblyNullErrors("""
            extension StrOps extends string {
                function shout(): string { return $this; }
            }
            function f(?string $s): string {
                return $s->shout();
            }
            """);

        errors.Should().ContainSingle()
            .Which.Message.Should().Contain("`->shout()`");
    }

    [Fact]
    public void ExtensionWithNullableTarget_OnNullableReceiver_DoesNotReport()
    {
        var diagnostics = CompileAndCheck(Declarations + """
            extension StrOps extends ?string {
                function orEmpty(): string { return $this ?? ''; }
            }
            extension Labels extends null|string|int {
                function tag(): string { return 'x'; }
            }
            function f(?string $s, null|string|int $v): string {
                return $s->orEmpty() . $v->tag();
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Theory]
    [InlineData("function f(?Account $a): string { if ($a !== null) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { if (null !== $a) { return $a->label(); } return ''; }")]
    [InlineData("function f(?Account $a): string { if ($a === null) { return ''; } return $a->name; }")]
    [InlineData("function f(?Account $a): string { if ($a === null) { throw new \\LogicException('x'); } return $a->name; }")]
    [InlineData("function f(?Account $a): string { if ($a instanceof Account) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { if ($a is Account) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { if (!($a instanceof Account)) { return ''; } return $a->name; }")]
    [InlineData("function f(mixed $a): string { if (isAccount($a)) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { return $a !== null ? $a->name : ''; }")]
    [InlineData("function f(?Account $a): bool { return $a !== null && $a->name === 'x'; }")]
    [InlineData("function f(?Account $a): bool { return $a === null || $a->name === 'x'; }")]
    [InlineData("function f(?Account $a): string { return $a->name ?? ''; }")]
    [InlineData("function f(?Account $a): string { return $a?->name ?? ''; }")]
    [InlineData("function f(?Account $a): string { return $a?->label() ?? ''; }")]
    [InlineData("function f(?Account $a): ?string { return $a?->parent?->name; }")]
    [InlineData("function f(?Account $a): bool { return isset($a->name); }")]
    [InlineData("function f(?Account $a): bool { return empty($a->parent); }")]
    [InlineData("function f(?Account $a): string { $a = new Account('x'); return $a->name; }")]
    [InlineData("function f(?Account $a): string { $a ??= new Account('x'); return $a->name; }")]
    [InlineData("function f(Account $a): string { if ($a->parent !== null) { return $a->parent->name; } return ''; }")]
    [InlineData("function f(User $u): string { if ($u->address !== null) { return $u->address->city; } return ''; }")]
    [InlineData("function f(?Account $a): string { if ($a === null) { return ''; } $g = fn(): string => $a->name; return $g(); }")]
    [InlineData("function f(Account $a): int { $n = 0; ?Account $cur = $a; while ($cur !== null) { $n++; $cur = $cur->parent; } return $n; }")]
    [InlineData("function f(array<?Account> $all): int { $n = 0; foreach ($all as $a) { if ($a === null) { continue; } $n += \\strlen($a->name); } return $n; }")]
    [InlineData("function f(Account $a): string { return $a->parentName() . Account::currentName(); }")]
    public void NarrowedOrProbedReceiver_DoesNotReport(string body)
    {
        var diagnostics = CompileAndCheck(Declarations + body);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Theory]
    [InlineData("function f(?Node $n): string { $o = $n?->owner; return $o->name; }", "->name", "?\\App\\Account")]
    [InlineData("function f(?Node $n): string { $o = $n?->owner; return $o->label(); }", "->label()", "?\\App\\Account")]
    [InlineData("function f(?Node $n): string { $o = $n?->nextOrNull()?->owner; return $o->name; }", "->name", "?\\App\\Account")]
    [InlineData("function f(?Node $n): ?string { return $n?->next->owner->label(); }", "->owner", "?\\App\\Node")]
    [InlineData("function f(?Node $n): ?string { return $n?->nextOrNull()->owner->name; }", "->owner", "?\\App\\Node")]
    public void NullSafeRead_LeavingTheChain_IsNullable(string body, string access, string type)
    {
        var errors = PossiblyNullErrors(body);

        errors.Should().ContainSingle()
            .Which.Message.Should().Contain("`" + access + "`").And.Contain("`" + type + "`");
    }

    [Theory]
    [InlineData("function f(?Node $n): string { return $n?->owner->name ?? ''; }")]
    [InlineData("function f(?Node $n): string { return $n?->owner->label() ?? ''; }")]
    [InlineData("function f(?Node $n): string { return ($n?->owner)->label() ?? ''; }")]
    [InlineData("function f(?Node $n): string { return $n?->next?->owner->label() ?? ''; }")]
    [InlineData("function f(?Node $n): string { return $n?->nextOrNull()?->owner->name ?? ''; }")]
    [InlineData("function f(Node $n): string { $o = $n?->owner; return $o->name; }")]
    public void NullSafeChain_LaterLinksSeeNonNullReceiver(string body)
    {
        var diagnostics = CompileAndCheck(Declarations + body);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Theory]
    [InlineData("function f(?Node $n): Account { return $n?->owner; }")]
    [InlineData("function f(?Node $n): string { return $n?->owner->name; }")]
    [InlineData("function f(?Node $n): string { return $n?->owner->label(); }")]
    public void NullSafeChainValue_IsNullable(string body)
    {
        var diagnostics = CompileAndCheck(Declarations + body);

        diagnostics.Errors.Should().ContainSingle(Describe(diagnostics))
            .Which.Code.Should().Be(MessageCode.CheckerIncompatibleReturnType);
    }

    [Theory]
    [InlineData("function f(?Account $a): string { \\assert($a !== null); return $a->name; }")]
    [InlineData("function f(mixed $a): string { \\assert($a instanceof Account); return $a->name; }")]
    [InlineData("function f(?Account $a): string { if ($a !== null) \\assert($a->parent !== null); else return ''; return $a->parent->name; }")]
    [InlineData("function f(?Account $a, int $k): string { if ($a !== null) { $k++; } else { return ''; } return $a->name; }")]
    [InlineData("function f(?Account $a, int $k): string { if ($a === null) { return ''; } elseif ($k > 0) { $k--; } else { $k++; } return $a->name; }")]
    [InlineData("function f(?Account $a): string { if ($a != null) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { if (null != $a) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { if ($a <> null) { return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { if ($a == null) { return ''; } return $a->name; }")]
    [InlineData("function f(Account $a): string { if ($a->parent !== null && $a->parent->parent !== null) { return $a->parent->parent->name; } return ''; }")]
    [InlineData("function f(Account $a): string { if ($a->parent === null || $a->parent->parent === null) { return ''; } return $a->parent->parent->name; }")]
    [InlineData("function f(Node $n): string { if ($n->next !== null && $n->next->next !== null) { return $n->next->next->owner->name; } return ''; }")]
    [InlineData("function f(Node $n): void { if ($n->next !== null && $n->next->next !== null) { $n->next = $n->next->next; } }")]
    [InlineData("class Tree { public function __construct(public Node $root) {} public function second(): string { if ($this->root->next !== null) { return $this->root->next->owner->name; } return ''; } }")]
    [InlineData("function f(?Account $a): string { switch (true) { case $a !== null: return $a->name; default: break; } return ''; }")]
    [InlineData("function f(?Account $a): string { switch (true) { case $a === null: break; default: return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a, bool $b): string { switch (true) { case $a === null: return ''; case $b: return $a->name; } return ''; }")]
    [InlineData("function f(?Account $a): string { switch (true) { case $a === null: return ''; case $a->name === 'x': return 'y'; } return ''; }")]
    [InlineData("function f(?Account $a): string { return match (true) { $a === null => '', default => $a->name }; }")]
    [InlineData("function f(?Account $a, bool $b): string { return match (true) { $a === null => '', $b => $a->name, default => $a->label() }; }")]
    [InlineData("function f(?Account $a): string { return match (true) { $a === null => '', $a->name === 'x' => 'y', default => $a->label() }; }")]
    [InlineData("function f(?Account $a): string { return match (true) { $a === null, $a->name === '' => '', default => $a->name }; }")]
    [InlineData("function f(?Account $a): string { return match (true) { $a !== null => $a->name, default => '' }; }")]
    [InlineData("function f(): string { if (Account::$current !== null) { return Account::$current->name; } return ''; }")]
    [InlineData("function f(): string { if (Account::$current === null) { return ''; } return Account::$current->label(); }")]
    public void SafeNarrowingForms_DoNotReport(string body)
    {
        var diagnostics = CompileAndCheck(Declarations + body);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Theory]
    [InlineData("function f(?Account $a): string { if ($a == null) { return $a->name; } return ''; }", "->name")]
    [InlineData("function f(?Account $a): string { if ($a != null) { return ''; } return $a->name; }", "->name")]
    [InlineData("function f(?Account $a, int $k): string { if ($k > 0) { $k++; } elseif ($a !== null) { return ''; } return $a->name; }", "->name")]
    [InlineData("function f(Account $a): string { if ($a->parent !== null) { $a->parent = $a->parentOrNull(); return $a->parent->name; } return ''; }", "->name")]
    [InlineData("function f(Account $a): string { if ($a->parent !== null && $a->parent->parent !== null) { $a->parent = new Account('x'); return $a->parent->parent->name; } return ''; }", "->name")]
    [InlineData("function f(Account $a): string { if ($a->parent !== null && $a->parent->parent !== null) { unset($a->parent); return $a->parent->parent->name; } return ''; }", "->name")]
    [InlineData("function f(Node $n, bool $c): string { if ($n->next !== null) { if ($c) { $n->next = null; } return $n->next->owner->name; } return ''; }", "->owner")]
    [InlineData("function f(Node $n, bool $c): string { if ($n->next !== null) { while ($c) { if ($c) { $n->next = null; } $c = false; } return $n->next->owner->name; } return ''; }", "->owner")]
    [InlineData("class Tree { public function __construct(public Node $root) {} public function second(): string { if ($this->root->next !== null) { $this->root = new Node(new Account('x')); return $this->root->next->owner->name; } return ''; } }", "->owner")]
    [InlineData("function f(?Account $a, int $k): string { switch ($k) { case 1: return ''; default: return $a->name; } }", "->name")]
    [InlineData("function f(?Account $a): string { return match (true) { $a !== null => '', default => $a->name }; }", "->name")]
    [InlineData("function f(?Account $a): string { return match (true) { $a->name === 'x' => 'y', $a === null => '', default => '' }; }", "->name")]
    [InlineData("function f(?Account $a, bool $b): string { switch (true) { case $b: return ''; case $a->name === 'x': return 'y'; } return ''; }", "->name")]
    [InlineData("function f(): string { if (Account::$current !== null) { Account::$current = null; return Account::$current->name; } return ''; }", "->name")]
    public void UnsafeOrInvalidatedNarrowing_Reports(string body, string access)
    {
        var errors = PossiblyNullErrors(body);

        errors.Should().Contain(e => e.Message.Contains("`" + access + "`"), Describe(errors));
    }

    [Fact]
    public void MixedReceiver_ReportsMixedNarrowingOnly()
    {
        var diagnostics = CompileAndCheck(Declarations + """
            function f(?mixed $m): void {
                $m->name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPossiblyNullReceiver);
    }

    private static List<IDiagnostic> PossiblyNullErrors(string body) =>
        CompileAndCheck(Declarations + body).Errors
            .Where(d => d.Code == MessageCode.CheckerPossiblyNullReceiver)
            .ToList();

    private static string Describe(DiagnosticBag diagnostics) => Describe(diagnostics.Errors);

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(d => $"{(int)d.Code} {d.Message} @{d.Line}:{d.Column}"));

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
