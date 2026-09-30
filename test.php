<?php
declare(strict_types=1);

/*
 * Hand-written stand-in for what the Tyhp compiler would emit from `test.tyhp`,
 * plus a harness that diffs the two build modes against each other.
 *
 * THE RULES UNDER TEST
 *   1. A parameter that is written — assigned, compound-assigned, incremented or
 *      decremented either way, or passed to a callee's `&` parameter — is by
 *      reference, and only those are. The author must have declared exactly
 *      those `&`; a mismatch is a compile-time error.
 *   2. A `&` parameter's argument must be referenceable, in every optimize mode.
 *      Literals, consts, call results, readonly properties, and members with a
 *      by-value read path are compile-time errors. `&get`, `&__get`, and
 *      `&offsetGet` are by-reference read paths and stay legal.
 *   3. Repeated evaluation is hoisted into a local — by value for a read,
 *      ref-bound for a `&` parameter — which needs a statement slot. With no
 *      slot, or when the rewrite would drop an argument or make it lazy, the call
 *      site keeps the real call and warns. An erased member has no call to keep,
 *      so there it is an error.
 *
 * HOW TO READ THE OUTPUT
 *   MATCH — the `optimize: none` build and the spliced build agree on the return
 *           value, on the final state of everything the call could touch, and on
 *           which side effects ran in which order.
 *   DIFF  — a gap. Both columns are printed.
 *
 * Run:  php test.php
 */

/* =========================================================================
 * Harness
 * ========================================================================= */

final class Log
{
    /** @var list<string> */
    public static array $events = [];

    public static function note(string $event): void
    {
        self::$events[] = $event;
    }

    /** @return list<string> */
    public static function take(): array
    {
        $events = self::$events;
        self::$events = [];

        return $events;
    }
}

/** Wraps an argument so its evaluation is observable. */
function effect(string $name, mixed $value): mixed
{
    Log::note($name);

    return $value;
}

/** @return array{value: mixed, error: ?string, log: list<string>} */
function capture(callable $fn): array
{
    Log::take();

    try {
        $value = $fn();
        $error = null;
    } catch (\Throwable $e) {
        $value = null;
        $error = \get_class($e) . ': ' . $e->getMessage();
    }

    return ['value' => $value, 'error' => $error, 'log' => Log::take()];
}

function render(array $captured): string
{
    $shown = $captured['error']
        ?? \preg_replace('/\s+/', ' ', \var_export($captured['value'], true));

    if ($captured['log'] !== []) {
        $shown .= '  [ran: ' . \implode(',', $captured['log']) . ']';
    }

    return (string) $shown;
}

/** Diffs an `optimize: none` call against the spliced rewrite of the same source. */
function checkParity(string $label, callable $optimizeNone, callable $spliced): void
{
    $none = capture($optimizeNone);
    $inl  = capture($spliced);

    if ($none == $inl) {
        echo '  ', \str_pad($label, 44), ' MATCH  ', render($none), \PHP_EOL;

        return;
    }

    echo '  ', \str_pad($label, 44), ' DIFF', \PHP_EOL;
    echo '  ', \str_pad('', 44), '   optimize:none  ', render($none), \PHP_EOL;
    echo '  ', \str_pad('', 44), '   spliced        ', render($inl), \PHP_EOL;
}

/** For probing one form on its own, with no counterpart to diff against. */
function probe(string $label, callable $fn): void
{
    $result = capture($fn);
    $status = $result['error'] === null ? 'OK  ' : 'FAIL';
    echo '  ', \str_pad($label, 44), ' ', $status, '   ', render($result), \PHP_EOL;
}

/** Compilability needs a subprocess: some of these are fatals, not exceptions. */
function probeCompile(string $label, string $code): void
{
    $path = \sys_get_temp_dir() . '/tyhp_probe_' . \bin2hex(\random_bytes(6)) . '.php';
    \file_put_contents($path, '<?php ' . $code . \PHP_EOL);

    $lines = [];
    \exec(\escapeshellarg(\PHP_BINARY) . ' -l ' . \escapeshellarg($path) . ' 2>&1', $lines, $status);
    \unlink($path);

    $detail = $status === 0
        ? 'compiles'
        : \trim(\preg_replace('/\s+in\s+\S+\s+on line \d+/', '', $lines[0] ?? '?') ?? '');

    echo '  ', \str_pad($label, 44), ' ', $status === 0 ? 'OK  ' : 'FAIL', '   ', $detail, \PHP_EOL;
}

function heading(string $text): void
{
    echo \PHP_EOL, $text, \PHP_EOL, \str_repeat('-', \strlen($text)), \PHP_EOL;
}

// Surface warnings as throwables so silently-dropped writes cannot pass as OK.
\set_error_handler(static function (int $no, string $msg): bool {
    throw new \ErrorException($msg, 0, $no);
});

/* =========================================================================
 * Emitted from test.tyhp — Holder
 * ========================================================================= */

final class Holder implements \ArrayAccess
{
    private string $backing = 'raw';
    private int $counter = 4;
    private array $bag = ['magic' => 4];
    private array $offsets = ['k' => 4];
    private array $items = ['x'];

    public readonly int $frozenCount;

    /** By-reference read path, so this one IS referenceable. */
    public array $refItems {
        &get { Log::note('&get'); return $this->items; }
    }

    public string $hooked {
        get { Log::note('get'); return $this->backing; }
        set(string $value) { Log::note('set'); $this->backing = $value; }
    }

    public int $hookedCount {
        get { Log::note('get'); return $this->counter; }
        set(int $value) { Log::note('set'); $this->counter = $value; }
    }

    public function __construct()
    {
        $this->frozenCount = 4;
    }

    public function __get(string $name): mixed
    {
        Log::note('__get');

        return $this->bag[$name] ?? null;
    }

    public function __set(string $name, mixed $value): void
    {
        Log::note('__set');
        $this->bag[$name] = $value;
    }

    public function offsetExists(mixed $offset): bool
    {
        return isset($this->offsets[$offset]);
    }

    public function offsetGet(mixed $offset): mixed
    {
        Log::note('offsetGet');

        return $this->offsets[$offset];
    }

    public function offsetSet(mixed $offset, mixed $value): void
    {
        Log::note('offsetSet');
        $this->offsets[$offset] = $value;
    }

    public function offsetUnset(mixed $offset): void
    {
        unset($this->offsets[$offset]);
    }

    public function readBacking(): string
    {
        return $this->backing;
    }

    public function readCounter(): int
    {
        return $this->counter;
    }

    public function readMagic(): int
    {
        return $this->bag['magic'];
    }

    public function readOffset(): int
    {
        return $this->offsets['k'];
    }

    public function readItems(): array
    {
        return $this->items;
    }
}

/** The by-reference accessor variants, which rule 2 must NOT reject. */
final class RefAccessors implements \ArrayAccess
{
    private array $bag = ['n' => 4];
    private array $offsets = ['k' => 4];

    public function &__get(string $name): mixed
    {
        Log::note('&__get');

        return $this->bag[$name];
    }

    public function offsetExists(mixed $offset): bool
    {
        return isset($this->offsets[$offset]);
    }

    public function &offsetGet(mixed $offset): mixed
    {
        Log::note('&offsetGet');

        return $this->offsets[$offset];
    }

    public function offsetSet(mixed $offset, mixed $value): void
    {
        $this->offsets[$offset] = $value;
    }

    public function offsetUnset(mixed $offset): void
    {
        unset($this->offsets[$offset]);
    }

    public function readBag(): array
    {
        return $this->bag;
    }

    public function readOffsets(): array
    {
        return $this->offsets;
    }
}

/* =========================================================================
 * Emitted from test.tyhp — Probe
 *
 * `&` appears on exactly the parameters the body writes. Everything else is by
 * value. This is the whole of the emitted difference from the Tyhp source.
 * ========================================================================= */

final class Probe
{
    public string $first = 'Ada';
    public string $last = 'Lovelace';

    // GROUP A — nothing written, nothing by reference.

    public function doubleTrim(string $s): string
    {
        return \trim($s . $s);
    }

    public function ignoreSecond(int $a, int $b): int
    {
        return $a;
    }

    public function coalesce(?string $a, string $b): string
    {
        return $a ?? $b;
    }

    public function reversed(string $a, string $b): string
    {
        return $b . $a;
    }

    public function label(): string
    {
        return $this->first . ' ' . $this->last;
    }

    // GROUP B — written parameters, declared `&`.

    public function appendTo(string &$s, string $suffix = '__end'): string
    {
        return ($s = ($s . $s) . $suffix);
    }

    public function postInc(int &$i): int
    {
        return $i++;
    }

    public function preInc(int &$i): int
    {
        return ++$i;
    }

    public function addTwo(int &$i): int
    {
        return $i += 2;
    }

    public function crazy(int &$i): int
    {
        return $i *= ($i += 2);
    }

    public function sortKeys(array &$a): bool
    {
        return \ksort($a);
    }

    public function appendItem(array &$a, string $item): int
    {
        return ($a[] = $item) !== null ? \count($a) : 0;
    }

    // GROUP D — variadics.

    public function joinAll(string $glue, string ...$parts): string
    {
        return \implode($glue, $parts);
    }

    public function upperFirst(string &...$parts): string
    {
        return ($parts[0] = \strtoupper($parts[0]));
    }
}

/* =========================================================================
 * GROUP A — pure bodies
 * ========================================================================= */

heading('GROUP A: pure bodies, no parameter is by reference');

$p = new Probe();

checkParity(
    'doubleTrim <- variable',
    static function () use ($p) {
        $s = ' asdf ';

        return [$p->doubleTrim($s), $s];
    },
    static function () {
        $s = ' asdf ';

        return [\trim($s . $s), $s];
    }
);

checkParity(
    'doubleTrim <- plain property',
    static function () use ($p) {
        return [$p->doubleTrim($p->first), $p->first];
    },
    static function () use ($p) {
        return [\trim($p->first . $p->first), $p->first];
    }
);

checkParity(
    'doubleTrim <- literal',
    static fn() => $p->doubleTrim('wnerkgfoemwe'),
    static fn() => \trim('wnerkgfoemwe' . 'wnerkgfoemwe')
);

checkParity(
    'doubleTrim <- call result (read local)',
    static fn() => $p->doubleTrim(effect('arg', 'x')),
    static function () {
        $out = \trim(($__tyhpInlineTemp1 = effect('arg', 'x')) . $__tyhpInlineTemp1);
        unset($__tyhpInlineTemp1);

        return $out;
    }
);

checkParity(
    'doubleTrim <- hooked prop (read local)',
    static function () use ($p) {
        $h = new Holder();

        return [$p->doubleTrim($h->hooked), $h->readBacking()];
    },
    static function () {
        $h = new Holder();
        $out = \trim(($__tyhpInlineTemp1 = $h->hooked) . $__tyhpInlineTemp1);
        unset($__tyhpInlineTemp1);

        return [$out, $h->readBacking()];
    }
);

echo \PHP_EOL, '  Rule 3 — arguments with no side effects: splicing is safe.', \PHP_EOL;

checkParity(
    'ignoreSecond(1, 2) — spliced',
    static fn() => $p->ignoreSecond(1, 2),
    static fn() => 1
);

checkParity(
    'coalesce(a, b) — spliced',
    static fn() => $p->coalesce('a', 'b'),
    static fn() => 'a' ?? 'b'
);

checkParity(
    'reversed(a, b) — spliced',
    static fn() => $p->reversed('a', 'b'),
    static fn() => 'b' . 'a'
);

echo \PHP_EOL, '  Rule 3 — dropped and lazy evaluation. No local can restore these, so', \PHP_EOL;
echo '  the call site keeps the real call and the compiler warns.', \PHP_EOL;

checkParity(
    'ignoreSecond — naive splice drops arg 2',
    static fn() => $p->ignoreSecond(effect('a', 1), effect('b', 2)),
    static fn() => effect('a', 1)
);

checkParity(
    'ignoreSecond — fallback to the real call',
    static fn() => $p->ignoreSecond(effect('a', 1), effect('b', 2)),
    static fn() => $p->ignoreSecond(effect('a', 1), effect('b', 2))
);

checkParity(
    'coalesce — naive splice makes arg 2 lazy',
    static fn() => $p->coalesce('a', effect('b', 'B')),
    static fn() => 'a' ?? effect('b', 'B')
);

checkParity(
    'coalesce — fallback to the real call',
    static fn() => $p->coalesce('a', effect('b', 'B')),
    static fn() => $p->coalesce('a', effect('b', 'B'))
);

checkParity(
    'reversed — naive splice flips order',
    static fn() => $p->reversed(effect('a', 'A'), effect('b', 'B')),
    static fn() => effect('b', 'B') . effect('a', 'A')
);

checkParity(
    'reversed — fallback to the real call',
    static fn() => $p->reversed(effect('a', 'A'), effect('b', 'B')),
    static fn() => $p->reversed(effect('a', 'A'), effect('b', 'B'))
);

echo \PHP_EOL, '  Receiver substitution:', \PHP_EOL;

final class ProbeFactory
{
    public function make(): Probe
    {
        Log::note('make');

        return new Probe();
    }
}

$factory = new ProbeFactory();

checkParity(
    'label() <- variable receiver, spliced',
    static function () use ($p) {
        return $p->label();
    },
    static function () use ($p) {
        return $p->first . ' ' . $p->last;
    }
);

checkParity(
    'label() <- call receiver, naive splice',
    static function () use ($factory) {
        return $factory->make()->label();
    },
    static function () use ($factory) {
        return $factory->make()->first . ' ' . $factory->make()->last;
    }
);

echo \PHP_EOL, '  Rule 3 hoists instead: a receiver read local keeps the splice and runs', \PHP_EOL;
echo '  `make()` once. Falling back is only for when there is no statement slot.', \PHP_EOL;

checkParity(
    'label() <- call receiver, receiver read local',
    static function () use ($factory) {
        return $factory->make()->label();
    },
    static function () use ($factory) {
        $out = ($__tyhpInlineTemp1 = $factory->make())->first . ' ' . $__tyhpInlineTemp1->last;
        unset($__tyhpInlineTemp1);

        return $out;
    }
);

checkParity(
    'label() <- call receiver, no slot: real call',
    static function () use ($factory) {
        return $factory->make()->label();
    },
    static function () use ($factory) {
        return $factory->make()->label();
    }
);

/* =========================================================================
 * GROUP B — written parameters, declared `&`
 * ========================================================================= */

heading('GROUP B: written parameters declared `&`');

checkParity(
    'appendTo($s, literal)',
    static function () use ($p) {
        $s = 'ab';

        return [$p->appendTo($s, '__blah'), $s];
    },
    static function () {
        $s = 'ab';

        return [($s = ($s . $s) . '__blah'), $s];
    }
);

checkParity(
    'appendTo($s) — default argument',
    static function () use ($p) {
        $s = 'ab';

        return [$p->appendTo($s), $s];
    },
    static function () {
        $s = 'ab';

        return [($s = ($s . $s) . '__end'), $s];
    }
);

checkParity(
    'postInc($i) — returns pre-value',
    static function () use ($p) {
        $i = 10;

        return [$p->postInc($i), $i];
    },
    static function () {
        $i = 10;

        return [$i++, $i];
    }
);

checkParity(
    'preInc($i) — returns post-value',
    static function () use ($p) {
        $i = 10;

        return [$p->preInc($i), $i];
    },
    static function () {
        $i = 10;

        return [++$i, $i];
    }
);

checkParity(
    'addTwo($i)',
    static function () use ($p) {
        $i = 4;

        return [$p->addTwo($i), $i];
    },
    static function () {
        $i = 4;

        return [$i += 2, $i];
    }
);

checkParity(
    'crazy($i) — written twice, read twice',
    static function () use ($p) {
        $i = 4;

        return [$p->crazy($i), $i];
    },
    static function () {
        $i = 4;

        return [$i *= ($i += 2), $i];
    }
);

checkParity(
    'sortKeys($map) — write via callee `&`',
    static function () use ($p) {
        $map = ['b' => 2, 'a' => 1];

        return [$p->sortKeys($map), \array_keys($map)];
    },
    static function () {
        $map = ['b' => 2, 'a' => 1];

        return [\ksort($map), \array_keys($map)];
    }
);

echo \PHP_EOL, '  `&` parameter with a property argument:', \PHP_EOL;

checkParity(
    'addTwo($obj->plainProp)',
    static function () use ($p) {
        $h = new class { public int $n = 4; };

        return [$p->addTwo($h->n), $h->n];
    },
    static function () {
        $h = new class { public int $n = 4; };

        return [$h->n += 2, $h->n];
    }
);

echo \PHP_EOL, '  Rule 2 — arguments the checker must reject for a `&` parameter:', \PHP_EOL;

probe('addTwo(literal)', static fn() => $p->addTwo(4));
probe('addTwo(call result)', static fn() => $p->addTwo(\intval('4')));
probe('addTwo($obj->readonlyProp)', static function () use ($p) {
    $h = new Holder();

    return $p->addTwo($h->frozenCount);
});
probe('addTwo($obj->hookedProp) — by-value get', static function () use ($p) {
    $h = new Holder();

    return $p->addTwo($h->hookedCount);
});
probe('addTwo($obj->__getProp) — by-value __get', static function () use ($p) {
    $h = new Holder();

    return $p->addTwo($h->magic);
});
probe('addTwo($arrayAccess["k"]) — by-value offsetGet', static function () use ($p) {
    $h = new Holder();

    return $p->addTwo($h['k']);
});

echo \PHP_EOL, '  Rule 2 — the same accessors declared by reference are LEGAL:', \PHP_EOL;

probe('appendItem($obj->refItems) — &get hook', static function () use ($p) {
    $h = new Holder();
    $n = $p->appendItem($h->refItems, 'added');

    return [$n, $h->readItems()];
});
probe('addTwo($obj->n) — &__get', static function () use ($p) {
    $r = new RefAccessors();
    $out = $p->addTwo($r->n);

    return [$out, $r->readBag()];
});
probe('addTwo($obj["k"]) — &offsetGet', static function () use ($p) {
    $r = new RefAccessors();
    $out = $p->addTwo($r['k']);

    return [$out, $r->readOffsets()];
});

echo \PHP_EOL, '  A by-reference accessor read is referenceable, but it is still an', \PHP_EOL;
echo '  accessor: a `&` parameter used twice runs it twice.', \PHP_EOL;

checkParity(
    'appendItem($obj->refItems) — naive splice',
    static function () use ($p) {
        $h = new Holder();
        $n = $p->appendItem($h->refItems, 'added');

        return [$n, $h->readItems()];
    },
    static function () {
        $h = new Holder();
        $n = ($h->refItems[] = 'added') !== null ? \count($h->refItems) : 0;

        return [$n, $h->readItems()];
    }
);

echo \PHP_EOL, '  A by-value read local cannot fix a `&` parameter — it breaks the', \PHP_EOL;
echo '  reference and the write never lands:', \PHP_EOL;

checkParity(
    'appendItem via by-value read local',
    static function () use ($p) {
        $h = new Holder();
        $n = $p->appendItem($h->refItems, 'added');

        return [$n, $h->readItems()];
    },
    static function () {
        $h = new Holder();
        $__tyhpInlineTemp1 = $h->refItems;
        $n = ($__tyhpInlineTemp1[] = 'added') !== null ? \count($__tyhpInlineTemp1) : 0;
        unset($__tyhpInlineTemp1);

        return [$n, $h->readItems()];
    }
);

echo \PHP_EOL, '  A ref-bound local does. It needs a statement slot, which is why rule 3', \PHP_EOL;
echo '  falls back when the call site has none:', \PHP_EOL;

checkParity(
    'appendItem via ref-bound local',
    static function () use ($p) {
        $h = new Holder();
        $n = $p->appendItem($h->refItems, 'added');

        return [$n, $h->readItems()];
    },
    static function () {
        $h = new Holder();
        $__tyhpInlineTemp1 = &$h->refItems;
        $n = ($__tyhpInlineTemp1[] = 'added') !== null ? \count($__tyhpInlineTemp1) : 0;
        unset($__tyhpInlineTemp1);

        return [$n, $h->readItems()];
    }
);

echo \PHP_EOL, '  Same shape without an accessor: a side-effecting index is evaluated', \PHP_EOL;
echo '  once per use, so it needs the same treatment.', \PHP_EOL;

checkParity(
    'addTwo($a[$i++]) — one use, splice is exact',
    static function () use ($p) {
        $a = [4, 40];
        $i = 0;
        $out = $p->addTwo($a[$i++]);

        return [$out, $a, $i];
    },
    static function () {
        $a = [4, 40];
        $i = 0;
        $out = $a[$i++] += 2;

        return [$out, $a, $i];
    }
);

checkParity(
    'crazy($a[$i++]) — two uses, naive splice',
    static function () use ($p) {
        $a = [4, 40];
        $i = 0;
        $out = $p->crazy($a[$i++]);

        return [$out, $a, $i];
    },
    static function () {
        $a = [4, 40];
        $i = 0;
        $out = $a[$i++] *= ($a[$i++] += 2);

        return [$out, $a, $i];
    }
);

checkParity(
    'crazy($a[$i++]) — ref-bound local',
    static function () use ($p) {
        $a = [4, 40];
        $i = 0;
        $out = $p->crazy($a[$i++]);

        return [$out, $a, $i];
    },
    static function () {
        $a = [4, 40];
        $i = 0;
        $__tyhpInlineTemp1 = &$a[$i++];
        $out = $__tyhpInlineTemp1 *= ($__tyhpInlineTemp1 += 2);
        unset($__tyhpInlineTemp1);

        return [$out, $a, $i];
    }
);

echo \PHP_EOL, '  The spliced form of the rejected ones. Where the splice would have', \PHP_EOL;
echo '  worked, rule 2 rejects it anyway to keep the modes identical:', \PHP_EOL;

probeCompile('literal in a write position', '$r = (4 += 2);');
probeCompile('call result in a write position', '$r = (\intval("4") += 2);');
probe('readonly prop in a write position', static function () {
    $h = new Holder();

    return $h->frozenCount += 2;
});
probe('hooked prop in a write position', static function () {
    $h = new Holder();

    return [$h->hookedCount += 2, $h->readCounter()];
});
probe('__get prop in a write position', static function () {
    $h = new Holder();

    return [$h->magic += 2, $h->readMagic()];
});
probe('ArrayAccess in a write position', static function () {
    $h = new Holder();

    return [$h['k'] += 2, $h->readOffset()];
});

/* =========================================================================
 * GROUP C — a written parameter left by value is a compile-time error
 *
 * Rule 1 makes no exception for pre-increment, so nothing in this group
 * compiles and the emitter never needs a write local to fake a by-value copy.
 * What follows is the evidence for that choice, kept as a regression guard: if
 * anyone reopens "let the write stay local", these are the costs.
 * ========================================================================= */

heading('GROUP C: written parameter left by value is rejected');

echo '  If `++$i` on a by-value parameter were allowed, the naive splice would', \PHP_EOL;
echo '  leak the write into the caller:', \PHP_EOL;

checkParity(
    'preIncLocal($i) — naive splice',
    static function () use ($p) {
        $i = 10;

        // What `optimize: none` would do: PHP copies the argument.
        $copy = $i;

        return [++$copy, $i];
    },
    static function () {
        $i = 10;

        return [++$i, $i];
    }
);

echo \PHP_EOL, '  Rewriting `++$i` to `($i + 1)` avoids the leak for ints, but `++` is', \PHP_EOL;
echo '  not `+ 1` for every type Tyhp allows, so the rewrite is not type-safe:', \PHP_EOL;

probe('++ on a numeric string', static function () {
    $s = '9';

    return [++$s, $s];
});
probe('+ 1 on a numeric string', static function () {
    $s = '9';

    return [($s + 1), $s];
});
probe('++ on an alphabetic string', static function () {
    $s = 'az';

    return [++$s, $s];
});
probe('+ 1 on an alphabetic string', static function () {
    $s = 'az';

    return [($s + 1), $s];
});

echo \PHP_EOL, '  A write local restores parity, but needs a statement slot the call site', \PHP_EOL;
echo '  may not have. Requiring `&` costs neither:', \PHP_EOL;

checkParity(
    'preIncLocal($i) via write local',
    static function () use ($p) {
        $i = 10;
        $copy = $i;

        return [++$copy, $i];
    },
    static function () {
        $i = 10;
        $__tyhpInlineTemp1 = $i;
        $out = ++$__tyhpInlineTemp1;
        unset($__tyhpInlineTemp1);

        return [$out, $i];
    }
);

echo \PHP_EOL, '  Declared `&`, the same body is exact with no local at all:', \PHP_EOL;

checkParity(
    'preInc($i) — declared `&`',
    static function () use ($p) {
        $i = 10;

        return [$p->preInc($i), $i];
    },
    static function () {
        $i = 10;

        return [++$i, $i];
    }
);

/* =========================================================================
 * GROUP D — variadics
 * ========================================================================= */

heading('GROUP D: variadics');

checkParity(
    'joinAll(glue, ...parts) — nothing written',
    static fn() => $p->joinAll('-', 'a', 'b'),
    static fn() => \implode('-', ['a', 'b'])
);

checkParity(
    'upperFirst(&...parts) — array of references',
    static function () use ($p) {
        $one = 'a';
        $two = 'b';

        return [$p->upperFirst($one, $two), $one, $two];
    },
    static function () {
        $one = 'a';
        $two = 'b';
        $__tyhpInlineTemp1 = [&$one, &$two];
        $out = ($__tyhpInlineTemp1[0] = \strtoupper($__tyhpInlineTemp1[0]));
        unset($__tyhpInlineTemp1);

        return [$out, $one, $two];
    }
);

probeCompile('by-ref param then by-value variadic', 'function d1(string &$g, string ...$p) {};');
probeCompile('by-value param then by-ref variadic', 'function d2(string $g, string &...$p) {};');
probeCompile('optional then by-ref variadic', 'function d3(string $g = "-", string &...$p) {};');

/* ========================================================================= */

heading('Scratch area');
echo '  Add experiments below this line.', \PHP_EOL, \PHP_EOL;
