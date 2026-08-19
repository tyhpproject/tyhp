/**
 * Argv for contributed Tyhp tasks. Flags match `DisplayHelp.BuildHelp` /
 * `DisplayHelp.LintHelp` and the global `--tyhp-project` (must be a file).
 */

export const TYHP_TASK_TYPE = "tyhp";
export const BUILD_ACTION = "build";
export const LINT_ACTION = "lint";

export type TyhpTaskAction = "build" | "lint";

export function isTyhpTaskAction(value: unknown): value is TyhpTaskAction {
    return value === BUILD_ACTION || value === LINT_ACTION;
}

/**
 * Returns argv for `tyhp build` / `tyhp lint` (not including the executable).
 *
 * - build: `build --quiet [--tyhp-project=<file>]`
 * - lint: `lint --quiet --format=json [--tyhp-project=<file>]`
 */
export function buildTyhpTaskArgs(
    action: TyhpTaskAction,
    projectFilePath?: string
): string[] {
    const args: string[] = [action, "--quiet"];
    if (action === LINT_ACTION) {
        args.push("--format=json");
    }
    const project = projectFilePath?.trim() ?? "";
    if (project !== "") {
        args.push(`--tyhp-project=${project}`);
    }
    return args;
}
