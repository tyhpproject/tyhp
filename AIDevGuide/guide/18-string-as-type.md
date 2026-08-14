## 18. String-as-type features (advanced; all erase to `string`)

**Symbol-name types** — typed strings naming real symbols: `__ClassName __EnumName __InterfaceName
__TraitName __FunctionName __StructName __ConstName __VarName __TypedVarName<T> __PropertyName<T>
__MethodName<T> __ObjectConstName<T> __EnumCaseName<T> __CompatibleTypeName<T>`. They narrow through
existence checks and verify literal assignment at compile time:
```tyhp
if (\class_exists($n)) { /* $n: __ClassName */ }
if (\method_exists($o, $n)) { /* $n: __MethodName<typeof($o)> */ }
__ClassName $c = 'App\\User';   // errors if class unknown
```
**Template string types** — types describing sets of strings, written as a double-quoted string in
type position; `${T}` = hole (any string-valued type); quantifier right after `}` (`?`=0-1, `+`=1+,
`*`=0+, `{n} {n,} {,m} {n,m}`); canonical `${T}+` not `${T+}`:
```tyhp
type ApiMethod = "${'GET'|'POST'}";
type ApiPath   = "api/${string}/items";
```
