package com.tyhp.lang.highlight

import com.intellij.openapi.fileTypes.SyntaxHighlighter
import com.intellij.openapi.fileTypes.SyntaxHighlighterFactory
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import org.jetbrains.plugins.textmate.language.syntax.highlighting.TextMateSyntaxHighlighterFactory

/**
 * Delegates to the bundled TextMate plugin so highlighting uses the shared
 * `source.tyhp` grammar (and `source.tyhp.php` includes) without forking it.
 */
class TyhpSyntaxHighlighterFactory : SyntaxHighlighterFactory() {
    private val textMate = TextMateSyntaxHighlighterFactory()

    override fun getSyntaxHighlighter(project: Project?, virtualFile: VirtualFile?): SyntaxHighlighter {
        return textMate.getSyntaxHighlighter(project, virtualFile)
    }
}
