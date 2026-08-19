package com.tyhp.lang

import com.intellij.lang.Language

/**
 * Shared language for `.tyhp` and `.tyhpdef`. Highlighting comes from the
 * canonical VS Code TextMate grammars (not a PhpStorm-only fork).
 */
object TyhpLanguage : Language("Tyhp") {
    override fun getDisplayName(): String = "Tyhp"
}
