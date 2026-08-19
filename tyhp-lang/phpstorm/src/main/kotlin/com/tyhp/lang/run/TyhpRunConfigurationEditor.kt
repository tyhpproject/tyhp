package com.tyhp.lang.run

import com.intellij.openapi.options.SettingsEditor
import com.intellij.openapi.ui.ComboBox
import com.intellij.ui.dsl.builder.panel
import javax.swing.JComponent

class TyhpRunConfigurationEditor : SettingsEditor<TyhpRunConfiguration>() {
    private val actionCombo = ComboBox(arrayOf(BUILD_ACTION, LINT_ACTION))

    override fun resetEditorFrom(config: TyhpRunConfiguration) {
        actionCombo.selectedItem = config.action
    }

    override fun applyEditorTo(config: TyhpRunConfiguration) {
        config.action = (actionCombo.selectedItem as? String)?.takeIf { isTyhpTaskAction(it) } ?: BUILD_ACTION
    }

    override fun createEditor(): JComponent {
        return panel {
            row("CLI action:") {
                cell(actionCombo)
                    .comment("build --quiet [--tyhp-project=<file>] or lint --quiet --format=json [--tyhp-project=<file>]")
            }
        }
    }
}
