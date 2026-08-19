package com.tyhp.lang.editor

import com.intellij.codeInsight.editorActions.QuoteHandler
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.editor.highlighter.HighlighterIterator

/**
 * Quote pairing equivalent to VS Code `autoClosingPairs` for `'` and `"`.
 */
class TyhpQuoteHandler : QuoteHandler {
    override fun isClosingQuote(iterator: HighlighterIterator, offset: Int): Boolean {
        val document = iterator.document
        if (offset <= 0 || offset > document.textLength) {
            return false
        }
        return isQuote(document.charsSequence[offset - 1])
    }

    override fun isOpeningQuote(iterator: HighlighterIterator, offset: Int): Boolean {
        val document = iterator.document
        if (offset >= document.textLength) {
            return false
        }
        return isQuote(document.charsSequence[offset])
    }

    override fun hasNonClosedLiteral(editor: Editor, iterator: HighlighterIterator, offset: Int): Boolean = true

    override fun isInsideLiteral(iterator: HighlighterIterator): Boolean = false

    private fun isQuote(c: Char): Boolean = c == '\'' || c == '"'
}
