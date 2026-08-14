namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override TokenValueAst VisitReservedNonModifiers([NotNull] TyhpParser.ReservedNonModifiersContext context)
        {
            var result = context.RNM != null ?
                this.VisitReservedNonModifiersWithoutConstructor(context.RNM) :
                this.GetTokenValueAst(context, context.TokenValue);
            if (result == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "reserved-non-modifiers", context.GetType().Name
                );
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return result;
        }

        public override TokenValueAst VisitReservedNonModifiersWithoutConstructor([NotNull] TyhpParser.ReservedNonModifiersWithoutConstructorContext context)
        {
            var result = context.RNM != null ?
                this.VisitReservedNonModifiersBase(context.RNM) :
                this.VisitReservedNonModifiersGrammarAddon(context.RNMGrammarAddon);
            if (result == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "reserved-non-modifiers-without-constructor", context.GetType().Name
                );
                return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return result;
        }

        public override TokenValueAst VisitReservedNonModifiersGrammarAddon([NotNull] TyhpParser.ReservedNonModifiersGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "reserved-non-modifiers-grammar-addon", context.GetType().Name
            );
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override TokenValueAst VisitReservedNonModifiersBase([NotNull] TyhpParser.ReservedNonModifiersBaseContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        public override TokenValueAst VisitSemiReserved([NotNull] TyhpParser.SemiReservedContext context)
        {
            if (context.RNM != null) return this.VisitReservedNonModifiers(context.RNM);
            if (context.SemiReserved != null) return this.VisitSemiReservedBase(context.SemiReserved);
            if (context.SemiReservedGrammarAddon != null) return this.VisitSemiReservedGrammarAddon(context.SemiReservedGrammarAddon);

            this.ReportUnexpectedAlternative(context, "semi-reserved");
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override TokenValueAst VisitSemiReservedWithoutConstructor([NotNull] TyhpParser.SemiReservedWithoutConstructorContext context)
        {
            if (context.RNM != null) return this.VisitReservedNonModifiersWithoutConstructor(context.RNM);
            if (context.SemiReserved != null) return this.VisitSemiReservedBase(context.SemiReserved);
            if (context.SemiReservedGrammarAddon != null) return this.VisitSemiReservedGrammarAddon(context.SemiReservedGrammarAddon);

            this.ReportUnexpectedAlternative(context, "semi-reserved-without-constructor");
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override TokenValueAst VisitSemiReservedGrammarAddon([NotNull] TyhpParser.SemiReservedGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "semi-reserved-grammar-addon", context.GetType().Name
            );
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override TokenValueAst VisitSemiReservedBase([NotNull] TyhpParser.SemiReservedBaseContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        public override TokenValueAst VisitAmpersand([NotNull] TyhpParser.AmpersandContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        public override PhpNameAst VisitIdentifier([NotNull] TyhpParser.IdentifierContext context)
        {
            if (context.TokenValue != null) return PhpNameAst.Create(context.TokenValue, context);
            if (context.SemiReserved != null) return PhpNameAst.Create(this.VisitSemiReserved(context.SemiReserved).ValueString ?? "", 0, context);

            this.ReportUnexpectedAlternative(context, "identifier");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitIdentifierWithoutConstructor([NotNull] TyhpParser.IdentifierWithoutConstructorContext context)
        {
            if (context.TokenValue != null) return PhpNameAst.Create(context.TokenValue, context);
            if (context.SemiReserved != null) return PhpNameAst.Create(this.VisitSemiReservedWithoutConstructor(context.SemiReserved).ValueString ?? "", 0, context);

            this.ReportUnexpectedAlternative(context, "identifier-without-constructor");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitNamespaceDeclarationName([NotNull] TyhpParser.NamespaceDeclarationNameContext context)
        {
            if (context.Name != null) return this.VisitIdentifier(context.Name);
            if (context.QualifiedName != null) return PhpNameAst.Create(context.QualifiedName, context);

            this.ReportUnexpectedAlternative(context, "namespace-declaration-name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitNamespaceName([NotNull] TyhpParser.NamespaceNameContext context)
        {
            if (context.Name != null) return PhpNameAst.Create(context.Name, context);
            if (context.QualifiedName != null) return PhpNameAst.Create(context.QualifiedName, context);
            if (context.namespaceNameGrammarAddon() != null) return this.VisitNamespaceNameGrammarAddon(context.namespaceNameGrammarAddon());

            this.ReportUnexpectedAlternative(context, "namespace-name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitNamespaceNameGrammarAddon([NotNull] TyhpParser.NamespaceNameGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "namespace-name-grammar-addon", context.GetType().Name
            );
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitLegacyNamespaceName([NotNull] TyhpParser.LegacyNamespaceNameContext context)
        {
            if (context.Name != null) return this.VisitNamespaceName(context.Name);
            if (context.FullyQualifiedName != null) return PhpNameAst.Create(context.FullyQualifiedName, context);
            if (context.legacyNamespaceNameGrammarAddon() != null) return this.VisitLegacyNamespaceNameGrammarAddon(context.legacyNamespaceNameGrammarAddon());

            this.ReportUnexpectedAlternative(context, "legacy-namespace-name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitLegacyNamespaceNameGrammarAddon([NotNull] TyhpParser.LegacyNamespaceNameGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "legacy-namespace-name-grammar-addon", context.GetType().Name
            );
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public PhpNameAst VisitName([NotNull] TyhpParser.NameContext context)
            => context switch {
                TyhpParser.NameNotQualifiedContext nameNotQualifiedContext => this.VisitNameNotQualified(nameNotQualifiedContext),
                TyhpParser.NameSemiQualifiedContext nameSemiQualifiedContext => this.VisitNameSemiQualified(nameSemiQualifiedContext),
                TyhpParser.NameFullyQualifiedContext nameFullyQualifiedContext => this.VisitNameFullyQualified(nameFullyQualifiedContext),
                TyhpParser.NameRelativeContext nameRelativeContext => this.VisitNameRelative(nameRelativeContext),
                TyhpParser.NameTokenValueGrammarAddonHandlerContext nameTokenValueGrammarAddonHandlerContext => this.VisitNameTokenValueGrammarAddonHandler(nameTokenValueGrammarAddonHandlerContext),
                _ => this.VisitNameAlt(context),
            };

        public virtual PhpNameAst VisitNameAlt(TyhpParser.NameContext context)
        {
            this.ReportUnexpectedAlternative(context, "name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitNameNotQualified([NotNull] TyhpParser.NameNotQualifiedContext context)
            => PhpNameAst.Create(context.TokenValue, context);

        public override PhpNameAst VisitNameSemiQualified([NotNull] TyhpParser.NameSemiQualifiedContext context)
            => PhpNameAst.Create(context.TokenValue, context);

        public override PhpNameAst VisitNameFullyQualified([NotNull] TyhpParser.NameFullyQualifiedContext context)
            => PhpNameAst.Create(context.TokenValue, context);

        public override PhpNameAst VisitNameRelative([NotNull] TyhpParser.NameRelativeContext context)
            => PhpNameAst.Create(context.TokenValue, context);

        public override PhpNameAst VisitNameTokenValueGrammarAddonHandler([NotNull] TyhpParser.NameTokenValueGrammarAddonHandlerContext context)
            => this.VisitNameTokenValueGrammarAddon(context.TokenValueGrammarAddon);

        public override PhpNameAst VisitNameTokenValueGrammarAddon([NotNull] TyhpParser.NameTokenValueGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "name-token-value-grammar-addon", context.GetType().Name
            );
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNameAst VisitClassName([NotNull] TyhpParser.ClassNameContext context)
        {
            PhpNameAst result;
            if (context.Identifier != null)
            {
                result = this.VisitName(context.Identifier);
            }
            else if (context.IsStatic != null)
            {
                result = PhpNameAst.Create(context.IsStatic, context);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "class-name");
                result = PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return result.WithGrammarAddon("identifier", this.VisitClassNameIdentifierGrammarAddon(context.classNameIdentifierGrammarAddon()));
        }

        public override IBase2Ast? VisitClassNameIdentifierGrammarAddon([NotNull] TyhpParser.ClassNameIdentifierGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "class-name-identifier-grammar-addon", context.GetType().Name
            );
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override Ast.Interfaces.IClassNameReference VisitClassNameReference([NotNull] TyhpParser.ClassNameReferenceContext context)
        {
            if (context.ClassName != null)
            {
                return this.VisitClassName(context.ClassName);
            }
            if (context.NewVariable != null)
            {
                return this.VisitNewVariable(context.NewVariable)
                    .WithGrammarAddon("identifier", this.VisitClassNameIdentifierGrammarAddon(context.classNameIdentifierGrammarAddon()));
            }
            if (context.Expr != null)
            {
                return this.VisitExpr(context.Expr)
                    .WithGrammarAddon("identifier", this.VisitClassNameIdentifierGrammarAddon(context.classNameIdentifierGrammarAddon()));
            }

            this.ReportUnexpectedAlternative(context, "class-name-reference");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public Ast.Interfaces.IExpression VisitNewVariable([NotNull] TyhpParser.NewVariableContext context)
            => context switch {
                TyhpParser.NewVariableSimpleContext newVariableSimpleContext => this.VisitNewVariableSimple(newVariableSimpleContext),
                TyhpParser.NewVariableArrayIndexContext newVariableArrayIndexContext => this.VisitNewVariableArrayIndex(newVariableArrayIndexContext),
                TyhpParser.NewVariablePropertyContext newVariablePropertyContext => this.VisitNewVariableProperty(newVariablePropertyContext),
                TyhpParser.NewVariableStaticPropertyContext newVariableStaticPropertyContext => this.VisitNewVariableStaticProperty(newVariableStaticPropertyContext),
                _ => this.VisitNewVariableAlt(context),
            };

        public virtual Ast.Interfaces.IExpression VisitNewVariableAlt(TyhpParser.NewVariableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-variable");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
    }
}