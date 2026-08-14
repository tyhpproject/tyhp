/*
 * Php lexer, based off of PHP lexer located at:
 * https://github.com/php/php-src/blob/PHP-8.5.9/Zend/zend_language_scanner.l
 *
 * Lineage: php-src PHP-8.5.x (highest supported PHP minor for Tyhp).
 * https://php.watch/versions
 */

lexer grammar PhpLexer;

@lexer::header {
#pragma warning disable CS3021
}

channels {
    DocBlockCommentsChannel,
    SimpleCommentsChannel,
    WhiteSpaceChannel,
    ErrorLexemChannel,
    SkipChannel,
    StubTokenChannel
}

options {
    caseInsensitive = false;
}

tokens {
    T_ERROR,
    T_STRING,
    T_WHITESPACE,
    T_OBJECT_OPERATOR,
    T_NULLSAFE_OBJECT_OPERATOR,
    T_DOLLAR_OPEN_CURLY_BRACES,
    T_VARIABLE,
    T_OPEN_SQUARE_BRACE,
    T_CLOSE_SQUARE_BRACE,
    T_OPEN_ROUND_BRACE,
    T_CLOSE_ROUND_BRACE,
    T_OPEN_CURLY_BRACE,
    T_CLOSE_CURLY_BRACE,
    T_ENCAPSED_AND_WHITESPACE,
    T_CONSTANT_ENCAPSED_STRING,
    T_BINARY_CONSTANT_ENCAPSED_STRING,
    T_COMMENT,
    T_DOC_COMMENT,
    T_BACKQUOTE,
    T_DOUBLE_QUOTE,
    T_START_NOWDOC,
    T_START_HEREDOC,
    T_BINARY_BACKQUOTE,
    T_BINARY_DOUBLE_QUOTE,
    T_BINARY_START_NOWDOC,
    T_BINARY_START_HEREDOC,
    T_END_HEREDOC,
    T_OPEN_TAG_WITH_ECHO,
    T_OPEN_TAG,
    T_BAD_CHARACTER,
    T_INLINE_HTML,
    T_SYM_SEMICOLON,
    T_SYM_COLON,
    T_SYM_COMMA, 
    T_SYM_PERIOD,
    T_SYM_PIPE,
    T_SYM_CARET,
    T_SYM_AMPERSAND,
    T_SYM_PLUS,
    T_SYM_MINUS,
    T_SYM_SLASH,
    T_SYM_ASTERISK,
    T_SYM_EQUAL,
    T_SYM_PERCENT,
    T_SYM_BANG,
    T_SYM_TILDE,
    T_SYM_DOLLAR,
    T_SYM_LT,
    T_SYM_GT,
    T_SYM_QUESTION,
    T_SYM_AT,
    T_STRING_VARNAME,
    T_IS_NOT_EQUAL,
    T_ATTRIBUTE,
    T_EXIT,
    T_YIELD_FROM,
    T_ENUM,
    T_INT_CAST,
    T_DOUBLE_CAST,
    T_STRING_CAST,
    T_ARRAY_CAST,
    T_OBJECT_CAST,
    T_BOOL_CAST,
    T_VOID_CAST,
    T_EVAL,
    T_INCLUDE,
    T_INCLUDE_ONCE,
    T_REQUIRE,
    T_REQUIRE_ONCE,
    T_NAMESPACE,
    T_USE,
    T_INSTEADOF,
    T_GLOBAL,
    T_ISSET,
    T_EMPTY,
    T_LNUMBER,
    T_DNUMBER,
    T_ONUMBER,
    T_HNUMBER,
    T_BNUMBER,
    T_NAME_RELATIVE,
    T_NAME_QUALIFIED,
    T_NAME_FULLY_QUALIFIED,
    T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG,
    T_EXIT,
    T_FN,
    T_FUNCTION,
    T_CONST,
    T_RETURN,
    T_YIELD,
    T_TRY,
    T_CATCH,
    T_FINALLY,
    T_THROW,
    T_IF,
    T_ELSEIF,
    T_ENDIF,
    T_ELSE,
    T_WHILE,
    T_ENDWHILE,
    T_DO,
    T_FOR,
    T_ENDFOR,
    T_FOREACH,
    T_ENDFOREACH,
    T_DECLARE,
    T_ENDDECLARE,
    T_INSTANCEOF,
    T_AS,
    T_SWITCH,
    T_MATCH,
    T_ENDSWITCH,
    T_CASE,
    T_DEFAULT,
    T_BREAK,
    T_CONTINUE,
    T_GOTO,
    T_ECHO,
    T_PRINT,
    T_CLASS,
    T_INTERFACE,
    T_TRAIT,
    T_EXTENDS,
    T_IMPLEMENTS,
    T_DOUBLE_COLON,
    T_ELLIPSIS,
    T_COALESCE,
    T_NEW,
    T_CLONE,
    T_VAR,
    T_HALT_COMPILER,
    T_STATIC,
    T_ABSTRACT,
    T_FINAL,
    T_PRIVATE,
    T_PROTECTED,
    T_PUBLIC_SET,
    T_PROTECTED_SET,
    T_PRIVATE_SET,
    T_PUBLIC,
    T_READONLY,
    T_UNSET,
    T_DOUBLE_ARROW,
    T_LIST,
    T_ARRAY,
    T_CALLABLE,
    T_INC,
    T_DEC,
    T_IS_IDENTICAL,
    T_IS_NOT_IDENTICAL,
    T_IS_EQUAL,
    T_SPACESHIP,
    T_IS_SMALLER_OR_EQUAL,
    T_IS_GREATER_OR_EQUAL,
    T_PLUS_EQUAL,
    T_MINUS_EQUAL,
    T_MUL_EQUAL,
    T_POW,
    T_POW_EQUAL,
    T_DIV_EQUAL,
    T_CONCAT_EQUAL,
    T_MOD_EQUAL,
    T_SL_EQUAL,
    T_AND_EQUAL,
    T_OR_EQUAL,
    T_XOR_EQUAL,
    T_COALESCE_EQUAL,
    T_PIPE,
    T_BOOLEAN_OR,
    T_BOOLEAN_AND,
    T_LOGICAL_OR,
    T_LOGICAL_AND,
    T_LOGICAL_XOR,
    T_SL,
    T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG,
    T_CLASS_C,
    T_TRAIT_C,
    T_FUNC_C,
    T_PROPERTY_C,
    T_METHOD_C,
    T_LINE,
    T_FILE,
    T_DIR,
    T_NS_C,
    T_NS_SEPARATOR,
    T_CLOSE_TAG,
    T_SR,
    T_SR_EQUAL,
    T_CONSTRUCT_METHOD,
    T_STUB_TOKEN,

    // grammar addon place holders
    // these are place holders for virtual grammar rules so they do not match to empty, this token does not really exist
    T_NO_GRAMMAR_ADDON_0000
}

fragment DIGIT:                 [0-9];
fragment LNUM:                  DIGIT+ ('_' DIGIT+)*;
fragment DNUM:                  (LNUM? '.' LNUM) | (LNUM '.' LNUM?);
fragment EXPONENT_DNUM:	        ((LNUM | DNUM)[eE][+-]? LNUM);
fragment HNUM:                  '0x' [0-9a-fA-F]+ ('_' [0-9a-fA-F]+)*;
fragment BNUM:                  '0b' [01]+ ('_' [01]+)*;
fragment ONUM:                  '0o' [0-7]+ ('_' [0-7]+)*;
fragment LABEL:                 [a-zA-Z_\u0080-\u00ff][a-zA-Z0-9_\u0080-\u00ff]*;
fragment WHITESPACE:            [ \n\r\t]+;
fragment OPTIONAL_WHITESPACE:   [ \n\r\t]*;
fragment WHITESPACE_SINGLE:     ('\r\n' | [ \n\r\t]);
fragment MULTI_LINE_COMMENT:    '/*'([^*\u0000]*'*'+)([^*/\u0000][^*\u0000]*'*'+)*'/';
fragment SINGLE_LINE_COMMENT:   '//'[^\u0000\n\r]*[\n\r];
fragment HASH_COMMENT:          '#'(([^[\u0000][^\u0000\n\r]*[\n\r])|[\n\r]);
fragment WHITESPACE_OR_COMMENTS:
                                (WHITESPACE|MULTI_LINE_COMMENT|SINGLE_LINE_COMMENT|HASH_COMMENT)+;
fragment OPTIONAL_WHITESPACE_OR_COMMENTS:
                                (WHITESPACE|MULTI_LINE_COMMENT|SINGLE_LINE_COMMENT|HASH_COMMENT)*;
fragment TABS_AND_SPACES:       [ \t]*;
fragment ANY_CHAR:              [^];
fragment NEWLINE:               ('\r\n' | '\r' | '\n');
fragment SYM_SEMICOLON:         ';';
fragment SYM_COLON:             ':';
fragment SYM_COMMA:             ',';
fragment SYM_PERIOD:            '.';
fragment SYM_PIPE:              '|';
fragment SYM_CARET:             '^';
fragment SYM_AMPERSAND:         '&';
fragment SYM_PLUS:              '+';
fragment SYM_MINUS:             '-';
fragment SYM_SLASH:             '/';
fragment SYM_ASTERISK:          '*';
fragment SYM_EQUAL:             '=';
fragment SYM_PERCENT:           '%';
fragment SYM_BANG:              '!';
fragment SYM_TILDE:             '~';
fragment SYM_DOLLAR:            '$';
fragment SYM_LT:                '<';
fragment SYM_GT:                '>';
fragment SYM_QUESTION:          '?';
fragment SYM_AT:                '@';
fragment SYM_TOKENS:            SYM_SEMICOLON | SYM_COLON | SYM_COMMA | SYM_PERIOD | SYM_PIPE |
                                    SYM_CARET | SYM_AMPERSAND | SYM_PLUS | SYM_MINUS | SYM_SLASH |
                                    SYM_ASTERISK | SYM_EQUAL | SYM_PERCENT | SYM_BANG | SYM_TILDE |
                                    SYM_DOLLAR | SYM_LT | SYM_GT | SYM_QUESTION | SYM_AT;
fragment VARIABLE:              '$' LABEL;

INITIAL_T_OPEN_TAG_WITH_ECHO:   SYM_LT SYM_QUESTION SYM_EQUAL {this._languageMode = "phpEcho";} -> type(T_OPEN_TAG_WITH_ECHO), pushMode(ST_IN_SCRIPTING);
INITIAL_T_OPEN_PHP_TAG_EOF:     SYM_LT SYM_QUESTION 'php' WHITESPACE* EOF -> type(T_OPEN_TAG);
INITIAL_T_OPEN_PHP_TAG:         SYM_LT SYM_QUESTION 'php' WHITESPACE_SINGLE {this._languageMode = "php";} -> type(T_OPEN_TAG), pushMode(ST_IN_SCRIPTING);
// these are disabled for now:
// INITIAL_SHORT_T_OPEN_TAG:       SYM_LT SYM_QUESTION .*? ((SYM_QUESTION SYM_GT) | EOF) -> type(T_PHP_BLOCK_STRING_SHORTTAG);
// INITIAL_SHORT_T_OPEN_TAG_EOF:   SYM_LT SYM_QUESTION WHITESPACE* EOF -> type(T_OPEN_TAG);
CHECK_FOR_OTHER_OPEN_TAGS:      SYM_LT SYM_QUESTION -> more, pushMode(ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON);

// CHECK_FOR_INLINE_HTML:          . {(this.Text != "<" || !this.streamLAEq("?"))}? {this.less(1, "CHECK_FOR_INLINE_HTML");} -> more, pushMode(ST_INLINE_HTML);
// CHECK_FOR_INLINE_HTML:          (~[<\r\n] ~[\r\n] | ~[\r\n] ~[?\r\n]) {this.less(2, "CHECK_FOR_INLINE_HTML");} -> more, pushMode(ST_INLINE_HTML);
INLINE_HTML_CHAR_TO_OPEN_TAG:   . {this.streamLAEq("<?")}? -> type(T_INLINE_HTML);
INLINE_HTML_CHAR_TO_EOF:        . EOF -> type(T_INLINE_HTML);
INLINE_HTML_NEWLINE:            NEWLINE -> more, pushMode(ST_INLINE_HTML);
INLINE_HTML_CHAR_TO_CHAR:       (~[<] . | . ~[?]) {!(this.Text[this.Text.Length - 1].ToString() == "<" && this.streamLAEq("?"))}? -> more, pushMode(ST_INLINE_HTML);

mode ST_INLINE_HTML;
    INLINE_HTML_OPEN_TAG:        SYM_LT SYM_QUESTION {this.less(2, "INLINE_HTML_OPEN_TAG");} -> type(T_INLINE_HTML), popMode;
    INLINE_HTML_EOF2:            EOF -> type(T_INLINE_HTML), popMode;
    INLINE_HTML_CHAR:            . -> more;

mode ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON;
    VIRTUAL_ST_CHECK_FOR_OTHER_OPEN_TAGS: . -> type(T_ERROR), popMode;

mode ST_IN_SCRIPTING;
    // adding a semicolon before the close tag allows our parser to handle the close tag as a statement terminator without a bunch of extra rules
    T_INLINE_HTML_IN_PHP_CODE:  {this._languageMode == "php"}? SYM_SEMICOLON? '?>' .*? SYM_LT SYM_QUESTION 'php' WHITESPACE_SINGLE {this.closeTagHandler();} -> type(T_INLINE_HTML);
    T_CLOSE_TAG_WITH_NEWLINE:   '?>' NEWLINE? {this.closeTagHandler();} -> type(T_CLOSE_TAG), popMode;
    // T_STUB_TOKEN:               '{{' .*? '}}' -> type(T_STUB_TOKEN), channel(StubTokenChannel);
    // T_CLOSE_TAG:                '?>' -> type(T_CLOSE_TAG), popMode;
    T_EXIT:                     ([eE][xX][iI][tT]|[dD][iI][eE]) -> type(T_EXIT);
    T_FN:                       [fF][nN] -> type(T_FN);
    T_FUNCTION:                 [fF][uU][nN][cC][tT][iI][oO][nN] -> type(T_FUNCTION);
    T_CONST:                    [cC][oO][nN][sS][tT] -> type(T_CONST);
    T_RETURN:                   [rR][eE][tT][uU][rR][nN] -> type(T_RETURN);
    T_ATTRIBUTE:                '#[' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.square);} -> type(T_ATTRIBUTE);
    T_YIELD_FROM:               [yY][iI][eE][lL][dD] WHITESPACE_OR_COMMENTS [fF][rR][oO][mM] {this.streamLA(1, "[^a-zA-Z0-9_\x80-\xff]")}? -> type(T_YIELD_FROM);
    T_YIELD:                     [yY][iI][eE][lL][dD] -> type(T_YIELD);
    T_TRY:                      [tT][rR][yY] -> type(T_TRY);
    T_CATCH:                    [cC][aA][tT][cC][hH] -> type(T_CATCH);
    T_FINALLY:                  [fF][iI][nN][aA][lL][lL][yY] -> type(T_FINALLY);
    T_THROW:                    [tT][hH][rR][oO][wW] -> type(T_THROW);
    T_IF:                       [iI][fF] -> type(T_IF);
    T_ELSEIF:                   ([eE][lL][sS][eE][iI][fF] | [eE][lL][sS][eE] WHITESPACE_OR_COMMENTS [iI][fF]) -> type(T_ELSEIF);
    T_ENDIF:                    [eE][nN][dD][iI][fF] -> type(T_ENDIF);
    T_ELSE:                     [eE][lL][sS][eE] -> type(T_ELSE);
    T_WHILE:                    [wW][hH][iI][lL][eE] -> type(T_WHILE);
    T_ENDWHILE:                 [eE][nN][dD][wW][hH][iI][lL][eE] -> type(T_ENDWHILE);
    T_DO:                       [dD][oO] -> type(T_DO);
    T_FOR:                      [fF][oO][rR] -> type(T_FOR);
    T_ENDFOR:                   [eE][nN][dD][fF][oO][rR] -> type(T_ENDFOR);
    T_FOREACH:                  [fF][oO][rR][eE][aA][cC][hH] -> type(T_FOREACH);
    T_ENDFOREACH:               [eE][nN][dD][fF][oO][rR][eE][aA][cC][hH] -> type(T_ENDFOREACH);
    T_DECLARE:                  [dD][eE][cC][lL][aA][rR][eE] -> type(T_DECLARE);
    T_ENDDECLARE:               [eE][nN][dD][dD][eE][cC][lL][aA][rR][eE] -> type(T_ENDDECLARE);
    T_INSTANCEOF:               [iI][nN][sS][tT][aA][nN][cC][eE][oO][fF] -> type(T_INSTANCEOF);
    T_AS:                       [aA][sS] -> type(T_AS);
    T_SWITCH:                   [sS][wW][iI][tT][cC][hH] -> type(T_SWITCH);
    T_MATCH:                    [mM][aA][tT][cC][hH] -> type(T_MATCH);
    T_ENDSWITCH:                [eE][nN][dD][sS][wW][iI][tT][cC][hH] -> type(T_ENDSWITCH);
    T_CASE:                     [cC][aA][sS][eE] -> type(T_CASE);
    T_DEFAULT:                  [dD][eE][fF][aA][uU][lL][tT] -> type(T_DEFAULT);
    T_BREAK:                    [bB][rR][eE][aA][kK] -> type(T_BREAK);
    T_CONTINUE:                 [cC][oO][nN][tT][iI][nN][uU][eE] -> type(T_CONTINUE);
    T_GOTO:                     [gG][oO][tT][oO] -> type(T_GOTO);
    T_ECHO:                     [eE][cC][hH][oO] -> type(T_ECHO);
    T_PRINT:                    [pP][rR][iI][nN][tT] -> type(T_PRINT);
    T_DOUBLE_COLON:             '::' -> type(T_DOUBLE_COLON);
    T_CLASS:                    [cC][lL][aA][sS][sS] -> type(T_CLASS);
    T_INTERFACE:                [iI][nN][tT][eE][rR][fF][aA][cC][eE] -> type(T_INTERFACE);
    T_TRAIT:                    [tT][rR][aA][iI][tT] -> type(T_TRAIT);
    /*
     * The enum keyword must be followed by whitespace and another identifier.
     * This avoids the BC break of using enum in classes, namespaces, functions and constants.
     */
    T_ENUM_AS_T_STRING:         [eE][nN][uU][mM] {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(8, "extends\\b.") || this.streamLA(11, "implements\\b.") || !this.streamLA(1, "[a-zA-Z_\x80-\xff]")}? {this.doPreparedLess();} -> type(T_STRING);
    T_ENUM:                     [eE][nN][uU][mM] {this.prepareLess()}? WHITESPACE_OR_COMMENTS* {this.streamLA(1, "[a-zA-Z_\x80-\xff]")}? {this.doPreparedLess();} -> type(T_ENUM);
    T_EXTENDS:                  [eE][xX][tT][eE][nN][dD][sS] -> type(T_EXTENDS);
    T_IMPLEMENTS:               [iI][mM][pP][lL][eE][mM][eE][nN][tT][sS] -> type(T_IMPLEMENTS);
    ST_IN_SCRIPTING_OBJECT_OPERATOR:
                                '->' -> type(T_OBJECT_OPERATOR), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_IN_SCRIPTING_NULLSAFE_OBJECT_OPERATOR:
                                '?->' -> type(T_NULLSAFE_OBJECT_OPERATOR), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_IN_SCRIPTING_WHITESPACE: WHITESPACE+ -> type(T_WHITESPACE), channel(WhiteSpaceChannel);
    T_ELLIPSIS:                 '...' -> type(T_ELLIPSIS);
    T_COALESCE:                 '??' -> type(T_COALESCE);
    T_NEW:                      [nN][eE][wW] -> type(T_NEW);
    T_CLONE:                    [cC][lL][oO][nN][eE] -> type(T_CLONE);
    T_VAR:                      [vV][aA][rR] -> type(T_VAR);
    T_INT_CAST:                 '(' TABS_AND_SPACES ([iI][nN][tT]|[iI][nN][tT][eE][gG][eE][rR]) TABS_AND_SPACES ')' -> type(T_INT_CAST);
    ERROR_REAL_CAST_NOT_ALLOWED:'(' TABS_AND_SPACES [rR][eE][aA][lL] TABS_AND_SPACES ')' -> type(T_ERROR), channel(ErrorLexemChannel);
    T_DOUBLE_CAST:              '(' TABS_AND_SPACES ([dD][oO][uU][bB][lL][eE]|[fF][lL][oO][aA][tT]) TABS_AND_SPACES ')' -> type(T_DOUBLE_CAST);
    T_STRING_CAST:              '(' TABS_AND_SPACES ([sS][tT][rR][iI][nN][gG]|[bB][iI][nN][aA][rR][yY]) TABS_AND_SPACES ')' -> type(T_STRING_CAST);
    T_ARRAY_CAST:               '(' TABS_AND_SPACES [aA][rR][rR][aA][yY] TABS_AND_SPACES ')' -> type(T_ARRAY_CAST);
    T_OBJECT_CAST:              '(' TABS_AND_SPACES [oO][bB][jJ][eE][cC][tT] TABS_AND_SPACES ')' -> type(T_OBJECT_CAST);
    T_BOOL_CAST:                '(' TABS_AND_SPACES ([bB][oO][oO][lL]|[bB][oO][oO][lL][eE][aA][nN]) TABS_AND_SPACES ')' -> type(T_BOOL_CAST);
    T_VOID_CAST:                '(' TABS_AND_SPACES [vV][oO][iI][dD] TABS_AND_SPACES ')' -> type(T_VOID_CAST);
    T_EVAL:                     [eE][vV][aA][lL] -> type(T_EVAL);
    T_INCLUDE:                  [iI][nN][cC][lL][uU][dD][eE] -> type(T_INCLUDE);
    T_INCLUDE_ONCE:             [iI][nN][cC][lL][uU][dD][eE] '_' [oO][nN][cC][eE] -> type(T_INCLUDE_ONCE);
    T_REQUIRE:                  [rR][eE][qQ][uU][iI][rR][eE] -> type(T_REQUIRE);
    T_REQUIRE_ONCE:             [rR][eE][qQ][uU][iI][rR][eE] '_' [oO][nN][cC][eE] -> type(T_REQUIRE_ONCE);
    T_NAMESPACE:                [nN][aA][mM][eE][sS][pP][aA][cC][eE] -> type(T_NAMESPACE);
    T_USE:                      [uU][sS][eE] -> type(T_USE);
    T_INSTEADOF:                [iI][nN][sS][tT][eE][aA][dD][oO][fF] -> type(T_INSTEADOF);
    T_GLOBAL:                   [gG][lL][oO][bB][aA][lL] -> type(T_GLOBAL);
    T_ISSET:                    [iI][sS][sS][eE][tT] -> type(T_ISSET);
    T_EMPTY:                    [eE][mM][pP][tT][yY] -> type(T_EMPTY);
    T_HALT_COMPILER:            '__' [hH][aA][lL][tT] '_'[cC][oO][mM][pP][iI][lL][eE][rR] -> type(T_HALT_COMPILER);
    T_STATIC:                   [sS][tT][aA][tT][iI][cC] -> type(T_STATIC);
    T_ABSTRACT:                 [aA][bB][sS][tT][rR][aA][cC][tT] -> type(T_ABSTRACT);
    T_FINAL:                    [fF][iI][nN][aA][lL] -> type(T_FINAL);
    T_PRIVATE:                  [pP][rR][iI][vV][aA][tT][eE] -> type(T_PRIVATE);
    T_PROTECTED:                [pP][rR][oO][tT][eE][cC][tT][eE][dD] -> type(T_PROTECTED);
    T_PUBLIC_SET:               [pP][uU][bB][lL][iI][cC] '(' [sS][eE][tT] ')' -> type(T_PUBLIC_SET);
    T_PROTECTED_SET:            [pP][rR][oO][tT][eE][cC][tT][eE][dD] '(' [sS][eE][tT] ')' -> type(T_PROTECTED_SET);
    T_PRIVATE_SET:              [pP][rR][iI][vV][aA][tT][eE] '(' [sS][eE][tT] ')' -> type(T_PRIVATE_SET);
    T_PUBLIC:                   [pP][uU][bB][lL][iI][cC] -> type(T_PUBLIC);
    T_READONLY:                 [rR][eE][aA][dD][oO][nN][lL][yY] -> type(T_READONLY);
    T_UNSET:                    [uU][nN][sS][eE][tT] -> type(T_UNSET);
    T_DOUBLE_ARROW:             '=>' -> type(T_DOUBLE_ARROW);
    T_LIST:                     [lL][iI][sS][tT] -> type(T_LIST);
    T_ARRAY:                    [aA][rR][rR][aA][yY] -> type(T_ARRAY);
    T_CALLABLE:                 [cC][aA][lL][lL][aA][bB][lL][eE] -> type(T_CALLABLE);
    T_CONSTRUCT_METHOD options{caseInsensitive=true;}:
                                '__construct' -> type(T_CONSTRUCT_METHOD);
    T_INC:                      '++' -> type(T_INC);
    T_DEC:                      '--' -> type(T_DEC);
    T_IS_IDENTICAL:             '===' -> type(T_IS_IDENTICAL);
    T_IS_NOT_IDENTICAL:         '!==' -> type(T_IS_NOT_IDENTICAL);
    T_IS_EQUAL:                 '==' -> type(T_IS_EQUAL);
    T_IS_NOT_EQUAL:             ('!=' | '<>') -> type(T_IS_NOT_EQUAL);
    T_SPACESHIP:                '<=>' -> type(T_SPACESHIP);
    T_IS_SMALLER_OR_EQUAL:      '<=' -> type(T_IS_SMALLER_OR_EQUAL);
    T_IS_GREATER_OR_EQUAL:      '>=' -> type(T_IS_GREATER_OR_EQUAL);
    T_PLUS_EQUAL:               '+=' -> type(T_PLUS_EQUAL);
    T_MINUS_EQUAL:              '-=' -> type(T_MINUS_EQUAL);
    T_MUL_EQUAL:                '*=' -> type(T_MUL_EQUAL);
    T_POW:                      '**' -> type(T_POW);
    T_POW_EQUAL:                '**=' -> type(T_POW_EQUAL);
    T_DIV_EQUAL:                '/=' -> type(T_DIV_EQUAL);
    T_CONCAT_EQUAL:             '.=' -> type(T_CONCAT_EQUAL);
    T_MOD_EQUAL:                '%=' -> type(T_MOD_EQUAL);
    T_SL_EQUAL:                 '<<=' -> type(T_SL_EQUAL);
    T_SR_EQUAL:                 '>>=' -> type(T_SR_EQUAL);
    T_AND_EQUAL:                '&=' -> type(T_AND_EQUAL);
    T_OR_EQUAL:                 '|=' -> type(T_OR_EQUAL);
    T_XOR_EQUAL:                '^=' -> type(T_XOR_EQUAL);
    T_COALESCE_EQUAL:           '??=' -> type(T_COALESCE_EQUAL);
    T_PIPE:                     '|>' -> type(T_PIPE);
    T_BOOLEAN_OR:               '||' -> type(T_BOOLEAN_OR);
    T_BOOLEAN_AND:              '&&' -> type(T_BOOLEAN_AND);
    T_LOGICAL_OR:               [oO][rR] -> type(T_LOGICAL_OR);
    T_LOGICAL_AND:              [aA][nN][dD] -> type(T_LOGICAL_AND);
    T_LOGICAL_XOR:              [xX][oO][rR] -> type(T_LOGICAL_XOR);
    T_SL:                       '<<' -> type(T_SL);
    // T_SR:                       '>>' -> type(T_SR); // T_SR is decomposed to `T_SYM_GT T_SYM_GT` the prevent issues with nested generics in Tyhp
    T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG:
                                '&' {this.isFollowedByVarOrVarArg()}? -> type(T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG);
    T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG:
                                '&' -> type(T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG);
    ST_IN_SCRIPTING_CLOSE_SQUARE_BRACE:
                                ']' {this.exitNesting(Tyhp.TyhpLang.Enum.BraceType.square);} -> type(T_CLOSE_SQUARE_BRACE);
    ST_IN_SCRIPTING_CLOSE_ROUND_BRACE:
                                ')' {this.exitNesting(Tyhp.TyhpLang.Enum.BraceType.round);} -> type(T_CLOSE_ROUND_BRACE);
    ST_IN_SCRIPTING_OPEN_SQUARE_BRACE:
                                '[' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.square);} -> type(T_OPEN_SQUARE_BRACE);
    ST_IN_SCRIPTING_OPEN_ROUND_BRACE:
                                '(' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.round);} -> type(T_OPEN_ROUND_BRACE);
    ST_IN_SCRIPTING_OPEN_CURLY_BRACE:
                                '{' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly);} -> type(T_OPEN_CURLY_BRACE);
    ST_IN_SCRIPTING_CLOSE_CURLY_BRACE:
                                '}' {this.exitNesting(Tyhp.TyhpLang.Enum.BraceType.curly);} -> type(T_CLOSE_CURLY_BRACE);
    T_BNUMBER:                  BNUM -> type(T_BNUMBER);
    T_ONUMBER:                  ONUM -> type(T_ONUMBER);
    T_LNUMBER:                  LNUM -> type(T_LNUMBER);
    T_HNUMBER:                  HNUM -> type(T_HNUMBER);
    T_DNUMBER:                  (DNUM | EXPONENT_DNUM) -> type(T_DNUMBER);
    T_CLASS_C:                  '__' [cC][lL][aA][sS][sS] '__' -> type(T_CLASS_C);
    T_TRAIT_C:                  '__' [tT][rR][aA][iI][tT] '__' -> type(T_TRAIT_C);
    T_FUNC_C:                   '__' [fF][uU][nN][cC][tT][iI][oO][nN] '__' -> type(T_FUNC_C);
    T_PROPERTY_C:               '__' [pP][rR][oO][pP][eE][rR][tT][yY] '__' -> type(T_PROPERTY_C);
    T_METHOD_C:                 '__' [mM][eE][tT][hH][oO][dD] '__' -> type(T_METHOD_C);
    T_LINE:                     '__' [lL][iI][nN][eE] '__' -> type(T_LINE);
    T_FILE:                     '__' [fF][iI][lL][eE] '__' -> type(T_FILE);
    T_DIR:                      '__' [dD][iI][rR] '__' -> type(T_DIR);
    T_NS_C:                     '__' [nN][aA][mM][eE][sS][pP][aA][cC][eE] '__' -> type(T_NS_C);
    ST_IN_SCRIPTING_VARIABLE:   VARIABLE -> type(T_VARIABLE);
    T_NAME_RELATIVE:            [nN][aA][mM][eE][sS][pP][aA][cC][eE] ('\\' LABEL)+ -> type(T_NAME_RELATIVE);
    T_NAME_QUALIFIED:           LABEL ('\\' LABEL)+ -> type(T_NAME_QUALIFIED);
    T_NAME_FULLY_QUALIFIED:     '\\' LABEL ('\\' LABEL)* -> type(T_NAME_FULLY_QUALIFIED);
    T_NS_SEPARATOR:             '\\' -> type(T_NS_SEPARATOR);
    ST_IN_SCRIPTING_SIMPLE_COMMENT_DOUBLE_SLASH_NEWLINE:
                                '//' ~[\r\n]*? {this.streamLA(1, "[\r\n]") && this.prepareLess()}? NEWLINE {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_SIMPLE_COMMENT_DOUBLE_SLASH_CLOSE_TAG:
                                '//' ~[\r\n]*? {this.streamLAEq("?>") && this.prepareLess()}? '?>' {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_SIMPLE_COMMENT_DOUBLE_SLASH_EOF:
                                '//' ~[\r\n]*? EOF -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_SIMPLE_COMMENT_HASH_NEWLINE:
                                '#' {!this.streamLAEq("[")}? ~[\r\n]*? {this.streamLA(1, "[\r\n]") && this.prepareLess()}? NEWLINE {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_SIMPLE_COMMENT_HASH_CLOSE_TAG:
                                '#' {!this.streamLAEq("[")}? ~[\r\n]*? {this.streamLAEq("?>") && this.prepareLess()}? '?>' {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_SIMPLE_COMMENT_HASH_EOF:
                                '#' {!this.streamLAEq("[")}? ~[\r\n]*? EOF -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_DOC_COMMENT:'/**' WHITESPACE .*? '*/' -> type(T_DOC_COMMENT), channel(DocBlockCommentsChannel);
    ST_IN_SCRIPTING_COMMENT_BLOCK:'/*' .*? '*/' -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_IN_SCRIPTING_BINARY_SINGLE_QUOTE_STRING:
                               'b' '\'' (~('\'' | '\\') | '\\' . )* '\'' -> type(T_BINARY_CONSTANT_ENCAPSED_STRING);
    ST_IN_SCRIPTING_SINGLE_QUOTE_STRING:
                               '\'' (~('\'' | '\\') | '\\' . )* '\'' -> type(T_CONSTANT_ENCAPSED_STRING);
    ST_IN_SCRIPTING_BINARY_DOUBLE_QUOTE_STRING:
                                'b' '"' -> type(T_BINARY_DOUBLE_QUOTE), pushMode(ST_DOUBLE_QUOTES);
    ST_IN_SCRIPTING_BINARY_NOWDOC_STRING:
                                'b' '<<<' TABS_AND_SPACES ['] LABEL ['] NEWLINE {this.startHereDoc(this.Text);} -> type(T_BINARY_START_NOWDOC), pushMode(ST_NOWDOC);
    ST_IN_SCRIPTING_BINARY_HEREDOC_STRING:
                                'b' '<<<' TABS_AND_SPACES (LABEL | (["] LABEL ["])) NEWLINE {this.startHereDoc(this.Text);} -> type(T_BINARY_START_HEREDOC), pushMode(ST_HEREDOC);
    ST_IN_SCRIPTING_BINARY_BACKQUOTE_STRING:
                                'b' '`' -> type(T_BINARY_BACKQUOTE), pushMode(ST_BACKQUOTE);
    ST_IN_SCRIPTING_DOUBLE_QUOTE_STRING:
                                '"' -> type(T_DOUBLE_QUOTE), pushMode(ST_DOUBLE_QUOTES);
    ST_IN_SCRIPTING_BINARY_DOUBLE_QUOTE_STRING_CONSTANT:
                                'b' '"' (~('"' | '$' | '\\') | '\\' . )* '"' -> type(T_CONSTANT_ENCAPSED_STRING);
    ST_IN_SCRIPTING_DOUBLE_QUOTE_STRING_CONSTANT:
                                '"' (~('"' | '$' | '\\') | '\\' . )* '"' -> type(T_CONSTANT_ENCAPSED_STRING);
    ST_IN_SCRIPTING_NOWDOC_STRING:
                                '<<<' TABS_AND_SPACES ['] LABEL ['] NEWLINE {this.startHereDoc(this.Text);} -> type(T_START_HEREDOC), pushMode(ST_NOWDOC);
    ST_IN_SCRIPTING_HEREDOC_STRING:
                                '<<<' TABS_AND_SPACES (LABEL | (["] LABEL ["])) NEWLINE {this.startHereDoc(this.Text);} -> type(T_START_HEREDOC), pushMode(ST_HEREDOC);
    ST_IN_SCRIPTING_BACKQUOTE_STRING:
                                '`' -> type(T_BACKQUOTE), pushMode(ST_BACKQUOTE);
    T_SYM_SEMICOLON:            ';' -> type(T_SYM_SEMICOLON);
    T_SYM_COLON:                ':' -> type(T_SYM_COLON);
    T_SYM_COMMA:                ',' -> type(T_SYM_COMMA);
    T_SYM_PERIOD:               '.' -> type(T_SYM_PERIOD);
    T_SYM_PIPE:                 '|' -> type(T_SYM_PIPE);
    T_SYM_CARET:                '^' -> type(T_SYM_CARET);
    T_SYM_PLUS:                 '+' -> type(T_SYM_PLUS);
    T_SYM_MINUS:                '-' -> type(T_SYM_MINUS);
    T_SYM_SLASH:                '/' -> type(T_SYM_SLASH);
    T_SYM_ASTERISK:             '*' -> type(T_SYM_ASTERISK);
    T_SYM_EQUAL:                '=' -> type(T_SYM_EQUAL);
    T_SYM_PERCENT:              '%' -> type(T_SYM_PERCENT);
    T_SYM_BANG:                 '!' -> type(T_SYM_BANG);
    T_SYM_TILDE:                '~' -> type(T_SYM_TILDE);
    T_SYM_DOLLAR:               '$' -> type(T_SYM_DOLLAR);
    T_SYM_LT:                   '<' -> type(T_SYM_LT);
    T_SYM_GT:                   '>' -> type(T_SYM_GT);
    T_SYM_QUESTION:             '?' -> type(T_SYM_QUESTION);
    T_SYM_AT:                   '@' -> type(T_SYM_AT);
    ST_IN_SCRIPTING_LABEL:      LABEL -> type(T_STRING);
    BAD_CHAR:                   . -> type(T_BAD_CHARACTER);

// mode ST_SET_MODE_IN_SCRIPTING;
//     SET_MODE_IN_SCRIPTING:      . {
//                                     this.less(1, "SET_MODE_IN_SCRIPTING");
//                                     this.setInScriptingMode("ST_IN_SCRIPTING");
//                                 } -> more, pushMode(ST_PUSH_MODE_IN_SCRIPTING);

// mode ST_PUSH_MODE_IN_SCRIPTING;
//     PUSH_MODE_IN_SCRIPTING:  . {this.isInScriptingMode("ST_IN_SCRIPTING")}? {this.less(1, "PUSH_MODE_IN_SCRIPTING");} -> more, pushMode(ST_IN_SCRIPTING);

mode ST_LOOKING_FOR_PROPERTY;
    ST_LOOKING_FOR_PROPERTY_WHITESPACE:
                                WHITESPACE+ -> type(T_WHITESPACE), channel(WhiteSpaceChannel);
    // ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT:
    //                             ('#' | '//') .*? ([\r\n]|'?>'|EOF) -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT_DOUBLE_SLASH_NEWLINE:
                                '//' ~[\r\n]*? {this.streamLA(1, "[\r\n]") && this.prepareLess()}? NEWLINE {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT_DOUBLE_SLASH_CLOSE_TAG:
                                '//' ~[\r\n]*? {this.streamLAEq("?>") && this.prepareLess()}? '?>' {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT_DOUBLE_SLASH_EOF:
                                '//' ~[\r\n]*? EOF -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT_HASH_NEWLINE:
                                '#' {!this.streamLAEq("[")}? ~[\r\n]*? {this.streamLA(1, "[\r\n]") && this.prepareLess()}? NEWLINE {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT_HASH_CLOSE_TAG:
                                '#' {!this.streamLAEq("[")}? ~[\r\n]*? {this.streamLAEq("?>") && this.prepareLess()}? '?>' {this.doPreparedLess();} -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_SIMPLE_COMMENT_HASH_EOF:
                                '#' {!this.streamLAEq("[")}? ~[\r\n]*? EOF -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_DOC_COMMENT:
                                '/**' WHITESPACE .*? '*/' -> type(T_DOC_COMMENT), channel(DocBlockCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_COMMENT_BLOCK:
                                '/*' .*? '*/' -> type(T_COMMENT), channel(SimpleCommentsChannel);
    ST_LOOKING_FOR_PROPERTY_OBJECT_OPERATOR:
                                '->' -> type(T_OBJECT_OPERATOR);
    ST_LOOKING_FOR_PROPERTY_NULLSAFE_OBJECT_OPERATOR:
                                '?->' -> type(T_NULLSAFE_OBJECT_OPERATOR);
    PROPERTY_STRING:            LABEL -> type(T_STRING), popMode;
    ST_LOOKING_FOR_PROPERTY_BRACE:
                                '{' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly, "ST_LOOKING_FOR_PROPERTY", true);} -> type(T_OPEN_CURLY_BRACE), pushMode(ST_IN_SCRIPTING);
    ST_LOOKING_FOR_PROPERTY_VARIABLE:
                                VARIABLE -> type(T_VARIABLE), popMode;
    // Catch-all: incomplete `->` / `?->` (no property name) must popMode so the rest of the file
    // is lexed normally — without this, ANTLR stays in ST_LOOKING_FOR_PROPERTY forever. Unlike
    // ST_VAR_OFFSET_INVALID, this stays on the default channel (mirrors
    // VIRTUAL_ST_CHECK_FOR_OTHER_OPEN_TAGS): `memberName` has no T_ERROR alternative, so hiding the
    // token on ErrorLexemChannel would let the parser silently splice the next real tokens into this
    // member-access expression (e.g. `$x->;\n$y = 1;` reparsing as `$x->$y = 1;` with zero diagnostics).
    // Keeping it visible forces a real, local parser-level syntax error instead.
    ST_LOOKING_FOR_PROPERTY_INVALID:
                                . -> type(T_ERROR), popMode; // only match this one char at a time

mode ST_DOUBLE_QUOTES;
    ST_DOUBLE_QUOTES_DOLLAR_OPEN_CURLY_BRACES:
                                '${' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly, "ST_DOUBLE_QUOTES");} -> type(T_DOLLAR_OPEN_CURLY_BRACES), pushMode(ST_LOOKING_FOR_VARNAME);
    ST_DOUBLE_QUOTES_OBJECT_VARIABLE:
                                VARIABLE {this.streamLA(3, "->[a-zA-Z_\x80-\xff]") }? -> type(T_VARIABLE), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_DOUBLE_QUOTES_OBJECT_VARIABLE_NULLSAFE:
                                VARIABLE {this.streamLA(4, "\\?->[a-zA-Z_\x80-\xff]") }? -> type(T_VARIABLE), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_DOUBLE_QUOTES_DIM_VARIABLE:
                                VARIABLE {this.streamLAEq("[") }? -> type(T_VARIABLE), pushMode(ST_VAR_OFFSET);
    ST_DOUBLE_QUOTES_VARIABLE:  VARIABLE -> type(T_VARIABLE);
    ST_DOUBLE_QUOTES_CURLY_OPEN_DOLLAR:
                                '{' {this.streamLAEq("$")}? {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly, "ST_DOUBLE_QUOTES");} -> type(T_OPEN_CURLY_BRACE), pushMode(ST_IN_SCRIPTING);
    ST_DOUBLE_QUOTES_CURLY_OPEN_TEXT:
                                '{'+ {!this.streamLAEq("$")}? -> type(T_ENCAPSED_AND_WHITESPACE);
    // ST_DOUBLE_QUOTES_WHITESPACE:WHITESPACE -> type(T_ENCAPSED_AND_WHITESPACE);
    ST_DOUBLE_QUOTES_SPECIAL:   [${] -> type(T_ENCAPSED_AND_WHITESPACE); // only match this one char at a time
    ST_DOUBLE_QUOTES_TEXT:      (~[\\"${] | '\\' .)+ -> type(T_ENCAPSED_AND_WHITESPACE);
    // ST_DOUBLE_QUOTES_ESCAPE:    '\\' .  -> type(T_ENCAPSED_AND_WHITESPACE);
    ST_DOUBLE_QUOTES_END:       '"' -> type(T_DOUBLE_QUOTE), popMode;

mode ST_BACKQUOTE;
    ST_BACKQUOTE_DOLLAR_OPEN_CURLY_BRACES:
                                '${' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly);} -> type(T_DOLLAR_OPEN_CURLY_BRACES), pushMode(ST_LOOKING_FOR_VARNAME);
    ST_BACKQUOTE_OBJECT_VARIABLE:
                                VARIABLE {this.streamLA(3, "->[a-zA-Z_\x80-\xff]") }? -> type(T_VARIABLE), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_BACKQUOTE_OBJECT_VARIABLE_NULLSAFE:
                                VARIABLE {this.streamLA(4, "\\?->[a-zA-Z_\x80-\xff]") }? -> type(T_VARIABLE), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_BACKQUOTE_DIM_VARIABLE:
                                VARIABLE {this.streamLAEq("[") }? -> type(T_VARIABLE), pushMode(ST_VAR_OFFSET);
    ST_BACKQUOTE_VARIABLE:      VARIABLE -> type(T_VARIABLE);
    ST_BACKQUOTE_CURLY_OPEN_DOLLAR:
                                '{' {this.streamLAEq("$")}? {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly, "ST_BACKQUOTE");} -> type(T_OPEN_CURLY_BRACE), pushMode(ST_IN_SCRIPTING);
    ST_BACKQUOTE_CURLY_OPEN_TEXT:
                                '{'+ {!this.streamLAEq("$")}? -> type(T_ENCAPSED_AND_WHITESPACE);
    // ST_BACKQUOTE_WHITESPACE:    WHITESPACE -> type(T_ENCAPSED_AND_WHITESPACE);
    ST_BACKQUOTE_TEXT:          (~[\\`{] | '\\' .)+ -> type(T_ENCAPSED_AND_WHITESPACE);
    // ST_BACKQUOTE_ESCAPE:        '\\' .  -> type(T_ENCAPSED_AND_WHITESPACE);
    ST_BACKQUOTE_END:           '`' -> type(T_BACKQUOTE), popMode;

mode ST_HEREDOC;
    // has interpolation
    ST_HEREDOC_END:             NEWLINE TABS_AND_SPACES LABEL {this.checkEndHereDoc(this.Text)}? {this.endHereDoc();} -> type(T_END_HEREDOC), popMode;
    // ST_HEREDOC_EMPTY_LINE:      NEWLINE -> type(T_ENCAPSED_AND_WHITESPACE); // only match this one char at a time
    // ST_HEREDOC_ESCAPE_WITHOUT_NEWLINE:
    //                             '\\' ~[\r\n] -> type(T_ENCAPSED_AND_WHITESPACE); // only match this with esacapr char and one char at a time
    // ST_HEREDOC_ESCAPE_BEFORE_NEWLINE:
    //                             '\\' {this.streamLA(1, "[\r\n]")}?  -> type(T_ENCAPSED_AND_WHITESPACE); // only match this with esacapr char and one char at a time
    ST_HEREDOC_DOLLAR_OPEN_CURLY_BRACES:
                                '${' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly, "ST_HEREDOC");} -> type(T_DOLLAR_OPEN_CURLY_BRACES), pushMode(ST_LOOKING_FOR_VARNAME);
    ST_HEREDOC_OBJECT_VARIABLE:
                                VARIABLE {this.streamLA(3, "->[a-zA-Z_\x80-\xff]") }? -> type(T_VARIABLE), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_HEREDOC_OBJECT_VARIABLE_NULLSAFE:
                                VARIABLE {this.streamLA(4, "\\?->[a-zA-Z_\x80-\xff]") }? -> type(T_VARIABLE), pushMode(ST_LOOKING_FOR_PROPERTY);
    ST_HEREDOC_DIM_VARIABLE:
                                VARIABLE {this.streamLAEq("[") }? -> type(T_VARIABLE), pushMode(ST_VAR_OFFSET);
    ST_HEREDOC_VARIABLE:        VARIABLE -> type(T_VARIABLE);
    ST_HEREDOC_CURLY_OPEN_DOLLAR:
                                '{' {this.streamLAEq("$")}? {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.curly, "ST_HEREDOC");} -> type(T_OPEN_CURLY_BRACE), pushMode(ST_IN_SCRIPTING);
    // ST_HEREDOC_CURLY_OPEN_TEXT:
    //                             '{' {!this.streamLAEq("$")}? -> type(T_ENCAPSED_AND_WHITESPACE);
    ST_HEREDOC_SPECIAL:         ([${] | NEWLINE) -> type(T_ENCAPSED_AND_WHITESPACE); // only match this one char at a time
    ST_HEREDOC_TEXT:            (~[\\\r\n${] | '\\' .)+ -> type(T_ENCAPSED_AND_WHITESPACE);

mode ST_NOWDOC;
    // no interpolation
    ST_NOWDOC_END:              NEWLINE TABS_AND_SPACES LABEL {this.checkEndHereDoc(this.Text)}? {this.endHereDoc();} -> type(T_END_HEREDOC), popMode;
    ST_NOWDOC_EMPTY_LINE:       NEWLINE -> type(T_ENCAPSED_AND_WHITESPACE); // only match this one char at a time
    ST_NOWDOC_TEXT:             ~[\r\n]+ -> type(T_ENCAPSED_AND_WHITESPACE);

mode ST_LOOKING_FOR_VARNAME;
    ST_LOOKING_FOR_VARNAME_STRING_VARNAME:
                                LABEL {this.streamLA(1, "[[}]")}? -> type(T_STRING_VARNAME), mode(ST_IN_SCRIPTING);
    ST_LOOKING_FOR_VARNAME_STRING_VARNAME_END:
                                '}' {this.exitNesting(Tyhp.TyhpLang.Enum.BraceType.curly);} -> type(T_CLOSE_CURLY_BRACE), popMode;
    ST_LOOKING_FOR_VARNAME_OTHER:
                                . {this.less(1, "ST_LOOKING_FOR_VARNAME_OTHER");} -> more, pushMode(ST_IN_SCRIPTING);

mode ST_VAR_OFFSET;
    ST_VAR_OFFSET_OPEN_SQUARE_BRACE:
                                '[' {this.enterNesting(Tyhp.TyhpLang.Enum.BraceType.square);} -> type(T_OPEN_SQUARE_BRACE);
    T_NUM_STRING:               ([0] | ([1-9][0-9]*)) | LNUM | HNUM | BNUM | ONUM;
    ST_VAR_OFFSET_VARIABLE:     VARIABLE -> type(T_VARIABLE);
    ST_VAR_OFFSET_LABEL:        LABEL -> type(T_STRING);
    ST_VAR_OFFSET_CLOSE_SQUARE_BRACE:
                                ']' {this.exitNesting(Tyhp.TyhpLang.Enum.BraceType.square);} -> type(T_CLOSE_SQUARE_BRACE), popMode;
    ST_VAR_OFFSET_INVALID:      (SYM_TOKENS | '[' | '(' | ')' | '{' | '}' | '"' | '`' | [ \n\r\t\\'#]) -> type(T_ERROR), popMode, channel(ErrorLexemChannel); // only match this one char at a time
