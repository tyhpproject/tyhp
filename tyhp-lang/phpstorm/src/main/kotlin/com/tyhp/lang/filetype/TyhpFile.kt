package com.tyhp.lang.filetype

import com.intellij.extapi.psi.PsiFileBase
import com.intellij.openapi.fileTypes.FileType
import com.intellij.psi.FileViewProvider
import com.tyhp.lang.TyhpLanguage

class TyhpFile(viewProvider: FileViewProvider) : PsiFileBase(viewProvider, TyhpLanguage) {
    override fun getFileType(): FileType {
        val name = virtualFile?.name ?: originalFile.virtualFile?.name ?: ""
        return if (name.endsWith(".tyhpdef", ignoreCase = true)) {
            TyhpdefFileType
        } else {
            TyhpFileType
        }
    }
}
