/**
 * Pure start/stop state machine for the XDebug proxy child process.
 * Unexpected exit from `running` is `error`; a requested stop ends in `stopped`.
 */

export type ProxyRunState = "stopped" | "starting" | "running" | "stopping" | "error";

export type ProxyLifecycleEvent =
    | { type: "startRequested" }
    | { type: "listening" }
    | { type: "startFailed" }
    | { type: "stopRequested" }
    | { type: "exited"; expected: boolean };

export function nextProxyState(state: ProxyRunState, event: ProxyLifecycleEvent): ProxyRunState {
    switch (event.type) {
        case "startRequested":
            if (state === "running" || state === "starting") {
                return state;
            }
            return "starting";
        case "listening":
            return state === "starting" || state === "running" ? "running" : state;
        case "startFailed":
            return state === "starting" ? "error" : state;
        case "stopRequested":
            if (state === "stopped") {
                return "stopped";
            }
            return "stopping";
        case "exited":
            if (state === "error") {
                return "error";
            }
            if (event.expected || state === "stopping" || state === "stopped") {
                return "stopped";
            }
            return "error";
        default:
            return state;
    }
}

export function proxyIsActive(state: ProxyRunState): boolean {
    return state === "starting" || state === "running" || state === "stopping";
}

export function proxyIsListening(state: ProxyRunState): boolean {
    return state === "running";
}
