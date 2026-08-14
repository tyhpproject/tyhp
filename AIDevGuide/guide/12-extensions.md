## 12. Extensions (add methods/operators to types you don't own)

```tyhp
<?tyhp
extension MoneyFormatting {
    function format(extends Money $this, string $currency): string {   // `extends T $this` = receiver
        return $currency . ' ' . $this->amount;
    }
    operator +<Money>(self $left, self $right): Money {}               // target type in <>
}
use extension MoneyFormatting;      // .tyhp requires explicit activation
```
- Method: first param `extends <Type> $this`. Operator: target in `<…>`, `self` = that type.
  `abstract`/`final` not allowed.
- → a class of `public static` methods; `$money->format('USD')` → `\MoneyFormatting::format($money,
  'USD')`.
- Inline `.tyhpdef` extensions ([§23](23-tyhpdef.md)) are auto-active for consumers; `.tyhp` needs `use extension`.
