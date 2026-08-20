export type StatusLspState = "stopped" | "starting" | "running" | "error";
export type StatusBinaryStatus = "ok" | "missing" | "invalid";
export type StatusProxyState = "stopped" | "starting" | "running" | "stopping" | "error";

export interface StatusBarView {
    text: string;
    tooltip: string;
    error: boolean;
    warning: boolean;
}

export interface StatusBarInput {
    projectLabel: string;
    lspState: StatusLspState;
    binaryStatus: StatusBinaryStatus;
    binaryPath?: string;
    binaryMessage?: string;
    proxyState?: StatusProxyState;
    proxyDetail?: string;
}

/**
 * Compact status-bar copy: `Tyhp` + owner project / “not in a Tyhp project” + LSP + binary health
 * + optional XDebug proxy listening state.
 */
export function formatStatusBar(input: StatusBarInput): StatusBarView {
    const project = input.projectLabel.trim() !== "" ? input.projectLabel.trim() : "not in a Tyhp project";
    const lsp = lspLabel(input.lspState);
    const binaryOk = input.binaryStatus === "ok";
    const proxyState = input.proxyState ?? "stopped";
    const proxy = proxyLabel(proxyState, input.proxyDetail);
    const missingProject = isMissingProject(project);

    const tooltipLines = [
        `Project: ${project}`,
        `Language server: ${lsp}`,
        `CLI: ${binaryOk ? input.binaryPath ?? "ok" : input.binaryMessage ?? "missing"}`,
        `XDebug proxy: ${proxy}`,
        "Click for Tyhp actions.",
    ];

    if (!binaryOk) {
        return {
            text: `$(error) Tyhp · ${project} · CLI missing`,
            tooltip: tooltipLines.join("\n"),
            error: true,
            warning: false,
        };
    }

    if (input.lspState === "error") {
        return {
            text: `$(error) Tyhp · ${project} · LSP error`,
            tooltip: tooltipLines.join("\n"),
            error: true,
            warning: false,
        };
    }

    if (input.lspState === "starting") {
        return {
            text: `$(sync~spin) Tyhp · ${project} · starting`,
            tooltip: tooltipLines.join("\n"),
            error: false,
            warning: false,
        };
    }

    if (input.lspState === "stopped") {
        return {
            text: `$(debug-disconnect) Tyhp · ${project} · LSP stopped`,
            tooltip: tooltipLines.join("\n"),
            error: false,
            warning: missingProject,
        };
    }

    if (proxyState === "error") {
        return {
            text: `$(warning) Tyhp · ${project} · proxy error`,
            tooltip: tooltipLines.join("\n"),
            error: false,
            warning: true,
        };
    }

    const proxySuffix = proxyTextSuffix(proxyState);
    const icon = missingProject ? "$(warning)" : "$(check)";
    return {
        text: `${icon} Tyhp · ${project} · ${lsp}${proxySuffix}`,
        tooltip: tooltipLines.join("\n"),
        error: false,
        warning: missingProject,
    };
}

function isMissingProject(label: string): boolean {
    return label === "not in a Tyhp project" || label === "no project";
}

function lspLabel(state: StatusLspState): string {
    switch (state) {
        case "running":
            return "ready";
        case "starting":
            return "starting";
        case "error":
            return "error";
        default:
            return "stopped";
    }
}

function proxyLabel(state: StatusProxyState, detail?: string): string {
    const extra = detail && detail.trim() !== "" ? ` (${detail.trim()})` : "";
    switch (state) {
        case "running":
            return `listening${extra}`;
        case "starting":
            return "starting";
        case "stopping":
            return "stopping";
        case "error":
            return "error";
        default:
            return "stopped";
    }
}

function proxyTextSuffix(state: StatusProxyState): string {
    switch (state) {
        case "running":
            return " · proxy";
        case "starting":
        case "stopping":
            return " · proxy…";
        default:
            return "";
    }
}

/** Status-bar quick-pick actions for the current proxy lifecycle state. */
export function proxyStatusActions(state: StatusProxyState): Array<"start" | "stop" | "restart"> {
    const actions: Array<"start" | "stop" | "restart"> = [];
    if (state !== "running" && state !== "starting") {
        actions.push("start");
    }
    if (state === "running" || state === "starting" || state === "stopping") {
        actions.push("stop", "restart");
    } else if (state === "error") {
        actions.push("restart");
    }
    return actions;
}
