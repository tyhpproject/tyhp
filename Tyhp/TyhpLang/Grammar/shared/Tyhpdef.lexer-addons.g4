    INITIAL_T_OPEN_TYHPDEF_TAG_EOF:     'tyhpdef' WHITESPACE* EOF -> type(T_TYHPDEF_OPEN_TAG), popMode;
    INITIAL_T_OPEN_TYHPDEF_TAG:         'tyhpdef' WHITESPACE_SINGLE {this._languageMode = "tyhpdef";} -> type(T_TYHPDEF_OPEN_TAG), mode(ST_IN_SCRIPTING);

// #part
    // Contextual and language-mode keywords (`type`, `async`, `operator`, `void`,
    // `await`, `parent`, `extension`, `is`, `deprecated`, `obsolete`, `partial`,
    // `extern`, `omit`, `fallback`) lex as T_STRING. NextToken reclassifies them from
    // language mode, spelling, and the same lookahead the old rules used.
    // `struct` stays T_STRING; the parser decides keyword vs name.
    // `(decimal)` stays a predicated lexer rule: in PHP it must remain `(`, identifier, `)`.
    T_DECIMAL_CAST:                     '(' TABS_AND_SPACES [dD][eE][cC][iI][mM][aA][lL] TABS_AND_SPACES ')' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_DECIMAL_CAST);

// #part

// #part
    TAGLESS_TYHPDEF_OPEN_TAG_EOF:       '<?tyhpdef' WHITESPACE* EOF {this._languageMode = "tyhpdef";} -> type(T_TYHPDEF_OPEN_TAG), mode(ST_IN_SCRIPTING);
    TAGLESS_TYHPDEF_OPEN_TAG:           '<?tyhpdef' WHITESPACE_SINGLE {this._languageMode = "tyhpdef";} -> type(T_TYHPDEF_OPEN_TAG), mode(ST_IN_SCRIPTING);
