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

// Chunks stay in source order: inline HTML, `<?=` echoes, `<?tyhp` blocks, and
// the `?>` that separates them. A `<?php` block is its own chunk so the visitor
// can reject it without treating the PHP as Tyhp.
tyhpSrcFile
    : chunks+=tyhpFileChunk* EOF                                                #tyhpFile
    ;

tyhpFileChunk
    : Output=tyhpInlineOutput                                                   #tyhpFileChunkInline
    | Rejected=tyhpRejectedPhpBlock                                             #tyhpFileChunkPhp
    | Code=tyhpCodeBlock                                                        #tyhpFileChunkCode
    | T_CLOSE_TAG                                                               #tyhpFileChunkClose
    ;

// A `<?php … ?>` block in a `.tyhp` file. The interior is not a Tyhp statement
// list, so it is not type-checked or emitted. Non-greedy so the block ends at
// the next `?>`.
tyhpRejectedPhpBlock
    : T_OPEN_TAG .*? T_CLOSE_TAG
    ;

// Tagless `.tyhp` entry. Used instead of tyhpSrcFile when `source.tagless` is
// enabled, except for a file whose first six characters are `<?tyhp`, which
// uses tyhpSrcFile. This rule has no open tag and no inline output / closing
// tag, so a `?>` cannot appear (it is not in this rule's follow set). The
// leading action sets the parser language mode.
tyhpTaglessSrcFile
locals [_languageMode:string = ""]
    : {
        this._languageMode = "tyhp";
        _localctx._languageMode = this._languageMode;
      }
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
    // HTML, or `<?= … ?>` parsed as an echo. phpEchoBlock sets the parser mode to
    // "php" before the predicate runs, so the echo is accepted in a `.tyhp` file.
    : InlineHtml=T_INLINE_HTML
    | PhpEchoBlock=phpEchoBlock {this.isLanguageMode("php")}?
    ;

// Inside a statement (function body, control-flow block). `.php` keeps the
// imported rule's `?> … <?php` form via the override below. `.tyhp` reopens
// with `<?tyhp`. A `<?php` block is parsed only so it can be rejected.
tyhpInlineOutputStatement
    : T_CLOSE_TAG InlineOutput+=tyhpInlineOutput* (
          T_TYHP_OPEN_TAG
        | Rejected=tyhpRejectedPhpBlock After+=tyhpInlineOutput* T_TYHP_OPEN_TAG?
      )
        { this._languageMode = "tyhp"; }
    ;

// ! OVERRIDE
// A `.tyhp` file reopens with `<?tyhp`. A `<?php` block there is rejected.
// A `.php` file keeps `?> … <?php`.
phpInlineOutputStatement
    : {this.isLanguageMode("tyhp")}? Reopen=tyhpInlineOutputStatement
    | T_CLOSE_TAG InlineOutput+=phpInlineOutput+ T_OPEN_TAG
    | T_INLINE_HTML
    | phpInlineOutputStatementGrammarAddon
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
