import { Installer, fetchLatestRelease } from "./Installer";
import { InstallMetadata, isExtensionOwnedInstall } from "./metadata";
import { decideStartupUpdate } from "./policy";
import { InstallMode } from "../config/settingsCore";

export interface UpdateServiceSettings {
    installMode: InstallMode;
    autoUpdate: boolean;
    pinnedVersion: string;
}

export interface AutoUpdatePlan {
    shouldInstall: boolean;
    version?: string;
    reason: string;
}

export function planAutoUpdate(
    settings: UpdateServiceSettings,
    metadata: InstallMetadata | undefined,
    latestTag: string
): AutoUpdatePlan {
    const decision = decideStartupUpdate({
        installMode: settings.installMode,
        installedByExtension: isExtensionOwnedInstall(metadata),
        autoUpdate: settings.autoUpdate,
        pinnedVersion: settings.pinnedVersion,
        currentVersion: metadata?.version ?? "",
        latestVersion: latestTag,
    });
    if (decision.action === "install") {
        return { shouldInstall: true, version: decision.version, reason: decision.reason };
    }
    return { shouldInstall: false, reason: decision.reason };
}

export class UpdateService {
    constructor(private readonly installer: Installer) {}

    async checkAndApply(
        settings: UpdateServiceSettings,
        metadata: InstallMetadata | undefined,
        log: { appendLine(message: string): void }
    ): Promise<{ updated: boolean; reason: string; path?: string }> {
        let latestTag = "";
        try {
            const needsLatest =
                settings.installMode === "extension" &&
                isExtensionOwnedInstall(metadata) &&
                settings.pinnedVersion.trim() === "" &&
                settings.autoUpdate;
            if (needsLatest) {
                latestTag = (await fetchLatestRelease()).tag_name;
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            log.appendLine(`Auto-update check failed: ${message}`);
            return { updated: false, reason: message };
        }

        const plan = planAutoUpdate(settings, metadata, latestTag);
        log.appendLine(plan.reason);
        if (!plan.shouldInstall || !plan.version) {
            return { updated: false, reason: plan.reason };
        }

        const result = await this.installer.install("extension", plan.version);
        return { updated: true, reason: plan.reason, path: result.executablePath };
    }
}
