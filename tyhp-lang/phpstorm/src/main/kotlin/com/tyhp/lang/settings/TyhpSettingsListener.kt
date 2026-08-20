package com.tyhp.lang.settings

import com.intellij.openapi.project.Project
import com.intellij.util.messages.Topic

/**
 * Fired after the Tyhp settings panel is applied. The LSP client (optional
 * Ultimate/LSP module) subscribes so it can restart without the settings UI
 * importing Platform LSP types.
 */
fun interface TyhpSettingsListener {
    fun settingsChanged(project: Project)

    companion object {
        @Topic.ProjectLevel
        val TOPIC = Topic.create("Tyhp settings changed", TyhpSettingsListener::class.java)
    }
}
