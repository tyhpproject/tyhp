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
    T_TYHP_ASYNC,
    T_TYHP_USING,
    T_TYHP_STRUCT,
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

    // grammor addon place holders
    // these are place holders for virtual grammar rules so they do not match to empty, this token does not really exist
    T_NO_GRAMMAR_ADDON_0000
}

import PhpLexer;

mode ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON;
    INITIAL_T_OPEN_TYHPDEF_TAG_EOF:
                                'tyhpdef' WHITESPACE* EOF -> type(T_TYHPDEF_OPEN_TAG), popMode;
    INITIAL_T_OPEN_TYHPDEF_TAG:
                                'tyhpdef' WHITESPACE_SINGLE {this._languageMode = "tyhpdef";} -> type(T_TYHPDEF_OPEN_TAG), mode(ST_IN_SCRIPTING);
    INITIAL_T_OPEN_TYHP_TAG_EOF:
                                'tyhp' WHITESPACE* EOF -> type(T_TYHP_OPEN_TAG), popMode;
    INITIAL_T_OPEN_TYHP_TAG:    'tyhp' WHITESPACE_SINGLE {this._languageMode = "tyhp";} -> type(T_TYHP_OPEN_TAG), mode(ST_IN_SCRIPTING);

mode ST_IN_SCRIPTING;
    // TYHP or TYHPDEF
    STRUCT_AS_T_STRING options{caseInsensitive=true;}:
                                'struct' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(8, "extends\\b.", true) || this.streamLA(11, "implements\\b.", true) || !this.streamLA(1, "[a-zA-Z_\x80-\xff({]", true)}? {this.doPreparedLess();} -> type(T_STRING);
    T_TYHP_STRUCT options{caseInsensitive=true;}:
                                'struct' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(1, "[a-zA-Z_\x80-\xff({]", true)}? {this.doPreparedLess();} -> type(T_TYHP_STRUCT);
    TYPE_ALIAS_AS_T_STRING options{caseInsensitive=true;}:
                                'type' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(8, "extends\\b.") || this.streamLA(11, "implements\\b.") || !this.streamLA(1, "[a-zA-Z_\x80-\xff]")}? {this.doPreparedLess();} -> type(T_STRING);
    T_TYHP_TYPE_ALIAS options{caseInsensitive=true;}:
                                'type' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(1, "[a-zA-Z_\x80-\xff]")}? {this.doPreparedLess();} -> type(T_TYHP_TYPE_ALIAS);
    T_TYHP_ASYNC options{caseInsensitive=true;}:
                                'async' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_TYHP_ASYNC);
    T_TYHP_AWAIT options{caseInsensitive=true;}:
                                'await' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_TYHP_AWAIT);
    T_TYHP_OPERATOR options{caseInsensitive=true;}:
                                'operator' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_TYHP_OPERATOR);
    T_TYHP_VOID options{caseInsensitive=true;}:
                                'void' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_TYHP_VOID);
    T_TYHP_PARENT options{caseInsensitive=true;}:
                                'parent' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_TYHP_PARENT);
    T_DECIMAL_CAST:             '(' TABS_AND_SPACES [dD][eE][cC][iI][mM][aA][lL] TABS_AND_SPACES ')' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? -> type(T_DECIMAL_CAST);


    // TYHPDEF
    T_TYHPDEF_DEPRECATED options{caseInsensitive=true;}:
                                'deprecated' {this._languageMode == "tyhpdef"}? -> type(T_TYHPDEF_DEPRECATED);
    T_TYHPDEF_OBSOLETE options{caseInsensitive=true;}:
                                'obsolete' {this._languageMode == "tyhpdef"}? -> type(T_TYHPDEF_OBSOLETE);
    
    // TYHP
    T_TYHP_WITH options{caseInsensitive=true;}:
                                'with' {this._languageMode == "tyhp"}? -> type(T_TYHP_WITH);
    T_TYHP_USING options{caseInsensitive=true;}:
                                'using' {this._languageMode == "tyhp"}? -> type(T_TYHP_USING);
    EXTENSION_AS_T_STRING options{caseInsensitive=true;}:
                                'extension' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(8, "extends\\b.") || this.streamLA(11, "implements\\b.") || !this.streamLA(1, "[a-zA-Z_\x80-\xff]")}? {this.doPreparedLess();} -> type(T_STRING);
    T_TYHP_EXTENSION options{caseInsensitive=true;}:
                                'extension' {(this._languageMode == "tyhp" || this._languageMode == "tyhpdef")}? {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(1, "[a-zA-Z_\x80-\xff]")}? {this.doPreparedLess();} -> type(T_TYHP_EXTENSION);
    T_TYHP_IS options{caseInsensitive=true;}:
                                ('is'|'isa'|'isan'|'is_a'|'is_an') {this._languageMode == "tyhp" || this._languageMode == "tyhpdef"}? -> type(T_TYHP_IS);
    T_TYHP_TYPEOF options{caseInsensitive=true;}:
                                'typeof' {this._languageMode == "tyhp"}? -> type(T_TYHP_TYPEOF);
    T_TYHP_NAMEOF options{caseInsensitive=true;}:
                                'nameof' {this._languageMode == "tyhp"}? -> type(T_TYHP_NAMEOF);
    T_TYHP_VARIABLE_EXISTS options{caseInsensitive=true;}:
                                'variable_exists' {this._languageMode == "tyhp"}? -> type(T_TYHP_VARIABLE_EXISTS);
    T_TYHP_USING_EQUAL options{caseInsensitive=true;}:
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
    TAGLESS_LEADING_WHITESPACE: WHITESPACE -> skip;
    TAGLESS_TYHPDEF_OPEN_TAG_EOF:
                                '<?tyhpdef' WHITESPACE* EOF {this._languageMode = "tyhpdef";} -> type(T_TYHPDEF_OPEN_TAG), mode(ST_IN_SCRIPTING);
    TAGLESS_TYHPDEF_OPEN_TAG:   '<?tyhpdef' WHITESPACE_SINGLE {this._languageMode = "tyhpdef";} -> type(T_TYHPDEF_OPEN_TAG), mode(ST_IN_SCRIPTING);
    TAGLESS_TYHP_OPEN_TAG_EOF:  '<?tyhp' WHITESPACE* EOF {this._languageMode = "tyhp";} -> type(T_TYHP_OPEN_TAG), mode(ST_IN_SCRIPTING);
    TAGLESS_TYHP_OPEN_TAG:      '<?tyhp' WHITESPACE_SINGLE {this._languageMode = "tyhp";} -> type(T_TYHP_OPEN_TAG), mode(ST_IN_SCRIPTING);

