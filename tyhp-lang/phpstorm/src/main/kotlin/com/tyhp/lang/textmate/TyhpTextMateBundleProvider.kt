package com.tyhp.lang.textmate

import com.intellij.openapi.diagnostic.logger
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider

class TyhpTextMateBundleProvider : TextMateBundleProvider {
    override fun getBundles(): List<TextMateBundleProvider.PluginBundle> {
        val path = TyhpTextMateBundleSupport.resolveBundlePath()
        if (path == null) {
            log.warn("Tyhp TextMate bundle not found; .tyhp highlighting will be unstyled until the plugin is rebuilt")
            return emptyList()
        }
        return listOf(TextMateBundleProvider.PluginBundle("Tyhp", path))
    }

    companion object {
        private val log = logger<TyhpTextMateBundleProvider>()
    }
}
