## 25. Runtime packages (`\Tyhp\`, PHP ≥ 8.1; compiler auto-adds the dependency)

| Package | Needed for | Key symbols |
|---------|-----------|-------------|
| `tyhp/core` | generics runtime, `typeof`, `:=`/disposal, object `with` | `\Tyhp\Type`, `Contracts\IsDisposable`, `DisposableScope`, `ObjectHelper` |
| `tyhp/decimal` | `decimal` | `\Tyhp\Decimal`, `\Tyhp\decimal()` |
| `tyhp/async` | `async`/`await` | `\Tyhp\Promise`, `EventLoop`, `CancellationToken(Source)`, `Contracts\AsyncIterator` |
| `tyhp/lambda` | `Expression<>`/`PropertyPath<>` | `\Tyhp\Expression`, `ExpressionVisitor` |
