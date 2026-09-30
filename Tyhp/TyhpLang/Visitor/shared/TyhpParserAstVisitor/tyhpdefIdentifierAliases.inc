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

        //#endregion Const and Property Lists

        //#region Identifier Visitors

        /// <summary>
        /// Visits a tyhpdef identifier with optional alias.
        /// Grammar: tyhpdefIdentifierWithOptionalAlias
        ///   : Identifier=name GenericParameters=tyhpGenericParameterDeclarations?
        ///   | SemiReserved=semiReserved GenericParameters=tyhpGenericParameterDeclarations?
        ///   | AliasedIdentifier=tyhpdefIdentifierWithAlias
        /// </summary>
        public override IBase2Ast VisitTyhpdefIdentifierWithOptionalAlias([NotNull] TyhpParser.TyhpdefIdentifierWithOptionalAliasContext context)
        {
            if (context.Identifier != null)
            {
                var identifier = this.VisitName(context.Identifier);
                if (context.GenericParameters != null)
                {
                    var genericParameters = this.VisitTyhpGenericParameterDeclarations(context.GenericParameters);
                    identifier.AddGrammarAddon("GenericParameters", genericParameters);
                }

                return identifier;
            }

            if (context.SemiReserved != null)
            {
                var reserved = this.VisitSemiReserved(context.SemiReserved);
                var identifier = PhpNameAst.Create(reserved.ValueString ?? "", 0, context);
                if (context.GenericParameters != null)
                {
                    var genericParameters = this.VisitTyhpGenericParameterDeclarations(context.GenericParameters);
                    identifier.AddGrammarAddon("GenericParameters", genericParameters);
                }

                return identifier;
            }

            if (context.AliasedIdentifier != null)
            {
                return this.VisitTyhpdefIdentifierWithAlias(context.AliasedIdentifier);
            }

            this.ReportMissingRequired(context, "tyhpdefIdentifierWithOptionalAlias");
            return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Dispatches tyhpdefIdentifierWithAlias labeled alternatives.
        /// </summary>
        public TyhpdefIdentifierAliasAst VisitTyhpdefIdentifierWithAlias(TyhpParser.TyhpdefIdentifierWithAliasContext context)
        {
            return context switch
            {
                TyhpParser.TyhpdefIdentifierAliasContext ctx => this.VisitTyhpdefIdentifierAlias(ctx),
                TyhpParser.TyhpdefClassMemberIdentifierAliasContext ctx => this.VisitTyhpdefClassMemberIdentifierAlias(ctx),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpdefIdentifierWithAlias",
                    () => TyhpdefIdentifierAliasAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }

        private T HandleUnexpectedAlternativeSpecial<T>(ParserRuleContext context, string ruleName, Func<T> errorFactory)
        {
            this.ReportUnexpectedAlternative(context, ruleName);
            return errorFactory();
        }

        /// <summary>
        /// Visits a tyhpdef class name with optional alias.
        /// Grammar: tyhpdefClassNameWithOptionalAlias
        ///   : (AliasOf=className T_AS)?
        ///       Identifier=name GenericParameters=tyhpGenericParameterDeclarations?
        /// </summary>
        public override PhpNameAst VisitTyhpdefClassNameWithOptionalAlias([NotNull] TyhpParser.TyhpdefClassNameWithOptionalAliasContext context)
        {
            if (context.Identifier == null)
            {
                this.ReportMissingRequired(context, "tyhpdefClassNameWithOptionalAlias.Identifier");
                return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var identifier = this.VisitName(context.Identifier);
            if (context.GenericParameters != null)
            {
                var genericParameters = this.VisitTyhpGenericParameterDeclarations(context.GenericParameters);
                identifier.AddGrammarAddon("GenericParameters", genericParameters);
            }

            if (context.AliasOf != null)
            {
                var aliasOf = this.VisitClassName(context.AliasOf);
                identifier.AddGrammarAddon("aliasOf", aliasOf);
            }
            return identifier;
        }

        /// <summary>
        /// Dispatches tyhpdefFunctionNameWithOptionalAlias labeled alternatives.
        /// </summary>
        public IBase2Ast VisitTyhpdefFunctionNameWithOptionalAlias(TyhpParser.TyhpdefFunctionNameWithOptionalAliasContext context)
        {
            return context switch
            {
                TyhpParser.TyhpdefFunctionNameGenericAliasContext ctx => this.VisitTyhpdefFunctionNameGenericAlias(ctx),
                TyhpParser.TyhpdefFunctionNameAliasContext ctx => this.VisitTyhpdefFunctionNameAlias(ctx),
                TyhpParser.TyhpdefFunctionNameSemiReservedAliasContext ctx => this.VisitTyhpdefFunctionNameSemiReservedAlias(ctx),
                _ => HandleUnexpectedAlternative<IBase2Ast>(context, "tyhpdefFunctionNameWithOptionalAlias")
            };
        }

        /// <summary>
        /// Visits a tyhpdef function name with generic arguments and optional alias.
        /// Grammar: tyhpdefFunctionNameWithOptionalAlias
        ///   : Identifier=name GenericArguments=tyhpGenericParameterDeclarations
        ///       (T_AS AliasedAs=tyhpStringWithOptionalGeneric)?   #tyhpdefFunctionNameGenericAlias
        /// </summary>
        public override PhpNameAst VisitTyhpdefFunctionNameGenericAlias([NotNull] TyhpParser.TyhpdefFunctionNameGenericAliasContext context)
        {
            var name = this.VisitName(context.Identifier);
            var genericArgs = this.VisitTyhpGenericParameterDeclarations(context.GenericArguments);
            name.AddGrammarAddon("GenericArguments", genericArgs);

            if (context.AliasedAs != null)
            {
                var aliasedAs = this.VisitTyhpStringWithOptionalGeneric(context.AliasedAs);
                name.AddGrammarAddon("aliasedAs", aliasedAs);
            }
            return name;
        }

        /// <summary>
        /// Visits a tyhpdef function name with optional simple alias (no generics).
        /// Grammar: tyhpdefFunctionNameWithOptionalAlias
        ///   : Identifier=name (T_AS AliasedAs=T_STRING)?   #tyhpdefFunctionNameAlias
        /// </summary>
        public override PhpNameAst VisitTyhpdefFunctionNameAlias([NotNull] TyhpParser.TyhpdefFunctionNameAliasContext context)
        {
            var name = this.VisitName(context.Identifier);
            if (context.AliasedAs != null)
            {
                var aliasedAs = PhpNameAst.Create(context.AliasedAs, context);
                name.AddGrammarAddon("aliasedAs", aliasedAs);
            }
            return name;
        }

        /// <summary>
        /// Visits a tyhpdef function name that is a PHP semi-reserved keyword, with optional
        /// generic parameters and optional alias. Covers both <c>function finally(...)</c> and
        /// <c>function catch&lt;TResult&gt;(...)</c>.
        /// Grammar: tyhpdefFunctionNameWithOptionalAlias
        ///   : SemiReserved=semiReserved GenericArguments=tyhpGenericParameterDeclarations?
        ///       (T_AS AliasedAs=T_STRING)?   #tyhpdefFunctionNameSemiReservedAlias
        /// </summary>
        public override PhpNameAst VisitTyhpdefFunctionNameSemiReservedAlias([NotNull] TyhpParser.TyhpdefFunctionNameSemiReservedAliasContext context)
        {
            var reserved = this.VisitSemiReserved(context.SemiReserved);
            var name = PhpNameAst.Create(reserved.ValueString ?? "", 0, context);
            if (context.GenericArguments != null)
            {
                var genericArgs = this.VisitTyhpGenericParameterDeclarations(context.GenericArguments);
                name.AddGrammarAddon("GenericArguments", genericArgs);
            }
            if (context.AliasedAs != null)
            {
                var aliasedAs = PhpNameAst.Create(context.AliasedAs, context);
                name.AddGrammarAddon("aliasedAs", aliasedAs);
            }
            return name;
        }

        /// <summary>
        /// Visits a tyhpdef identifier alias (name AS aliasedName).
        /// Grammar: tyhpdefIdentifierWithAlias
        ///   : Identifier=name T_AS AliasedAs=tyhpOptionalGenericIdentifier   #tyhpdefIdentifierAlias
        /// </summary>
        public override TyhpdefIdentifierAliasAst VisitTyhpdefIdentifierAlias([NotNull] TyhpParser.TyhpdefIdentifierAliasContext context)
            => TyhpdefIdentifierAliasAst.Create(
                null,
                this.VisitName(context.Identifier),
                this.VisitTyhpOptionalGenericIdentifier(context.AliasedAs),
                context,
                GetCurrentLanguageMode(context)
            );

        /// <summary>
        /// Visits a tyhpdef class member identifier alias (ClassName::name AS aliasedName).
        /// Grammar: tyhpdefIdentifierWithAlias
        ///   : ClassName=className T_DOUBLE_COLON Identifier=tyhpOptionalGenericIdentifier
        ///       T_AS AliasedAs=tyhpOptionalGenericIdentifier   #tyhpdefClassMemberIdentifierAlias
        /// </summary>
        public override TyhpdefIdentifierAliasAst VisitTyhpdefClassMemberIdentifierAlias([NotNull] TyhpParser.TyhpdefClassMemberIdentifierAliasContext context)
            => TyhpdefIdentifierAliasAst.Create(
                this.VisitClassName(context.ClassName),
                this.VisitTyhpOptionalGenericIdentifier(context.Identifier),
                this.VisitTyhpOptionalGenericIdentifier(context.AliasedAs),
                context,
                GetCurrentLanguageMode(context)
            );
