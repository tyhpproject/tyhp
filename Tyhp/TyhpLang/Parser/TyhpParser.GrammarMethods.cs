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
    }
}