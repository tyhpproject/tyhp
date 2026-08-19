package com.tyhp.lang.workspace

import com.intellij.openapi.Disposable
import com.intellij.openapi.components.Service
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.FileEditorManagerEvent
import com.intellij.openapi.fileEditor.FileEditorManagerListener
import com.intellij.openapi.project.Project
import com.intellij.openapi.roots.ProjectRootManager
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.vfs.VfsUtilCore
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.vfs.VirtualFileManager
import com.intellij.openapi.vfs.newvfs.BulkFileListener
import com.intellij.openapi.vfs.newvfs.events.VFileEvent
import com.tyhp.lang.lsp.TYHP_PROJECT_FILE
import com.tyhp.lang.lsp.contentRootPaths
import com.tyhp.lang.lsp.isForcedProjectPath
import com.tyhp.lang.lsp.isTyhpLanguageFile
import com.tyhp.lang.lsp.resolveTyhpProjectFileFor
import com.tyhp.lang.settings.TyhpSettings
import com.tyhp.lang.settings.TyhpSettingsListener
import java.io.File
import java.util.concurrent.CopyOnWriteArrayList

fun interface WorkspaceSnapshotListener {
    fun onSnapshotChanged(snapshot: WorkspaceSnapshot)
}

/**
 * Indexes every `tyhp.json` under content roots (or the single forced
 * `tyhp.projectPath`), matches files by include/exclude, and exposes the
 * active editor’s owner to run configs / status bar / init / LSP.
 */
@Service(Service.Level.PROJECT)
class WorkspaceService(private val project: Project) : Disposable {
    @Volatile
    var snapshot: WorkspaceSnapshot = ProjectIndexSnapshot()
        private set

    @Volatile
    var index: ProjectIndex = EMPTY_PROJECT_INDEX
        private set

    private val snapshotListeners = CopyOnWriteArrayList<WorkspaceSnapshotListener>()
    private var indexFingerprint: String = ""

    init {
        project.messageBus.connect(this).subscribe(
            VirtualFileManager.VFS_CHANGES,
            object : BulkFileListener {
                override fun after(events: List<VFileEvent>) {
                    if (events.any { isProjectFileEvent(it) }) {
                        refreshIndex(notifyIndexListeners = true)
                    }
                }
            },
        )
        project.messageBus.connect(this).subscribe(
            TyhpSettingsListener.TOPIC,
            TyhpSettingsListener { refreshIndex(notifyIndexListeners = true) },
        )
        project.messageBus.connect(this).subscribe(
            FileEditorManagerListener.FILE_EDITOR_MANAGER,
            object : FileEditorManagerListener {
                override fun selectionChanged(event: FileEditorManagerEvent) {
                    updateSnapshotFromActiveEditor()
                }

                override fun fileOpened(source: FileEditorManager, file: VirtualFile) {
                    updateSnapshotFromActiveEditor()
                }

                override fun fileClosed(source: FileEditorManager, file: VirtualFile) {
                    updateSnapshotFromActiveEditor()
                }
            },
        )
        refreshIndex(notifyIndexListeners = false)
    }

    fun addSnapshotListener(parentDisposable: Disposable, listener: WorkspaceSnapshotListener) {
        snapshotListeners.add(listener)
        Disposer.register(parentDisposable) { snapshotListeners.remove(listener) }
    }

    fun ownerOfPath(filePath: String): IndexedProject? = index.ownerOf(filePath)

    fun ownerOf(file: VirtualFile?): IndexedProject? {
        if (file == null || !file.isInLocalFileSystem) {
            return null
        }
        return index.ownerOf(file.path)
    }

    fun fileHasAncestorTyhpJson(filePath: String): Boolean {
        val root = matchingWorkspaceRoot(filePath, contentRootPaths(project), isWindows())
        return hasAncestorTyhpJson(filePath, root, { File(it).isFile }) { dir, name -> File(dir, name).path }
    }

    fun isForcedProject(): Boolean = isForcedProjectPath(TyhpSettings.getProjectPath())

    fun refresh(): WorkspaceSnapshot {
        updateSnapshotFromActiveEditor()
        return snapshot
    }

    fun refreshIndex(notifyIndexListeners: Boolean = true): ProjectIndex {
        val caseInsensitive = isWindows()
        val projects = ArrayList<IndexedProject>()
        val forced = resolveTyhpProjectFileFor(project)
        if (isForcedProject()) {
            if (!forced.isNullOrBlank()) {
                projects.add(readIndexedProject(forced))
            }
        } else {
            for (path in collectTyhpJsonFiles(project)) {
                if (shouldSkipIndexedTyhpJson(path)) {
                    continue
                }
                projects.add(readIndexedProject(path))
            }
        }
        index = ProjectIndex(projects, caseInsensitive)
        val fingerprint = projects.joinToString("\u0001") { p ->
            listOf(p.projectFilePath, p.include.joinToString(","), p.exclude.joinToString(",")).joinToString("\u0000")
        }
        val indexChanged = fingerprint != indexFingerprint
        indexFingerprint = fingerprint
        updateSnapshotFromActiveEditor()
        if (notifyIndexListeners && indexChanged && !project.isDisposed) {
            project.messageBus.syncPublisher(TyhpProjectFileListener.TOPIC)
                .projectFileChanged(project, null, snapshot.projectFilePath)
        }
        return index
    }

    fun reloadAfterProjectChange(): WorkspaceSnapshot {
        refreshIndex(notifyIndexListeners = true)
        return snapshot
    }

    fun contentRootFor(file: VirtualFile?): String? =
        contentRootForPath(file?.path, contentRootPaths(project))

    override fun dispose() {
        snapshotListeners.clear()
    }

    private fun updateSnapshotFromActiveEditor() {
        if (project.isDisposed) {
            return
        }
        val file = FileEditorManager.getInstance(project).selectedFiles.firstOrNull()
        val owner = if (file != null && isTyhpLanguageFile(file)) ownerOf(file) else null
        val next = snapshotFromOwner(owner)
        val changed = next.projectFilePath != snapshot.projectFilePath || next.projectName != snapshot.projectName
        snapshot = next
        if (changed) {
            for (listener in snapshotListeners) {
                listener.onSnapshotChanged(next)
            }
        }
    }

    companion object {
        fun getInstance(project: Project): WorkspaceService = project.getService(WorkspaceService::class.java)
    }
}

private fun isWindows(): Boolean =
    System.getProperty("os.name").orEmpty().lowercase().contains("win")

private fun readIndexedProject(projectFilePath: String): IndexedProject {
    return try {
        indexedProjectFromJson(projectFilePath, File(projectFilePath).readText())
    } catch (_: Throwable) {
        indexedProjectFromJson(projectFilePath, "{")
    }
}

private fun collectTyhpJsonFiles(project: Project): List<String> {
    val out = ArrayList<String>()
    val roots = ProjectRootManager.getInstance(project).contentRoots.toMutableList()
    val base = project.basePath
    if (!base.isNullOrBlank()) {
        val baseFile = File(base)
        if (roots.none { toPosix(it.path) == toPosix(base) }) {
            val vf = VirtualFileManager.getInstance().findFileByNioPath(baseFile.toPath())
            if (vf != null) {
                roots.add(vf)
            }
        }
    }
    for (root in roots) {
        VfsUtilCore.iterateChildrenRecursively(
            root,
            { file ->
                !(file.isDirectory && file.name in INDEX_SKIP_DIR_NAMES)
            },
        ) { file ->
            if (!file.isDirectory && file.name == TYHP_PROJECT_FILE) {
                out.add(file.path)
            }
            true
        }
    }
    return out.distinct()
}

private fun isProjectFileEvent(event: VFileEvent): Boolean {
    val path = event.path
    return path == TYHP_PROJECT_FILE ||
        path.endsWith("/$TYHP_PROJECT_FILE") ||
        path.endsWith("\\$TYHP_PROJECT_FILE")
}
