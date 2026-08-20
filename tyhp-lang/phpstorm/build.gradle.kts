plugins {
    java
    id("org.jetbrains.kotlin.jvm")
    id("org.jetbrains.intellij.platform")
}

group = providers.gradleProperty("pluginGroup").get()
version = providers.gradleProperty("pluginVersion").get()

kotlin {
    // PhpStorm 2026.2.1 (platform branch 262) requires a Java 25 toolchain;
    // a lower toolchain compiles but is flagged by verifyPluginProjectConfiguration
    // as unsafe for the platform's API surface.
    jvmToolchain(25)
}

val vscodeRoot = layout.projectDirectory.dir("../vscode")
val generatedIconsRoot = layout.buildDirectory.dir("generated/icons")
val generatedTextMateRoot = layout.buildDirectory.dir("generated/textmate-resources")
val generatedTextMateBundle = layout.buildDirectory.dir("generated/textmate-resources/textmate/tyhp")

val syncFileIcons by tasks.registering(Copy::class) {
    group = "tyhp"
    description = "Copy Tyhp file icons from the canonical VS Code media/ directory."
    from(vscodeRoot.dir("media")) {
        include(
            "tyhp-file-light.svg",
            "tyhp-file-dark.svg",
            "tyhpdef-file-light.svg",
            "tyhpdef-file-dark.svg",
        )
        rename { name ->
            when (name) {
                "tyhp-file-light.svg" -> "tyhp-file.svg"
                "tyhp-file-dark.svg" -> "tyhp-file_dark.svg"
                "tyhpdef-file-light.svg" -> "tyhpdef-file.svg"
                "tyhpdef-file-dark.svg" -> "tyhpdef-file_dark.svg"
                else -> name
            }
        }
    }
    eachFile { path = "icons/$name" }
    includeEmptyDirs = false
    into(generatedIconsRoot)
}

val syncTextMateBundle by tasks.registering(Copy::class) {
    group = "tyhp"
    description = "Copy canonical VS Code TextMate grammars into the plugin TextMate bundle."
    from(vscodeRoot.dir("syntaxes")) {
        into("syntaxes")
    }
    from(vscodeRoot.file("language-configuration.json"))
    from(layout.projectDirectory.file("src/main/textmate/package.json"))
    into(generatedTextMateBundle)
    doLast {
        val dest = destinationDir
        val grammar = dest.resolve("syntaxes/tyhp.tmLanguage.json")
        if (!grammar.isFile) {
            throw GradleException("Canonical Tyhp grammar was not copied: ${grammar.path}")
        }
        // The shared PHP grammar is included by source.tyhp (comments/strings/keywords).
        // Its fileTypes: ["php"] must not be shipped or TextMate would steal .php from PhpStorm.
        val phpGrammar = dest.resolve("syntaxes/tyhp-php.tmLanguage.json")
        if (phpGrammar.isFile) {
            val original = phpGrammar.readText()
            val stripped = original.replace("  \"fileTypes\": [\"php\"],\n", "")
            if (stripped == original) {
                throw GradleException("Failed to strip fileTypes from copied tyhp-php.tmLanguage.json")
            }
            phpGrammar.writeText(stripped)
        }
    }
}

sourceSets {
    main {
        resources {
            srcDir(syncFileIcons)
            srcDir(generatedTextMateRoot)
        }
    }
    create("unitTest") {
        compileClasspath += sourceSets["main"].output
        runtimeClasspath += sourceSets["main"].output
    }
}

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        val type = providers.gradleProperty("platformType")
        val platformVersion = providers.gradleProperty("platformVersion")
        create(type, platformVersion)

        bundledPlugins(
            providers.gradleProperty("platformBundledPlugins").map { it.split(',').map(String::trim) },
        )
        bundledModules(
            providers.gradleProperty("platformBundledModules").map { it.split(',').map(String::trim) },
        )
    }

    "unitTestImplementation"("org.jetbrains.kotlin:kotlin-stdlib:2.4.10")
    "unitTestImplementation"("org.jetbrains.kotlin:kotlin-test-junit5:2.4.10")
    "unitTestImplementation"("org.junit.jupiter:junit-jupiter:5.11.4")
    "unitTestRuntimeOnly"("org.junit.platform:junit-platform-launcher:1.11.4")
}

intellijPlatform {
    buildSearchableOptions = false

    pluginConfiguration {
        id = providers.gradleProperty("pluginGroup")
        name = providers.gradleProperty("pluginName")
        version = providers.gradleProperty("pluginVersion")
        vendor {
            name = "tyhp-lang"
            url = "https://github.com/tyhpproject/tyhp"
        }
        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            // Open-ended so a sideload ZIP still loads on later 2026.x builds.
            untilBuild = provider { null }
        }
    }
}

// Default Gradle task `runIde` launches the PhpStorm version declared above
// (platformType=PS). Do not retarget it at IntelliJ IDEA.

tasks {
    named("processResources") {
        dependsOn(syncTextMateBundle, syncFileIcons)
    }

    withType<org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask>().configureEach {
        dependsOn(syncTextMateBundle)
        from(generatedTextMateBundle) {
            into(pluginName.map { "$it/textmate/tyhp" })
        }
    }

    wrapper {
        gradleVersion = "9.5.0"
        distributionType = Wrapper.DistributionType.BIN
    }

    val unitTest by registering(Test::class) {
        group = "verification"
        description = "Pure JVM unit tests (settings, binary policy, LSP argv / project-file / backoff, workspace detection / init gating / run argv / status bar / XDebug proxy argv-ports-lifecycle; no IntelliJ test framework / runIde)."
        val unitTestSourceSet = sourceSets.named("unitTest").get()
        testClassesDirs = unitTestSourceSet.output.classesDirs
        classpath = unitTestSourceSet.runtimeClasspath
        useJUnitPlatform()
    }

    named("test") {
        dependsOn(unitTest)
    }

    named("check") {
        dependsOn(unitTest)
    }
}
