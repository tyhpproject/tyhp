# Story 06 Phase 7 — Tagless Source Mode Fixtures

Requires `source.tagless: true` in `tyhp.json` or `--source:tagless=true` on the CLI.

> **Automation:** expectations are asserted by `manifest.json` in this directory (Story 07 Phase 5A).
> Run `dotnet test tyhp.sln --filter "Category=Conformance"`. Keep this README in sync when editing cases.

## Expected lint results

| Fixture | Tagless enabled | Expected |
|---------|-----------------|----------|
| `tagless_function.tyhp` | yes | 0 errors (parses without `<?tyhp`) |
| `tagless_with_open_tag.tyhp` | yes | 0 errors (open tag still allowed) |
| `tagless_tyhpdef.tyhpdef` | yes | 0 errors (parses without `<?tyhpdef`) |
| `tagless_close_tag_error.tyhp` | yes | `TYHP1004` at the `?>` line (may also report parse follow-on errors) |
| `tagless_function.tyhp` | no (default) | 0 errors (classic mode: content without `<?tyhp` is treated as inline HTML, unchanged from pre-tagless behavior) |

## Verification commands

```bash
dotnet run --project tyhp.csproj -- lint \
  --source:tagless=true \
  --phpVersion=8.2 \
  tests/conformance/story06/tagless/tagless_function.tyhp \
  tests/conformance/story06/tagless/tagless_with_open_tag.tyhp \
  tests/conformance/story06/tagless/tagless_tyhpdef.tyhpdef

dotnet run --project tyhp.csproj -- lint \
  --source:tagless=true \
  --phpVersion=8.2 \
  tests/conformance/story06/tagless/tagless_close_tag_error.tyhp
```
