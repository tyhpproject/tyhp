import { normalizeReleaseTag } from "../config/settingsCore";

export const GITHUB_REPO = "tyhpproject/tyhp";
export const CHECKSUMS_ASSET = "checksums.txt";

export interface GithubReleaseAsset {
    name: string;
    browser_download_url: string;
    size: number;
}

export interface GithubRelease {
    tag_name: string;
    draft: boolean;
    prerelease: boolean;
    published_at: string;
    assets: GithubReleaseAsset[];
}

export function releasesListUrl(repo: string = GITHUB_REPO): string {
    return `https://api.github.com/repos/${repo}/releases?per_page=20`;
}

export function releaseByTagUrl(tag: string, repo: string = GITHUB_REPO): string {
    return `https://api.github.com/repos/${repo}/releases/tags/${encodeURIComponent(normalizeReleaseTag(tag))}`;
}

/** First non-draft release, including prereleases. `/releases/latest` hides prereleases. */
export function selectLatestRelease(releases: GithubRelease[]): GithubRelease | undefined {
    return releases.find((release) => !release.draft);
}

export function findAsset(release: GithubRelease, name: string): GithubReleaseAsset | undefined {
    return (release.assets ?? []).find((asset) => asset.name === name);
}

export function requireAsset(release: GithubRelease, name: string): GithubReleaseAsset {
    const asset = findAsset(release, name);
    if (!asset || !asset.browser_download_url) {
        throw new Error(
            `GitHub release ${release.tag_name} has no asset named \`${name}\`. ` +
                "See the extension README for the expected asset names. If this tag predates compiler binaries, pick another tag or install from PATH."
        );
    }
    return asset;
}
