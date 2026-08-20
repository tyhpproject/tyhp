import * as child_process from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import {
    assertChecksum,
    expectedChecksum,
    parseChecksumFile,
    sha256File,
} from "./checksum";
import {
    CHECKSUMS_ASSET,
    GithubRelease,
    findAsset,
    releaseByTagUrl,
    releasesListUrl,
    requireAsset,
    selectLatestRelease,
} from "./github";
import { downloadHeaders, httpDownloadFile, httpGetJson, httpGetText } from "./http";
import { InstallMetadata, EXTENSION_ID, writeInstallMetadata } from "./metadata";
import {
    AssetVariant,
    HostPlatform,
    chooseAssetVariant,
    detectHostPlatform,
    extensionInstallPath,
    globalInstallPath,
    releaseAssetName,
} from "./platform";

export interface InstallResult {
    executablePath: string;
    version: string;
    assetName: string;
    mode: "global" | "extension";
    variant: AssetVariant;
}

export interface InstallerLogger {
    appendLine(message: string): void;
}

function hasDotNet9Runtime(): boolean {
    try {
        const result = child_process.spawnSync("dotnet", ["--list-runtimes"], {
            encoding: "utf8",
            timeout: 5000,
        });
        if (result.status !== 0 || !result.stdout) {
            return false;
        }
        return /Microsoft\.NETCore\.App 9\./.test(result.stdout);
    } catch {
        return false;
    }
}

export async function fetchLatestRelease(): Promise<GithubRelease> {
    const payload = await httpGetJson<GithubRelease[] | { message?: string }>(releasesListUrl());
    if (!Array.isArray(payload)) {
        const apiMessage = payload && typeof payload === "object" ? payload.message : undefined;
        throw new Error(
            `GitHub API error listing releases for tyhpproject/tyhp${apiMessage ? `: ${apiMessage}` : ""}. ` +
                "Set GITHUB_TOKEN if you are rate-limited, or set `tyhp.path` to a local binary."
        );
    }
    const releases = payload;
    if (releases.length === 0) {
        throw new Error(
            "No GitHub Releases were returned for tyhpproject/tyhp. " +
                "Compiler binaries may not be published yet. Install a local `tyhp` on PATH, or set `tyhp.path`."
        );
    }
    const latest = selectLatestRelease(releases);
    if (!latest || !latest.tag_name) {
        throw new Error(
            "Unable to determine a GitHub release tag (all listed releases are drafts, or the repo has no public release yet)."
        );
    }
    return latest;
}

export async function fetchReleaseByTag(tag: string): Promise<GithubRelease> {
    const release = await httpGetJson<GithubRelease & { message?: string }>(releaseByTagUrl(tag));
    if (!release || !release.tag_name) {
        const apiMessage = release && typeof release === "object" ? release.message : undefined;
        throw new Error(
            `GitHub release tag \`${tag}\` was not found on tyhpproject/tyhp` +
                `${apiMessage ? ` (${apiMessage})` : ""}. Check \`tyhp.binary.pinnedVersion\`.`
        );
    }
    return release;
}

async function downloadAndVerify(
    release: GithubRelease,
    assetName: string,
    destPath: string,
    log: InstallerLogger
): Promise<void> {
    const asset = requireAsset(release, assetName);
    const checksumsAsset = findAsset(release, CHECKSUMS_ASSET);
    if (!checksumsAsset?.browser_download_url) {
        throw new Error(
            `GitHub release ${release.tag_name} has no \`${CHECKSUMS_ASSET}\` asset. ` +
                "Refusing to install without a SHA-256 checksum."
        );
    }

    log.appendLine(`Fetching ${CHECKSUMS_ASSET} from ${release.tag_name}…`);
    const checksumText = await httpGetText(checksumsAsset.browser_download_url, downloadHeaders(), 30_000);
    const checksums = parseChecksumFile(checksumText);
    const expected = expectedChecksum(checksums, assetName);

    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "tyhp-cli-"));
    const tmpFile = path.join(tmpDir, assetName);
    try {
        log.appendLine(`Downloading ${assetName} (${asset.size} bytes)…`);
        await httpDownloadFile(asset.browser_download_url, tmpFile);
        const stat = fs.statSync(tmpFile);
        if (stat.size <= 0) {
            throw new Error(`Downloaded artifact \`${assetName}\` is empty.`);
        }
        log.appendLine("Verifying SHA-256…");
        const actual = await sha256File(tmpFile);
        assertChecksum(actual, expected, assetName);

        fs.mkdirSync(path.dirname(destPath), { recursive: true });
        fs.copyFileSync(tmpFile, destPath);
        if (process.platform !== "win32") {
            fs.chmodSync(destPath, 0o755);
        }
    } finally {
        try {
            fs.rmSync(tmpDir, { recursive: true, force: true });
        } catch {
            // ignore
        }
    }
}

export class Installer {
    constructor(
        private readonly globalStorageFsPath: string,
        private readonly log: InstallerLogger,
        private readonly platform: HostPlatform = detectHostPlatform()
    ) {}

    async install(mode: "global" | "extension", versionTag?: string): Promise<InstallResult> {
        const variant = chooseAssetVariant(mode, hasDotNet9Runtime());
        const assetName = releaseAssetName(this.platform, variant);
        this.log.appendLine(
            `Resolving ${mode} install for ${this.platform.os}-${this.platform.arch} (${variant}, asset ${assetName})…`
        );

        const release = versionTag
            ? await fetchReleaseByTag(versionTag)
            : await fetchLatestRelease();
        const dest =
            mode === "extension"
                ? extensionInstallPath(this.globalStorageFsPath, this.platform)
                : globalInstallPath(this.platform);

        await downloadAndVerify(release, assetName, dest, this.log);

        const metadata: InstallMetadata = {
            installedBy: EXTENSION_ID,
            version: release.tag_name,
            mode: mode === "extension" ? "extension" : "global",
            assetName,
            installedAt: new Date().toISOString(),
        };
        if (mode === "extension") {
            writeInstallMetadata(path.dirname(dest), metadata);
        }

        this.log.appendLine(`Installed tyhp ${release.tag_name} at ${dest}`);
        return {
            executablePath: dest,
            version: release.tag_name,
            assetName,
            mode,
            variant,
        };
    }
}
