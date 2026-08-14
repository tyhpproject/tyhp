using System.Diagnostics;

namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;
    using static Tyhp.TyhpLang.Enum.PhpModifierExtensions;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override PhpObjectTypeDeclAst VisitClassDeclarationStatement([NotNull] TyhpParser.ClassDeclarationStatementContext context)
        {
            if (context.classDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitClassDeclarationStatementGrammarAddon(context.classDeclarationStatementGrammarAddon());
            }

            // Claimed before the body is visited: any declaration inside the body that looks up its
            // own docblock advances the shared cursor past this one.
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                context.Modifiers != null ? this.VisitClassModifiers(context.Modifiers) : null,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon(
                "identifier",
                context.classNameGrammarAddon() != null
                    ? this.VisitClassNameGrammarAddon(context.classNameGrammarAddon())
                    : null);
        }

        public override PhpObjectTypeDeclAst VisitClassDeclarationStatementGrammarAddon([NotNull] TyhpParser.ClassDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "class-declaration-statement-grammar-addon", context.GetType().Name
            );
            return PhpObjectTypeDeclAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IBase2Ast? VisitClassNameGrammarAddon([NotNull] TyhpParser.ClassNameGrammarAddonContext context)
            => null;

        public override PhpModifierListAst VisitClassModifiers([NotNull] TyhpParser.ClassModifiersContext context)
        {
            var modifiers = (context._Items ?? Enumerable.Empty<TyhpParser.ClassModifierContext>())
                .Select(this.VisitClassModifier)
                .Select(x => FromToken((int)(x.ValueInt64 ?? 0)));
            return PhpModifierListAst.Create(modifiers, context);
        }

        public override PhpModifierListAst? VisitClassModifiersOptional([NotNull] TyhpParser.ClassModifiersOptionalContext context)
            => context.classModifiers() != null ? this.VisitClassModifiers(context.classModifiers()) : null;

        public override TokenValueAst VisitClassModifier([NotNull] TyhpParser.ClassModifierContext context)
        {
            if (context.classModifierGrammarAddon() != null)
            {
                return this.VisitClassModifierGrammarAddon(context.classModifierGrammarAddon());
            }

            if (context.TokenValue is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "classModifier");
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return TokenValueAst.Create(context.TokenValue, context);
        }

        public override TokenValueAst VisitClassModifierGrammarAddon([NotNull] TyhpParser.ClassModifierGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "class-modifier-grammar-addon", context.GetType().Name
            );
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpObjectTypeDeclAst VisitTraitDeclarationStatement([NotNull] TyhpParser.TraitDeclarationStatementContext context)
        {
            if (context.traitDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitTraitDeclarationStatementGrammarAddon(context.traitDeclarationStatementGrammarAddon());
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var modifiersAddon = context.traitModifiersGrammarAddon();
            var modifiers = modifiersAddon != null ? this.VisitTraitModifiersGrammarAddon(modifiersAddon) : null;
            var nameAddon = context.traitNameGrammarAddon();

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                modifiers,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon("modifiers", modifiers)
            .WithGrammarAddon(
                "identifier",
                nameAddon != null ? this.VisitTraitNameGrammarAddon(nameAddon) : null);
        }

        public override PhpObjectTypeDeclAst VisitTraitDeclarationStatementGrammarAddon([NotNull] TyhpParser.TraitDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "trait-declaration-statement-grammar-addon", context.GetType().Name
            );
            return PhpObjectTypeDeclAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpModifierListAst? VisitTraitModifiersGrammarAddon([NotNull] TyhpParser.TraitModifiersGrammarAddonContext context)
            => null;

        public override PhpObjectTypeDeclAst VisitInterfaceDeclarationStatement([NotNull] TyhpParser.InterfaceDeclarationStatementContext context)
        {
            if (context.interfaceDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitInterfaceDeclarationStatementGrammarAddon(context.interfaceDeclarationStatementGrammarAddon());
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var modifiersAddon = context.interfaceModifiersGrammarAddon();
            var modifiers = modifiersAddon != null ? this.VisitInterfaceModifiersGrammarAddon(modifiersAddon) : null;
            var nameAddon = context.interfaceNameGrammarAddon();

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                modifiers,
                null,
                context.Extends != null ? this.VisitInterfaceExtendsList(context.Extends) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon("modifiers", modifiers)
            .WithGrammarAddon(
                "identifier",
                nameAddon != null ? this.VisitInterfaceNameGrammarAddon(nameAddon) : null);
        }

        public override PhpObjectTypeDeclAst VisitInterfaceDeclarationStatementGrammarAddon([NotNull] TyhpParser.InterfaceDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "interface-declaration-statement-grammar-addon", context.GetType().Name
            );
            return PhpObjectTypeDeclAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IBase2Ast? VisitInterfaceNameGrammarAddon([NotNull] TyhpParser.InterfaceNameGrammarAddonContext context)
            => null;

        public override PhpModifierListAst? VisitInterfaceModifiersGrammarAddon([NotNull] TyhpParser.InterfaceModifiersGrammarAddonContext context)
            => null;

        public override PhpObjectTypeDeclAst VisitEnumDeclarationStatement([NotNull] TyhpParser.EnumDeclarationStatementContext context)
        {
            if (context.enumDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitEnumDeclarationStatementGrammarAddon(context.enumDeclarationStatementGrammarAddon());
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var modifiersAddon = context.enumModifiersGrammarAddon();
            var modifiers = modifiersAddon != null ? this.VisitEnumModifiersGrammarAddon(modifiersAddon) : null;
            var nameAddon = context.enumNameGrammarAddon();
            var backingType = context.enumBackingType();

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                modifiers,
                null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                backingType != null ? this.VisitEnumBackingType(backingType) as ITypeExpression : null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon("modifiers", modifiers)
            .WithGrammarAddon(
                "identifier",
                nameAddon != null ? this.VisitEnumNameGrammarAddon(nameAddon) : null);
        }

        public override PhpObjectTypeDeclAst VisitEnumDeclarationStatementGrammarAddon([NotNull] TyhpParser.EnumDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "enum-declaration-statement-grammar-addon", context.GetType().Name
            );
            return PhpObjectTypeDeclAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpModifierListAst? VisitEnumModifiersGrammarAddon([NotNull] TyhpParser.EnumModifiersGrammarAddonContext context)
            => null;

        public override IBase2Ast? VisitEnumNameGrammarAddon([NotNull] TyhpParser.EnumNameGrammarAddonContext context)
            => null;

        public override ITypeExpression? VisitEnumBackingType([NotNull] TyhpParser.EnumBackingTypeContext context)
            => context.TypeExpr != null ? this.VisitTypeExpr(context.TypeExpr) as ITypeExpression : null;

        public override PhpEnumCaseAst VisitEnumCase([NotNull] TyhpParser.EnumCaseContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpEnumCaseAst.Create(
                this.VisitIdentifier(context.Identifier),
                this.VisitEnumCaseExpr(context.Expr),
                docComment,
                context
            );
        }

        public override IExpression? VisitEnumCaseExpr([NotNull] TyhpParser.EnumCaseExprContext context)
            => context.Expr != null ? this.VisitExpr(context.Expr) : null;

        public override IClassName? VisitExtendsFrom([NotNull] TyhpParser.ExtendsFromContext context)
            => context.ClassName != null ? this.VisitClassName(context.ClassName) : null;

        public override PhpClassNameListAst VisitInterfaceExtendsList([NotNull] TyhpParser.InterfaceExtendsListContext context)
            => context.ClassNameList != null ? this.VisitClassNameList(context.ClassNameList) : PhpClassNameListAst.Create([], context);

        public override PhpClassNameListAst VisitImplementsList([NotNull] TyhpParser.ImplementsListContext context)
            => context.ClassNameList != null ? this.VisitClassNameList(context.ClassNameList) : PhpClassNameListAst.Create([], context);

        public override PhpClassBodyAst VisitClassStatementList([NotNull] TyhpParser.ClassStatementListContext context)
            => PhpClassBodyAst.Create(
                context._Items?.Select(this.VisitClassStatement),
                context
            );

        public IClassMember VisitAttributedClassStatement([NotNull] TyhpParser.AttributedClassStatementContext context)
            => context switch
            {
                TyhpParser.ClassPropertiesContext ctx => this.VisitClassProperties(ctx),
                TyhpParser.ClassPropertyAccessorsContext ctx => this.VisitClassPropertyAccessors(ctx),
                TyhpParser.ClassConstsContext ctx => this.VisitClassConsts(ctx),
                TyhpParser.ClassTypedConstsContext ctx => this.VisitClassTypedConsts(ctx),
                TyhpParser.ClassEnumCaseContext ctx => this.VisitClassEnumCase(ctx),
                TyhpParser.PhpClassCtorContext ctx => this.VisitPhpClassCtor(ctx),
                TyhpParser.PhpClassMethodContext ctx => this.VisitPhpClassMethod(ctx),
                TyhpParser.AttributedClassStatementGrammarAddonHandlerContext ctx => this.VisitAttributedClassStatementGrammarAddonHandler(ctx),
                _ => this.VisitAttributedClassStatementAlternative(context)
            };

        public virtual IClassMember VisitAttributedClassStatementAlternative([NotNull] TyhpParser.AttributedClassStatementContext context)
        {
            this.ReportUnexpectedAlternative(context, "attributed-class-statement");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpPropertyDeclAst VisitClassProperties([NotNull] TyhpParser.ClassPropertiesContext context)
            => PhpPropertyDeclAst.Create(
                this.VisitPropertyModifiers(context.Modifiers),
                this.VisitOptionalTypeWithoutStatic(context.TypeExpr),
                this.VisitPropertyList(context.PropertyList),
                context
            );

        public override PhpPropertyDeclAst VisitClassPropertyAccessors([NotNull] TyhpParser.ClassPropertyAccessorsContext context)
        {
            var hookedProperty = this.VisitHookedProperty(context.PropertyAccessors);
            return PhpPropertyDeclAst.Create(
                this.VisitPropertyModifiers(context.Modifiers),
                this.VisitOptionalTypeWithoutStatic(context.TypeExpr),
                PhpPropertyListAst.Create([hookedProperty], context),
                context
            );
        }

        public override PhpConstDeclListAst VisitClassConsts([NotNull] TyhpParser.ClassConstsContext context)
            => this.VisitClassConstList(context.ConstList, this.VisitClassConstModifiers(context.Modifiers));

        public override PhpConstDeclListAst VisitClassTypedConsts([NotNull] TyhpParser.ClassTypedConstsContext context)
            => this.VisitClassConstList(
                context.ConstList,
                this.VisitClassConstModifiers(context.Modifiers),
                this.VisitTypeExpr(context.typeExpr()));

        public override PhpEnumCaseAst VisitClassEnumCase([NotNull] TyhpParser.ClassEnumCaseContext context)
            => this.VisitEnumCase(context.EnumCase);

        public override PhpMethodDeclAst VisitPhpClassCtor([NotNull] TyhpParser.PhpClassCtorContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpMethodDeclAst.Create(
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                false,
                this.VisitMethodModifiers(context.Modifiers),
                this.VisitCtorParameterList(context.ParameterList),
                null,
                this.VisitMethodBody(context.StatementList),
                docComment,
                context
            );
        }

        public override PhpMethodDeclAst VisitPhpClassMethod([NotNull] TyhpParser.PhpClassMethodContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpMethodDeclAst.Create(
                this.VisitIdentifierWithoutConstructor(context.Identifier).ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitMethodModifiers(context.Modifiers),
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitMethodBody(context.StatementList),
                docComment,
                context
            );
        }

        public override IClassMember VisitAttributedClassStatementGrammarAddonHandler([NotNull] TyhpParser.AttributedClassStatementGrammarAddonHandlerContext context)
            => this.VisitAttributedClassStatementGrammarAddon(context.attributedClassStatementGrammarAddon());

        public override IClassMember VisitAttributedClassStatementGrammarAddon([NotNull] TyhpParser.AttributedClassStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "attributed-class-statement-grammar-addon", context.GetType().Name
            );
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public IClassMember VisitClassStatement([NotNull] TyhpParser.ClassStatementContext context)
            => context switch
            {
                TyhpParser.ClassStatementAttributedContext ctx => this.VisitClassStatementAttributed(ctx),
                TyhpParser.ClassTraitUseContext ctx => this.VisitClassTraitUse(ctx),
                TyhpParser.ClassStatementGrammarAddonHandlerContext ctx => this.VisitClassStatementGrammarAddonHandler(ctx),
                _ => this.VisitClassStatementAlternative(context)
            };

        public virtual IClassMember VisitClassStatementAlternative([NotNull] TyhpParser.ClassStatementContext context)
        {
            this.ReportUnexpectedAlternative(context, "class-statement");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IClassMember VisitClassStatementAttributed([NotNull] TyhpParser.ClassStatementAttributedContext context)
            => this.VisitAttributedClassStatement(context.attributedClassStatement()).WithAttributes(
                context.Attributes != null ? this.VisitAttributes(context.Attributes) : null
            );

        public override PhpTraitUseAst VisitClassTraitUse([NotNull] TyhpParser.ClassTraitUseContext context)
            => PhpTraitUseAst.Create(
                this.VisitClassNameList(context.TraitNameList),
                this.VisitTraitAdaptations(context.Adaptations),
                context
            );

        public override IClassMember VisitClassStatementGrammarAddonHandler([NotNull] TyhpParser.ClassStatementGrammarAddonHandlerContext context)
            => this.VisitClassStatementGrammarAddon(context.classStatementGrammarAddon());

        public virtual IClassMember VisitClassStatementGrammarAddon([NotNull] TyhpParser.ClassStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "class-statement-grammar-addon", context.GetType().Name
            );
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpClassNameListAst VisitClassNameList([NotNull] TyhpParser.ClassNameListContext context)
            => PhpClassNameListAst.Create(
                context._Items.Select(this.VisitClassName),
                context
            );

        public override PhpTraitAdaptationListAst? VisitTraitAdaptations([NotNull] TyhpParser.TraitAdaptationsContext context)
            => context.TraitAdaptationList != null ? this.VisitTraitAdaptationList(context.TraitAdaptationList) : null;

        public override PhpTraitAdaptationListAst VisitTraitAdaptationList([NotNull] TyhpParser.TraitAdaptationListContext context)
            => PhpTraitAdaptationListAst.Create(
                context._Items.Select(this.VisitTraitAdaptation),
                context
            );

        public override ITraitAdaptation VisitTraitAdaptation([NotNull] TyhpParser.TraitAdaptationContext context)
        {
            if (context.Precedence != null)
            {
                return this.VisitTraitPrecedence(context.Precedence);
            }
            else if (context.Alias != null)
            {
                return this.VisitTraitAlias(context.Alias);
            }
            this.ReportUnexpectedAlternative(context, "trait-adaptation");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpTraitPrecedenceAst VisitTraitPrecedence([NotNull] TyhpParser.TraitPrecedenceContext context)
            => PhpTraitPrecedenceAst.Create(
                this.VisitAbsoluteTraitMethodReference(context.MethodReference),
                this.VisitClassNameList(context.TraitNameList),
                context
            );

        public PhpTraitAliasAst VisitTraitAlias([NotNull] TyhpParser.TraitAliasContext context)
            => context switch
            {
                TyhpParser.TraitAliasRenameContext ctx => this.VisitTraitAliasRename(ctx),
                TyhpParser.TraitAliasVisibilityContext ctx => this.VisitTraitAliasVisibility(ctx),
                TyhpParser.TraitAliasGrammarAddonHandlerContext ctx => this.VisitTraitAliasGrammarAddonHandler(ctx),
                _ => this.VisitTraitAliasAlternative(context)
            };

        public virtual PhpTraitAliasAst VisitTraitAliasAlternative([NotNull] TyhpParser.TraitAliasContext context)
        {
            this.ReportUnexpectedAlternative(context, "trait-alias");
            return PhpTraitAliasAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpTraitAliasAst VisitTraitAliasRename([NotNull] TyhpParser.TraitAliasRenameContext context)
        {
            string newName = "";
            if (context.AliasString != null)
            {
                newName = this.GetTokenValueAst(context, context.AliasString)?.ValueString ?? "";
            }
            else if (context.AliasRNM != null)
            {
                newName = this.VisitReservedNonModifiers(context.AliasRNM).ValueString ?? "";
            }

            return PhpTraitAliasAst.Create(
                this.VisitTraitMethodReference(context.AliasOf),
                newName,
                context
            ).WithGrammarAddon("alias", this.VisitTraitAliasNameGrammarAddon(context.traitAliasNameGrammarAddon()));
        }

        public override PhpTraitAliasAst VisitTraitAliasVisibility([NotNull] TyhpParser.TraitAliasVisibilityContext context)
        {
            var newName = context.Identifier != null ? this.VisitIdentifier(context.Identifier).ValueString : null;
            var modifier = FromToken((int)(this.VisitMemberModifier(context.Modifier).ValueInt64 ?? 0));
            
            return PhpTraitAliasAst.Create(
                this.VisitTraitMethodReference(context.AliasOf),
                newName ?? "",
                modifier,
                context
            ).WithGrammarAddon("alias", this.VisitTraitAliasNameGrammarAddon(context.traitAliasNameGrammarAddon()));
        }

        public override PhpTraitAliasAst VisitTraitAliasGrammarAddonHandler([NotNull] TyhpParser.TraitAliasGrammarAddonHandlerContext context)
            => this.VisitTraitAliasGrammarAddon(context.traitAliasGrammarAddon());

        public virtual PhpTraitAliasAst VisitTraitAliasGrammarAddon([NotNull] TyhpParser.TraitAliasGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "trait-alias-grammar-addon", context.GetType().Name
            );
            return PhpTraitAliasAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IBase2Ast? VisitTraitAliasNameGrammarAddon([NotNull] TyhpParser.TraitAliasNameGrammarAddonContext context)
            => null;

        public override PhpTraitMemberRefAst VisitTraitMethodReference([NotNull] TyhpParser.TraitMethodReferenceContext context)
        {
            if (context.MethodReference != null)
            {
                return this.VisitAbsoluteTraitMethodReference(context.MethodReference);
            }
            else
            {
                return PhpTraitMemberRefAst.Create(
                    this.VisitIdentifier(context.Identifier),
                    context
                ).WithGrammarAddon("identifier", this.VisitTraitMethodIdentifierGrammarAddon(context.traitMethodIdentifierGrammarAddon()));
            }
        }

        public override PhpTraitMemberRefAst VisitAbsoluteTraitMethodReference([NotNull] TyhpParser.AbsoluteTraitMethodReferenceContext context)
            => PhpTraitMemberRefAst.Create(
                this.VisitClassName(context.ClassName),
                this.VisitIdentifier(context.Identifier),
                context
            ).WithGrammarAddon("identifier", this.VisitTraitMethodIdentifierGrammarAddon(context.traitMethodIdentifierGrammarAddon()));

        public override IBase2Ast? VisitTraitMethodIdentifierGrammarAddon([NotNull] TyhpParser.TraitMethodIdentifierGrammarAddonContext context)
            => null;

        public override PhpTraitMemberRefAst VisitTraitPropertyReference([NotNull] TyhpParser.TraitPropertyReferenceContext context)
        {
            if (context.VariableReference != null)
            {
                return this.VisitAbsoluteTraitPropertyReference(context.VariableReference);
            }
            else
            {
                return PhpTraitMemberRefAst.Create(
                    PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                    context
                );
            }
        }

        public override PhpTraitMemberRefAst VisitAbsoluteTraitPropertyReference([NotNull] TyhpParser.AbsoluteTraitPropertyReferenceContext context)
            => PhpTraitMemberRefAst.Create(
                this.VisitClassName(context.ClassName),
                PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                context
            );

        public override PhpStatementBlockAst? VisitMethodBody([NotNull] TyhpParser.MethodBodyContext context)
        {
            if (context.StatementList != null)
            {
                return this.VisitInnerStatementList(context.StatementList);
            }

            // Interface/abstract methods terminate with `;` and have no body. Model the absence as a
            // null body (rather than an empty block) so the checker's no-body branch — which exempts
            // interface and abstract methods from the "must return a value" rule — is reached.
            return null;
        }

        public override PhpModifierListAst VisitPropertyModifiers([NotNull] TyhpParser.PropertyModifiersContext context)
        {
            if (context.Modifiers != null)
            {
                return this.VisitNonEmptyMemberModifiers(context.Modifiers);
            }
            else if (context.IsVar != null)
            {
                return PhpModifierListAst.Create([PhpModifier.Var], context);
            }
            else if (context.ModifiersGrammarAddon != null)
            {
                return this.VisitPropertyModifiersGrammarAddon(context.ModifiersGrammarAddon);
            }
            this.ReportUnexpectedAlternative(context, "property-modifiers");
            return PhpModifierListAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpModifierListAst VisitPropertyModifiersGrammarAddon([NotNull] TyhpParser.PropertyModifiersGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "property-modifiers-grammar-addon", context.GetType().Name
            );
            return PhpModifierListAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpModifierListAst? VisitMethodModifiers([NotNull] TyhpParser.MethodModifiersContext context)
        {
            if (context.Modifiers != null)
            {
                return this.VisitNonEmptyMemberModifiers(context.Modifiers);
            }
            else if (context.ModifiersGrammarAddon != null)
            {
                return this.VisitMethodModifiersGrammarAddon(context.ModifiersGrammarAddon);
            }
            return null;
        }

        public override PhpModifierListAst? VisitMethodModifiersGrammarAddon([NotNull] TyhpParser.MethodModifiersGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "method-modifiers-grammar-addon", context.GetType().Name
            );
            return PhpModifierListAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpModifierListAst? VisitClassConstModifiers([NotNull] TyhpParser.ClassConstModifiersContext context)
        {
            if (context.Modifiers != null)
            {
                return this.VisitNonEmptyMemberModifiers(context.Modifiers);
            }
            else if (context.ModifiersGrammarAddon != null)
            {
                return this.VisitClassConstModifiersGrammarAddon(context.ModifiersGrammarAddon);
            }
            return null;
        }

        public override PhpModifierListAst? VisitClassConstModifiersGrammarAddon([NotNull] TyhpParser.ClassConstModifiersGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "class-const-modifiers-grammar-addon", context.GetType().Name
            );
            return PhpModifierListAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpModifierListAst VisitNonEmptyMemberModifiers([NotNull] TyhpParser.NonEmptyMemberModifiersContext context)
        {
            var modifiers = (context._Items ?? Enumerable.Empty<TyhpParser.MemberModifierContext>())
                .Select(this.VisitMemberModifier)
                .Select(x => FromToken((int)(x.ValueInt64 ?? 0)));
            return PhpModifierListAst.Create(modifiers, context);
        }

        public override TokenValueAst VisitMemberModifier([NotNull] TyhpParser.MemberModifierContext context)
        {
            if (context.TokenValueGrammarAddon != null)
            {
                return this.VisitMemberModifierGrammarAddon(context.TokenValueGrammarAddon);
            }

            if (context.TokenValue is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "memberModifier");
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return TokenValueAst.Create(context.TokenValue, context);
        }

        public override TokenValueAst VisitMemberModifierGrammarAddon([NotNull] TyhpParser.MemberModifierGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "member-modifier-grammar-addon", context.GetType().Name
            );
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpPropertyListAst VisitPropertyList([NotNull] TyhpParser.PropertyListContext context)
            => PhpPropertyListAst.Create(
                context._Items.Select(this.VisitProperty),
                context
            );

        public override PhpPropertyAst VisitProperty([NotNull] TyhpParser.PropertyContext context)
        {
            // The grammar anchors this after the declaration, and the scan runs backwards, so the
            // modifiers and type in front of the variable are simply passed over.
            var docComment = this.FindPossibleDocComment(context._findDocComment);

            return PhpPropertyAst.Create(
                context.Variable.Text,
                context.ValueExpr != null ? this.VisitExpr(context.ValueExpr) : null,
                null,
                docComment,
                context
            );
        }

        public virtual PhpPropertyAst VisitHookedProperty([NotNull] TyhpParser.HookedPropertyContext context)
            => context switch
            {
                TyhpParser.PropertyAccessorContext ctx => this.VisitPropertyAccessor(ctx),
                TyhpParser.PropertyAccessorWithDefaultValueContext ctx => this.VisitPropertyAccessorWithDefaultValue(ctx),
                TyhpParser.HookedPropertyGrammarAddonHandlerContext ctx => this.VisitHookedPropertyGrammarAddonHandler(ctx),
                _ => this.VisitHookedPropertyAlternative(context)
            };

        public virtual PhpPropertyAst VisitHookedPropertyAlternative([NotNull] TyhpParser.HookedPropertyContext context)
        {
            this.ReportUnexpectedAlternative(context, "hooked-property");
            return PhpPropertyAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpPropertyAst VisitPropertyAccessor([NotNull] TyhpParser.PropertyAccessorContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpPropertyAst.Create(
                context.Variable.Text,
                null,
                this.VisitPropertyHookList(context.Accessors),
                docComment,
                context
            );
        }

        public override PhpPropertyAst VisitPropertyAccessorWithDefaultValue([NotNull] TyhpParser.PropertyAccessorWithDefaultValueContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpPropertyAst.Create(
                context.Variable.Text,
                this.VisitExpr(context.Expr),
                this.VisitPropertyHookList(context.Accessors),
                docComment,
                context
            );
        }

        public override PhpPropertyAst VisitHookedPropertyGrammarAddonHandler([NotNull] TyhpParser.HookedPropertyGrammarAddonHandlerContext context)
            => this.VisitHookedPropertyGrammarAddon(context.hookedPropertyGrammarAddon());

        public override PhpPropertyAst VisitHookedPropertyGrammarAddon([NotNull] TyhpParser.HookedPropertyGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "hooked-property-grammar-addon", context.GetType().Name
            );
            return PhpPropertyAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpPropertyHookListAst VisitPropertyHookList([NotNull] TyhpParser.PropertyHookListContext context)
            => PhpPropertyHookListAst.Create(
                context._Items.Select(this.VisitPropertyHook),
                context
            );

        public override PhpPropertyHookListAst? VisitOptionalPropertyHookList([NotNull] TyhpParser.OptionalPropertyHookListContext context)
            => context.propertyHookList() != null ? this.VisitPropertyHookList(context.propertyHookList()) : null;

        public override PhpModifierListAst? VisitPropertyHookModifiers([NotNull] TyhpParser.PropertyHookModifiersContext context)
        {
            if (context.Modifiers != null)
            {
                return this.VisitNonEmptyMemberModifiers(context.Modifiers);
            }
            else if (context.ModifiersGrammarAddon != null)
            {
                return this.VisitPropertyHookModifiersGrammarAddon(context.ModifiersGrammarAddon);
            }
            return null;
        }

        public override PhpModifierListAst? VisitPropertyHookModifiersGrammarAddon([NotNull] TyhpParser.PropertyHookModifiersGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "property-hook-modifiers-grammar-addon", context.GetType().Name
            );
            return PhpModifierListAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpPropertyHookAst VisitPropertyHook([NotNull] TyhpParser.PropertyHookContext context)
        {
            var isExpressionBody = context.AccessorBody?.Expr != null;
            return PhpPropertyHookAst.Create(
                this.GetTokenValueAst(context, context.AccessorName)?.ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitPropertyHookModifiers(context.Modifiers),
                this.VisitOptionalParameterList(context.Parameters),
                this.VisitPropertyHookBody(context.AccessorBody),
                context,
                languageMode: null,
                isExpressionBody: isExpressionBody
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }

        public override PhpStatementBlockAst? VisitPropertyHookBody([NotNull] TyhpParser.PropertyHookBodyContext context)
        {
            if (context.StatementList != null)
            {
                return this.VisitInnerStatementList(context.StatementList);
            }
            else if (context.Expr != null)
            {
                // Arrow function style: => expr;
                var returnStmt = PhpUnaryOpAst.Create(
                    TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                    this.VisitExpr(context.Expr),
                    context
                );
                return PhpStatementBlockAst.Create([returnStmt], context);
            }
            else if (context.Start?.Type == TyhpParser.T_SYM_SEMICOLON)
            {
                // Abstract / interface hooks terminate with `;` and have no body (php-src
                // property_hook_body: ';'). Model absence as null, same as VisitMethodBody.
                return null;
            }
            else if (context.propertyHookBodyGrammarAddon() != null)
            {
                return this.VisitPropertyHookBodyGrammarAddon(context.propertyHookBodyGrammarAddon());
            }
            this.ReportUnexpectedAlternative(context, "property-hook-body");
            return PhpStatementBlockAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpStatementBlockAst VisitPropertyHookBodyGrammarAddon([NotNull] TyhpParser.PropertyHookBodyGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "property-hook-body-grammar-addon", context.GetType().Name
            );
            return PhpStatementBlockAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpParameterListAst? VisitOptionalParameterList([NotNull] TyhpParser.OptionalParameterListContext context)
            => context.ParameterList != null ? this.VisitParameterList(context.ParameterList) : null;

        public override PhpConstDeclListAst VisitClassConstList([NotNull] TyhpParser.ClassConstListContext context)
            => this.VisitClassConstList(context, modifiers: null, type: null);

        private PhpConstDeclListAst VisitClassConstList(
            [NotNull] TyhpParser.ClassConstListContext context,
            PhpModifierListAst? modifiers,
            ITypeExpression? type = null)
            => PhpConstDeclListAst.Create(
                context._Items.Select(item => this.VisitClassConstDecl(item, modifiers, type)),
                context
            );

        public override PhpConstDeclAst VisitClassConstDecl([NotNull] TyhpParser.ClassConstDeclContext context)
            => this.VisitClassConstDecl(context, modifiers: null, type: null);

        private PhpConstDeclAst VisitClassConstDecl(
            [NotNull] TyhpParser.ClassConstDeclContext context,
            PhpModifierListAst? modifiers,
            ITypeExpression? type)
        {
            string name = "";
            if (context.Identifier != null)
            {
                name = this.GetTokenValueAst(context, context.Identifier)?.ValueString ?? "";
            }
            else if (context.IdentifierSR != null)
            {
                name = this.VisitSemiReserved(context.IdentifierSR).ValueString ?? "";
            }

            var docComment = this.FindPossibleDocComment(context._findDocComment);

            return PhpConstDeclAst.Create(
                name,
                this.VisitExpr(context.ValueExpr),
                docComment,
                context,
                modifiers: modifiers,
                type: type
            );
        }

        public override PhpObjectTypeDeclAst VisitAnonymousClass([NotNull] TyhpParser.AnonymousClassContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                null, // null here means we are creating an anonymous class
                context.Modifiers != null ? this.VisitClassModifiersOptional(context.Modifiers) : null,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            );
        }

        public override PhpArgumentListAst? VisitCtorArguments([NotNull] TyhpParser.CtorArgumentsContext context)
            => context.ArgumentList != null ? this.VisitArgumentList(context.ArgumentList) : null;

        /// <summary>
        /// Visits a class/trait/interface/enum body, or reports
        /// <see cref="MessageCode.VisitorMissingRequiredNode"/> and returns an empty body when
        /// ANTLR error recovery left <c>StatementList</c> null (e.g. a reserved keyword used as a
        /// type name: <c>class Struct { … }</c>).
        /// </summary>
        protected PhpClassBodyAst VisitClassStatementListOrEmpty(
            TyhpParser.ClassStatementListContext? statementList,
            ParserRuleContext parentContext)
        {
            if (statementList is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename,
                    parentContext.Start?.Line ?? 0,
                    parentContext.Start?.Column ?? 0,
                    "classStatementList");
                return PhpClassBodyAst.Create(null, parentContext);
            }

            return this.VisitClassStatementList(statementList);
        }

        /// <summary>
        /// Builds the decl-kind token (<c>class</c>/<c>trait</c>/…), or an error placeholder when
        /// recovery left <c>ObjectType</c> null.
        /// </summary>
        protected TokenValueAst CreateObjectTypeToken(IToken? objectType, ParserRuleContext context)
        {
            if (objectType is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename,
                    context.Start?.Line ?? 0,
                    context.Start?.Column ?? 0,
                    "objectType");
                return TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return TokenValueAst.Create(objectType, context);
        }
    }
}