/**
 * Builds argv for `tyhp language_server`. Flags are taken from the CLI
 * (`Action.language_server`, `DisplayHelp.LanguageServerHelp`, global
 * `--quiet` / `--tyhp-project`) — do not invent extra switches here.
 */

export const LANGUAGE_SERVER_ACTION = "language_server";

export interface LanguageServerArgOptions {
    /** Absolute (or CLI-accepted) path to `tyhp.json`. Omitted when unknown. */
    projectFilePath?: string;
    /** Extra tokens from `tyhp.languageServer.args` (after the subcommand). */
    extraArgs?: readonly string[];
    /** Pass `--quiet` (global). Default true so the banner cannot pollute stdio. */
    quiet?: boolean;
    /** Pass `--stdio` (documented default / only implemented transport). Default true. */
    stdio?: boolean;
}

function hasFlag(args: readonly string[], flag: string): boolean {
    return args.some((arg) => arg === flag || arg.startsWith(`${flag}=`));
}

/**
 * Returns argv for the language-server process, not including the executable.
 *
 * Exact shape (defaults):
 * `language_server --quiet --stdio [--tyhp-project=<path>] [extra…]`
 */
export function buildLanguageServerArgs(options: LanguageServerArgOptions = {}): string[] {
    const extra = [...(options.extraArgs ?? [])];
    if (extra[0] === LANGUAGE_SERVER_ACTION) {
        extra.shift();
    }

    const args: string[] = [LANGUAGE_SERVER_ACTION];

    if ((options.quiet ?? true) && !hasFlag(extra, "--quiet") && !hasFlag(extra, "-q")) {
        args.push("--quiet");
    }

    if ((options.stdio ?? true) && !hasFlag(extra, "--stdio")) {
        args.push("--stdio");
    }

    const project = options.projectFilePath?.trim() ?? "";
    if (project !== "" && !hasFlag(extra, "--tyhp-project")) {
        args.push(`--tyhp-project=${project}`);
    }

    args.push(...extra);
    return args;
}
