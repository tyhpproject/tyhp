/*
Tyhpdef lexer. PHP lexer body plus the tyhpdef keyword rules.
*/

lexer grammar TyhpdefLexer;

options {
    caseInsensitive = false;
}

tokens {
    T_TYHPDEF_OPEN_TAG,
    T_TYHP_EXTENSION,
    T_TYHPDEF_DEPRECATED,
    T_TYHPDEF_OBSOLETE,
    T_TYHPDEF_PARTIAL,
    T_TYHPDEF_EXTERN,
    T_TYHPDEF_OMIT,
    T_TYHP_FALLBACK,
    T_TYHP_ASYNC,
    T_TYHP_TYPE_ALIAS,
    T_TYHP_AWAIT,
    T_TYHP_OPERATOR,
    T_TYHP_VOID,
    T_TYHP_PARENT,
    T_TYHP_IS,
    T_DECIMAL_CAST,

    // Named by shared parser rules. No lexer rule emits these.
    T_TYHP_INTERNAL,
    T_TYHP_WITH,
    T_TYHP_USING,
    T_TYHP_TYPEOF,
    T_TYHP_NAMEOF,
    T_TYHP_VARIABLE_EXISTS,
    T_TYHP_USING_EQUAL
}

// #include "shared/PhpLexer.body.g4"

mode ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON;
// #include "shared/Tyhpdef.lexer-addons.g4#1"

mode ST_IN_SCRIPTING;
// #include "shared/Tyhpdef.lexer-addons.g4#2"
// #include "shared/Tyhpdef.lexer-addons.g4#3"

mode ST_TYHP_TAGLESS;
// #include "shared/Tyhpdef.lexer-addons.g4#4"
