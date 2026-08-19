package com.tyhp.lang.filetype

import com.intellij.extapi.psi.ASTWrapperPsiElement
import com.intellij.lang.ASTNode
import com.intellij.lang.ParserDefinition
import com.intellij.lang.PsiParser
import com.intellij.lexer.EmptyLexer
import com.intellij.lexer.Lexer
import com.intellij.openapi.project.Project
import com.intellij.psi.FileViewProvider
import com.intellij.psi.PsiElement
import com.intellij.psi.PsiFile
import com.intellij.psi.tree.IFileElementType
import com.intellij.psi.tree.TokenSet
import com.tyhp.lang.TyhpLanguage

/**
 * Minimal PSI so [TyhpFileType] can own `.tyhp` / `.tyhpdef`. Lexical highlighting
 * is TextMate ([com.tyhp.lang.highlight.TyhpSyntaxHighlighterFactory]), not this lexer.
 */
class TyhpParserDefinition : ParserDefinition {
    override fun createLexer(project: Project): Lexer = EmptyLexer()

    override fun createParser(project: Project): PsiParser {
        return PsiParser { root, builder ->
            val marker = builder.mark()
            while (!builder.eof()) {
                builder.advanceLexer()
            }
            marker.done(root)
            builder.treeBuilt
        }
    }

    override fun getFileNodeType(): IFileElementType = FILE

    override fun getCommentTokens(): TokenSet = TokenSet.EMPTY

    override fun getStringLiteralElements(): TokenSet = TokenSet.EMPTY

    override fun createElement(node: ASTNode): PsiElement = ASTWrapperPsiElement(node)

    override fun createFile(viewProvider: FileViewProvider): PsiFile = TyhpFile(viewProvider)

    companion object {
        val FILE: IFileElementType = IFileElementType(TyhpLanguage)
    }
}
