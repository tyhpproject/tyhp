## 23. Typing external PHP: `.tyhpdef` (signatures only, like `.d.ts`)

```tyhpdef
<?tyhpdef
namespace Acme\Payments;
use Acme\Currency\Formatter;
deprecated class LegacyMoney { public function __toFloat(): float; }   // `deprecated`/`obsolete` keywords
class Money implements \Stringable {
    public readonly string $amount;
    public function plus(Money $other): self;
    extension operator +(self $a, self $b): self => $a->plus($b);      // mapped: body required → rewrite
    use extension Formatter { Formatter::format as formatMoney; };      // attach extension
}
// Native engine/PECL operators: bodyless `operator` (no `extension`) → no rewrite
class Instant { operator +(self $left, DateInterval $right): Instant; }
async function fetchRate(string $currency): Promise<float>;
const string DEFAULT_CURRENCY = 'USD';
function strlen as str_len(string $s): int;                            // rename with `as`
```
- Declarable: functions, classes, interfaces, traits, enums, constants (`const T NAME;`, optional
  `?? default`), globals (`T $var;`), structs, type aliases, `namespace`/`use`.
- `deprecated`/`obsolete` are keywords (not docblocks); checker warns on use.
- `extends` on an imported function's first param = extension-method import.
- Inline `.tyhpdef` extensions (with bodies) are auto-active for consumers.
- **Operators:** bodyless `operator …;` = native PHP passthrough (no emit rewrite);
  `extension operator` **requires a body** and rewrites call sites. Bodyless `extension operator` is an error.
- A library exposes types via `package.tyhp.json`: `{ "include": ["./package.tyhpdef",
  "src/**/*.tyhp"] }`. Projects can add extra `.tyhpdef` via config.
