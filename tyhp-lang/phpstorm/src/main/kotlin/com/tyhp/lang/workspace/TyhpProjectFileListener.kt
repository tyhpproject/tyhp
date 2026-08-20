package com.tyhp.lang.workspace

import com.intellij.openapi.project.Project
import com.intellij.util.messages.Topic

fun interface TyhpProjectFileListener {
    fun projectFileChanged(project: Project, previousPath: String?, nextPath: String?)

    companion object {
        @Topic.ProjectLevel
        val TOPIC = Topic.create("Tyhp project file changed", TyhpProjectFileListener::class.java)
    }
}
