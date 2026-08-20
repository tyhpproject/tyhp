/**
 * vscode-languageclient middleware that keeps each session bound to files it
 * owns. Default `{ language: "tyhp" }` would otherwise send every `.tyhp` /
 * `.tyhpdef` document to every client.
 */

import * as vscode from "vscode";
import { Middleware } from "vscode-languageclient/node";
import * as settings from "../config/settings";
import { isSessionOwner } from "./sessionOwner";

export { isSessionOwner };

export function createOwnerMiddleware(
    sessionProjectFile: string,
    ownerProjectFileOf: (uri: vscode.Uri) => string | undefined
): Middleware {
    const owned = (uri: vscode.Uri): boolean =>
        isSessionOwner(sessionProjectFile, ownerProjectFileOf(uri));

    const passDoc = <A extends unknown[], R>(
        document: vscode.TextDocument,
        next: (document: vscode.TextDocument, ...args: A) => R,
        ...args: A
    ): R | undefined => {
        if (!owned(document.uri)) {
            return undefined;
        }
        return next(document, ...args);
    };

    return {
        didOpen: (data, next) => {
            if (owned(data.uri)) {
                return next(data);
            }
            return Promise.resolve();
        },
        didChange: (data, next) => {
            if (owned(data.document.uri)) {
                return next(data);
            }
            return Promise.resolve();
        },
        didClose: (data, next) => {
            if (owned(data.uri)) {
                return next(data);
            }
            return Promise.resolve();
        },
        didSave: (data, next) => {
            if (owned(data.uri)) {
                return next(data);
            }
            return Promise.resolve();
        },
        handleDiagnostics: (uri, diagnostics, next) => {
            if (!owned(uri) || !settings.getDiagnosticsEnable()) {
                next(uri, []);
                return;
            }
            next(uri, diagnostics);
        },
        provideHover: (document, position, token, next) =>
            passDoc(document, next, position, token),
        provideDefinition: (document, position, token, next) =>
            passDoc(document, next, position, token),
        provideCompletionItem: (document, position, context, token, next) =>
            passDoc(document, next, position, context, token),
        provideRenameEdits: (document, position, newName, token, next) =>
            passDoc(document, next, position, newName, token),
        prepareRename: (document, position, token, next) =>
            passDoc(document, next, position, token),
        provideDocumentSymbols: (document, token, next) => passDoc(document, next, token),
        provideSignatureHelp: (document, position, context, token, next) =>
            passDoc(document, next, position, context, token),
        provideCodeActions: (document, range, context, token, next) =>
            passDoc(document, next, range, context, token),
        provideDocumentHighlights: (document, position, token, next) =>
            passDoc(document, next, position, token),
        provideDocumentFormattingEdits: (document, options, token, next) =>
            passDoc(document, next, options, token),
        provideDocumentRangeFormattingEdits: (document, range, options, token, next) =>
            passDoc(document, next, range, options, token),
        provideReferences: (document, position, options, token, next) =>
            passDoc(document, next, position, options, token),
        provideWorkspaceSymbols: (query, token, next) => next(query, token),
        provideFoldingRanges: (document, context, token, next) =>
            passDoc(document, next, context, token),
        provideSelectionRanges: (document, positions, token, next) =>
            passDoc(document, next, positions, token),
        provideDocumentSemanticTokens: (document, token, next) => passDoc(document, next, token),
        provideDocumentSemanticTokensEdits: (document, previousResultId, token, next) =>
            passDoc(document, next, previousResultId, token),
        provideTypeDefinition: (document, position, token, next) =>
            passDoc(document, next, position, token),
        provideImplementation: (document, position, token, next) =>
            passDoc(document, next, position, token),
        provideDeclaration: (document, position, token, next) =>
            passDoc(document, next, position, token),
    };
}
