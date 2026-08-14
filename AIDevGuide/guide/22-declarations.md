## 22. Declarations — deltas vs PHP

Classes, interfaces, traits, enums, visibility, and members are **the same as PHP** except:

- **Declaration-site generics** on `class`/`interface`/`trait`/`enum`/`function`/method names:
  `class Box<T> {}`, `interface Query<T> {}`, `trait Timestamped<T> {}`, `function map<T,U>(…)`.
  Generic args allowed on `extends`/`implements` (`extends Base<int>`).
- **Constructors must declare a return type:** `public function __construct(…): void {}` (or the
  base-call form `: parent(<args>)`). Plain PHP `__construct(…)` without a return type is not the
  Tyhp form.
- **`async`** is a function/method modifier ([§13](13-async-await.md)).
- **Return type guards** `: $x is T` ([§16](16-type-guards.md)).
- **Overload signatures** (declaration only, no body) exist at top level:
  `function area(int $r): float; function area(int $w, int $h): float;`.
- **Trait property alias** adds a Tyhp form renaming a *property*: `use T { $prop as $renamed; }`
  (PHP only aliases methods).
- Typed properties, typed `const`, constructor promotion, `abstract`/`final`/`readonly`/`static`,
  `&`-return, enum backing types/cases, trait `insteadof`/`as` (methods): **identical to PHP**.
- **`internal` visibility is not in the language** — don't use it.
- **No nested named functions/methods:** declaring a named `function` inside another function or
  method's body is a compile error (`TYHP4802`) — use a private method or a closure instead.
  Closures/arrow functions are unaffected.
