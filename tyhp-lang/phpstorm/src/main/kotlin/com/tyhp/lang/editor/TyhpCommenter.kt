package com.tyhp.lang.editor

import com.intellij.lang.Commenter

/**
 * Matches `tyhp-lang/vscode/language-configuration.json` comments.
 */
class TyhpCommenter : Commenter {
    override fun getLineCommentPrefix(): String = "//"

    override fun getBlockCommentPrefix(): String = "/*"

    override fun getBlockCommentSuffix(): String = "*/"

    override fun getCommentedBlockCommentPrefix(): String? = null

    override fun getCommentedBlockCommentSuffix(): String? = null
}
