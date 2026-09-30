# Runtime package tests

Runtime-package overlay contracts and tyhpdef suites live beside the packages
and are run with `runtime/packages/test-all-tyhpdef.sh`. C# unit tests in this
repo do not load or rebuild those packages.
