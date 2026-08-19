import { execFile } from "child_process";
import { promisify } from "util";
import { classifyLanguageServerHelp, type LanguageServerHelpClass } from "./cliCapability";

const execFileAsync = promisify(execFile);

const HELP_TIMEOUT_MS = 8_000;

export type HelpExec = (
    file: string,
    args: readonly string[],
    options: { timeout: number; encoding: BufferEncoding; windowsHide: boolean }
) => Promise<{ stdout: string; stderr: string }>;

/**
 * Runs `tyhp help --subject=language_server` and classifies the output.
 * Timeouts and spawn errors are `unknown` so we still attempt to start.
 */
export async function probeLanguageServerSupport(
    executable: string,
    exec: HelpExec = execFileAsync as HelpExec
): Promise<LanguageServerHelpClass> {
    try {
        const { stdout, stderr } = await exec(executable, ["help", "--subject=language_server"], {
            timeout: HELP_TIMEOUT_MS,
            encoding: "utf8",
            windowsHide: true,
        });
        return classifyLanguageServerHelp(`${stdout ?? ""}\n${stderr ?? ""}`);
    } catch (err) {
        const text = helpTextFromExecError(err);
        if (text !== "") {
            return classifyLanguageServerHelp(text);
        }
        return "unknown";
    }
}

function helpTextFromExecError(err: unknown): string {
    if (err === null || typeof err !== "object") {
        return "";
    }
    const record = err as { stdout?: unknown; stderr?: unknown };
    const stdout = typeof record.stdout === "string" ? record.stdout : "";
    const stderr = typeof record.stderr === "string" ? record.stderr : "";
    return `${stdout}\n${stderr}`;
}
