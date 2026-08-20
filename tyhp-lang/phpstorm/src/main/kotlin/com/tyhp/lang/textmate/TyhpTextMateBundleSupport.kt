package com.tyhp.lang.textmate

import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.extensions.PluginId
import com.tyhp.lang.TyhpPlugin
import java.net.URI
import java.nio.file.FileSystemNotFoundException
import java.nio.file.FileSystems
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption

/**
 * Resolves the TextMate bundle directory copied from `tyhp-lang/vscode/syntaxes/`
 * at Gradle build time. Prefers the unpacked plugin layout (`textmate/tyhp` next
 * to `lib/`); falls back to extracting classpath resources.
 */
internal object TyhpTextMateBundleSupport {
    private val log = logger<TyhpTextMateBundleSupport>()

    const val BUNDLE_RESOURCE_ROOT: String = "textmate/tyhp"
    private const val GRAMMAR_RELATIVE: String = "syntaxes/tyhp.tmLanguage.json"

    private val extractedPath: Path? by lazy { extractClasspathBundle() }

    fun resolveBundlePath(): Path? {
        pluginInstallBundlePath()?.let { return it }
        return extractedPath
    }

    private fun pluginInstallBundlePath(): Path? {
        val plugin = PluginManagerCore.getPlugin(PluginId.getId(TyhpPlugin.PLUGIN_ID)) ?: return null
        val dir = plugin.pluginPath.resolve("textmate/tyhp")
        return dir.takeIf { Files.isRegularFile(it.resolve(GRAMMAR_RELATIVE)) }
    }

    private fun extractClasspathBundle(): Path? {
        val classLoader = TyhpTextMateBundleSupport::class.java.classLoader
        val grammarUrl = classLoader.getResource("$BUNDLE_RESOURCE_ROOT/$GRAMMAR_RELATIVE")
        if (grammarUrl == null) {
            log.warn("Tyhp TextMate grammar missing from plugin resources: $BUNDLE_RESOURCE_ROOT/$GRAMMAR_RELATIVE")
            return null
        }

        val rootUrl = classLoader.getResource(BUNDLE_RESOURCE_ROOT)
        if (rootUrl != null && rootUrl.protocol == "file") {
            val dir = Path.of(rootUrl.toURI())
            if (Files.isRegularFile(dir.resolve(GRAMMAR_RELATIVE))) {
                return dir
            }
        }

        return try {
            copyBundleFromUrl(grammarUrl.toURI())
        } catch (e: Exception) {
            log.warn("Failed to extract Tyhp TextMate bundle from classpath", e)
            null
        }
    }

    private fun copyBundleFromUrl(grammarUri: URI): Path? {
        val target = Files.createTempDirectory("tyhp-textmate-")
        target.toFile().deleteOnExit()

        val grammarPath = nioPath(grammarUri) ?: return null
        val bundleRoot = grammarPath.parent?.parent ?: return null

        Files.walk(bundleRoot).use { walk ->
            walk.forEach { source ->
                val relative = bundleRoot.relativize(source).toString()
                if (relative.isEmpty()) {
                    return@forEach
                }
                val dest = target.resolve(relative)
                if (Files.isDirectory(source)) {
                    Files.createDirectories(dest)
                } else {
                    Files.createDirectories(dest.parent)
                    Files.copy(source, dest, StandardCopyOption.REPLACE_EXISTING)
                    dest.toFile().deleteOnExit()
                }
            }
        }

        return target.takeIf { Files.isRegularFile(it.resolve(GRAMMAR_RELATIVE)) }
    }

    private fun nioPath(uri: URI): Path? {
        if (uri.scheme == "file") {
            return Path.of(uri)
        }
        if (uri.scheme != "jar") {
            return null
        }
        val fs = try {
            FileSystems.getFileSystem(uri)
        } catch (_: FileSystemNotFoundException) {
            FileSystems.newFileSystem(uri, emptyMap<String, Any>())
        }
        return fs.provider().getPath(uri)
    }
}
