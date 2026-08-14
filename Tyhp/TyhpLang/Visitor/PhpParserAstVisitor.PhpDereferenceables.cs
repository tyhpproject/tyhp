namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Enum;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public PhpNewAst VisitNewDereferenceable([NotNull] TyhpParser.NewDereferenceableContext context)
            => context switch {
                TyhpParser.NewClassInstanceContext newClassInstanceContext => this.VisitNewClassInstance(newClassInstanceContext),
                TyhpParser.NewAnonClassInstanceContext newAnonClassInstanceContext => this.VisitNewAnonClassInstance(newAnonClassInstanceContext),
                TyhpParser.NewDereferenceableGrammarAddonHandlerContext newDereferenceableGrammarAddonHandlerContext => this.VisitNewDereferenceableGrammarAddonHandler(newDereferenceableGrammarAddonHandlerContext),
                _ => this.VisitNewDereferenceableAlt(context),
            };

        public virtual PhpNewAst VisitNewDereferenceableAlt(TyhpParser.NewDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-dereferenceable");
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNewAst VisitNewClassInstance([NotNull] TyhpParser.NewClassInstanceContext context)
            => PhpNewAst.Create(
                this.VisitClassNameReference(context.Identifier),
                this.VisitArgumentList(context.argumentList()),
                context
            );

        public override PhpNewAst VisitNewAnonClassInstance([NotNull] TyhpParser.NewAnonClassInstanceContext context)
            => PhpNewAst.CreateAnonymous(
                this.VisitAnonymousClass(context.anonymousClass()),
                this.VisitCtorArguments(context.anonymousClass().ctorArguments()),
                context
            );

        public override PhpNewAst VisitNewDereferenceableGrammarAddonHandler([NotNull] TyhpParser.NewDereferenceableGrammarAddonHandlerContext context)
            => this.VisitNewDereferenceableGrammarAddon(context.newDereferenceableGrammarAddon());

        public virtual PhpNewAst VisitNewDereferenceableGrammarAddon([NotNull] TyhpParser.NewDereferenceableGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "new-dereferenceable-grammar-addon", context.GetType().Name
            );
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public PhpNewAst VisitNewNonDereferenceable([NotNull] TyhpParser.NewNonDereferenceableContext context)
            => context switch {
                TyhpParser.NewClassInstanceNonDereferenceableContext newClassInstanceNonDereferenceableContext => this.VisitNewClassInstanceNonDereferenceable(newClassInstanceNonDereferenceableContext),
                TyhpParser.NewNonDereferenceableGrammarAddonHandlerContext newNonDereferenceableGrammarAddonHandlerContext => this.VisitNewNonDereferenceableGrammarAddonHandler(newNonDereferenceableGrammarAddonHandlerContext),
                _ => this.VisitNewNonDereferenceableAlt(context),
            };
        
        public virtual PhpNewAst VisitNewNonDereferenceableAlt(TyhpParser.NewNonDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-non-dereferenceable");
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpNewAst VisitNewClassInstanceNonDereferenceable([NotNull] TyhpParser.NewClassInstanceNonDereferenceableContext context)
            => PhpNewAst.Create(
                this.VisitClassNameReference(context.Identifier),
                null,
                context
            );

        public override PhpNewAst VisitNewNonDereferenceableGrammarAddonHandler([NotNull] TyhpParser.NewNonDereferenceableGrammarAddonHandlerContext context)
            => this.VisitNewNonDereferenceableGrammarAddon(context.newNonDereferenceableGrammarAddon());

        public override PhpNewAst VisitNewNonDereferenceableGrammarAddon([NotNull] TyhpParser.NewNonDereferenceableGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "new-non-dereferenceable-grammar-addon", context.GetType().Name
            );
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IDereferenceableBase VisitDereferenceableScalar([NotNull] TyhpParser.DereferenceableScalarContext context)
        {
            if (context.ArrayPairList != null)
            {
                return this.VisitArrayPairList(context.ArrayPairList);
            }
            if (context.TokenValue.Type == TyhpParser.T_CONSTANT_ENCAPSED_STRING)
            {
                return PhpEncapsListAst.Create([
                    PhpEncapsStringAst.Create(
                        this.GetTokenValueAst(context, context.TokenValue),
                        context
                    )
                ], context).WithStringType(this.GetTokenValueAst(context, context.TokenValue));
            }
            if (((int[])[
                TyhpParser.T_BINARY_DOUBLE_QUOTE,
                TyhpParser.T_DOUBLE_QUOTE,
                TyhpParser.T_BACKQUOTE,
                TyhpParser.T_BINARY_BACKQUOTE
            ]).Contains(context.TokenValue.Type))
            {
                return (context.EncapsList != null ?
                    this.VisitEncapsList(context.EncapsList) :
                    PhpEncapsListAst.Create([], context)
                )
                    .WithStringType(this.GetTokenValueAst(context, context.TokenValue));
            }

            this.ReportUnexpectedAlternative(context, "dereferenceable-scalar");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public IScalar VisitScalar([NotNull] TyhpParser.ScalarContext context)
            => context switch {
                TyhpParser.ScalarRealContext scalarRealContext => this.VisitScalarReal(scalarRealContext),
                TyhpParser.ScalarDereferenceableContext scalarDereferenceableContext => this.VisitScalarDereferenceable(scalarDereferenceableContext),
                TyhpParser.ScalarConstantContext scalarConstantContext => this.VisitScalarConstant(scalarConstantContext),
                TyhpParser.ScalarClassConstantContext scalarClassConstantContext => this.VisitScalarClassConstant(scalarClassConstantContext),
                _ => this.VisitScalarAlt(context),
            };

        public virtual IScalar VisitScalarAlt(TyhpParser.ScalarContext context)
        {
            this.ReportUnexpectedAlternative(context, "scalar");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IScalar VisitScalarReal([NotNull] TyhpParser.ScalarRealContext context)
            => this.VisitRealScalar(context.RealScalar);

        public override IDereferenceableBase VisitScalarDereferenceable([NotNull] TyhpParser.ScalarDereferenceableContext context)
            => this.VisitDereferenceableScalar(context.Scalar);

        public override IScalar VisitScalarConstant([NotNull] TyhpParser.ScalarConstantContext context)
            => this.VisitConstant(context.Scalar);

        public override PhpDereferenceableAst VisitScalarClassConstant([NotNull] TyhpParser.ScalarClassConstantContext context)
            => this.VisitClassConstant(context.Scalar);

        public IScalar VisitRealScalar([NotNull] TyhpParser.RealScalarContext context)
            => context switch {
                TyhpParser.ScalarLNumberContext scalarLNumberContext => this.VisitScalarLNumber(scalarLNumberContext),
                TyhpParser.ScalarDNumberContext scalarDNumberContext => this.VisitScalarDNumber(scalarDNumberContext),
                TyhpParser.ScalarONumberContext scalarONumberContext => this.VisitScalarONumber(scalarONumberContext),
                TyhpParser.ScalarHNumberContext scalarHNumberContext => this.VisitScalarHNumber(scalarHNumberContext),
                TyhpParser.ScalarBNumberContext scalarBNumberContext => this.VisitScalarBNumber(scalarBNumberContext),
                TyhpParser.ScalarHeredocContext scalarHeredocContext => this.VisitScalarHeredoc(scalarHeredocContext),
                _ => this.VisitRealScalarAlt(context),
            };

        public virtual IScalar VisitRealScalarAlt(TyhpParser.RealScalarContext context)
        {
            this.ReportUnexpectedAlternative(context, "real-scalar");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpScalarAst VisitScalarLNumber([NotNull] TyhpParser.ScalarLNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.Integer,
                context
            );

        public override PhpScalarAst VisitScalarDNumber([NotNull] TyhpParser.ScalarDNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.Float,
                context
            );

        public override PhpScalarAst VisitScalarONumber([NotNull] TyhpParser.ScalarONumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.OctalNumber,
                context
            );

        public override PhpScalarAst VisitScalarHNumber([NotNull] TyhpParser.ScalarHNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.HexNumber,
                context
            );

        public override PhpScalarAst VisitScalarBNumber([NotNull] TyhpParser.ScalarBNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.BinaryNumber,
                context
            );

        public override PhpEncapsListAst VisitScalarHeredoc([NotNull] TyhpParser.ScalarHeredocContext context)
            => (context.EncapsList != null ?
                this.VisitEncapsList(context.EncapsList) :
                PhpEncapsListAst.Create([], context)
            )
                .WithStringType(this.GetTokenValueAst(context, context.TokenValue));

        public override IScalar VisitConstant([NotNull] TyhpParser.ConstantContext context)
        {
            if (context.Identifier != null) return this.VisitName(context.Identifier);
            if (context.TokenValue != null) return this.VisitConstantTokenValue(context.TokenValue);

            this.ReportUnexpectedAlternative(context, "constant");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpMagicConstantAst VisitConstantTokenValue([NotNull] TyhpParser.ConstantTokenValueContext context)
        {
            if (context.TokenValue != null) return PhpMagicConstantAst.Create(context.TokenValue, context);
            if (context.TokenValueGrammarAddon != null) return this.VisitConstantTokenValueGrammarAddon(context.TokenValueGrammarAddon);

            this.ReportUnexpectedAlternative(context, "constant-token-value");
            return PhpMagicConstantAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpMagicConstantAst VisitConstantTokenValueGrammarAddon([NotNull] TyhpParser.ConstantTokenValueGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "constant-token-value-grammar-addon", context.GetType().Name
            );
            return PhpMagicConstantAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpVariableAst VisitSimpleVariable([NotNull] TyhpParser.SimpleVariableContext context)
        {
            if (context.Variable != null) return PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context);
            if (context.DoubleDollarVariable != null) return PhpVariableAst.Create(this.VisitSimpleVariable(context.DoubleDollarVariable), false, context);
            if (context.BracedExpr != null) return PhpVariableAst.Create(this.VisitExpr(context.BracedExpr), false, context);
            if (context.simpleVariableGrammarAddon() != null) return this.VisitSimpleVariableGrammarAddon(context.simpleVariableGrammarAddon());

            this.ReportUnexpectedAlternative(context, "simple-variable");
            return PhpVariableAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpVariableAst VisitSimpleVariableGrammarAddon([NotNull] TyhpParser.SimpleVariableGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "simple-variable-grammar-addon", context.GetType().Name
            );
            return PhpVariableAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpDereferenceableAst VisitClassConstant([NotNull] TyhpParser.ClassConstantContext context)
        {
            IDereferenceableBase prefix;
            if (context.ClassName != null)
            {
                prefix = this.VisitClassName(context.ClassName);
            }
            else if (context.Prefix != null)
            {
                prefix = this.VisitVariableClassName(context.Prefix);
            }
            else
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "class-constant-prefix", context.GetType().Name
                );
                prefix = ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
            return PhpDereferenceableAst.Create(
                prefix,
                this.VisitDereferenceableClassConstantAccessSuffix(context.Suffix),
                context
            );
        }

        public override IDereferenceableBase VisitVariableClassName([NotNull] TyhpParser.VariableClassNameContext context)
            => this.VisitFullyDereferenceable(context.fullyDereferenceable());

        public override PhpCallAst VisitCallArgumentList([NotNull] TyhpParser.CallArgumentListContext context)
            => PhpCallAst.Create(
                context.ArgumentList != null ?
                    this.VisitArgumentList(context.ArgumentList) ?? PhpArgumentListAst.Create([], context) :
                    PhpArgumentListAst.Create([], context),
                context
            ).WithGrammarAddon("functionCall", this.VisitFunctionCallGrammarAddon(context.functionCallGrammarAddon()));

        public override PhpArgumentListAst? VisitFunctionCallGrammarAddon([NotNull] TyhpParser.FunctionCallGrammarAddonContext context)
            => null;

        public IDereferenceableBase VisitFullyDereferenceable([NotNull] TyhpParser.FullyDereferenceableContext context)
            => context switch {
                TyhpParser.FullyDereferenceableDRefSuffixContext fullyDereferenceableDRefSuffixContext => this.VisitFullyDereferenceableDRefSuffix(fullyDereferenceableDRefSuffixContext),
                TyhpParser.DereferenceableSimpleContext dereferenceableSimpleContext => this.VisitDereferenceableSimple(dereferenceableSimpleContext),
                TyhpParser.DereferenceableConstantContext dereferenceableConstantContext => this.VisitDereferenceableConstant(dereferenceableConstantContext),
                TyhpParser.DereferenceableClassNamePrefixContext dereferenceableClassNamePrefixContext => this.VisitDereferenceableClassNamePrefix(dereferenceableClassNamePrefixContext),
                TyhpParser.DereferenceableReadOnlyContext dereferenceableReadOnlyContext => this.VisitDereferenceableReadOnly(dereferenceableReadOnlyContext),
                TyhpParser.DereferenceableScalarRefContext dereferenceableScalarRefContext => this.VisitDereferenceableScalarRef(dereferenceableScalarRefContext),
                TyhpParser.DereferenceableNewDRefContext dereferenceableNewDRefContext => this.VisitDereferenceableNewDRef(dereferenceableNewDRefContext),
                TyhpParser.DereferenceableExprContext dereferenceableExprContext => this.VisitDereferenceableExpr(dereferenceableExprContext),
                TyhpParser.DereferenceableBaseGrammarAddonHandlerContext dereferenceableBaseGrammarAddonContext => this.VisitDereferenceableBaseGrammarAddonHandler(dereferenceableBaseGrammarAddonContext),
                _ => this.VisitFullyDereferenceableAlt(context)
            };

        public virtual IDereferenceableBase VisitFullyDereferenceableAlt(TyhpParser.FullyDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "fully-dereferenceable");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpDereferenceableAst VisitFullyDereferenceableDRefSuffix([NotNull] TyhpParser.FullyDereferenceableDRefSuffixContext context)
            => PhpDereferenceableAst.Create(
                this.VisitFullyDereferenceable(context.DRef),
                this.VisitFullyDereferenceableSuffix(context.Suffix),
                context
            );

        public override IDereferenceableBase VisitDereferenceableSimple([NotNull] TyhpParser.DereferenceableSimpleContext context)
            => this.VisitSimpleVariable(context.Variable);

        public override IDereferenceableBase VisitDereferenceableConstant([NotNull] TyhpParser.DereferenceableConstantContext context)
            => this.VisitConstantTokenValue(context.Constant);

        public override IDereferenceableBase VisitDereferenceableClassNamePrefix([NotNull] TyhpParser.DereferenceableClassNamePrefixContext context)
            => this.VisitClassName(context.ClassName);

        public override IDereferenceableBase VisitDereferenceableReadOnly([NotNull] TyhpParser.DereferenceableReadOnlyContext context)
           => PhpNameAst.Create(context.IsReadOnlyPrefix, context);

        public override IDereferenceableBase VisitDereferenceableScalarRef([NotNull] TyhpParser.DereferenceableScalarRefContext context)
            => this.VisitDereferenceableScalar(context.Scalar);

        public override IDereferenceableBase VisitDereferenceableNewDRef([NotNull] TyhpParser.DereferenceableNewDRefContext context)
            => this.VisitNewDereferenceable(context.NewDRef);

        public override PhpDereferenceableExpressionAst VisitDereferenceableExpr([NotNull] TyhpParser.DereferenceableExprContext context)
            => PhpDereferenceableExpressionAst.Create(this.VisitExpr(context.Expr), context);

        public override IDereferenceableBase VisitDereferenceableBaseGrammarAddonHandler([NotNull] TyhpParser.DereferenceableBaseGrammarAddonHandlerContext context)
            => this.VisitDereferenceableBaseGrammarAddon(context.dereferenceableBaseGrammarAddon());

        public override IDereferenceableBase VisitDereferenceableBaseGrammarAddon([NotNull] TyhpParser.DereferenceableBaseGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "dereferenceable-base-grammar-addon", context.GetType().Name
            );
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public IDereferenceableSuffix VisitFullyDereferenceableSuffix([NotNull] TyhpParser.FullyDereferenceableSuffixContext context)
            => context switch {
                TyhpParser.DereferenceableSuffixArrayAccessContext dereferenceableSuffixArrayAccessContext => this.VisitDereferenceableSuffixArrayAccess(dereferenceableSuffixArrayAccessContext),
                TyhpParser.DereferenceableSuffixMemberAccessContext dereferenceableSuffixMemberAccessContext => this.VisitDereferenceableSuffixMemberAccess(dereferenceableSuffixMemberAccessContext),
                TyhpParser.DereferenceableSuffixStaticMemberAccessContext dereferenceableSuffixStaticMemberAccessContext => this.VisitDereferenceableSuffixStaticMemberAccess(dereferenceableSuffixStaticMemberAccessContext),
                TyhpParser.DereferenceableSuffixClassConstantAccessContext dereferenceableSuffixClassConstantAccessContext => this.VisitDereferenceableSuffixClassConstantAccess(dereferenceableSuffixClassConstantAccessContext),
                TyhpParser.DereferenceableSuffixCallAccessContext dereferenceableSuffixCallAccessContext => this.VisitDereferenceableSuffixCallAccess(dereferenceableSuffixCallAccessContext),
                TyhpParser.DereferenceableSuffixGrammarAddonHandlerContext dereferenceableSuffixGrammarAddonHandlerContext => this.VisitDereferenceableSuffixGrammarAddonHandler(dereferenceableSuffixGrammarAddonHandlerContext),
                _ => this.VisitFullyDereferenceableSuffixAlt(context)
            };

        public virtual IDereferenceableSuffix VisitFullyDereferenceableSuffixAlt(TyhpParser.FullyDereferenceableSuffixContext context)
        {
            this.ReportUnexpectedAlternative(context, "fully-dereferenceable-suffix");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpArrayAccessAst VisitDereferenceableSuffixArrayAccess([NotNull] TyhpParser.DereferenceableSuffixArrayAccessContext context)
            => this.VisitDereferenceableArrayAccessSuffix(context.SuffixArray);

        public override PhpInstanceMemberAccessAst VisitDereferenceableSuffixMemberAccess([NotNull] TyhpParser.DereferenceableSuffixMemberAccessContext context)
            => this.VisitDereferenceableMemberAccessSuffix(context.SuffixMember);

        public override PhpStaticMemberAccessAst VisitDereferenceableSuffixStaticMemberAccess([NotNull] TyhpParser.DereferenceableSuffixStaticMemberAccessContext context)
            => this.VisitDereferenceableStaticMemberAccessSuffix(context.SuffixStaticMember);

        public override PhpClassConstantAccessAst VisitDereferenceableSuffixClassConstantAccess([NotNull] TyhpParser.DereferenceableSuffixClassConstantAccessContext context)
            => this.VisitDereferenceableClassConstantAccessSuffix(context.SuffixClassConst);

        public override PhpCallAst VisitDereferenceableSuffixCallAccess([NotNull] TyhpParser.DereferenceableSuffixCallAccessContext context)
            => this.VisitCallArgumentList(context.ArgumentList);

        public override IDereferenceableSuffix VisitDereferenceableSuffixGrammarAddonHandler([NotNull] TyhpParser.DereferenceableSuffixGrammarAddonHandlerContext context)
            => this.VisitDereferenceableSuffixGrammarAddon(context.dereferenceableSuffixGrammarAddon());

        public override IDereferenceableSuffix VisitDereferenceableSuffixGrammarAddon([NotNull] TyhpParser.DereferenceableSuffixGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "dereferenceable-suffix-grammar-addon", context.GetType().Name
            );
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IDereferenceableBase VisitVariable([NotNull] TyhpParser.VariableContext context)
            => this.VisitFullyDereferenceable(context.fullyDereferenceable());

        public override PhpInstanceMemberAccessAst VisitDereferenceableMemberAccessSuffix([NotNull] TyhpParser.DereferenceableMemberAccessSuffixContext context)
            => PhpInstanceMemberAccessAst.Create(
                this.GetTokenValueAst(context, context.TokenValue),
                this.VisitMemberName(context.MemberName),
                context
            );

        public override PhpStaticMemberAccessAst VisitDereferenceableStaticMemberAccessSuffix([NotNull] TyhpParser.DereferenceableStaticMemberAccessSuffixContext context)
            => PhpStaticMemberAccessAst.Create(
                this.VisitMemberInstanceName(context.Identifier),
                context
            );

        public override PhpClassConstantAccessAst VisitDereferenceableClassConstantAccessSuffix([NotNull] TyhpParser.DereferenceableClassConstantAccessSuffixContext context)
            => PhpClassConstantAccessAst.Create(
                this.VisitMemberConstantName(context.Identifier),
                context
            );

        public override PhpArrayAccessAst VisitDereferenceableArrayAccessSuffix([NotNull] TyhpParser.DereferenceableArrayAccessSuffixContext context)
            => PhpArrayAccessAst.Create(
                this.VisitOptionalExpr(context.OptionalExpr),
                context
            );

        public override PhpVariableAst VisitNewVariableSimple([NotNull] TyhpParser.NewVariableSimpleContext context)
            => this.VisitSimpleVariable(context.Variable);

        public override PhpMemberAccessAst VisitNewVariableStaticProperty([NotNull] TyhpParser.NewVariableStaticPropertyContext context)
            => PhpMemberAccessAst.Create(
                context.ClassName != null
                    ? this.VisitClassName(context.ClassName)
                    : this.VisitNewVariable(context.NewVariable) as IExpression,
                PhpNameAst.Create("::", TyhpParser.T_DOUBLE_COLON, context),
                this.VisitSimpleVariable(context.Identifier),
                context
            );

        public override PhpMemberAccessAst VisitNewVariableArrayIndex([NotNull] TyhpParser.NewVariableArrayIndexContext context)
            => PhpMemberAccessAst.Create(
                this.VisitNewVariable(context.NewVariable),
                PhpNameAst.Create(context.TokenValue, context),
                this.VisitOptionalExpr(context.OptionalExpr),
                context
            );

        public override PhpMemberAccessAst VisitNewVariableProperty([NotNull] TyhpParser.NewVariablePropertyContext context)
            => PhpMemberAccessAst.Create(
                this.VisitNewVariable(context.NewVariable),
                PhpNameAst.Create(context.TokenValue, context),
                this.VisitMemberName(context.MemberName),
                context
            );

        public IExpression VisitMemberName([NotNull] TyhpParser.MemberNameContext context)
            => context switch {
                TyhpParser.MemberNameConstantContext memberNameConstantContext => this.VisitMemberNameConstant(memberNameConstantContext),
                TyhpParser.MemberNameInstanceContext memberNameInstanceContext => this.VisitMemberNameInstance(memberNameInstanceContext),
                _ => this.VisitMemberNameAlt(context),
            };

        public virtual IExpression VisitMemberNameAlt(TyhpParser.MemberNameContext context)
        {
            this.ReportUnexpectedAlternative(context, "member-name");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public IExpression VisitMemberConstantName([NotNull] TyhpParser.MemberConstantNameContext context)
            => context switch {
                TyhpParser.MemberNameIdentifierContext memberNameIdentifierContext => this.VisitMemberNameIdentifier(memberNameIdentifierContext),
                TyhpParser.MemberNameExprContext memberNameExprContext => this.VisitMemberNameExpr(memberNameExprContext),
                _ => this.VisitMemberConstantNameAlt(context),
            };

        public virtual IExpression VisitMemberConstantNameAlt(TyhpParser.MemberConstantNameContext context)
        {
            this.ReportUnexpectedAlternative(context, "member-constant-name");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public IExpression VisitMemberInstanceName([NotNull] TyhpParser.MemberInstanceNameContext context)
            => context switch {
                TyhpParser.MemberNameSimpleContext memberNameSimpleContext => this.VisitMemberNameSimple(memberNameSimpleContext),
                _ => this.VisitMemberInstanceNameAlt(context),
            };

        public virtual IExpression VisitMemberInstanceNameAlt(TyhpParser.MemberInstanceNameContext context)
        {
            this.ReportUnexpectedAlternative(context, "member-instance-name");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IExpression VisitMemberNameIdentifier([NotNull] TyhpParser.MemberNameIdentifierContext context)
            => this.VisitIdentifier(context.Identifier)
                .WithGrammarAddon("memberName", this.VisitMemberNameIdentifierGrammarAddon(context.memberNameIdentifierGrammarAddon()));

        public override IExpression VisitMemberNameExpr([NotNull] TyhpParser.MemberNameExprContext context)
            => this.VisitExpr(context.Expr)
                .WithGrammarAddon("memberName", this.VisitMemberNameIdentifierGrammarAddon(context.memberNameIdentifierGrammarAddon()));

        public override IExpression VisitMemberNameSimple([NotNull] TyhpParser.MemberNameSimpleContext context)
            => this.VisitSimpleVariable(context.Identifier)
                .WithGrammarAddon("memberName", this.VisitMemberNameIdentifierGrammarAddon(context.memberNameIdentifierGrammarAddon()));

        public override IExpression VisitMemberNameConstant([NotNull] TyhpParser.MemberNameConstantContext context)
            => this.VisitMemberConstantName(context.Identifier);

        public override IExpression VisitMemberNameInstance([NotNull] TyhpParser.MemberNameInstanceContext context)
            => this.VisitMemberInstanceName(context.Identifier);

        public override IBase2Ast? VisitMemberNameIdentifierGrammarAddon([NotNull] TyhpParser.MemberNameIdentifierGrammarAddonContext context)
            => null;

        public override PhpArrayPairAst? VisitPossibleArrayPair([NotNull] TyhpParser.PossibleArrayPairContext context)
            => context.ArrayPair != null ? this.VisitArrayPair(context.ArrayPair) : null;

        public PhpArrayPairAst VisitArrayPair([NotNull] TyhpParser.ArrayPairContext context)
            => context switch {
                TyhpParser.ArrayPairItemContext arrayPairItemContext => this.VisitArrayPairItem(arrayPairItemContext),
                TyhpParser.ArrayPairExpansionContext arrayPairExpansionContext => this.VisitArrayPairExpansion(arrayPairExpansionContext),
                _ => this.VisitArrayPairAlt(context),
            };

        public virtual PhpArrayPairAst VisitArrayPairAlt(TyhpParser.ArrayPairContext context)
        {
            this.ReportUnexpectedAlternative(context, "array-pair");
            return PhpArrayPairAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpArrayPairListAst VisitArrayPairList([NotNull] TyhpParser.ArrayPairListContext context)
            => PhpArrayPairListAst.Create(
                context._Items.Select(this.VisitPossibleArrayPair),
                context
            );

        public override PhpArrayPairAst VisitArrayPairItem([NotNull] TyhpParser.ArrayPairItemContext context)
            => context.isKey != null
                ? PhpArrayPairAst.Create(
                    this.VisitExpr(context.KeyOrValueExpr),
                    this.VisitExpr(context.Value),
                    false,
                    context
                )
                : PhpArrayPairAst.Create(
                    null,
                    this.VisitExpr(context.KeyOrValueExpr),
                    false,
                    context
                );

        public override PhpArrayPairAst VisitArrayPairExpansion([NotNull] TyhpParser.ArrayPairExpansionContext context)
            => PhpArrayPairAst.Create(
                null,
                this.VisitExpr(context.Expr),
                true,
                context
            );

        public override PhpEncapsListAst VisitEncapsList([NotNull] TyhpParser.EncapsListContext context)
            => PhpEncapsListAst.Create(
                context._Items.Select(this.VisitEncapsVarOrWhitespace),
                context
            );

        public override Ast.Interfaces.IEncapsVarOrString VisitEncapsVarOrWhitespace([NotNull] TyhpParser.EncapsVarOrWhitespaceContext context)
        {
            if (context.EncapsVar != null)
            {
                switch (context.EncapsVar)
                {
                    case TyhpParser.EncapsVarVariableTokenWithArrayIndexContext varWithIndex:
                        return this.VisitEncapsVarVariableTokenWithArrayIndex(varWithIndex);
                    case TyhpParser.EncapsVarObjectMemberContext objMember:
                        return this.VisitEncapsVarObjectMember(objMember);
                    case TyhpParser.EncapsVarVariableTokenContext varToken:
                        return this.VisitEncapsVarVariableToken(varToken);
                    case TyhpParser.EncapsVarDollarBraceExprContext dollarBrace:
                        return this.VisitEncapsVarDollarBraceExpr(dollarBrace);
                    case TyhpParser.EncapsVarBraceDollarExprWithArrayIndexContext braceDollar:
                        return this.VisitEncapsVarBraceDollarExprWithArrayIndex(braceDollar);
                    case TyhpParser.EncapsVarBraceVariableContext braceVar:
                        return this.VisitEncapsVarBraceVariable(braceVar);
                    default:
                        this.ReportUnexpectedAlternative(context.EncapsVar, "encaps-var");
                        return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
                }
            }

            if (context.EncapsWhitespace != null)
            {
                var tokenValue = this.GetTokenValueAst(context, context.EncapsWhitespace);
                if (tokenValue == null)
                {
                    this.Diagnostics.AddError(
                        MessageCode.VisitorMissingRequiredNode,
                        this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                        "encaps-whitespace-token", context.GetType().Name
                    );
                    return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
                }
                return PhpEncapsStringAst.Create(tokenValue, context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            this.ReportUnexpectedAlternative(context, "encaps-var-or-whitespace");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override Ast.Interfaces.IExpression VisitEncapsVarVariableTokenWithArrayIndex([NotNull] TyhpParser.EncapsVarVariableTokenWithArrayIndexContext context)
            => PhpDereferenceableAst.Create(
                PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                PhpArrayAccessAst.Create(this.VisitEncapsVarOffset(context.ArrayIndex), context),
                context
            );

        public override Ast.Interfaces.IExpression VisitEncapsVarObjectMember([NotNull] TyhpParser.EncapsVarObjectMemberContext context)
            => PhpDereferenceableAst.Create(
                PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                PhpInstanceMemberAccessAst.Create(
                    this.GetTokenValueAst(context, context.TokenValue),
                    PhpNameAst.Create(context.Identifier, context),
                    context
                ),
                context
            );

        public override Ast.Interfaces.IExpression VisitEncapsVarVariableToken([NotNull] TyhpParser.EncapsVarVariableTokenContext context)
        {
            if (context.Variable == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "encaps-var-variable-token", context.GetType().Name
                );
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context);
        }

        public override Ast.Interfaces.IExpression VisitEncapsVarDollarBraceExpr([NotNull] TyhpParser.EncapsVarDollarBraceExprContext context)
        {
            if (context.Expr != null)
                return this.VisitExpr(context.Expr);

            if (context.VarName != null)
            {
                return PhpNameAst.Create(context.VarName, context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            this.ReportUnexpectedAlternative(context, "encaps-var-dollar-brace");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override Ast.Interfaces.IExpression VisitEncapsVarBraceDollarExprWithArrayIndex([NotNull] TyhpParser.EncapsVarBraceDollarExprWithArrayIndexContext context)
            => PhpDereferenceableAst.Create(
                PhpVariableAst.Create(
                    TokenValueAst.Create("$" + (context.VarName?.Text ?? ""), TyhpParser.T_VARIABLE, context),
                    false,
                    context
                ),
                PhpArrayAccessAst.Create(this.VisitExpr(context.Expr), context),
                context
            );

        public override Ast.Interfaces.IExpression VisitEncapsVarBraceVariable([NotNull] TyhpParser.EncapsVarBraceVariableContext context)
            => this.VisitVariable(context.Variable);

        public override Ast.Interfaces.IExpression VisitEncapsVarOffset([NotNull] TyhpParser.EncapsVarOffsetContext context)
        {
            if (context.TokenValue == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "encaps-var-offset-token", context.GetType().Name
                );
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            var token = this.GetTokenValueAst(context, context.TokenValue);

            // In PHP simple-syntax interpolation the offset is one of: a numeric literal ("$v[0]"), a
            // variable ("$v[$x]"), or a bare word treated as a string key ("$v[key]"). Build the node
            // shape that emits the same unquoted offset PHP expects inside the brackets.
            switch (context.TokenValue.Type)
            {
                case TyhpParser.T_NUM_STRING:
                    var number = PhpScalarAst.Create(token, PhpScalarType.Integer, context);
                    return context.IsNegative != null
                        ? PhpUnaryOpAst.Create(this.GetTokenValueAst(context, context.IsNegative), number, context)
                        : number;
                case TyhpParser.T_VARIABLE:
                    return PhpVariableAst.Create(token, false, context);
                default:
                    return PhpNameAst.Create(context.TokenValue, context);
            }
        }
    }
}