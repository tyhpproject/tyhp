import {
    InstallMode,
    normalizePinnedVersion,
    normalizeReleaseTag,
    tagsMatch,
} from "../config/settingsCore";

export interface UpdatePolicyInput {
    installMode: InstallMode;
    /** True only when metadata says this extension wrote the binary into globalStorage. */
    installedByExtension: boolean;
    autoUpdate: boolean;
    pinnedVersion: string;
    currentVersion: string;
    latestVersion: string;
}

export type UpdateDecision =
    | { action: "none"; reason: string }
    | { action: "install"; version: string; reason: string };

/**
 * Startup / background auto-update. Global and PATH binaries are never overwritten here.
 * Pin wins over auto-update: a non-empty pin installs/keeps that version and will not
 * float to some other latest tag.
 */
export function decideStartupUpdate(input: UpdatePolicyInput): UpdateDecision {
    if (input.installMode !== "extension" || !input.installedByExtension) {
        return {
            action: "none",
            reason: "Auto-update applies only to a CLI installed by this extension in extension-only mode",
        };
    }

    const pin = normalizePinnedVersion(input.pinnedVersion);
    if (pin !== "") {
        const pinTag = normalizeReleaseTag(pin);
        if (tagsMatch(input.currentVersion, pinTag)) {
            return { action: "none", reason: `Pinned version ${pinTag} is already installed` };
        }
        return {
            action: "install",
            version: pinTag,
            reason: `Keeping pinned version ${pinTag}`,
        };
    }

    if (!input.autoUpdate) {
        return { action: "none", reason: "tyhp.binary.autoUpdate is disabled" };
    }

    const latest = normalizeReleaseTag(input.latestVersion);
    if (latest === "") {
        return { action: "none", reason: "No latest release tag is available" };
    }
    if (tagsMatch(input.currentVersion, latest)) {
        return { action: "none", reason: `Already on latest ${latest}` };
    }
    return {
        action: "install",
        version: latest,
        reason: `Newer release ${latest} is available`,
    };
}

/** Explicit “Install / Update CLI”: always allowed; pin selects the tag when set. */
export function decideExplicitInstall(pinnedVersion: string, latestVersion: string): UpdateDecision {
    const pin = normalizePinnedVersion(pinnedVersion);
    if (pin !== "") {
        return {
            action: "install",
            version: normalizeReleaseTag(pin),
            reason: `Installing pinned version ${normalizeReleaseTag(pin)}`,
        };
    }
    const latest = normalizeReleaseTag(latestVersion);
    if (latest === "") {
        return { action: "none", reason: "No GitHub release tag is available to install" };
    }
    return {
        action: "install",
        version: latest,
        reason: `Installing latest release ${latest}`,
    };
}

export function shouldAutoUpdate(input: UpdatePolicyInput): boolean {
    return decideStartupUpdate(input).action === "install";
}
