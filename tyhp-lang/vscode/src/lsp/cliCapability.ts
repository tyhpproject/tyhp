/**
 * Detects a pre-Story-19 CLI whose `language_server` action is a stub that
 * prints help and exits. Do not match the generic phrase "not yet implemented"
 * — current CLIs still use that for `--tcp` / `--pipe`.
 */

export const LANGUAGE_SERVER_UNIMPLEMENTED_MARKERS = [
    "The language server action is not yet implemented (Story 19)",
    "The language server is not yet implemented (Story 19)",
] as const;

export type LanguageServerHelpClass = "unimplemented" | "available" | "unknown";

export function languageServerHelpLooksUnimplemented(helpText: string): boolean {
    return LANGUAGE_SERVER_UNIMPLEMENTED_MARKERS.some((marker) => helpText.includes(marker));
}

export function classifyLanguageServerHelp(helpText: string): LanguageServerHelpClass {
    const text = helpText.trim();
    if (text === "") {
        return "unknown";
    }
    if (languageServerHelpLooksUnimplemented(text)) {
        return "unimplemented";
    }
    return "available";
}
