this folder is for the generated Tyhpdef files organized like so:
    vendor/library_name/version/**/*.tyhpdef

each vendor/library_name/version folder is its own composer package

Note: Tyhp library projects (`"type": "library"` in `tyhp.json`) auto-generate a `package.tyhp.json`
manifest in the project root when compiled (Story 20, Track C). This manifest is distributed with
the Composer package and auto-discovered by consuming Tyhp projects from `vendor/*/package.tyhp.json`
(it references or embeds the library’s public API tyhpdef content).