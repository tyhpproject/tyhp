this folder is for the generated Tyhpdef files organized like so:
    vendor/library_name/version/**/*.tyhpdef

each vendor/library_name/version folder is its own composer package

Note: Tyhp library projects (`"type": "library"` in `tyhp.json`) additive-merge `extra.tyhp.package`
onto publish-directory `composer.json` when compiled. That object is the tyhpdef package spec
distributed with the Composer package and auto-discovered by consuming Tyhp projects from
`vendor/*/*/composer.json` (it lists the library’s public API tyhpdef files, conventionally
`package.tyhpdef`).