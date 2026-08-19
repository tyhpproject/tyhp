package com.tyhp.lang.icons

import com.intellij.openapi.util.IconLoader
import javax.swing.Icon

/**
 * Project-view icons. SVGs are copied at build time from
 * `tyhp-lang/vscode/media/` (canonical source). IntelliJ picks
 * `*_dark.svg` in Darcula / dark themes.
 */
object TyhpIcons {
    @JvmField
    val TyhpFile: Icon = IconLoader.getIcon("/icons/tyhp-file.svg", TyhpIcons::class.java)

    @JvmField
    val TyhpdefFile: Icon = IconLoader.getIcon("/icons/tyhpdef-file.svg", TyhpIcons::class.java)
}
