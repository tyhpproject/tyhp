package com.tyhp.lang.workspace

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.CapturingProcessHandler
import com.intellij.ide.util.PropertiesComponent
import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.Disposable
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.FileEditorManagerEvent
import com.intellij.openapi.fileEditor.FileEditorManagerListener
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.DumbAwareAction
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.vfs.VirtualFile
import com.tyhp.lang.binary.BinaryManager
import com.tyhp.lang.binary.resolveTyhpBinary
import com.tyhp.lang.lsp.TYHP_PROJECT_FILE
import com.tyhp.lang.lsp.isTyhpLanguageFile
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean

private const val NOTIFICATION_GROUP = "Tyhp Language"
private const val INIT_TIMEOUT_MS = 60_000

class InitAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        InitPromptController.getInstance(project).run(file = null)
    }
}

/**
 * Command + prompt for `tyhp init` when a Tyhp file is opened without a project.
 */
@Service(Service.Level.PROJECT)
class InitPromptController(private val project: Project) : Disposable {
    private val promptedThisSession = AtomicBoolean(false)

    init {
        project.messageBus.connect(this).subscribe(
            FileEditorManagerListener.FILE_EDITOR_MANAGER,
            object : FileEditorManagerListener {
                override fun fileOpened(source: FileEditorManager, file: VirtualFile) {
                    maybePrompt(file)
                }

                override fun selectionChanged(event: FileEditorManagerEvent) {
                    val file = event.newFile ?: return
                    maybePrompt(file)
                }
            },
        )
    }

    override fun dispose() {}

    fun considerOpenFiles() {
        ApplicationManager.getApplication().invokeLater {
            if (project.isDisposed) {
                return@invokeLater
            }
            FileEditorManager.getInstance(project).openFiles.forEach { maybePrompt(it) }
        }
    }

    fun run(file: VirtualFile?) {
        val workspace = WorkspaceService.getInstance(project)
        val cwdFile = workspace.contentRootFor(file)?.let { File(it, TYHP_PROJECT_FILE) }
        if (cwdFile != null && cwdFile.isFile) {
            notifyInfo("Tyhp project already exists at ${cwdFile.path}.")
            return
        }

        val cwd = workspace.contentRootFor(file)
        if (cwd.isNullOrBlank()) {
            notifyError("Open a folder in the project before running Tyhp: Initialize Project.")
            return
        }

        val resolved = resolveTyhpBinary(project)
        if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
            val detail = resolved.message
                ?: "Tyhp CLI was not found. Use “Tyhp: Install / Update CLI” or set `tyhp.path`."
            val notification = NotificationGroupManager.getInstance()
                .getNotificationGroup(NOTIFICATION_GROUP)
                .createNotification("Tyhp", detail, NotificationType.ERROR)
            notification.addAction(
                NotificationAction.createSimple("Install / Update CLI") {
                    BinaryManager.getInstance().installInteractive(project)
                },
            )
            notification.notify(project)
            return
        }

        val exe = resolved.executablePath
        val args = buildInitArgs()
        ProgressManager.getInstance().run(object : Task.Backgroundable(project, "Running tyhp init", false) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                try {
                    val cmd = GeneralCommandLine()
                    cmd.exePath = exe
                    cmd.addParameters(args)
                    cmd.charset = Charsets.UTF_8
                    cmd.setWorkDirectory(File(cwd))
                    val output = CapturingProcessHandler(cmd).runProcess(INIT_TIMEOUT_MS)
                    ApplicationManager.getApplication().invokeLater {
                        if (project.isDisposed) {
                            return@invokeLater
                        }
                        if (output.isTimeout) {
                            notifyError("tyhp init timed out after ${INIT_TIMEOUT_MS}ms.")
                            return@invokeLater
                        }
                        if (output.exitCode != 0) {
                            notifyError("tyhp init failed: ${initErrorMessage(output.stderr, output.stdout, output.exitCode)}")
                            return@invokeLater
                        }
                        LocalFileSystem.getInstance().refreshAndFindFileByIoFile(File(cwd, TYHP_PROJECT_FILE))
                        val snapshot = workspace.reloadAfterProjectChange()
                        if (snapshot.projectFilePath.isNullOrBlank()) {
                            notifyWarning(
                                "tyhp init finished but tyhp.json was not detected in $cwd. Reload the project if it does not appear.",
                            )
                            return@invokeLater
                        }
                        notifyInfo("Created tyhp.json in $cwd.")
                    }
                } catch (err: Throwable) {
                    ApplicationManager.getApplication().invokeLater {
                        notifyError("tyhp init failed: ${err.message ?: err}")
                    }
                }
            }
        })
    }

    private fun maybePrompt(file: VirtualFile) {
        val workspace = WorkspaceService.getInstance(project)
        workspace.refresh()
        val cwd = workspace.contentRootFor(file)
        val owner = workspace.ownerOf(file)
        if (
            !shouldPromptInit(
                InitPromptContext(
                    isTyhpFile = isTyhpLanguageFile(file),
                    hasOwner = owner != null,
                    hasAncestorTyhpJson = workspace.fileHasAncestorTyhpJson(file.path),
                    hasForcedProject = workspace.isForcedProject(),
                    hasContentRoot = !cwd.isNullOrBlank(),
                    dontAskAgain = PropertiesComponent.getInstance(project).getBoolean(INIT_DONT_ASK_AGAIN_KEY, false),
                    promptedThisSession = promptedThisSession.get(),
                ),
            )
        ) {
            return
        }

        promptedThisSession.set(true)
        val notification = NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification(
                "Tyhp",
                "This file is not in a Tyhp project. Initialize a Tyhp project?",
                NotificationType.INFORMATION,
            )
        notification.addAction(
            NotificationAction.createSimple("Initialize Project") {
                run(file)
            },
        )
        notification.addAction(
            NotificationAction.createSimple("Not Now") {
                notification.expire()
            },
        )
        notification.addAction(
            NotificationAction.createSimple("Don't Ask Again") {
                PropertiesComponent.getInstance(project).setValue(INIT_DONT_ASK_AGAIN_KEY, true)
            },
        )
        notification.notify(project)
    }

    private fun notifyInfo(message: String) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp", message, NotificationType.INFORMATION)
            .notify(project)
    }

    private fun notifyWarning(message: String) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp", message, NotificationType.WARNING)
            .notify(project)
    }

    private fun notifyError(message: String) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp", message, NotificationType.ERROR)
            .notify(project)
    }

    companion object {
        fun getInstance(project: Project): InitPromptController = project.getService(InitPromptController::class.java)
    }
}
