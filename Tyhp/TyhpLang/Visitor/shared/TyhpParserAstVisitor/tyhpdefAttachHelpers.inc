// #class TyhpParserAstVisitor
// #using System
// #using System.Collections.Generic
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        private static void ReadTyhpdefDeclModifier(
            TokenValueAst? token,
            out bool isDeprecated,
            out bool isObsolete,
            out bool isOmit)
        {
            isDeprecated = token?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            isObsolete = token?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;
            isOmit = token?.ValueInt64 == TyhpParser.T_TYHPDEF_OMIT;
        }

        private void AttachOverlayAgainst(Base2Ast node, ParserRuleContext context)
        {
            // Class-member rules start at a leading `#[…]`, so a backward walk from
            // context.Start misses `// @overlay-against:` written after the attribute
            // and before the keyword (where `tyhp overlay stamp` inserts it). Prefer
            // that comment, then a stamp that precedes the attributes.
            var stamp = this.FindOverlayAgainstAfterLeadingAttributes(context);
            if (string.IsNullOrEmpty(stamp))
            {
                stamp = this.FindPossibleOverlayAgainst(context.Start);
            }

            if (!string.IsNullOrEmpty(stamp))
            {
                node.AddGrammarAddon(
                    "overlayAgainst",
                    TokenValueAst.Create(stamp, 0, context, GetCurrentLanguageMode(context)));
            }
        }

        /// <summary>
        /// Stamp comment in the token gap after a leading <c>attributes</c> child and
        /// before the next default-channel token (modifier, keyword, or type).
        /// </summary>
        private string? FindOverlayAgainstAfterLeadingAttributes(ParserRuleContext context)
        {
            if (context.ChildCount == 0
                || context.GetChild(0) is not TyhpParser.AttributesContext attributes
                || attributes.Stop == null
                || this._tokens == null)
            {
                return null;
            }

            var limit = context.Stop?.TokenIndex ?? attributes.Stop.TokenIndex;
            string? stamp = null;
            for (var i = attributes.Stop.TokenIndex + 1; i <= limit; i++)
            {
                var token = this._tokens.Get(i);
                if (token == null)
                {
                    break;
                }

                if (token.Channel == TyhpLexer.WhiteSpaceChannel
                    || token.Channel == TokenConstants.HiddenChannel
                    || token.Channel == TyhpLexer.DocBlockCommentsChannel)
                {
                    continue;
                }

                if (token.Channel == TyhpLexer.SimpleCommentsChannel)
                {
                    var extracted = ExtractOverlayAgainstStamp(token.Text);
                    if (extracted != null)
                    {
                        stamp = extracted;
                    }

                    continue;
                }

                break;
            }

            return stamp;
        }

        private void AttachPartialFunctionAlias(IBase2Ast nameOrAlias, IToken? aliasedAs, ParserRuleContext context)
        {
            if (aliasedAs == null || nameOrAlias is not Base2Ast nameNode)
            {
                return;
            }

            nameNode.AddGrammarAddon("aliasedAs", PhpNameAst.Create(aliasedAs, context));
        }

        private void AttachProvidedBy(Base2Ast node, ParserRuleContext context)
        {
            var package = this.FindPossibleProvidedBy(context.Start);
            if (!string.IsNullOrEmpty(package))
            {
                node.AddGrammarAddon(
                    TyhpdefImportObjectDeclAst.ProvidedByAddonKey,
                    TokenValueAst.Create(package, 0, context, GetCurrentLanguageMode(context)));
            }
        }
