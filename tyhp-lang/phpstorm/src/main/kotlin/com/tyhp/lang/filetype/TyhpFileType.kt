package com.tyhp.lang.filetype

import com.intellij.openapi.fileTypes.LanguageFileType
import com.tyhp.lang.TyhpLanguage
import com.tyhp.lang.icons.TyhpIcons
import javax.swing.Icon

object TyhpFileType : LanguageFileType(TyhpLanguage) {
    override fun getName(): String = "Tyhp"

    override fun getDescription(): String = "Tyhp source"

    override fun getDefaultExtension(): String = "tyhp"

    override fun getIcon(): Icon = TyhpIcons.TyhpFile
}
