import assert from "node:assert/strict";
import { test } from "node:test";
import {
    classifyLanguageServerHelp,
    languageServerHelpLooksUnimplemented,
} from "./cliCapability";

const stubHelp = `Tyhp CLI v805.0.0-alpha.1

Note: The language server action is not yet implemented (Story 19). The options
below document the planned interface.

Start the Tyhp Language Server Protocol (LSP) server for IDE integration
`;

const currentHelp = `Start the Tyhp Language Server Protocol (LSP) server for IDE integration
(diagnostics, completion, hover, go-to-definition, and related features).

Options:
    --stdio                     Communicate via stdin/stdout (default; the only transport implemented)
    --tcp=<port>                Communicate via TCP on the given port (not yet implemented)
    --pipe=<name>               Communicate via named pipe (not yet implemented)
`;

test("stub Story 19 help is unimplemented", () => {
    assert.equal(classifyLanguageServerHelp(stubHelp), "unimplemented");
    assert.equal(languageServerHelpLooksUnimplemented(stubHelp), true);
});

test("current help is available even though tcp/pipe are not yet implemented", () => {
    assert.equal(classifyLanguageServerHelp(currentHelp), "available");
    assert.equal(languageServerHelpLooksUnimplemented(currentHelp), false);
});

test("empty help is unknown, not unimplemented", () => {
    assert.equal(classifyLanguageServerHelp(""), "unknown");
    assert.equal(classifyLanguageServerHelp("   "), "unknown");
});

test("the unused CLI_LanguageServerNotImplemented string is also unimplemented", () => {
    assert.equal(
        classifyLanguageServerHelp("The language server is not yet implemented (Story 19)."),
        "unimplemented"
    );
});
