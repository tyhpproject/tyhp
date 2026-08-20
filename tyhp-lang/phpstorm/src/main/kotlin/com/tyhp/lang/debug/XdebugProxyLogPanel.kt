package com.tyhp.lang.debug

import com.intellij.ui.components.JBScrollPane
import com.tyhp.lang.lsp.invokeOnEdt
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import javax.swing.JComponent
import javax.swing.JTextArea

/**
 * Dedicated log tab for `tyhp xdebug_proxy` start/stop/listen lines.
 */
class XdebugProxyLogPanel {
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
