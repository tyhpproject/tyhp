package com.tyhp.lang.editor

import com.intellij.codeInsight.highlighting.BraceMatcher
import com.intellij.openapi.editor.highlighter.HighlighterIterator
import com.intellij.openapi.fileTypes.FileType
import com.intellij.psi.PsiFile
import com.intellij.psi.tree.IElementType

/**
 * Character-level pairs matching VS Code `language-configuration.json` brackets.
 * TextMate tokens are scope-based, so matching uses the document character at
 * the token start rather than PSI token types.
 */
class TyhpBraceMatcher : BraceMatcher {
    override fun getBraceTokenGroupId(tokenType: IElementType): Int = 1

    override fun isLBraceToken(iterator: HighlighterIterator, fileText: CharSequence, fileType: FileType): Boolean {
        return leftBraces.contains(charAt(iterator, fileText))
    }

    override fun isRBraceToken(iterator: HighlighterIterator, fileText: CharSequence, fileType: FileType): Boolean {
        return rightBraces.contains(charAt(iterator, fileText))
    }

    override fun isPairBraces(tokenType: IElementType, tokenType2: IElementType): Boolean = true

    override fun isStructuralBrace(iterator: HighlighterIterator, text: CharSequence, fileType: FileType): Boolean {
        val c = charAt(iterator, text)
        return c == '{' || c == '}'
    }

    override fun getOppositeBraceTokenType(type: IElementType): IElementType? = null

    override fun isPairedBracesAllowedBeforeType(lbraceType: IElementType, contextType: IElementType?): Boolean = true

    override fun getCodeConstructStart(psiFile: PsiFile, openingBraceOffset: Int): Int = openingBraceOffset

    private fun charAt(iterator: HighlighterIterator, fileText: CharSequence): Char? {
        val start = iterator.start
        if (start < 0 || start >= fileText.length) {
            return null
        }
        return fileText[start]
    }

    companion object {
        private val leftBraces = setOf('{', '[', '(', '<')
        private val rightBraces = setOf('}', ']', ')', '>')
    }
}
