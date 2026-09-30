/**
 * Php parser, based off of PHP grammar located at:
 * https://github.com/php/php-src/blob/PHP-8.5.9/Zend/zend_language_parser.y
 *
 * Lineage: php-src PHP-8.5.x (highest supported PHP minor for Tyhp).
 * This has been heavily modified from the original PHP grammar to convert the
 * original LALR grammar to LL grammar.
 *
 * https://php.watch/versions
 */

parser grammar PhpParser;

options {
    tokenVocab=PhpLexer;
}

@header {
#pragma warning disable CS3021
}

//#region Root

noGrammarAddon
    : T_NO_GRAMMAR_ADDON_0000
    ;

phpSrcFile
    : (startingInlineOutput+=phpInlineOutput)* (firstCodeBlock=codeBlock
        (T_CLOSE_TAG codeBlocks+=codeBlock)* T_CLOSE_TAG?)?
        (endingInlineOutput+=phpInlineOutput)* EOF
    ;

codeBlock
    : PhpBlock=phpBlock                                                         #codeBlockPhpBlock
    | codeBlockGrammarAddon {this._languageMode = "";}                          #codeBlockGrammarAddonHandler
    | TokenValue=T_ERROR                                                        #codeBlockError
    ;

codeBlockGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

phpBlock
locals [_languageMode:string = ""]
    : T_OPEN_TAG
        {
            this._languageMode = "php";
            _localctx._languageMode = this._languageMode;
        }
        // StatementList=topStatementListWithOptionalFinalTerminal?
        StatementList=topStatementListWithRequiredFinalTerminal?
    ;
// #include "shared/PhpParser.reached.g4#1"

// PHP 8.5: attributes on compile-time non-class `const` (php-src
// attributed_top_statement includes T_CONST). Attributes require a single
// declarator — multi-const lists are illegal when attributed (php-src compile
// error: "Cannot apply attributes to multiple constants")
attributedConstStatement
    : T_CONST ConstDecl=constDecl T_SYM_SEMICOLON
    ;

//#endregion Attributes

//#region Top Statements

topStatementListWithRequiredFinalTerminal
    : Items+=topStatement+
    ;

topStatementNoTerminal
    : Statement=statementWithoutTerminal                                        #topStatementStatementWithoutTerminal
    // Attributed top-level const must precede the general attributedStatement
    // alternative so `#[…] const X = …;` is not attempted as class/function/etc
    | Attributes=attributes Statement=attributedConstStatement                  #attributedConstTopStatement
    | Attributes=attributes? Statement=attributedStatement                      #attributedTopStatement
    | T_NAMESPACE NamespaceName=namespaceDeclarationName T_SYM_SEMICOLON        #nameSpaceDecl
    | T_NAMESPACE NamespaceName=namespaceDeclarationName T_OPEN_CURLY_BRACE
        StatementList=topStatementListWithRequiredFinalTerminal?
        T_CLOSE_CURLY_BRACE                                                     #namespaceGroupDecl
    | T_NAMESPACE T_OPEN_CURLY_BRACE
        StatementList=topStatementListWithRequiredFinalTerminal?
        T_CLOSE_CURLY_BRACE                                                     #anonNamespaceDecl
    | T_USE UseDecl=mixedGroupUseDeclaration T_SYM_SEMICOLON                    #importGroupDecls
    | T_USE UseType=useType UseDecl=groupUseDeclaration T_SYM_SEMICOLON         #importTypeGroupDecls
    | T_USE UseDecl=useDeclarations T_SYM_SEMICOLON                             #importDecls
    | T_USE UseType=useType UseDecl=useDeclarations T_SYM_SEMICOLON             #importType
    | T_CONST ConstList=constList T_SYM_SEMICOLON                               #constDeclStmt
    | topStatementGrammarAddon                                                  #topStatementGrammarAddonHandler
    ;

topStatementNeedsTerminal
    : Statement=statementRequiringTerminal                                      #topStatementRequiringTerminal
    | T_HALT_COMPILER T_OPEN_ROUND_BRACE T_CLOSE_ROUND_BRACE                    #topStatementHaltCompiler
    ;

topStatement
    : topStatementNoTerminal
    | topStatementNeedsTerminal statementTerminal
    ;

topStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;
// #include "shared/PhpParser.reached.g4#2"
