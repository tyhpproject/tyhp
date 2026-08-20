package com.tyhp.lang.run

import com.intellij.execution.ExecutionException
import com.intellij.execution.Executor
import com.intellij.execution.configurations.CommandLineState
import com.intellij.execution.configurations.ConfigurationFactory
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.configurations.LocatableConfigurationBase
import com.intellij.execution.configurations.LocatableRunConfigurationOptions
import com.intellij.execution.configurations.RunConfiguration
import com.intellij.execution.configurations.RunProfileState
import com.intellij.execution.configurations.RuntimeConfigurationError
import com.intellij.execution.process.KillableColoredProcessHandler
import com.intellij.execution.process.ProcessHandler
import com.intellij.execution.process.ProcessTerminatedListener
import com.intellij.execution.runners.ExecutionEnvironment
import com.intellij.openapi.options.SettingsEditor
import com.intellij.openapi.project.Project
import com.tyhp.lang.binary.resolveTyhpBinary
import com.tyhp.lang.lsp.contentRootPaths
import com.tyhp.lang.lsp.serverWorkingDirectory
import com.tyhp.lang.workspace.WorkspaceService
import java.io.File

class TyhpRunConfigurationOptions : LocatableRunConfigurationOptions() {
    var cliAction by string(BUILD_ACTION)
}

class TyhpRunConfiguration(
    project: Project,
    factory: ConfigurationFactory,
    name: String,
) : LocatableConfigurationBase<TyhpRunConfigurationOptions>(project, factory, name) {
    override fun getOptionsClass(): Class<TyhpRunConfigurationOptions> = TyhpRunConfigurationOptions::class.java

    private val tyhpOptions: TyhpRunConfigurationOptions
        get() = options as TyhpRunConfigurationOptions

    var action: String
        get() = tyhpOptions.cliAction?.takeIf { isTyhpTaskAction(it) } ?: BUILD_ACTION
        set(value) {
            tyhpOptions.cliAction = if (isTyhpTaskAction(value)) value else BUILD_ACTION
        }

    override fun getConfigurationEditor(): SettingsEditor<out RunConfiguration> = TyhpRunConfigurationEditor()

    @Suppress("OVERRIDE_DEPRECATION")
    override fun excludeCompileBeforeLaunchOption(): Boolean = true

    override fun checkConfiguration() {
        val resolved = resolveTyhpBinary(project)
        if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
            throw RuntimeConfigurationError(
                resolved.message
                    ?: "Tyhp CLI was not found. Use Tools → Tyhp → Install / Update CLI or set `tyhp.path`.",
            )
        }
    }

    override fun getState(executor: Executor, environment: ExecutionEnvironment): RunProfileState {
        return object : CommandLineState(environment) {
            override fun startProcess(): ProcessHandler {
                val cmd = buildCommandLine()
                val handler = KillableColoredProcessHandler(cmd)
                ProcessTerminatedListener.attach(handler, project)
                return handler
            }
        }
    }

    internal fun buildCommandLine(): GeneralCommandLine {
        val resolved = resolveTyhpBinary(project)
        val exe = resolved.executablePath
        if (!resolved.isOk || exe.isNullOrBlank()) {
            throw ExecutionException(
                resolved.message
                    ?: "Tyhp CLI was not found. Use Tools → Tyhp → Install / Update CLI or set `tyhp.path`.",
            )
        }

        val snapshot = WorkspaceService.getInstance(project).snapshot
        val args = buildTyhpTaskArgs(action, snapshot.projectFilePath)
        val cwd = snapshot.projectDir
            ?: serverWorkingDirectory(snapshot.projectFilePath, contentRootPaths(project))
            ?: project.basePath

        val cmd = GeneralCommandLine()
        cmd.exePath = exe
        cmd.addParameters(args)
        cmd.charset = Charsets.UTF_8
        if (!cwd.isNullOrBlank()) {
            cmd.setWorkDirectory(File(cwd))
        }
        return cmd
    }
}
