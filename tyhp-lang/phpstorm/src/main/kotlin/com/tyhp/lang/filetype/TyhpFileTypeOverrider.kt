package com.tyhp.lang.filetype

import com.intellij.openapi.fileTypes.FileType
import com.intellij.openapi.fileTypes.impl.FileTypeOverrider
import com.intellij.openapi.vfs.VirtualFile

/**
 * Wins over PhpStorm's PHP detector so `<?tyhp` / `<?tyhpdef` files are never
 * claimed as PHP (PHP short-open-tag detection looks at `<?`).
 */
class TyhpFileTypeOverrider : FileTypeOverrider {
    override fun getOverriddenFileType(file: VirtualFile): FileType? {
        return when (file.extension?.lowercase()) {
            "tyhp" -> TyhpFileType
            "tyhpdef" -> TyhpdefFileType
            else -> null
        }
    }
}
