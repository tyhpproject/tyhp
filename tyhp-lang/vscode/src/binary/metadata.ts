import * as fs from "fs";
import * as path from "path";
import { InstallMode } from "../config/settingsCore";

export const EXTENSION_ID = "tyhp-lang.tyhp";

export interface InstallMetadata {
    installedBy: string;
    version: string;
    mode: InstallMode;
    assetName: string;
    installedAt: string;
}

export function metadataPath(cliDir: string): string {
    return path.join(cliDir, "metadata.json");
}

export function readInstallMetadata(cliDir: string): InstallMetadata | undefined {
    const file = metadataPath(cliDir);
    try {
        if (!fs.existsSync(file)) {
            return undefined;
        }
        const parsed = JSON.parse(fs.readFileSync(file, "utf8")) as InstallMetadata;
        if (!parsed || typeof parsed !== "object") {
            return undefined;
        }
        return parsed;
    } catch {
        return undefined;
    }
}

export function writeInstallMetadata(cliDir: string, metadata: InstallMetadata): void {
    fs.mkdirSync(cliDir, { recursive: true });
    fs.writeFileSync(metadataPath(cliDir), `${JSON.stringify(metadata, null, 2)}\n`, "utf8");
}

export function deleteInstallMetadata(cliDir: string): void {
    const file = metadataPath(cliDir);
    try {
        if (fs.existsSync(file)) {
            fs.unlinkSync(file);
        }
    } catch {
        // ignore
    }
}

export function isExtensionOwnedInstall(metadata: InstallMetadata | undefined): boolean {
    return (
        metadata !== undefined &&
        metadata.installedBy === EXTENSION_ID &&
        metadata.mode === "extension"
    );
}
