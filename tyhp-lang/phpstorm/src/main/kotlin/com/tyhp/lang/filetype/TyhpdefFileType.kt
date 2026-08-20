package com.tyhp.lang.filetype

import com.intellij.openapi.fileTypes.LanguageFileType
import com.tyhp.lang.TyhpLanguage
import com.tyhp.lang.icons.TyhpIcons
import javax.swing.Icon

object TyhpdefFileType : LanguageFileType(TyhpLanguage) {
    override fun getName(): String = "Tyhp Definition"

    override fun getDescription(): String = "Tyhp definition"

    override fun getDefaultExtension(): String = "tyhpdef"

    override fun getIcon(): Icon = TyhpIcons.TyhpdefFile
}
