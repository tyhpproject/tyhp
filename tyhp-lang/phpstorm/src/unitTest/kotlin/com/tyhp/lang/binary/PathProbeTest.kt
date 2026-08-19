package com.tyhp.lang.binary

import java.nio.file.Files
import kotlin.io.path.writeText
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class PathProbeTest {
    private val unix = HostPlatform(OsId.OSX, ArchId.ARM64)
    private val win = HostPlatform(OsId.WIN, ArchId.X64)

    @Test
    fun `PATH probe finds tyhp in the first matching directory`() {
        val root = Files.createTempDirectory("tyhp-path-")
        try {
            val hitDir = root.resolve("bin")
            val missDir = root.resolve("empty")
            Files.createDirectories(hitDir)
            Files.createDirectories(missDir)
            val binary = hitDir.resolve("tyhp")
            binary.writeText("#!/bin/sh\n")
            binary.toFile().setExecutable(true)
            val found = probeTyhpOnPath(
                PathProbeOptions(
                    pathEnv = listOf(missDir, hitDir).joinToString(System.getProperty("path.separator")),
                    pathDelimiter = System.getProperty("path.separator"),
                    platform = unix,
                ),
            )
            assertEquals(binary.toAbsolutePath().normalize().toString(), found)
        } finally {
            root.toFile().deleteRecursively()
        }
    }

    @Test
    fun `PATH probe returns null when tyhp is absent`() {
        val found = probeTyhpOnPath(
            PathProbeOptions(
                pathEnv = "/no/such/tyhp-bin",
                pathDelimiter = ":",
                platform = unix,
            ),
        )
        assertNull(found)
    }

    @Test
    fun `Windows PATH probe prefers tyhp exe`() {
        val names = mutableListOf<String>()
        val found = probeTyhpOnPath(
            PathProbeOptions(
                pathEnv = "C:\\Tools",
                pathDelimiter = ";",
                platform = win,
                fs = PathProbeFs { filePath ->
                    names.add(filePath.toString())
                    filePath.fileName.toString().equals("tyhp.exe", ignoreCase = true)
                },
            ),
        )
        assertNotNull(found)
        assertTrue(found.endsWith("tyhp.exe"))
        assertTrue(names.any { it.endsWith("tyhp.exe") })
    }

    @Test
    fun `setting precedence explicit path is validated and command names resolve via PATH`() {
        val root = Files.createTempDirectory("tyhp-validate-")
        try {
            val binary = root.resolve("tyhp")
            binary.writeText("#!/bin/sh\n")
            binary.toFile().setExecutable(true)
            val ok = validateTyhpPath(binary.toString(), PathProbeOptions(platform = unix))
            assertTrue(ok.ok)
            assertEquals(binary.toRealPath().toString(), ok.absolutePath)

            val missing = validateTyhpPath(root.resolve("missing").toString(), PathProbeOptions(platform = unix))
            assertEquals(false, missing.ok)
            assertTrue(missing.message.orEmpty().contains("missing or is not a file"))

            val viaPath = lookUpCommandOnPath(
                "tyhp",
                PathProbeOptions(
                    pathEnv = root.toString(),
                    pathDelimiter = System.getProperty("path.separator"),
                    platform = unix,
                ),
            )
            assertEquals(binary.toAbsolutePath().normalize().toString(), viaPath)

            val cmd = validateTyhpPath(
                "tyhp",
                PathProbeOptions(
                    pathEnv = root.toString(),
                    pathDelimiter = System.getProperty("path.separator"),
                    platform = unix,
                ),
            )
            assertTrue(cmd.ok)
            assertEquals(binary.toRealPath().toString(), cmd.absolutePath)
        } finally {
            root.toFile().deleteRecursively()
        }
    }
}
