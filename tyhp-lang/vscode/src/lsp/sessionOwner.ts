/** Whether a document's owner `tyhp.json` is this language-server session. */
export function isSessionOwner(
    sessionProjectFile: string,
    documentOwner: string | undefined
): boolean {
    return documentOwner !== undefined && documentOwner === sessionProjectFile;
}
