namespace Tyhp.TyhpLang.Parser {
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using Antlr4.Runtime;
    public partial class TyhpParser : Parser {
        protected string _languageMode = "";

        public long LanguageModeTotalTime = 0L;
        public long LanguageModeTotalCalls = 0L;

        public bool isLanguageMode(string mode) {
            return _languageMode == mode;
        }

        /// <summary>
        /// True when <paramref name="token"/> is the contextual <c>hide</c> keyword used only
        /// inside <c>use extension { … }</c> adaptation blocks. Not a lexer keyword, so
        /// <c>function hide()</c> elsewhere stays an ordinary identifier.
        /// </summary>
        public bool isHideKeyword(IToken token) {
            return token != null
                && string.Equals(token.Text, "hide", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="token"/> is the variable <c>$this</c>.
        /// Extension receiver annotations and the legacy member-target recovery
        /// both require that spelling.
        /// </summary>
        public bool isThisVariable(IToken token) {
            return token != null
                && string.Equals(token.Text, "$this", StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the next tokens are <c>&amp;$this</c>, the extension
        /// by-ref receiver annotation. Does not consume input.
        /// </summary>
        public bool extensionReceiverAhead() {
            return TokenStream.LA(1) == TyhpParser.T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG
                && TokenStream.LA(2) == TyhpParser.T_VARIABLE
                && isThisVariable(TokenStream.LT(2));
        }

        /// <summary>
        /// True when the next token is <c>extends</c>, the old per-member
        /// <c>extends Type $this</c> receiver. Does not consume input.
        /// </summary>
        public bool extensionLegacyReceiverAhead() {
            return TokenStream.LA(1) == TyhpParser.T_EXTENDS;
        }

        /// <summary>
        /// True when <paramref name="token"/> is the PHP <c>object</c> type spelling. Not a
        /// dedicated lexer token — <c>object $x</c> and <c>class object { }</c> stay
        /// <c>T_STRING</c>.
        /// </summary>
        public bool isObjectKeyword(IToken token) {
            return token != null
                && string.Equals(token.Text, "object", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="token"/> is the contextual <c>struct</c> spelling.
        /// Not a dedicated lexer token — <c>struct { … }</c> and
        /// <c>class struct { }</c> stay <c>T_STRING</c>.
        /// </summary>
        public bool isStructKeyword(IToken token) {
            return token != null
                && string.Equals(token.Text, "struct", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when lookahead is <c>callable (</c> or <c>callable</c> followed by a
        /// PHP builtin-cast token (<c>(int)</c> / <c>(string)</c> / <c>(void)</c> / …), the
        /// start of a callable shape rather than bare <c>callable</c>.
        /// </summary>
        public bool callableIsFollowedByOpenParen() {
            if (TokenStream.LA(1) != TyhpParser.T_CALLABLE) {
                return false;
            }

            int la2 = TokenStream.LA(2);
            return la2 == TyhpParser.T_OPEN_ROUND_BRACE
                || IsBuiltinCastToken(la2);
        }

        /// <summary>
        /// PHP lexes <c>(int)</c> / <c>(string)</c> / … as one token. Those tokens
        /// are callable-shape parameter lists when they immediately follow
        /// <c>callable</c> (see <c>callableType</c> BuiltinCast). <c>(void)</c> is
        /// included here (unlike the shared <c>typeof</c>/<c>default</c> BuiltinCast
        /// alternatives) because an unnamed <c>void</c> parameter is syntactically
        /// valid the same way a named <c>void $v</c> parameter already is; the checker
        /// still owns whether <c>void</c> is a legal parameter type.
        /// </summary>
        public static bool IsBuiltinCastToken(int tokenType) {
            switch (tokenType) {
                case TyhpParser.T_INT_CAST:
                case TyhpParser.T_STRING_CAST:
                case TyhpParser.T_DOUBLE_CAST:
                case TyhpParser.T_BOOL_CAST:
                case TyhpParser.T_ARRAY_CAST:
                case TyhpParser.T_OBJECT_CAST:
                case TyhpParser.T_DECIMAL_CAST:
                case TyhpParser.T_VOID_CAST:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when a callable-shape parameter's <c>=</c> is followed by a default
        /// expression rather than the end of the parameter (<c>)</c> / <c>,</c>).
        /// </summary>
        public bool callableShapeDefaultIsPresent() {
            int la = TokenStream.LA(1);
            return la != TyhpParser.T_CLOSE_ROUND_BRACE
                && la != TyhpParser.T_SYM_COMMA
                && la != TokenConstants.EOF;
        }

        /// <summary>
        /// True when lookahead is a type-position struct shape (<c>struct { … }</c> or
        /// <c>struct extends Name { … }</c>), not a named <c>struct Point { }</c>
        /// declaration (which is no longer a production) and not a class/type
        /// named <c>struct</c>. Also gates the first <c>typeWithoutStatic</c>
        /// alternative so SLL does not take <c>struct</c> as <c>Identifier=name</c>.
        /// </summary>
        public bool looksLikeStructShape() {
            return TokenStream.LA(1) == TyhpParser.T_STRING
                && isStructKeyword(TokenStream.LT(1))
                && (TokenStream.LA(2) == TyhpParser.T_OPEN_CURLY_BRACE
                    || TokenStream.LA(2) == TyhpParser.T_EXTENDS);
        }

        /// <summary>
        /// True when lookahead is <c>new struct { … }</c> (optional empty
        /// <c>()</c>, optional <c>extends</c>), not <c>new struct()</c> /
        /// <c>new struct;</c> constructing a class named <c>struct</c>.
        /// </summary>
        public bool looksLikeAnonymousStruct() {
            if (!isLanguageMode("tyhp")
                || TokenStream.LA(1) != TyhpParser.T_NEW
                || TokenStream.LA(2) != TyhpParser.T_STRING
                || !isStructKeyword(TokenStream.LT(2))) {
                return false;
            }

            int index = 3;
            if (TokenStream.LA(index) == TyhpParser.T_EXTENDS) {
                index++;
                if (!IsClassNameHeadToken(TokenStream.LA(index))
                    && !IsTypeNameHeadToken(TokenStream.LA(index))) {
                    return false;
                }

                index++;
                if (TokenStream.LA(index) == TyhpParser.T_SYM_LT
                    && !TrySkipGenericArgumentList(ref index)) {
                    return false;
                }
            }

            if (TokenStream.LA(index) == TyhpParser.T_OPEN_ROUND_BRACE) {
                if (TokenStream.LA(index + 1) != TyhpParser.T_CLOSE_ROUND_BRACE) {
                    return false;
                }

                index += 2;
            }

            return TokenStream.LA(index) == TyhpParser.T_OPEN_CURLY_BRACE;
        }

        /// <summary>
        /// True when lookahead is an object-shape alias RHS (<c>object { … }</c>), not a
        /// bare <c>object</c> type or <c>object extends</c>.
        /// </summary>
        public bool looksLikeObjectShape() {
            return TokenStream.LA(1) == T_STRING
                && TokenStream.LA(2) == T_OPEN_CURLY_BRACE
                && isObjectKeyword(TokenStream.LT(1));
        }

        /// <summary>
        /// True when lookahead is a type-alias RHS intersection with at least one
        /// object-shape item, for example <c>LoggerInterface &amp; object { … }</c> or
        /// <c>object { … } &amp; LoggerInterface</c>.
        /// </summary>
        /// <remarks>
        /// Plain nominal intersections (no <c>object { … }</c> item, e.g. <c>A &amp; B</c>)
        /// return false here so they keep parsing through <c>typeExpr</c> /
        /// <c>intersectionType</c> unchanged — this predicate only carves out the shape
        /// case, it does not take over alias-RHS intersections in general.
        /// </remarks>
        public bool looksLikeObjectShapeIntersection() {
            int index = 1;
            bool sawObjectShape = false;
            int itemCount = 0;

            while (true) {
                if (TokenStream.LA(index) == T_STRING
                    && TokenStream.LA(index + 1) == T_OPEN_CURLY_BRACE
                    && isObjectKeyword(TokenStream.LT(index))) {
                    sawObjectShape = true;
                    index++;
                    if (!TrySkipBalancedCurlyBraces(ref index)) {
                        return false;
                    }
                } else if (TokenStream.LA(index) == TyhpParser.T_STATIC
                    || IsTypeNameHeadToken(TokenStream.LA(index))) {
                    index++;
                    if (TokenStream.LA(index) == TyhpParser.T_SYM_LT
                        && !TrySkipGenericArgumentList(ref index)) {
                        return false;
                    }
                } else {
                    return false;
                }

                itemCount++;
                if (TokenStream.LA(index)
                    != TyhpParser.T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG) {
                    break;
                }

                index++;
            }

            return sawObjectShape && itemCount >= 2;
        }

        /// <summary>
        /// Advances <paramref name="index"/> from a <c>T_OPEN_CURLY_BRACE</c> lookahead
        /// position past its matching <c>T_CLOSE_CURLY_BRACE</c>, tracking nested braces.
        /// Used to skip an <c>object { … }</c> shape body during lookahead without
        /// re-parsing its contents.
        /// </summary>
        private bool TrySkipBalancedCurlyBraces(ref int index) {
            if (TokenStream.LA(index) != TyhpParser.T_OPEN_CURLY_BRACE) {
                return false;
            }

            int depth = 0;
            while (true) {
                int tokenType = TokenStream.LA(index);
                if (tokenType == TokenConstants.EOF) {
                    return false;
                }

                if (tokenType == TyhpParser.T_OPEN_CURLY_BRACE) {
                    depth++;
                } else if (tokenType == TyhpParser.T_CLOSE_CURLY_BRACE) {
                    depth--;
                }

                index++;
                if (depth == 0) {
                    return true;
                }
            }
        }

        /// <summary>
        /// Reports whether the <c>new</c> expression starting at the current token is followed
        /// by an argument list, for example <c>new Foo(1)</c> or <c>new Box&lt;int&gt;(1)</c>.
        /// </summary>
        /// <remarks>
        /// <c>new X&lt;T&gt;(args)</c> is ambiguous with the comparison chain
        /// <c>(new X) &lt; T &gt; (args)</c>, because the argument-less
        /// <c>newNonDereferenceable</c> alternative can consume the generic argument list on
        /// its own. This lookahead lets that alternative be ruled out before prediction
        /// commits to it. Only the unambiguous <c>new NAME [&lt;...&gt;] (</c> shape is
        /// recognized; every other shape returns false, leaving prediction as it was.
        /// </remarks>
        public bool newIsFollowedByArgumentList() {
            if (TokenStream.LA(1) != TyhpParser.T_NEW
                || !IsClassNameHeadToken(TokenStream.LA(2))) {
                return false;
            }

            int index = 3;
            if (TokenStream.LA(index) == TyhpParser.T_SYM_LT) {
                int depth = 0;
                int remaining = MaxGenericArgumentLookahead;
                while (remaining-- > 0) {
                    int tokenType = TokenStream.LA(index);
                    if (tokenType == TokenConstants.EOF) {
                        return false;
                    }

                    if (tokenType == TyhpParser.T_SYM_LT) {
                        depth++;
                    } else if (tokenType == TyhpParser.T_SYM_GT) {
                        depth--;
                    } else if (CannotAppearInGenericArguments(tokenType)) {
                        return false;
                    }

                    index++;
                    if (depth == 0) {
                        break;
                    }
                }

                if (depth != 0) {
                    return false;
                }
            }

            return TokenStream.LA(index) == TyhpParser.T_OPEN_ROUND_BRACE;
        }

        /// <summary>
        /// Reports whether the tokens at the current position look like a generic typed-local
        /// declaration such as <c>Box&lt;int&gt; $x = ...</c>, <c>(Box&lt;T&gt;) $x</c>, or a
        /// union type with a generic member such as <c>int|Box&lt;T&gt; $x</c>.
        /// </summary>
        /// <remarks>
        /// <c>Type&lt;Arg&gt; $var</c> is ambiguous with the comparison chain
        /// <c>(Type &lt; Arg) &gt; $var</c>. Statement prediction prefers <c>phpTopExpr</c> over the
        /// typed-local addon, so without this lookahead those declarations parse as comparisons and
        /// emit invalid PHP. Real comparison chains start with a variable (<c>$a &lt; $b &gt; $c</c>)
        /// and therefore return false here.
        /// </remarks>
        public bool looksLikeGenericTypedLocal() {
            int index = 1;

            // Optional parenthesized form: (Type<Arg>) $var
            bool parenthesized = false;
            if (TokenStream.LA(index) == TyhpParser.T_OPEN_ROUND_BRACE) {
                parenthesized = true;
                index++;
            }

            // Optional nullable marker on the type.
            if (TokenStream.LA(index) == TyhpParser.T_SYM_QUESTION) {
                index++;
            }

            // Walk a `Type1|Type2|...` union so a generic argument list on any member (not just
            // the first, e.g. `int|Box<T> $x`) is recognized. The ambiguity only exists when at
            // least one member has a generic argument list; plain unions like `int|string $x`
            // already parse unambiguously without this predicate.
            bool sawGenericArguments = false;
            while (true) {
                if (!IsTypeNameHeadToken(TokenStream.LA(index))) {
                    return false;
                }
                index++;

                if (TokenStream.LA(index) == TyhpParser.T_SYM_LT) {
                    sawGenericArguments = true;
                    if (!TrySkipGenericArgumentList(ref index)) {
                        return false;
                    }
                }

                if (TokenStream.LA(index) != TyhpParser.T_SYM_PIPE) {
                    break;
                }
                index++;
            }

            if (!sawGenericArguments) {
                return false;
            }

            if (parenthesized) {
                if (TokenStream.LA(index) != TyhpParser.T_CLOSE_ROUND_BRACE) {
                    return false;
                }
                index++;
            }

            return TokenStream.LA(index) == TyhpParser.T_VARIABLE;
        }

        /// <summary>
        /// True when lookahead is a typed file-level const declarator
        /// (<c>int X = 1</c>, <c>array&lt;int&gt; XS = []</c>), not <c>NAME = expr</c>.
        /// </summary>
        public bool looksLikeTypedConstDecl() {
            int index = 1;
            if (!TrySkipTypeExprWithoutStatic(ref index)) {
                return false;
            }

            if (TokenStream.LA(index) != TyhpParser.T_STRING) {
                return false;
            }

            index++;
            return TokenStream.LA(index) == TyhpParser.T_SYM_EQUAL;
        }

        private bool TrySkipTypeExprWithoutStatic(ref int index) {
            int start = index;
            if (!TrySkipTypeExprCore(ref index)) {
                index = start;
                return false;
            }

            return index > start;
        }

        /// <summary>
        /// Skips a <c>typeExpr</c>: optional <c>?</c>, then either a callable shape
        /// (<c>callable(…): R</c>, return included) or a union/intersection/atom.
        /// </summary>
        private bool TrySkipTypeExprCore(ref int index) {
            if (TokenStream.LA(index) == TyhpParser.T_SYM_QUESTION) {
                index++;
            }

            if (TokenStream.LA(index) == TyhpParser.T_CALLABLE
                && (TokenStream.LA(index + 1) == TyhpParser.T_OPEN_ROUND_BRACE
                    || IsBuiltinCastToken(TokenStream.LA(index + 1)))) {
                return TrySkipCallableShape(ref index);
            }

            return TrySkipUnionOrIntersectionOrType(ref index);
        }

        private bool TrySkipCallableShape(ref int index) {
            if (TokenStream.LA(index) != TyhpParser.T_CALLABLE) {
                return false;
            }

            index++;
            if (IsBuiltinCastToken(TokenStream.LA(index))) {
                index++;
            } else if (!TrySkipBalancedRoundBraces(ref index)) {
                return false;
            }

            if (TokenStream.LA(index) != TyhpParser.T_SYM_COLON) {
                return false;
            }

            index++;
            return TrySkipTypeExprCore(ref index);
        }

        private bool TrySkipStructShape(ref int index) {
            if (TokenStream.LA(index) != TyhpParser.T_STRING
                || !isStructKeyword(TokenStream.LT(index))) {
                return false;
            }

            int next = TokenStream.LA(index + 1);
            if (next != TyhpParser.T_OPEN_CURLY_BRACE
                && next != TyhpParser.T_EXTENDS) {
                return false;
            }

            index++;
            if (TokenStream.LA(index) == TyhpParser.T_EXTENDS) {
                index++;
                if (!IsClassNameHeadToken(TokenStream.LA(index))
                    && !IsTypeNameHeadToken(TokenStream.LA(index))) {
                    return false;
                }

                index++;
                if (TokenStream.LA(index) == TyhpParser.T_SYM_LT
                    && !TrySkipGenericArgumentList(ref index)) {
                    return false;
                }
            }

            return TrySkipBalancedCurlyBraces(ref index);
        }

        private bool TrySkipBalancedRoundBraces(ref int index) {
            if (TokenStream.LA(index) != TyhpParser.T_OPEN_ROUND_BRACE) {
                return false;
            }

            int depth = 0;
            while (true) {
                int tokenType = TokenStream.LA(index);
                if (tokenType == TokenConstants.EOF) {
                    return false;
                }

                if (tokenType == TyhpParser.T_OPEN_ROUND_BRACE) {
                    depth++;
                } else if (tokenType == TyhpParser.T_CLOSE_ROUND_BRACE) {
                    depth--;
                }

                index++;
                if (depth == 0) {
                    return true;
                }
            }
        }

        private bool TrySkipUnionOrIntersectionOrType(ref int index) {
            if (!TrySkipTypeAtom(ref index)) {
                return false;
            }

            while (true) {
                int op = TokenStream.LA(index);
                if (op != TyhpParser.T_SYM_PIPE
                    && op != TyhpParser.T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG) {
                    break;
                }

                index++;
                if (!TrySkipTypeAtom(ref index)) {
                    return false;
                }
            }

            return true;
        }

        private bool TrySkipTypeAtom(ref int index) {
            if (TokenStream.LA(index) == TyhpParser.T_OPEN_ROUND_BRACE) {
                index++;
                if (!TrySkipTypeExprCore(ref index)) {
                    return false;
                }

                if (TokenStream.LA(index) != TyhpParser.T_CLOSE_ROUND_BRACE) {
                    return false;
                }

                index++;
                return true;
            }

            if (TokenStream.LA(index) == TyhpParser.T_CALLABLE
                && (TokenStream.LA(index + 1) == TyhpParser.T_OPEN_ROUND_BRACE
                    || IsBuiltinCastToken(TokenStream.LA(index + 1)))) {
                return TrySkipCallableShape(ref index);
            }

            int structIndex = index;
            if (TrySkipStructShape(ref structIndex)) {
                index = structIndex;
                return true;
            }

            int tokenType = TokenStream.LA(index);
            if (tokenType == TyhpParser.T_SYM_MINUS || tokenType == TyhpParser.T_SYM_PLUS) {
                index++;
                tokenType = TokenStream.LA(index);
            }

            if (tokenType == TyhpParser.T_LNUMBER
                || tokenType == TyhpParser.T_DNUMBER
                || tokenType == TyhpParser.T_ONUMBER
                || tokenType == TyhpParser.T_HNUMBER
                || tokenType == TyhpParser.T_BNUMBER
                || tokenType == TyhpParser.T_CONSTANT_ENCAPSED_STRING) {
                index++;
                return true;
            }

            if (!IsTypeNameHeadToken(tokenType)) {
                return false;
            }

            index++;
            if (TokenStream.LA(index) == TyhpParser.T_SYM_LT
                && !TrySkipGenericArgumentList(ref index)) {
                return false;
            }

            return true;
        }

        private const int MaxGenericArgumentLookahead = 256;

        private bool TrySkipGenericArgumentList(ref int index) {
            int depth = 0;
            int remaining = MaxGenericArgumentLookahead;
            while (remaining-- > 0) {
                int tokenType = TokenStream.LA(index);
                if (tokenType == TokenConstants.EOF) {
                    return false;
                }

                if (tokenType == TyhpParser.T_SYM_LT) {
                    depth++;
                } else if (tokenType == TyhpParser.T_SYM_GT) {
                    depth--;
                } else if (CannotAppearInGenericArguments(tokenType, allowParens: true)) {
                    return false;
                }

                index++;
                if (depth == 0) {
                    return true;
                }
            }

            return false;
        }

        private static bool IsClassNameHeadToken(int tokenType) {
            switch (tokenType) {
                case TyhpParser.T_STRING:
                case TyhpParser.T_NAME_QUALIFIED:
                case TyhpParser.T_NAME_FULLY_QUALIFIED:
                case TyhpParser.T_NAME_RELATIVE:
                case TyhpParser.T_STATIC:
                case TyhpParser.T_TYHP_PARENT:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsTypeNameHeadToken(int tokenType) {
            switch (tokenType) {
                case TyhpParser.T_STRING:
                case TyhpParser.T_NAME_QUALIFIED:
                case TyhpParser.T_NAME_FULLY_QUALIFIED:
                case TyhpParser.T_NAME_RELATIVE:
                case TyhpParser.T_ARRAY:
                case TyhpParser.T_CALLABLE:
                case TyhpParser.T_TYHP_VOID:
                case TyhpParser.T_TYHP_PARENT:
                case TyhpParser.T_TYHP_USING:
                    return true;
                default:
                    return false;
            }
        }

        private static bool CannotAppearInGenericArguments(int tokenType, bool allowParens = false) {
            switch (tokenType) {
                case TyhpParser.T_SYM_SEMICOLON:
                case TyhpParser.T_OPEN_CURLY_BRACE:
                case TyhpParser.T_CLOSE_CURLY_BRACE:
                case TyhpParser.T_CLOSE_TAG:
                case TyhpParser.T_VARIABLE:
                    return true;
                case TyhpParser.T_OPEN_ROUND_BRACE:
                case TyhpParser.T_CLOSE_ROUND_BRACE:
                    return !allowParens;
                default:
                    return false;
            }
        }

        public bool checkIsTopExpr(RuleContext _localctx) {
            RuleContext ctx = _localctx;
            int depth = 0;
            while (ctx != null) {
                if (ctx is TyhpParser.PhpExprPrecContext) {
                    depth++;
                    if (depth > 1) {
                        return false;
                    }
                } else if (ctx is TyhpParser.PhpTopExprContext) {
                    return true;
                } else if (ctx is TyhpParser.ExprContext) {
                    return false;
                }
                ctx = ctx.Parent;
            }
            return false;
        }

        /// <summary>
        /// Records a syntax error for an illegal <c>extern class</c> shape (body, heritage,
        /// <c>as</c> alias, or type arguments). Explicit alternatives consume those tokens so
        /// ANTLR does not recover by deleting <c>extern</c> and parsing a normal class.
        /// </summary>
        public void reportExternClassCannotHaveBody() {
            NotifyErrorListeners("illegal extern class shape");
        }
    }
}