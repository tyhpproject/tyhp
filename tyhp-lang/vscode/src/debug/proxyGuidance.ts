/**
 * User-facing guidance for debug misconfiguration. Links to Story 17/18 docs
 * instead of re-implementing sourcemaps.
 */

export const PHP_DEBUG_EXTENSION_ID = "xdebug.php-debug";

export const SOURCEMAP_DOCS_URL =
    "https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_sourcemapGeneration.md";

export const XDEBUG_PROXY_DOCS_URL =
    "https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_xdebugProxy.md";

export const TYHP_PHP_DEBUG_CONFIG_NAME = "Listen for Tyhp (XDebug proxy)";

export function isTyhpPhpDebugConfig(config: { name?: unknown; type?: unknown }): boolean {
    return config.type === "php" && typeof config.name === "string" && /tyhp/i.test(config.name);
}

export function phpDebugMissingGuidance(): string {
    return (
        `The PHP Debug extension (${PHP_DEBUG_EXTENSION_ID}) is the DBGp client for Tyhp debugging. ` +
        `Install it, then listen on the proxy IDE port. See ${XDEBUG_PROXY_DOCS_URL}`
    );
}

export function proxyDownGuidance(idePort: number): string {
    return (
        `The Tyhp XDebug proxy is not listening on IDE port ${idePort}. ` +
        `Run “Tyhp: Start XDebug Proxy” (status bar or Command Palette), then start debugging. ` +
        `Docs: ${XDEBUG_PROXY_DOCS_URL}`
    );
}

export function sourcemapGuidance(options: {
    generateSourcemap: boolean;
    mapCount?: number;
    sourceMapDir?: string;
    outputPath?: string;
}): string | undefined {
    if (!options.generateSourcemap) {
        return (
            "`build.generateSourcemap` is not enabled in `tyhp.json`. Set it to true, run `tyhp build`, " +
            `then start the proxy so breakpoints map to .tyhp sources. Docs: ${SOURCEMAP_DOCS_URL}`
        );
    }
    if (options.mapCount === 0) {
        const where = options.sourceMapDir ?? options.outputPath ?? "the project output directory";
        return (
            `No \`.php.map\` files were found in ${where}. Build the project with sourcemaps enabled ` +
            `(\`tyhp build\`) before debugging .tyhp files. Docs: ${SOURCEMAP_DOCS_URL}`
        );
    }
    return undefined;
}

export function proxyStartFailedGuidance(detail: string): string {
    return `Tyhp XDebug proxy failed to start: ${detail}. Check Output > Tyhp XDebug Proxy. Docs: ${XDEBUG_PROXY_DOCS_URL}`;
}
