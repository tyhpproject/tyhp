import * as net from "node:net";

/** True when `host:port` accepts a TCP connection within `timeoutMs`. */
export function probeTcpPort(host: string, port: number, timeoutMs: number): Promise<boolean> {
    if (port <= 0) {
        return Promise.resolve(false);
    }
    return new Promise((resolve) => {
        const socket = net.connect({ host, port });
        const finish = (ok: boolean) => {
            socket.removeAllListeners();
            socket.destroy();
            resolve(ok);
        };
        socket.setTimeout(timeoutMs);
        socket.once("connect", () => finish(true));
        socket.once("timeout", () => finish(false));
        socket.once("error", () => finish(false));
    });
}

export function sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
}
