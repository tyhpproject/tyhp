#!/bin/bash

cd "$(dirname "$0")"

GREEN="\033[32m"
RED="\033[31m"
RESET="\033[0m"

GRAMMAR_DIR=./Tyhp/TyhpLang/Grammar
PARSER_DIR=./Tyhp/TyhpLang/Parser

echo "checking for antlr-ng command..."
if command -v antlr-ng &> /dev/null; then
    echo -e "${GREEN}**  antlr-ng command found${RESET}"
else
    echo -e "${RED}**  antlr-ng command not found, install it with: npm install -g antlr-ng${RESET}"
    exit 1
fi

# Compile lexer first so the token vocabulary in --lib is up-to-date
# before the parser reads it via tokenVocab=TyhpLexer
antlr-ng --define language=CSharp \
    --output-directory "$PARSER_DIR" \
    --package Tyhp.TyhpLang.Parser \
    --generate-visitor true \
    --generate-listener false \
    --long-messages true \
    --lib "$GRAMMAR_DIR" \
    "$GRAMMAR_DIR/TyhpLexer.g4"
mv "$PARSER_DIR"/*.tokens "$GRAMMAR_DIR" 2>/dev/null
mv "$PARSER_DIR"/*.interp "$GRAMMAR_DIR" 2>/dev/null

# Now compile both lexer and parser — the parser will see the updated token vocabulary
antlr-ng --define language=CSharp \
    --output-directory "$PARSER_DIR" \
    --package Tyhp.TyhpLang.Parser \
    --generate-visitor true \
    --generate-listener false \
    --long-messages true \
    --lib "$GRAMMAR_DIR" \
    "$GRAMMAR_DIR/TyhpLexer.g4" "$GRAMMAR_DIR/TyhpParser.g4"
mv "$PARSER_DIR"/*.tokens "$GRAMMAR_DIR"
mv "$PARSER_DIR"/*.interp "$GRAMMAR_DIR"

# antlr4 -Dlanguage=CSharp "$GRAMMAR_DIR/TyhpLexer.g4" "$GRAMMAR_DIR/TyhpParser.g4" -o "$PARSER_DIR" -package Tyhp.TyhpLang.Parser -visitor -no-listener -long-messages
