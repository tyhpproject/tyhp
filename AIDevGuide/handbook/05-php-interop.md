## 5. PHP interop — using existing PHP libraries

Compiled Tyhp is ordinary PHP, so calling a PHP library is about giving Tyhp its **types**:

1. Install the library the normal way (`composer require vendor/lib`).
2. **If it ships Tyhp types** (a `package.tyhp.json` pointing at `.tyhpdef`/`.tyhp`), they're
   discovered automatically — just `use` the classes.
3. **If it doesn't**, write a `.tyhpdef` describing only the symbols you call, and point config at it
   (`tyhpdefInclude`, or list it in `include`):
   ```tyhpdef
   <?tyhpdef
   namespace Monolog;
   class Logger {
       public function __construct(string $name): void;
       public function info(string $message, array<string, mixed> $context = []): void;
       public function error(string $message, array<string, mixed> $context = []): void;
   }
   ```
4. Use it from `.tyhp`; the emitted PHP references the real class (`\Monolog\Logger`) unchanged:
   ```tyhp
   <?tyhp
   namespace App;
   use Monolog\Logger;
   function makeLogger(): Logger {
       Logger $log = new Logger('app');
       $log->info('ready');
       return $log;
   }
   ```

Notes: most PHP stdlib functions are known built-ins; anything unknown, declare it in a `.tyhpdef`.
Symbol resolution order ([guide §24](../guide/24-php-interop.md)) is built-in → embedded tyhpdef → vendor package → your tyhpdef →
your `.tyhp`, and **the first registration wins** — you can't shadow a library symbol with a
same-named local declaration.
