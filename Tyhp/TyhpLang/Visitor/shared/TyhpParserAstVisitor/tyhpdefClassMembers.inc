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

        /// <summary>
        /// Visits a name-only overlay <c>partial function</c> method (attributes only; no signature).
        /// Grammar: Attributes=attributes? T_TYHPDEF_PARTIAL function
        ///   Identifier=tyhpdefPartialFunctionName T_SYM_SEMICOLON
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpdefPartialClassMethod(
            [NotNull] TyhpParser.TyhpdefPartialClassMethodContext context)
        {
            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitTyhpdefPartialFunctionName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefPartialClassMethod.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var methodName = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                ? nameOrAlias.Identifier
                : (nameOrAlias.ValueString ?? "");

            var result = PhpMethodDeclAst.Create(
                methodName,
                returnsRef: false,
                modifiers: null,
                parameters: null,
                returnType: null,
                body: null,
                docComment: null,
                context,
                GetCurrentLanguageMode(context),
                isPartial: true);
            this.AttachPartialFunctionAlias(nameOrAlias, context.AliasedAs, context);
            this.AttachOverlayAgainst(result, context);
            result.AddGrammarAddon("nameOrAlias", nameOrAlias);

            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }

        /// <summary>
        /// Visits a tyhpdef import class method.
        /// Grammar: tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC? Modifiers=methodModifiers function
        ///   ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        ///   FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///   T_CLOSE_ROUND_BRACE ReturnType=returnType T_SYM_SEMICOLON
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpdefImportClassMethod([NotNull] TyhpParser.TyhpdefImportClassMethodContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitTyhpdefFunctionNameWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassMethod.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            // Name AST nodes such as PhpNameAst carry the method name in ValueString, while the
            // Base2Ast.Identifier property defaults to "" (never null). A plain `Identifier ?? ValueString`
            // therefore always yields the empty string, leaving the method unnamed (and unresolvable as a
            // member). Prefer Identifier only when non-empty, otherwise fall back to ValueString.
            var methodName = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                ? nameOrAlias.Identifier
                : (nameOrAlias.ValueString ?? "");

            var isOmit = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OMIT;
            var result = PhpMethodDeclAst.Create(
                methodName,
                context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null,
                context.Modifiers != null ? this.VisitMethodModifiers(context.Modifiers) : null,
                context.ParameterList != null ? this.VisitParameterList(context.ParameterList) : null,
                context.ReturnType != null ? this.VisitReturnType(context.ReturnType) : null,
                null, // no body in tyhpdef
                docComment,
                context,
                GetCurrentLanguageMode(context),
                isOmit: isOmit
            );
            this.AttachOverlayAgainst(result, context);

            result.AddGrammarAddon("nameOrAlias", nameOrAlias);

            // Method-level generic parameters (e.g. `function with<T, U>(...)`) are attached to the
            // name AST under "GenericArguments". Re-expose them under "identifier" so the shared
            // method binder (PopulateGenericParametersFromGrammarAddon) registers them.
            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericArguments", out var methodGenerics))
            {
                result.AddGrammarAddon("identifier", methodGenerics);
            }

            if (context.IsAsync != null)
            {
                result.AddGrammarAddon("isAsync", TokenValueAst.Create("async", TyhpParser.T_TYHP_ASYNC, context));
            }
            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }

        /// <summary>
        /// Visits <c>use extension</c> inside a tyhpdef class body.
        /// Grammar: tyhpdefDeprecatedOrObsolete? T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        ///   Adaptations=traitAdaptations   #tyhpdefClassUseExtension
        /// </summary>
        public override TyhpImportExtensionAst VisitTyhpdefClassUseExtension([NotNull] TyhpParser.TyhpdefClassUseExtensionContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassUseExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassUseExtension.Adaptations");
            }

            var result = TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context));

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            return result;
        }

        /// <summary>
        /// Visits a tyhpdef enum case.
        /// Grammar: tyhpdefDeprecatedOrObsolete? EnumCase=enumCase
        /// </summary>
        public override PhpEnumCaseAst VisitTyhpdefEnumCase([NotNull] TyhpParser.TyhpdefEnumCaseContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var result = this.VisitEnumCase(context.EnumCase);
            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }

        /// <summary>
        /// Visits a tyhpdef trait use.
        /// Grammar: tyhpdefDeprecatedOrObsolete? T_USE TraitNameList=classNameList
        ///   Adaptations=traitAdaptations
        /// </summary>
        public override PhpTraitUseAst VisitTyhpdefTraitUse([NotNull] TyhpParser.TyhpdefTraitUseContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            PhpClassNameListAst? traitNames;
            if (context.TraitNameList != null)
            {
                traitNames = this.VisitClassNameList(context.TraitNameList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefTraitUse.TraitNameList");
                traitNames = PhpClassNameListAst.Create(null, context);
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefTraitUse.Adaptations");
            }

            var result = PhpTraitUseAst.Create(
                traitNames,
                adaptations,
                context,
                GetCurrentLanguageMode(context)
            );

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }
            return result;
        }
