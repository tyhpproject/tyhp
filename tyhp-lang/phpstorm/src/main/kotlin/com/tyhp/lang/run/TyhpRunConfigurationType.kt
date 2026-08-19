package com.tyhp.lang.run

import com.intellij.execution.configurations.ConfigurationFactory
import com.intellij.execution.configurations.ConfigurationTypeBase
import com.intellij.execution.configurations.RunConfiguration
import com.intellij.openapi.project.Project
import com.tyhp.lang.icons.TyhpIcons

const val TYHP_RUN_CONFIGURATION_TYPE_ID = "Tyhp"

class TyhpRunConfigurationType : ConfigurationTypeBase(
    TYHP_RUN_CONFIGURATION_TYPE_ID,
    "Tyhp",
    "Run tyhp CLI commands (build, lint)",
    TyhpIcons.TyhpFile,
) {
    init {
        addFactory(TyhpRunConfigurationFactory(this, BUILD_ACTION))
        addFactory(TyhpRunConfigurationFactory(this, LINT_ACTION))
    }
}

class TyhpRunConfigurationFactory(
    type: TyhpRunConfigurationType,
    val action: String,
) : ConfigurationFactory(type) {
    override fun getId(): String = "Tyhp.$action"

    override fun getName(): String = action

    override fun createTemplateConfiguration(project: Project): RunConfiguration {
        return TyhpRunConfiguration(project, this, "tyhp $action").also { it.action = action }
    }
}
