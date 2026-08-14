## 20. Property accessors (= PHP 8.4 property hooks; compile straight through)

```tyhp
class Temperature {
    private float $celsius = 0.0;
    public float $fahrenheit = 32.0 {
        get => ($this->celsius * 9 / 5) + 32;
        set(float $value) { $this->celsius = ($value - 32) * 5 / 9; }
    }
}
```
Write them exactly like PHP 8.4+ property hooks. ⚠️ They're currently emitted verbatim, so target
`output.phpVersion` ≥ 8.4 for now; automatic < 8.4 downlevel is planned.
