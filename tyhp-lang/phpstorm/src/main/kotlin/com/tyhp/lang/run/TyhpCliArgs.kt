package com.tyhp.lang.run

/**
 * Argv for contributed Tyhp run configurations. Flags match `DisplayHelp.BuildHelp` /
 * `DisplayHelp.LintHelp` and the global `--tyhp-project` (must be a file).
 */

const val BUILD_ACTION = "build"
const val LINT_ACTION = "lint"

fun isTyhpTaskAction(value: String?): Boolean =
    value == BUILD_ACTION || value == LINT_ACTION

/**
 * Returns argv for `tyhp build` / `tyhp lint` (not including the executable).
 *
 * - build: `build --quiet [--tyhp-project=<file>]`
 * - lint: `lint --quiet --format=json [--tyhp-project=<file>]`
 */
fun buildTyhpTaskArgs(action: String, projectFilePath: String? = null): List<String> {
    val args = mutableListOf(action, "--quiet")
    if (action == LINT_ACTION) {
        args.add("--format=json")
    }
    val project = projectFilePath?.trim().orEmpty()
    if (project.isNotEmpty()) {
        args.add("--tyhp-project=$project")
    }
    return args
}
