/**
 * Tyhpdef parser. Closure of tyhpdefSrcFile / tyhpdefTaglessSrcFile only.
 */

parser grammar TyhpdefParser;

options {
    tokenVocab=TyhpdefLexer;
}

@header {
#pragma warning disable CS3021
}

// #include "shared/Tyhpdef.rules.g4"
// #include "shared/Tyhp.reached.g4"
// #include "shared/TyhpOverrides.reached.g4"
// #include "shared/PhpParser.reached.g4"
