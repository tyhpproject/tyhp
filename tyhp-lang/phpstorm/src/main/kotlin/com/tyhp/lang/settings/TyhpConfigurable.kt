package com.tyhp.lang.settings

import com.intellij.openapi.options.BoundSearchableConfigurable
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogPanel
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindItem
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.columns
import com.intellij.ui.dsl.builder.panel
import com.tyhp.lang.binary.BinaryManager
import com.tyhp.lang.debug.parseExplicitPortText

/**
 * Settings panel mirroring VS Code `tyhp.*` keys. Applying the panel
 * publishes [TyhpSettingsListener] so the LSP client can restart.
 */
class TyhpConfigurable(private val project: Project) : BoundSearchableConfigurable("Tyhp", "com.tyhp.lang.settings") {
    private val model = TyhpSettingsFormModel()

    override fun createPanel(): DialogPanel {
        model.load(project)
        return panel {
            group("CLI binary") {
                row("Path (tyhp.path):") {
                    textField()
                        .bindText(model::path)
                        .align(AlignX.FILL)
                        .comment("Absolute path, ~/…, or a command on PATH. Empty probes PATH on startup / Refresh.")
                }
                row {
                    checkBox("Override path for this project")
                        .bindSelected(model::projectPathOverride)
                        .comment("When off, the path is stored as a user-level default (same policy as VS Code User settings).")
                }
                row("Install mode (tyhp.binary.installMode):") {
                    comboBox(InstallMode.entries.toList())
                        .bindItem(model::installMode)
                        .comment("Set automatically by PATH discovery and Install / Update CLI. Auto-update applies only to Plugin only (extension).")
                }
                row {
                    checkBox("Auto-update plugin-only CLI (tyhp.binary.autoUpdate)")
                        .bindSelected(model::autoUpdate)
                }
                row("Pinned version (tyhp.binary.pinnedVersion):") {
                    textField()
                        .bindText(model::pinnedVersion)
                        .columns(24)
                        .comment("GitHub tag, with or without a leading v. Empty tracks latest when auto-update is on.")
                }
            }
            group("Project") {
                row("Project path (tyhp.projectPath):") {
                    textField()
                        .bindText(model::projectPath)
                        .align(AlignX.FILL)
                        .comment("Force a single tyhp.json (file or directory). When set, other tyhp.json files are not scanned. Empty indexes every tyhp.json and routes files by include/exclude (same as tyhp build).")
                }
            }
            group("Language server") {
                row("Extra args (tyhp.languageServer.args):") {
                    textField()
                        .bindText(model::languageServerArgs)
                        .align(AlignX.FILL)
                        .comment("Space-separated tokens after tyhp language_server. The client always adds --quiet / --stdio and --tyhp-project when a project file is known.")
                }
                row("Trace (tyhp.languageServer.trace):") {
                    comboBox(listOf("off", "messages", "verbose"))
                        .bindItem(model::languageServerTrace)
                }
                row {
                    checkBox("Publish diagnostics (tyhp.diagnostics.enable)")
                        .bindSelected(model::diagnosticsEnable)
                    checkBox("Auto-import (tyhp.completion.autoImport)")
                        .bindSelected(model::completionAutoImport)
                }
            }
            group("XDebug proxy") {
                row("IDE port (tyhp.xdebugProxy.idePort):") {
                    textField()
                        .bindText(model::xdebugProxyIdePort)
                        .columns(8)
                        .comment("Empty uses tyhp.json xdebugProxy.idePort, then CLI default 9003. Do not leave a placeholder number or it will shadow tyhp.json.")
                }
                row("XDebug port (tyhp.xdebugProxy.xdebugPort):") {
                    textField()
                        .bindText(model::xdebugProxyXdebugPort)
                        .columns(8)
                        .comment("Empty uses tyhp.json xdebugProxy.xdebugPort, then CLI default 9004 (`xdebug.client_port`).")
                }
                row("Sourcemap dir (tyhp.xdebugProxy.sourceMapDir):") {
                    textField()
                        .bindText(model::xdebugProxySourceMapDir)
                        .align(AlignX.FILL)
                        .comment("Optional --sourcemap-dir. Empty uses tyhp.json xdebugProxy.sourceMapDir, otherwise the CLI uses output.path.")
                }
            }
        }
    }

    override fun reset() {
        model.load(project)
        super.reset()
    }

    override fun apply() {
        super.apply()
        model.save(project)
        BinaryManager.getInstance().refresh(project)
        if (!project.isDisposed) {
            project.messageBus.syncPublisher(TyhpSettingsListener.TOPIC).settingsChanged(project)
        }
    }
}

private class TyhpSettingsFormModel {
    var path: String = ""
    var projectPathOverride: Boolean = false
    var installMode: InstallMode? = InstallMode.PATH
    var autoUpdate: Boolean = true
    var pinnedVersion: String = ""
    var projectPath: String = ""
    var languageServerArgs: String = ""
    var languageServerTrace: String? = "off"
    var diagnosticsEnable: Boolean = true
    var completionAutoImport: Boolean = true
    var xdebugProxyIdePort: String = ""
    var xdebugProxyXdebugPort: String = ""
    var xdebugProxySourceMapDir: String = ""

    fun load(project: Project) {
        val inspect = TyhpSettings.inspectPath(project)
        projectPathOverride = inspect.projectValue != null
        path = TyhpSettings.getTyhpPath(project)
        installMode = TyhpSettings.getInstallMode()
        autoUpdate = TyhpSettings.getAutoUpdate()
        pinnedVersion = TyhpSettings.getPinnedVersion()
        projectPath = TyhpSettings.getProjectPath()
        languageServerArgs = TyhpApplicationSettings.getInstance().stored.languageServerArgs
        languageServerTrace = TyhpSettings.getLanguageServerTrace()
        diagnosticsEnable = TyhpSettings.getDiagnosticsEnable()
        completionAutoImport = TyhpSettings.getCompletionAutoImport()
        val proxy = TyhpApplicationSettings.getInstance().stored
        xdebugProxyIdePort = if (proxy.xdebugProxyIdePortSet) proxy.xdebugProxyIdePort.toString() else ""
        xdebugProxyXdebugPort = if (proxy.xdebugProxyXdebugPortSet) proxy.xdebugProxyXdebugPort.toString() else ""
        xdebugProxySourceMapDir = proxy.xdebugProxySourceMapDir
    }

    fun save(project: Project) {
        val app = TyhpSettings.applicationState()
        val proj = TyhpSettings.projectState(project)
        if (projectPathOverride) {
            proj.pathOverride = true
            proj.path = path
        } else {
            proj.pathOverride = false
            proj.path = ""
            app.path = path
        }
        app.installMode = (installMode ?: InstallMode.PATH).value
        app.autoUpdate = autoUpdate
        app.pinnedVersion = pinnedVersion.trim()
        app.projectPath = projectPath.trim()
        app.languageServerArgs = languageServerArgs.trim()
        app.languageServerTrace = (languageServerTrace ?: "off").trim().ifEmpty { "off" }
        app.diagnosticsEnable = diagnosticsEnable
        app.completionAutoImport = completionAutoImport
        val idePort = parseExplicitPortText(xdebugProxyIdePort)
        app.xdebugProxyIdePortSet = idePort != null
        if (idePort != null) {
            app.xdebugProxyIdePort = idePort
        }
        val xdebugPort = parseExplicitPortText(xdebugProxyXdebugPort)
        app.xdebugProxyXdebugPortSet = xdebugPort != null
        if (xdebugPort != null) {
            app.xdebugProxyXdebugPort = xdebugPort
        }
        app.xdebugProxySourceMapDir = xdebugProxySourceMapDir.trim()
    }
}
