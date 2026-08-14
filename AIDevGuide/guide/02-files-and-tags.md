## 2. Files & open tags

| File | Contents | Tag | Result |
|------|----------|-----|--------|
| `.tyhp` | Tyhp code | `<?tyhp ` | → `.php` |
| `.tyhpdef` | type declarations for external PHP (like `.d.ts`) | `<?tyhpdef ` | consumed, not emitted |
| `.php` | plain PHP | `<?php` | used as-is |

Open tag needs trailing whitespace. `?>`/inline HTML echo like PHP. **Tagless mode**
(`source.tagless: true`): tag optional (extension picks language), `?>` becomes an error, file must
be pure code. Default off.
