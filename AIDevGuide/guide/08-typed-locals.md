## 8. Typed locals (type erased)

```tyhp
int $x = 5;               // → $x = 5;
?string $label;
array<int> $nums = [1,2,3];
for (int $i = 0; $i < $n; $i++) {}
```
Only params, properties, and return types keep a spelled PHP type.
