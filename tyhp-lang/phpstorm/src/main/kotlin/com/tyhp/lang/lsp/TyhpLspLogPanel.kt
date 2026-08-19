package com.tyhp.lang.lsp

import com.intellij.ui.components.JBScrollPane
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import javax.swing.JComponent
import javax.swing.JTextArea

/**
 * Dedicated log tab for language-server start/stop/crash lines. Protocol
 * traces still go to idea.log when `#com.intellij.platform.lsp` is enabled.
 */
class TyhpLspLogPanel {
    private val area = JTextArea().apply {
        isEditable = false
        lineWrap = true
        wrapStyleWord = true
    }

    val component: JComponent = JBScrollPane(area)

    fun append(line: String) {
        val stamped = "${clock.format(LocalTime.now())} $line\n"
        invokeOnEdt {
            area.append(stamped)
            area.caretPosition = area.document.length
        }
    }

    companion object {
        private val clock: DateTimeFormatter = DateTimeFormatter.ofPattern("HH:mm:ss")
    }
}
