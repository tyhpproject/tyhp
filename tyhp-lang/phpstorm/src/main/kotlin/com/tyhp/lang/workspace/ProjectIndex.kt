package com.tyhp.lang.workspace

data class IndexedProject(
    override val projectFilePath: String,
    override val projectDir: String,
    val projectName: String,
    val include: List<String>,
    val exclude: List<String>,
) : OwnerCandidate

data class ProjectIndexSnapshot(
    val projectFilePath: String? = null,
    val projectDir: String? = null,
    val projectName: String? = null,
)

fun snapshotFromOwner(owner: IndexedProject?): ProjectIndexSnapshot {
    if (owner == null) {
        return ProjectIndexSnapshot()
    }
    return ProjectIndexSnapshot(
        projectFilePath = owner.projectFilePath,
        projectDir = owner.projectDir,
        projectName = owner.projectName,
    )
}

fun indexedProjectFromJson(projectFilePath: String, raw: String): IndexedProject {
    val globs = parseTyhpJsonGlobs(raw)
    val projectDir = posixDirname(toPosix(projectFilePath))
    return IndexedProject(
        projectFilePath = projectFilePath,
        projectDir = projectDir,
        projectName = posixBasename(projectDir),
        include = globs.include,
        exclude = globs.exclude,
    )
}

class ProjectIndex(
    val projects: List<IndexedProject>,
    val caseInsensitive: Boolean,
) {
    fun ownerOf(filePath: String): IndexedProject? {
        val posixFile = toPosix(filePath)
        val matches = projects.filter { project ->
            fileMatchesProject(
                projectDir = project.projectDir,
                filePath = posixFile,
                include = project.include,
                exclude = project.exclude,
                caseInsensitive = caseInsensitive,
            )
        }
        return selectOwner(posixFile, matches, caseInsensitive) as? IndexedProject
    }
}

val EMPTY_PROJECT_INDEX = ProjectIndex(emptyList(), false)
