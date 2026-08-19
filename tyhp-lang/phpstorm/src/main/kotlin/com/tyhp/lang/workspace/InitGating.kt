package com.tyhp.lang.workspace

const val INIT_ACTION = "init"
const val INIT_DONT_ASK_AGAIN_KEY = "tyhp.init.dontAskAgain"

data class InitPromptContext(
    val isTyhpFile: Boolean,
    val hasOwner: Boolean,
    val hasAncestorTyhpJson: Boolean,
    val hasForcedProject: Boolean,
    val hasContentRoot: Boolean,
    val dontAskAgain: Boolean,
    val promptedThisSession: Boolean,
)

/**
 * True when a Tyhp editor is open, the file is not owned by any project,
 * there is no ancestor `tyhp.json` up to the content root, and the user has
 * not already dismissed the prompt. `tyhp.projectPath` suppresses the prompt.
 */
fun shouldPromptInit(context: InitPromptContext): Boolean {
    return context.isTyhpFile &&
        !context.hasOwner &&
        !context.hasAncestorTyhpJson &&
        !context.hasForcedProject &&
        context.hasContentRoot &&
        !context.dontAskAgain &&
        !context.promptedThisSession
}

fun buildInitArgs(): List<String> = listOf(INIT_ACTION, "--yes")

fun initErrorMessage(stderr: String?, stdout: String?, exitCode: Int): String {
    val err = stderr?.trim().orEmpty()
    if (err.isNotEmpty()) {
        return err
    }
    val out = stdout?.trim().orEmpty()
    if (out.isNotEmpty()) {
        return out
    }
    return "exit code $exitCode"
}
