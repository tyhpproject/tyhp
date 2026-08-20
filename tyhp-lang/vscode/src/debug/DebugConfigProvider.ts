import * as vscode from "vscode";
import {
    PHP_DEBUG_EXTENSION_ID,
    TYHP_PHP_DEBUG_CONFIG_NAME,
    isTyhpPhpDebugConfig,
    phpDebugMissingGuidance,
    proxyDownGuidance,
} from "./proxyGuidance";
import { XdebugProxyManager } from "./XdebugProxyManager";

/**
 * Contributes a PHP Debug launch configuration that listens on the Tyhp
 * XDebug proxy IDE port. Does not implement DBGp — PHP Debug is the client.
 */
export class TyhpDebugConfigProvider implements vscode.DebugConfigurationProvider {
    constructor(private readonly proxy: XdebugProxyManager) {}

    provideDebugConfigurations(
        _folder: vscode.WorkspaceFolder | undefined
    ): vscode.ProviderResult<vscode.DebugConfiguration[]> {
        return [this.buildLaunchConfig()];
    }

    resolveDebugConfiguration(
        _folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration,
        _token?: vscode.CancellationToken
    ): vscode.ProviderResult<vscode.DebugConfiguration> {
        if (!isTyhpPhpDebugConfig(config)) {
            return config;
        }
        return this.prepareTyhpLaunch(config);
    }

    resolveDebugConfigurationWithSubstitutedVariables(
        _folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration,
        _token?: vscode.CancellationToken
    ): vscode.ProviderResult<vscode.DebugConfiguration> {
        if (!isTyhpPhpDebugConfig(config)) {
            return config;
        }
        return this.prepareTyhpLaunch(config);
    }

    private async prepareTyhpLaunch(
        config: vscode.DebugConfiguration
    ): Promise<vscode.DebugConfiguration | undefined> {
        const launch = this.proxy.resolveLaunch();
        if (typeof config.port !== "number") {
            config.port = launch.idePort;
        }
        if (!config.request) {
            config.request = "launch";
        }
        if (!config.type) {
            config.type = "php";
        }

        if (!phpDebugInstalled()) {
            const pick = await vscode.window.showErrorMessage(
                phpDebugMissingGuidance(),
                "Install PHP Debug",
                "Continue Anyway"
            );
            if (pick === "Install PHP Debug") {
                await vscode.commands.executeCommand(
                    "workbench.extensions.installExtension",
                    PHP_DEBUG_EXTENSION_ID
                );
                return undefined;
            }
            if (pick !== "Continue Anyway") {
                return undefined;
            }
        }

        if (!this.proxy.isListening) {
            const pick = await vscode.window.showWarningMessage(
                proxyDownGuidance(typeof config.port === "number" ? config.port : launch.idePort),
                "Start XDebug Proxy",
                "Continue Anyway"
            );
            if (pick === "Start XDebug Proxy") {
                const started = await this.proxy.start();
                if (!started) {
                    return undefined;
                }
            } else if (pick !== "Continue Anyway") {
                return undefined;
            }
        }

        return config;
    }

    private buildLaunchConfig(): vscode.DebugConfiguration {
        const launch = this.proxy.resolveLaunch();
        return {
            name: TYHP_PHP_DEBUG_CONFIG_NAME,
            type: "php",
            request: "launch",
            port: launch.idePort,
        };
    }
}

function phpDebugInstalled(): boolean {
    return Boolean(vscode.extensions.getExtension(PHP_DEBUG_EXTENSION_ID));
}

export function registerDebugConfigProvider(
    context: vscode.ExtensionContext,
    proxy: XdebugProxyManager
): vscode.Disposable {
    const provider = new TyhpDebugConfigProvider(proxy);
    const initial = vscode.debug.registerDebugConfigurationProvider(
        "php",
        provider,
        vscode.DebugConfigurationProviderTriggerKind.Initial
    );
    const dynamic = vscode.debug.registerDebugConfigurationProvider(
        "php",
        provider,
        vscode.DebugConfigurationProviderTriggerKind.Dynamic
    );
    context.subscriptions.push(initial, dynamic);
    return vscode.Disposable.from(initial, dynamic);
}
