## 27. Diagnostics you'll see

Format: `file(line,col): error TYHP####: message`. Checker codes are `TYHP4000`–`TYHP4999`;
severities are **error** (build fails), **warning** (fails only with `--strict`), and **info**.
Common ones:

| Code | Meaning |
|------|---------|
| `TYHP4008` | cannot assign type X to type Y |
| `TYHP4009` | return type not compatible with declared return |
| `TYHP4010` | argument not assignable to parameter |
| `TYHP4015` | value possibly null where non-null required |
| `TYHP4016` | variable needs a type annotation or inferable initializer |
| `TYHP4021` | assign to `readonly` property |
| `TYHP4025` | member visibility violation |
| `TYHP4030` | `:=` requires a type implementing `IsDisposable` |
| `TYHP4031` | property doesn't exist on type (e.g. in `with`) |
| `TYHP4032` | type-guard function must return `bool` |
| `TYHP4036` | wrong number of generic arguments |
| `TYHP4037` | struct property must be typed / required |
| `TYHP4043` | condition must be `bool` |
| `TYHP4138` | can't infer closure parameter type — annotate it |

Note: there is currently **no dedicated "unknown member" or "wrong argument count" error** — an
unresolved member/call infers the internal `unresolved` type, which is assignable to and from
everything, so it passes. Don't rely on the checker to catch those.
