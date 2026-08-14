## 21. Expression trees (parsable lambdas) — marquee feature

Pass an **inline `fn`** to a param typed `Expression<…>` / `PropertyPath<T,R>` and the compiler
captures a runtime AST of the lambda (not just a closure). Libraries translate the tree, e.g. → SQL
(like C# `Expression<Func<…>>` / LINQ-to-SQL).
```tyhp
class QueryBuilder<T> {
    public function where(Expression<T, bool> $predicate): static { return $this; }
    public function select<R>(Expression<T, R> $selector): static { return $this; }
}
$q = new QueryBuilder<User>()
    ->where(fn ($u) => $u->age > $minAge)
    ->select(fn ($u) => $u->firstName);
```
- `Expression<TArgs…, TReturn>` uses the same return-last convention as `callable`.
- Only inline `fn` converts (not a stored var or `function(){}`); the param type decides tree
  (`Expression<>`) vs plain closure (`callable`/`\Closure`).
- Every `Expression` also carries `->callable`, so it stays executable. Pkg `tyhp/lambda`.
  Still being wired up — treat end-to-end use as experimental.
