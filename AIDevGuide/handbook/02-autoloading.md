## 2. Autoloading & running the output

- **Automatic:** set `build.updateComposer: true`. After a successful build the compiler writes/merges
  `composer.json` **in the output dir** with `autoload.psr-4` (your `psr4` config + mappings derived
  from emitted classes), `autoload.files` (every `*_functions.php`), and `require` entries for the
  runtime packages you used — wired via Composer **path repositories** to `runtime/packages/` (`@dev`,
  `minimum-stability: dev`). It then runs `composer install`. If `updateComposer` is false, the build
  just logs which packages you need.
- **Manual:** point your own `composer.json` `autoload.psr-4` at the output segments (e.g.
  `"App\\": "build/App/"`) and `require` the `\Tyhp\*` runtime packages you use ([§7](07-runtime-api.md)).
- **Entry points:** `build.entryPointAutoloader` (e.g. `{ "composer": "vendor/autoload.php" }`) makes
  the emitter add `require_once __DIR__ . '/vendor/autoload.php';` to entry-point files.
- **Custom path:** `declare(output_file="custom/out.php")` routes subsequent root code (or a block
  body) to a specific file under `output.path`.
