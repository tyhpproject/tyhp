import * as vscode from "vscode";
import { resolveTyhpBinary } from "../binary/BinaryManager";
import { getWorkspaceService } from "../workspace/WorkspaceService";
import {
    BUILD_ACTION,
    LINT_ACTION,
    TYHP_TASK_TYPE,
    TyhpTaskAction,
    buildTyhpTaskArgs,
    isTyhpTaskAction,
} from "./taskArgs";

export interface TyhpTaskDefinition extends vscode.TaskDefinition {
    type: typeof TYHP_TASK_TYPE;
    action: TyhpTaskAction;
}

/**
 * Provides `tyhp build` and `tyhp lint` tasks using the resolved CLI binary
 * and detected `tyhp.json` (`--tyhp-project=<file>`).
 */
export class TyhpTaskProvider implements vscode.TaskProvider {
    async provideTasks(): Promise<vscode.Task[]> {
        const build = await this.createTask(BUILD_ACTION);
        const lint = await this.createTask(LINT_ACTION);
        return [build, lint].filter((task): task is vscode.Task => task !== undefined);
    }

    async resolveTask(task: vscode.Task): Promise<vscode.Task | undefined> {
        const action = (task.definition as TyhpTaskDefinition).action;
        if (!isTyhpTaskAction(action)) {
            return undefined;
        }
        return this.createTask(action, task.definition as TyhpTaskDefinition, task.scope);
    }

    private async createTask(
        action: TyhpTaskAction,
        definition?: TyhpTaskDefinition,
        scope?: vscode.TaskScope | vscode.WorkspaceFolder
    ): Promise<vscode.Task | undefined> {
        const resolved = await resolveTyhpBinary();
        if (resolved.status !== "ok" || !resolved.executablePath) {
            return undefined;
        }

        const snapshot = getWorkspaceService()?.snapshot;
        const projectFilePath = snapshot?.projectFilePath;
        const cwd =
            snapshot?.projectDir ?? scopeFolderPath(scope) ?? vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        const args = buildTyhpTaskArgs(action, projectFilePath);
        const execution = new vscode.ProcessExecution(resolved.executablePath, args, cwd ? { cwd } : undefined);
        const def: TyhpTaskDefinition = definition ?? { type: TYHP_TASK_TYPE, action };
        const task = new vscode.Task(
            def,
            scope ?? vscode.TaskScope.Workspace,
            action,
            "tyhp",
            execution,
            action === BUILD_ACTION ? "$tyhp" : undefined
        );
        task.detail = `${resolved.executablePath} ${args.join(" ")}`;
        task.presentationOptions = {
            reveal: vscode.TaskRevealKind.Always,
            panel: vscode.TaskPanelKind.Dedicated,
            showReuseMessage: false,
            clear: true,
        };
        if (action === BUILD_ACTION) {
            task.group = vscode.TaskGroup.Build;
        }
        return task;
    }
}

/**
 * `.vscode/tasks.json` entries in a multi-root workspace resolve with `scope`
 * set to the owning folder; prefer that folder's cwd over the first workspace
 * folder when no `tyhp.json` has been detected.
 */
function scopeFolderPath(scope: vscode.TaskScope | vscode.WorkspaceFolder | undefined): string | undefined {
    return scope !== undefined && typeof scope === "object" ? scope.uri.fsPath : undefined;
}

export function registerTyhpTaskProvider(context: vscode.ExtensionContext): vscode.Disposable {
    const provider = vscode.tasks.registerTaskProvider(TYHP_TASK_TYPE, new TyhpTaskProvider());
    context.subscriptions.push(provider);
    return provider;
}
