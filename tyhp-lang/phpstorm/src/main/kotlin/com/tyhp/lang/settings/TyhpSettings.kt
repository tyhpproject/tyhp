package com.tyhp.lang.settings

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.util.xmlb.XmlSerializerUtil
import com.tyhp.lang.debug.ExplicitProxySettings
import com.tyhp.lang.debug.isValidPort
import com.tyhp.lang.debug.parseExplicitString

@Service(Service.Level.APP)
@State(name = "TyhpApplicationSettings", storages = [Storage("tyhp.xml")])
class TyhpApplicationSettings : PersistentStateComponent<TyhpApplicationSettings.State> {
    class State {
        var path: String = ""
        var projectPath: String = ""
        var installMode: String = InstallMode.PATH.value
        var autoUpdate: Boolean = true
        var pinnedVersion: String = ""
        var languageServerArgs: String = ""
        var languageServerTrace: String = "off"
        var diagnosticsEnable: Boolean = true
        var completionAutoImport: Boolean = true
        /** When false, [xdebugProxyIdePort] is a placeholder and must not shadow `tyhp.json`. */
        var xdebugProxyIdePortSet: Boolean = false
        var xdebugProxyIdePort: Int = 9003
        /** When false, [xdebugProxyXdebugPort] is a placeholder and must not shadow `tyhp.json`. */
        var xdebugProxyXdebugPortSet: Boolean = false
        var xdebugProxyXdebugPort: Int = 9004
        var xdebugProxySourceMapDir: String = ""
        var lastUpdateCheckEpochMs: Long = 0
    }

    val stored: State = State()

    override fun getState(): State = stored

    override fun loadState(state: State) {
        XmlSerializerUtil.copyBean(state, stored)
    }

    companion object {
        fun getInstance(): TyhpApplicationSettings =
            ApplicationManager.getApplication().getService(TyhpApplicationSettings::class.java)
    }
}

@Service(Service.Level.PROJECT)
@State(name = "TyhpProjectSettings", storages = [Storage("tyhp.xml")])
class TyhpProjectSettings : PersistentStateComponent<TyhpProjectSettings.State> {
    class State {
        /** When true, [path] is a project override even if it is the empty string. */
        var pathOverride: Boolean = false
        var path: String = ""
    }

    val stored: State = State()

    override fun getState(): State = stored

    override fun loadState(state: State) {
        XmlSerializerUtil.copyBean(state, stored)
    }

    companion object {
        fun getInstance(project: Project): TyhpProjectSettings = project.service()
    }
}

/**
 * Facade over application + project persistent state. Key names match VS Code `tyhp.*`.
 */
object TyhpSettings {
    fun inspectPath(project: Project?): InspectedPath {
        val application = TyhpApplicationSettings.getInstance().stored.path
        if (project == null || project.isDisposed) {
            return InspectedPath(applicationValue = application)
        }
        val projectState = TyhpProjectSettings.getInstance(project).stored
        val projectValue = if (projectState.pathOverride) projectState.path else null
        return InspectedPath(applicationValue = application, projectValue = projectValue)
    }

    fun getTyhpPath(project: Project?): String = effectiveTyhpPath(inspectPath(project))

    fun tyhpPathIsUnset(project: Project?): Boolean = isPathUnset(getTyhpPath(project))

    fun getPathWriteTarget(project: Project?): PathWriteTarget = pathWriteTarget(inspectPath(project))

    fun setTyhpPath(project: Project?, absolutePath: String) {
        when (getPathWriteTarget(project)) {
            PathWriteTarget.Project -> {
                if (project != null && !project.isDisposed) {
                    val state = TyhpProjectSettings.getInstance(project).stored
                    state.pathOverride = true
                    state.path = absolutePath
                }
            }
            PathWriteTarget.Application -> {
                TyhpApplicationSettings.getInstance().stored.path = absolutePath
            }
        }
    }

    fun getInstallMode(): InstallMode =
        parseInstallMode(TyhpApplicationSettings.getInstance().stored.installMode)

    fun setInstallMode(mode: InstallMode) {
        TyhpApplicationSettings.getInstance().stored.installMode = mode.value
    }

    fun getAutoUpdate(): Boolean = TyhpApplicationSettings.getInstance().stored.autoUpdate

    fun getPinnedVersion(): String =
        normalizePinnedVersion(TyhpApplicationSettings.getInstance().stored.pinnedVersion)

    fun getProjectPath(): String = TyhpApplicationSettings.getInstance().stored.projectPath.trim()

    fun getLanguageServerArgs(): List<String> {
        val raw = TyhpApplicationSettings.getInstance().stored.languageServerArgs.trim()
        if (raw.isEmpty()) {
            return emptyList()
        }
        return raw.split(Regex("\\s+")).filter { it.isNotEmpty() }
    }

    fun getLanguageServerTrace(): String {
        val value = TyhpApplicationSettings.getInstance().stored.languageServerTrace.trim()
        return value.ifEmpty { "off" }
    }

    fun getDiagnosticsEnable(): Boolean = TyhpApplicationSettings.getInstance().stored.diagnosticsEnable

    fun getCompletionAutoImport(): Boolean = TyhpApplicationSettings.getInstance().stored.completionAutoImport

    /**
     * Explicit `tyhp.xdebugProxy.*` values only. Unset ports/dirs fall through to
     * `tyhp.json` then Story 18 defaults — UI placeholders (9003/9004) must not
     * count as stored settings.
     */
    fun getExplicitProxySettings(): ExplicitProxySettings {
        val stored = TyhpApplicationSettings.getInstance().stored
        val idePort = if (stored.xdebugProxyIdePortSet) stored.xdebugProxyIdePort else null
        val xdebugPort = if (stored.xdebugProxyXdebugPortSet) stored.xdebugProxyXdebugPort else null
        return ExplicitProxySettings(
            idePort = idePort?.takeIf { isValidPort(it) },
            xdebugPort = xdebugPort?.takeIf { isValidPort(it) },
            sourceMapDir = parseExplicitString(stored.xdebugProxySourceMapDir),
        )
    }

    fun getLastUpdateCheckEpochMs(): Long = TyhpApplicationSettings.getInstance().stored.lastUpdateCheckEpochMs

    fun setLastUpdateCheckEpochMs(value: Long) {
        TyhpApplicationSettings.getInstance().stored.lastUpdateCheckEpochMs = value
    }

    fun applicationState(): TyhpApplicationSettings.State = TyhpApplicationSettings.getInstance().stored

    fun projectState(project: Project): TyhpProjectSettings.State = TyhpProjectSettings.getInstance(project).stored
}
