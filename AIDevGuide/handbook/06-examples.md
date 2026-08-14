## 6. Worked examples

**Extension (method + operator), then use it:**
```tyhp
<?tyhp
namespace App\Money;
extension MoneyText {
    function format(extends Money $this, string $currency): string {   // `extends T $this` = receiver
        return $currency . ' ' . $this->amount;
    }
    operator +<Money>(self $left, self $right): Money {                 // target type in <>
        return new Money($left->amount + $right->amount);
    }
}
```
```tyhp
<?tyhp
namespace App;
use App\Money\Money;
use extension App\Money\MoneyText;          // .tyhp must activate the extension
function total(Money $a, Money $b): string {
    Money $sum = $a + $b;                    // uses the extension operator
    return $sum->format('USD');              // → \App\Money\MoneyText::format($sum, 'USD')
}
```

**Operator overloading on a class:**
```tyhp
class Money {
    public function __construct(public int $amount): void {}
    operator +(self $l, self $r): self => new Money($l->amount + $r->amount);
    operator ==(self $l, self $r): bool => $l->amount === $r->amount;
    operator convert(self $v): int { return $v->amount; }
}
```

**Deterministic disposal (`using` + `:=`):** the resource must implement `\Tyhp\Contracts\IsDisposable`.
```tyhp
using (FileReader $f = FileReader::open('data.csv')) {
    string $line = $f->readLine();
}                                            // $f->dispose() runs in finally (reverse order for many)

function importAll(): void {
    $conn := Db::connect();                  // disposed at the end of this scope
    $conn->run('IMPORT ...');
}
```
Failure rules ([guide §14](../guide/14-disposal.md)): a throwing `dispose()` in a `using` masks the body exception; multiple
disposal failures surface as `\Tyhp\Exceptions\AggregateException` (disposal errors only). `:=`
disposal only warns on failure — use `using` when you need dispose errors to propagate.

**`with` on a struct (only the `new` form compiles today):**
```tyhp
struct Config { bool $enabled = true; string $name = ''; }
Config $base = new Config() with [name => 'base', enabled => true];   // → ['name'=>'base','enabled'=>true]
// To "update", build a new value (clone/in-place struct `with` doesn't compile yet):
Config $off  = new Config() with [name => $base->name, enabled => false];
```

**Type guards + narrowing:**
```tyhp
function isUser(mixed $v): $v instanceof User { return $v instanceof User; }
function greet(mixed $v): string {
    if (isUser($v)) { return 'Hi ' . $v->name; }   // $v : User here
    if ($v === null) { return 'anon'; }
    return 'unknown';
}
```

**async + cancellation (Promise API):**
```tyhp
async function fetchAll(array<string> $urls, CancellationToken $token): array<string> {
    array<string> $out = [];
    foreach ($urls as $u) {
        $token->throwIfCancellationRequested();
        string $body = await fetchAsync($u);
        $out[] = $body;
    }
    return $out;
}
function run(array<string> $urls): array<string> {
    $cts = new \Tyhp\CancellationTokenSource(5000);        // auto-cancel after 5s
    try {
        return \Tyhp\Promise::run(fn() => fetchAll($urls, $cts->getToken()), $cts->getToken());
    } catch (\Tyhp\Exceptions\OperationCancelledException $e) {
        return [];
    } finally {
        $cts->dispose();
    }
}
```
