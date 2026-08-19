package com.tyhp.lang.debug

import com.intellij.execution.RunManager
import com.intellij.execution.configurations.ConfigurationType
import com.intellij.ide.BrowserUtil
import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.options.ShowSettingsUtil
import com.intellij.openapi.project.Project
import com.tyhp.lang.settings.TyhpConfigurable

private const val NOTIFICATION_GROUP = "Tyhp Language"

data class PhpRemoteDebugEnsureResult(
    val created: Boolean,
    val alreadyExisted: Boolean,
    val typeFound: Boolean,
    val debugPortApplied: Boolean,
    val message: String,
)

/**
 * Contributes a **PHP Remote Debug** run configuration that uses PhpStorm’s
 * built-in XDebug as the DBGp client, pointed at the proxy IDE port.
 */
fun ensurePhpRemoteDebugConfiguration(
    project: Project,
    plan: PhpRemoteDebugPlan,
): PhpRemoteDebugEnsureResult {
    val runManager = RunManager.getInstance(project)
    val existing = runManager.allSettings.firstOrNull { settings ->
        settings.name == plan.configurationName ||
            isTyhpPhpRemoteDebugConfig(settings.name, settings.type.id)
    }
    if (existing != null) {
        val portApplied = trySetPhpStormDebugPort(plan.debugPort)
        trySetIdeKey(existing.configuration, plan.ideKey)
        return PhpRemoteDebugEnsureResult(
            created = false,
            alreadyExisted = true,
            typeFound = true,
            debugPortApplied = portApplied,
            message = phpRemoteDebugSummary(plan) +
                if (portApplied) {
                    " Debug port is set to ${plan.debugPort}."
                } else {
                    " Set Settings → PHP → Debug → Xdebug → Debug port to ${plan.debugPort}."
                },
        )
    }

    val type = findPhpRemoteDebugType()
    if (type == null) {
        return PhpRemoteDebugEnsureResult(
            created = false,
            alreadyExisted = false,
            typeFound = false,
            debugPortApplied = false,
            message = phpRemoteDebugMissingGuidance() +
                " Set Settings → PHP → Debug → Xdebug → Debug port to ${plan.debugPort}.",
        )
    }

    val factory = type.configurationFactories.firstOrNull()
        ?: return PhpRemoteDebugEnsureResult(
            created = false,
            alreadyExisted = false,
            typeFound = true,
            debugPortApplied = false,
            message = phpRemoteDebugMissingGuidance(),
        )

    val settings = runManager.createConfiguration(plan.configurationName, factory)
    trySetIdeKey(settings.configuration, plan.ideKey)
    runManager.addConfiguration(settings)
    runManager.selectedConfiguration = settings
    val portApplied = trySetPhpStormDebugPort(plan.debugPort)
    return PhpRemoteDebugEnsureResult(
        created = true,
        alreadyExisted = false,
        typeFound = true,
        debugPortApplied = portApplied,
        message = "Created PHP Remote Debug “${plan.configurationName}”. " +
            phpRemoteDebugSummary(plan) +
            if (portApplied) {
                " Debug port is set to ${plan.debugPort}."
            } else {
                " Set Settings → PHP → Debug → Xdebug → Debug port to ${plan.debugPort}."
            },
    )
}

fun findPhpRemoteDebugType(): ConfigurationType? {
    return ConfigurationType.CONFIGURATION_TYPE_EP.extensionList.firstOrNull { type ->
        type.id.equals(PHP_REMOTE_DEBUG_TYPE_ID, ignoreCase = true) ||
            (
                type.displayName.contains("Remote Debug", ignoreCase = true) &&
                    (
                        type.id.contains("php", ignoreCase = true) ||
                            type.displayName.contains("PHP", ignoreCase = true)
                        )
                )
    }
}

fun trySetPhpStormDebugPort(port: Int): Boolean {
    if (!isValidPort(port) || port == 0) {
        return false
    }
    val classNames = listOf(
        "com.jetbrains.php.debug.xdebug.XDebugConfiguration",
        "com.jetbrains.php.debug.xdebug.XdebugConfiguration",
        "com.jetbrains.php.debug.PhpDebugGeneralSettings",
        "com.jetbrains.php.debug.PhpDebugSettings",
    )
    for (className in classNames) {
        try {
            val clazz = Class.forName(className)
            val instance = invokeNoArg(clazz, "getInstance") ?: clazz.getDeclaredConstructor().newInstance()
            if (setPortOn(instance, port)) {
                return true
            }
        } catch (_: Throwable) {
            // Best-effort against bundled PHP plugin internals; README documents the UI path.
        }
    }
    return false
}

private fun trySetIdeKey(configuration: Any?, ideKey: String?) {
    if (configuration == null || ideKey.isNullOrBlank()) {
        return
    }
    val names = listOf("setFilter", "setIdeKey", "setSessionId", "setDebugSessionId")
    for (name in names) {
        try {
            val method = configuration.javaClass.methods.firstOrNull { method ->
                method.name == name && method.parameterCount == 1 && method.parameterTypes[0] == String::class.java
            } ?: continue
            method.invoke(configuration, ideKey)
            return
        } catch (_: Throwable) {
            // Optional filter; PHP Remote Debug still listens without it.
        }
    }
}

private fun invokeNoArg(clazz: Class<*>, name: String): Any? {
    val method = clazz.methods.firstOrNull { it.name == name && it.parameterCount == 0 } ?: return null
    return method.invoke(null)
}

private fun setPortOn(instance: Any, port: Int): Boolean {
    val names = listOf("setDebugPort", "setPort", "setXdebugPort", "setXdebugDebugPort")
    for (name in names) {
        try {
            val method = instance.javaClass.methods.firstOrNull { method ->
                method.name == name && method.parameterCount == 1 && method.parameterTypes[0] == Int::class.javaPrimitiveType
            } ?: continue
            method.invoke(instance, port)
            return true
        } catch (_: Throwable) {
            // Try the next candidate.
        }
    }
    return false
}

fun notifyProxy(project: Project, title: String, message: String, type: NotificationType) {
    val notification = NotificationGroupManager.getInstance()
        .getNotificationGroup(NOTIFICATION_GROUP)
        .createNotification(title, message, type)
    notification.addAction(
        NotificationAction.createSimple("Open XDebug proxy docs") {
            BrowserUtil.browse(XDEBUG_PROXY_DOCS_URL)
        },
    )
    notification.addAction(
        NotificationAction.createSimple("Open sourcemap docs") {
            BrowserUtil.browse(SOURCEMAP_DOCS_URL)
        },
    )
    if (type == NotificationType.ERROR) {
        notification.addAction(
            NotificationAction.createSimple("Open Settings") {
                if (!project.isDisposed) {
                    ShowSettingsUtil.getInstance().showSettingsDialog(project, TyhpConfigurable::class.java)
                }
            },
        )
    }
    notification.notify(project)
}

fun runOnEdt(action: () -> Unit) {
    val app = ApplicationManager.getApplication()
    if (app == null || app.isDispatchThread) {
        action()
    } else {
        app.invokeAndWait(action)
    }
}
