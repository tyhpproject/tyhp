package com.tyhp.lang.lsp

/**
 * Builds argv for `tyhp language_server`. Flags match the CLI
 * (`Action.language_server`, `DisplayHelp.LanguageServerHelp`, global
 * `--quiet` / `--tyhp-project`) — do not invent extra switches here.
 */

const val LANGUAGE_SERVER_ACTION = "language_server"

data class LanguageServerArgOptions(
    /** Absolute (or CLI-accepted) path to `tyhp.json`. Omitted when unknown. */
    val projectFilePath: String? = null,
    /** Extra tokens from `tyhp.languageServer.args` (after the subcommand). */
    val extraArgs: List<String> = emptyList(),
    /** Pass `--quiet` (global). Default true so the banner cannot pollute stdio. */
    val quiet: Boolean = true,
    /** Pass `--stdio` (documented default / only implemented transport). Default true. */
    val stdio: Boolean = true,
)

private fun hasFlag(args: List<String>, flag: String): Boolean =
    args.any { it == flag || it.startsWith("$flag=") }

/**
 * Returns argv for the language-server process, not including the executable.
 *
 * Exact shape (defaults):
 * `language_server --quiet --stdio [--tyhp-project=<path>] [extra…]`
 */
fun buildLanguageServerArgs(options: LanguageServerArgOptions = LanguageServerArgOptions()): List<String> {
    val extra = options.extraArgs.toMutableList()
    if (extra.firstOrNull() == LANGUAGE_SERVER_ACTION) {
        extra.removeAt(0)
    }

    val args = mutableListOf(LANGUAGE_SERVER_ACTION)

    if (options.quiet && !hasFlag(extra, "--quiet") && !hasFlag(extra, "-q")) {
        args.add("--quiet")
    }

    if (options.stdio && !hasFlag(extra, "--stdio")) {
        args.add("--stdio")
    }

    val project = options.projectFilePath?.trim().orEmpty()
    if (project.isNotEmpty() && !hasFlag(extra, "--tyhp-project")) {
        args.add("--tyhp-project=$project")
    }

    args.addAll(extra)
    return args
}
