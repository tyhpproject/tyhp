import * as os from "os";
import * as path from "path";

export type AssetVariant = "self-contained" | "framework-dependent";

export interface HostPlatform {
    os: "osx" | "linux" | "win";
    arch: "x64" | "arm64";
    nodePlatform: NodeJS.Platform;
}

export class UnsupportedPlatformError extends Error {
    constructor(readonly nodePlatform: string, readonly nodeArch: string) {
        super(
            `Tyhp CLI has no GitHub Release asset for ${nodePlatform}/${nodeArch}. ` +
                "Supported assets: tyhp-osx-arm64, tyhp-osx-x64, tyhp-linux-x64, tyhp-linux-arm64, tyhp-win-x64.exe " +
                "(and matching -fxdependent variants)."
        );
        this.name = "UnsupportedPlatformError";
    }
}

export function detectHostPlatform(
    nodePlatform: NodeJS.Platform = process.platform,
    nodeArch: string = process.arch
): HostPlatform {
    let osId: HostPlatform["os"];
    if (nodePlatform === "darwin") {
        osId = "osx";
    } else if (nodePlatform === "linux") {
        osId = "linux";
    } else if (nodePlatform === "win32") {
        osId = "win";
    } else {
        throw new UnsupportedPlatformError(nodePlatform, nodeArch);
    }

    let arch: HostPlatform["arch"];
    if (nodeArch === "x64" || nodeArch === "amd64") {
        arch = "x64";
    } else if (nodeArch === "arm64") {
        arch = "arm64";
    } else {
        throw new UnsupportedPlatformError(nodePlatform, nodeArch);
    }

    if (osId === "win" && arch === "arm64") {
        throw new UnsupportedPlatformError(nodePlatform, nodeArch);
    }

    return { os: osId, arch, nodePlatform };
}

/**
 * GitHub Release asset names from `scripts/release.sh` EXPECTED_ASSETS.
 * Self-contained: `tyhp-{os}-{arch}` (Windows adds `.exe`).
 * Framework-dependent: same with `-fxdependent` before the optional `.exe`.
 */
export function releaseAssetName(platform: HostPlatform, variant: AssetVariant): string {
    const id = `${platform.os}-${platform.arch}`;
    const suffix = variant === "framework-dependent" ? "-fxdependent" : "";
    if (platform.os === "win") {
        return `tyhp-${id}${suffix}.exe`;
    }
    return `tyhp-${id}${suffix}`;
}

export function installedBinaryFileName(platform: HostPlatform = detectHostPlatform()): string {
    return platform.os === "win" ? "tyhp.exe" : "tyhp";
}

export function pathProbeNames(platform: HostPlatform = detectHostPlatform()): string[] {
    if (platform.os === "win") {
        return ["tyhp.exe", "tyhp"];
    }
    return ["tyhp"];
}

/** Matches `scripts/install.sh` (`$HOME/.local/bin`) and `scripts/install.ps1` (`%LOCALAPPDATA%\Programs\tyhp`). */
export function globalInstallDir(
    platform: HostPlatform = detectHostPlatform(),
    homedir: string = os.homedir(),
    localAppData: string | undefined = process.env.LOCALAPPDATA
): string {
    if (platform.os === "win") {
        const root =
            localAppData && localAppData.trim() !== ""
                ? localAppData
                : path.join(homedir, "AppData", "Local");
        return path.join(root, "Programs", "tyhp");
    }
    return path.join(homedir, ".local", "bin");
}

export function globalInstallPath(
    platform: HostPlatform = detectHostPlatform(),
    homedir?: string,
    localAppData?: string
): string {
    return path.join(globalInstallDir(platform, homedir, localAppData), installedBinaryFileName(platform));
}

export function extensionInstallDir(globalStorageFsPath: string): string {
    return path.join(globalStorageFsPath, "cli");
}

export function extensionInstallPath(
    globalStorageFsPath: string,
    platform: HostPlatform = detectHostPlatform()
): string {
    return path.join(extensionInstallDir(globalStorageFsPath), installedBinaryFileName(platform));
}

/**
 * Whether a configured `tyhp.path` value currently resolves to this extension's own
 * managed install location. `tyhp.binary.installMode` can drift from `tyhp.path` when a
 * user hand-edits settings.json directly (bypassing "Tyhp: Install / Update CLI" or a PATH
 * re-probe, either of which resets `installMode`); auto-update must not treat a stale
 * `installMode: "extension"` as license to overwrite a `tyhp.path` the user pointed
 * elsewhere. Empty/unset paths are never considered managed.
 */
export function isManagedInstallPath(
    configuredPath: string,
    globalStorageFsPath: string,
    platform: HostPlatform = detectHostPlatform()
): boolean {
    const trimmed = configuredPath.trim();
    if (trimmed === "") {
        return false;
    }
    return path.resolve(trimmed) === path.resolve(extensionInstallPath(globalStorageFsPath, platform));
}

/**
 * Extension-only installs always use the self-contained asset so the IDE does not
 * depend on a machine-wide .NET 9 runtime. Global installs match `scripts/install.sh`:
 * framework-dependent when .NET 9 is present, otherwise self-contained.
 */
export function chooseAssetVariant(
    mode: "global" | "extension",
    hasDotNet9: boolean
): AssetVariant {
    if (mode === "extension") {
        return "self-contained";
    }
    return hasDotNet9 ? "framework-dependent" : "self-contained";
}
