/*
Tyhp lexer, extended from PhpLexer
*/

lexer grammar TyhpLexer;

options {
    caseInsensitive = false;
}

tokens {
    T_TYHP_OPEN_TAG,
    T_TYHPDEF_OPEN_TAG,
    T_TYHP_EXTENSION,
    T_TYHPDEF_DEPRECATED,
    T_TYHPDEF_OBSOLETE,
    T_TYHPDEF_PARTIAL,
    T_TYHPDEF_EXTERN,
    T_TYHPDEF_OMIT,
    T_TYHP_FALLBACK,
    T_TYHP_ASYNC,
    T_TYHP_INTERNAL,
    T_TYHP_USING,
    T_TYHP_TYPE_ALIAS,
    T_TYHP_AWAIT,
    T_TYHP_WITH,
    T_TYHP_OPERATOR,
    T_TYHP_VOID,
    T_TYHP_PARENT,
    T_TYHP_IS,
    T_TYHP_USING_EQUAL,
    T_TYHP_TYPEOF,
    T_TYHP_NAMEOF,
    T_TYHP_VARIABLE_EXISTS,
    T_DECIMAL_CAST,

    // grammar addon place holders
    // these are place holders for virtual grammar rules so they do not match to empty, this token does not really exist
    T_NO_GRAMMAR_ADDON_0000
}

// #include "shared/PhpLexer.body.g4"

mode ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON;
// #include "shared/Tyhpdef.lexer-addons.g4#1"
    INITIAL_T_OPEN_TYHP_TAG_EOF:        'tyhp' WHITESPACE* EOF -> type(T_TYHP_OPEN_TAG), popMode;
    INITIAL_T_OPEN_TYHP_TAG:            'tyhp' WHITESPACE_SINGLE {this._languageMode = "tyhp";} -> type(T_TYHP_OPEN_TAG), mode(ST_IN_SCRIPTING);

mode ST_IN_SCRIPTING;
// #include "shared/Tyhpdef.lexer-addons.g4#2"
    // `internal`, `with`, `using`, `typeof`, `nameof`, and `variable_exists` lex as
    // T_STRING and are reclassified in NextToken when the language mode is tyhp.
// #include "shared/Tyhpdef.lexer-addons.g4#3"
    // `:=` stays predicated. Dropping it would split the token into `:` and `=` in PHP
    // and in tyhp unless NextToken merged them back.
    T_TYHP_USING_EQUAL              options{caseInsensitive=true;}:
                                        ':' '=' {this._languageMode == "tyhp"}? -> type(T_TYHP_USING_EQUAL);

// Tagless source mode (Story 06, Phase 7).
// The lexer is started in this mode (in code, via ConfigureTagless) only when
// `source.tagless` is enabled AND the file begins with a literal open tag. The mode
// consumes that optional open tag and transitions into ST_IN_SCRIPTING. When there is
// no open tag, ConfigureTagless instead starts the lexer directly in ST_IN_SCRIPTING so
// the whole file is lexed natively (which keeps line/column tracking correct).
// There is no transition back to inline output, so a closing `?>` is rejected by the
// parser (its tagless entry rule omits T_CLOSE_TAG).
mode ST_TYHP_TAGLESS;
    TAGLESS_LEADING_WHITESPACE:         WHITESPACE -> skip;
// #include "shared/Tyhpdef.lexer-addons.g4#4"
    TAGLESS_TYHP_OPEN_TAG_EOF:          '<?tyhp' WHITESPACE* EOF {this._languageMode = "tyhp";} -> type(T_TYHP_OPEN_TAG), mode(ST_IN_SCRIPTING);
    TAGLESS_TYHP_OPEN_TAG:              '<?tyhp' WHITESPACE_SINGLE {this._languageMode = "tyhp";} -> type(T_TYHP_OPEN_TAG), mode(ST_IN_SCRIPTING);

