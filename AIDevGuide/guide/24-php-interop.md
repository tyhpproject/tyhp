## 24. PHP interop & name resolution

- **Compiled output is ordinary PHP** in the same namespaces/class names, so plain PHP can call it and
  it can call plain PHP. To call PHP that has no Tyhp types, describe it in a `.tyhpdef` ([§23](23-tyhpdef.md)); the
  standard library / extensions come from `tyhp/php-{version}` packages.
- Namespaces, `use`, FQCN (`\A\B\C`), and `use function`/`use const` **resolve like PHP**. Constant
  names are case-sensitive and matched before the case-insensitive class/function index.
- **Symbol discovery order** (first registered wins; earlier layers shadow later same-name symbols):
  1. built-in types/functions (compiler) → 2. embedded tyhpdefs → 3. vendor `package.tyhp.json`
  packages → 4. your `tyhpdef` include paths → 5. your `.tyhp` sources. A duplicate name in the same
  scope is an error; you cannot override a tyhpdef symbol with a same-named user declaration.
