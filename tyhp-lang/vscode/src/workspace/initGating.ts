/**
 * Pure helpers for “Tyhp: Initialize Project”.
 *
 * CLI (`DisplayHelp.InitHelp`): `tyhp init [--yes / -y] [directory]`.
 * `--yes` accepts template defaults without prompting. Do not pass
 * `--tyhp-project` — that flag requires an existing file
 * (`CliStartup.TryValidateProjectFile`).
 *
 * Prompt only when THIS file has no include-owner and no ancestor `tyhp.json`
 * (walk up to the workspace root). An ancestor that does not include the file
 * stays silent (TextMate only). `tyhp.projectPath` suppresses the prompt.
 */

export const INIT_ACTION = "init";
export const INIT_DONT_ASK_AGAIN_KEY = "tyhp.init.dontAskAgain";

export interface InitPromptContext {
    languageId: string;
    hasOwner: boolean;
    hasAncestorTyhpJson: boolean;
    hasForcedProject: boolean;
    hasWorkspaceFolder: boolean;
    dontAskAgain: boolean;
    promptedThisSession: boolean;
}

/**
 * True when a Tyhp editor is open, the file is not owned by any project,
 * there is no ancestor `tyhp.json` up to the workspace root, and the user
 * has not already dismissed the prompt.
 */
export function shouldPromptInit(context: InitPromptContext): boolean {
    return (
        context.languageId === "tyhp" &&
        !context.hasOwner &&
        !context.hasAncestorTyhpJson &&
        !context.hasForcedProject &&
        context.hasWorkspaceFolder &&
        !context.dontAskAgain &&
        !context.promptedThisSession
    );
}

/**
 * Argv for a non-interactive `tyhp init` (not including the executable).
 * Cwd must be the workspace folder that should receive `tyhp.json`.
 */
export function buildInitArgs(): string[] {
    return [INIT_ACTION, "--yes"];
}
