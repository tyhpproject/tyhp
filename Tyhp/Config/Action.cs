using System.ComponentModel;

namespace Tyhp.Config
{
    enum Action
    {
        [Description("Represents an invalid action (internal use only).")]
        invalid,

        [Description("Displays helpful information for running and using Tyhp from the command line.")]
        help,

        [Description("Display the version of Tyhp and exit.")]
        version,

        [Description("Print the long-form explanation for a diagnostic code (TYHP####).")]
        explain,

        [Description("Run a composer command on this Tyhp project.")]
        composer,

        [Description("Initialize a new Tyhp project.")]
        init,

        [Description("Build this Tyhp project.")]
        build,

        [Description("Check for errors and warnings on this Tyhp project.")]
        lint,

        [Description("Lex a source file and dump the token list as JSON (lexer debugging).")]
        tokenize,

        [Description("Parse a source file and dump the AST as JSON (parser debugging).")]
        dump_ast,

        [Description("Start the Tyhp language server.")]
        language_server,

        [Description("Start the XDebug proxy to allow debugging Tyhp code using XDebug.")]
        xdebug_proxy,

        [Description("Generate Tyhpdef file(s) for a composer package or PHP module.")]
        generate_tyhpdef,

        [Description("Run internal debugging tools (for compiler development).")]
        debug,

        [Description("Run integrity checks on this Tyhp build.")]
        integrity_check,

        [Description("Delete the on-disk AST cache for all builds.")]
        clear_cache,
    }
}