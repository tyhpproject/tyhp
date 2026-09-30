/**
 * Tyhp parser, extends PhpParser.
 *
 * Shared PHP syntax comes from the imported PhpParser (php-src PHP-8.5.x
 * lineage; see PhpParser.g4 / PhpLexer.g4 headers). Tyhp-only rules and
 * *GrammarAddon overrides live in this file.
 */

parser grammar TyhpParser;

options {
    tokenVocab=TyhpLexer;
}

@header {
#pragma warning disable CS3021
}

import PhpParser;

//#region Tyhp Root

// #include "shared/Tyhpdef.rules.g4#1"

tyhpSrcFile
    : (startingInlineOutput+=tyhpInlineOutput)*
        (firstCodeBlock=tyhpCodeBlock
        (T_CLOSE_TAG codeBlocks+=tyhpCodeBlock)* T_CLOSE_TAG?)?
        (endingInlineOutput+=tyhpInlineOutput)* EOF                             #tyhpFile
    ;

// Tagless entry points (Story 06, Phase 7). Used instead of tyhpSrcFile /
// tyhpdefSrcFile when `source.tagless` is enabled. The open tag is optional and
// there is no inline output / closing tag, so a `?>` cannot appear (it is not
// in this rule's follow set). The leading action sets the parser language mode
// so that semantic predicates behave identically whether or not a literal open
// tag is present.
tyhpTaglessSrcFile
locals [_languageMode:string = ""]
    : {
        this._languageMode = "tyhp";
        _localctx._languageMode = this._languageMode;
      }
        T_TYHP_OPEN_TAG?
        StatementList=topStatementListWithRequiredFinalTerminal?
        EOF                                                                     #tyhpTaglessFile
    ;

// #include "shared/Tyhpdef.rules.g4#2"

tyhpCodeBlock
    : TyhpBlock=tyhpBlock                                                       #tyhpCodeBlockTyhpBlock
    ;

// #include "shared/Tyhpdef.rules.g4#3"

tyhpBlock
locals [_languageMode:string = ""]
    : T_TYHP_OPEN_TAG
        {
            this._languageMode = "tyhp";
            _localctx._languageMode = this._languageMode;
        }
        StatementList=topStatementListWithRequiredFinalTerminal?
    ;

tyhpInlineOutput
    // all items here are the same as echoing out the content
    : InlineHtml=T_INLINE_HTML
    | PhpEchoBlock=phpEchoBlock {this.isLanguageMode("php")}?
    | PhpBlock=phpBlock (T_CLOSE_TAG | T_SYM_SEMICOLON)+
    ;

tyhpInlineOutputStatement
    : T_CLOSE_TAG InlineOutput+=tyhpInlineOutput+ T_TYHP_OPEN_TAG
    | T_INLINE_HTML
    ;

//#endregion Tyhp Root

// #include "shared/Tyhpdef.rules.g4#4"

tyhpdefClassConstDecl
locals [ _findDocComment:IToken = null ]
@after { _localctx._findDocComment = _localctx.Stop; }
    : Identifier=identifier (T_COALESCE CoalesceExpr=expr)?
    ;

tyhpdefClassConstList
    : Items+=tyhpdefClassConstDecl (T_SYM_COMMA Items+=tyhpdefClassConstDecl)*
    ;

// #include "shared/Tyhpdef.rules.g4#5"
// #include "shared/Tyhp.reached.g4#1"
// #include "shared/TyhpOverrides.reached.g4#1"

tyhpWithList
    : T_OPEN_CURLY_BRACE ArrayPairList=arrayPairList T_CLOSE_CURLY_BRACE
    ;
// #include "shared/TyhpOverrides.reached.g4#2"
// #include "shared/Tyhp.reached.g4#2"
// #include "shared/TyhpOverrides.reached.g4#3"
// #include "shared/Tyhp.reached.g4#3"
// #include "shared/TyhpOverrides.reached.g4#4"

//#endregion Tyhp Dereferencables

//#region Tyhp Top Statements

// ! OVERRIDE
topStatementGrammarAddon
    : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations {this.isLanguageMode("tyhp")}?             #tyhpImportExtension
    | T_GLOBAL T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations {this.isLanguageMode("tyhp")}?             #tyhpGlobalImportExtension
    | T_GLOBAL T_USE UseDecl=mixedGroupUseDeclaration T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpGlobalImportGroupDecls
    | T_GLOBAL T_USE UseType=useType UseDecl=groupUseDeclaration T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpGlobalImportTypedGroupDecls
    | T_GLOBAL T_USE UseDecl=useDeclarations T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpGlobalImportDecls
    | T_GLOBAL T_USE UseType=useType UseDecl=useDeclarations T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpGlobalImportType
    | Attributes=attributes? IsInternal=T_TYHP_INTERNAL? Statement=tyhpTypeAlias
        {this.isLanguageMode("tyhp")}?                                          #tyhpTypeAliasDecl
    | Attributes=attributes? Statement=tyhpExtensionDeclarationStatement
        {this.isLanguageMode("tyhp")}?                                          #tyhpExtensionDecl
    | IsInternal=T_TYHP_INTERNAL T_CONST ConstList=constList T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalConstDecl
    | Attributes=attributes? IsFallback=T_TYHP_FALLBACK
        Statement=attributedStatement {this.isLanguageMode("tyhp")}?            #tyhpFallbackDecl
    | IsFallback=T_TYHP_FALLBACK T_CONST ConstList=constList T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpFallbackConstDecl
    ;
// #include "shared/TyhpOverrides.reached.g4#5"
// #include "shared/Tyhp.reached.g4#4"
// #include "shared/TyhpOverrides.reached.g4#6"
// #include "shared/Tyhp.reached.g4#5"
// #include "shared/TyhpOverrides.reached.g4#7"
// #include "shared/Tyhp.reached.g4#6"
// #include "shared/TyhpOverrides.reached.g4#8"
// #include "shared/Tyhp.reached.g4#7"
// #include "shared/TyhpOverrides.reached.g4#9"
// #include "shared/Tyhp.reached.g4#8"
// #include "shared/TyhpOverrides.reached.g4#10"
// #include "shared/Tyhp.reached.g4#9"
// #include "shared/TyhpOverrides.reached.g4#11"
// #include "shared/Tyhp.reached.g4#10"
// #include "shared/TyhpOverrides.reached.g4#12"
// #include "shared/Tyhp.reached.g4#11"
// #include "shared/TyhpOverrides.reached.g4#13"
