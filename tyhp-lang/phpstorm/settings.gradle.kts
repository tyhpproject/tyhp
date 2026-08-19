rootProject.name = "tyhp-lang"

pluginManagement {
    plugins {
        // PhpStorm 2026.2 is compiled with Kotlin 2.4 metadata; 2.2.x cannot read it.
        id("org.jetbrains.kotlin.jvm") version "2.4.10"
        id("org.jetbrains.intellij.platform") version "2.18.1"
    }
    repositories {
        mavenCentral()
        gradlePluginPortal()
    }
}

plugins {
    id("org.gradle.toolchains.foojay-resolver-convention") version "1.0.0"
}
