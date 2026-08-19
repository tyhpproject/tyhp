import * as fs from "fs";
import * as http from "http";
import * as https from "https";
import { IncomingMessage } from "http";
import { URL } from "url";
import { pipeline } from "stream/promises";

export const USER_AGENT = "tyhp-lang-vscode";

export class HttpError extends Error {
    constructor(message: string, readonly statusCode?: number) {
        super(message);
        this.name = "HttpError";
    }
}

export function githubApiHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
        "User-Agent": USER_AGENT,
        Accept: "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    };
    const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN;
    if (token && token.trim() !== "") {
        headers.Authorization = `Bearer ${token.trim()}`;
    }
    return headers;
}

export function downloadHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
        "User-Agent": USER_AGENT,
        Accept: "application/octet-stream",
    };
    const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN;
    if (token && token.trim() !== "") {
        headers.Authorization = `Bearer ${token.trim()}`;
    }
    return headers;
}

function requestOnce(urlString: string, headers: Record<string, string>, timeoutMs: number): Promise<IncomingMessage> {
    return new Promise((resolve, reject) => {
        let parsed: URL;
        try {
            parsed = new URL(urlString);
        } catch {
            reject(new HttpError(`Invalid URL: ${urlString}`));
            return;
        }
        const lib = parsed.protocol === "http:" ? http : https;
        const req = lib.request(
            parsed,
            {
                method: "GET",
                headers,
                timeout: timeoutMs,
            },
            (res) => resolve(res)
        );
        req.on("timeout", () => {
            req.destroy();
            reject(new HttpError(`Request timed out after ${timeoutMs}ms: ${urlString}`));
        });
        req.on("error", (err) => reject(new HttpError(`Network error: ${err.message}`)));
        req.end();
    });
}

async function followGet(
    urlString: string,
    headers: Record<string, string>,
    timeoutMs: number,
    maxRedirects: number
): Promise<IncomingMessage> {
    let current = urlString;
    for (let i = 0; i <= maxRedirects; i++) {
        const res = await requestOnce(current, headers, timeoutMs);
        const status = res.statusCode ?? 0;
        if (status >= 300 && status < 400 && res.headers.location) {
            res.resume();
            current = new URL(res.headers.location, current).toString();
            continue;
        }
        if (status < 200 || status >= 300) {
            const body = await readText(res);
            const hint = body.trim().slice(0, 240);
            throw new HttpError(
                `HTTP ${status} fetching ${current}${hint ? `: ${hint}` : ""}`,
                status
            );
        }
        return res;
    }
    throw new HttpError(`Too many redirects fetching ${urlString}`);
}

async function readText(res: IncomingMessage): Promise<string> {
    const chunks: Buffer[] = [];
    for await (const chunk of res) {
        chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
    }
    return Buffer.concat(chunks).toString("utf8");
}

export async function httpGetText(
    urlString: string,
    headers: Record<string, string> = githubApiHeaders(),
    timeoutMs: number = 30_000
): Promise<string> {
    const res = await followGet(urlString, headers, timeoutMs, 5);
    return readText(res);
}

export async function httpDownloadFile(
    urlString: string,
    destPath: string,
    headers: Record<string, string> = downloadHeaders(),
    timeoutMs: number = 180_000
): Promise<void> {
    const res = await followGet(urlString, headers, timeoutMs, 5);
    const out = fs.createWriteStream(destPath);
    try {
        await pipeline(res, out);
    } catch (err) {
        out.destroy();
        try {
            fs.unlinkSync(destPath);
        } catch {
            // ignore
        }
        throw err;
    }
}

export async function httpGetJson<T>(urlString: string): Promise<T> {
    const text = await httpGetText(urlString, githubApiHeaders());
    try {
        return JSON.parse(text) as T;
    } catch {
        throw new HttpError(`GitHub API returned non-JSON (${text.slice(0, 180)})`);
    }
}
