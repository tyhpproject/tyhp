namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Dispatches the topStatementGrammarAddon labeled alternatives to
        /// their corresponding Tyhp visitor methods.
        ///
        /// Grammar (TyhpParser.g4):
        ///   topStatementGrammarAddon
        ///     : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        ///         Adaptations=traitAdaptations                                    #tyhpImportExtension
        ///     | Statement=tyhpTypeAlias                                           #tyhpTypeAliasDecl
        ///     | Statement=tyhpStructDeclarationStatement                          #tyhpStructDecl
        ///     | Statement=tyhpExtensionDeclarationStatement                       #tyhpExtensionDecl
        ///     ;
        /// </summary>
        public override Ast.Interfaces.ITopStatement VisitTopStatementGrammarAddonHandler([NotNull] TyhpParser.TopStatementGrammarAddonHandlerContext context)
        {
            var addon = context.topStatementGrammarAddon();
            return addon switch
            {
                TyhpParser.TyhpTypeAliasDeclContext c => this.VisitTyhpTypeAliasDecl(c),
                TyhpParser.TyhpStructDeclContext c => this.VisitTyhpStructDecl(c),
                TyhpParser.TyhpExtensionDeclContext c => this.VisitTyhpExtensionDecl(c),
                TyhpParser.TyhpImportExtensionContext c => this.VisitTyhpImportExtension(c),
                _ => base.VisitTopStatementGrammarAddonHandler(context),
            };
        }

        /// <summary>
        /// Visits a Tyhp extension import declaration.
        ///
        /// Grammar (TyhpParser.g4):
        ///   topStatementGrammarAddon
        ///     : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        ///         Adaptations=traitAdaptations                                    #tyhpImportExtension
        ///     ;
        ///
        /// Syntax: `use extension Foo\Bar, Baz\Qux { ... }`
        ///
        /// The UseDecl is a comma-separated list of namespace names (use declarations),
        /// and Adaptations are trait-like adaptations (precedence, aliasing) enclosed
        /// in curly braces, or a simple semicolon if no adaptations are needed.
        /// </summary>
        public override TyhpImportExtensionAst VisitTyhpImportExtension([NotNull] TyhpParser.TyhpImportExtensionContext context)
        {
            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpImportExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpImportExtension.Adaptations");
            }

            return TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        public override TyhpTypeAliasAst VisitTyhpTypeAliasDecl([NotNull] TyhpParser.TyhpTypeAliasDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpTypeAliasDecl.Statement");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpTypeAlias(context.Statement);
        }

        public override TyhpStructDeclAst VisitTyhpStructDecl([NotNull] TyhpParser.TyhpStructDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpStructDecl.Statement");
                return TyhpStructDeclAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpStructDeclarationStatement(context.Statement);
        }

        public override TyhpExtensionDeclAst VisitTyhpExtensionDecl([NotNull] TyhpParser.TyhpExtensionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpExtensionDecl.Statement");
                return TyhpExtensionDeclAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpExtensionDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Overrides the unprefixed use declaration grammar addon for Tyhp generics.
        ///
        /// Grammar (TyhpParser.g4):
        ///   unprefixedUseDeclarationGrammarAddon
        ///     : NamespaceName=namespaceName
        ///         (T_AS AliasedAs=T_STRING GenericArguments=tyhpGenericTypeArguments)
        ///     ;
        ///
        /// Syntax: `use Foo\Bar as Alias&lt;T, U&gt;`
        ///
        /// Creates a PhpImportDeclAst with the generic arguments attached
        /// as a grammar addon under the key "GenericArguments".
        /// </summary>
        public override PhpImportDeclAst VisitUnprefixedUseDeclarationGrammarAddon([NotNull] TyhpParser.UnprefixedUseDeclarationGrammarAddonContext context)
        {
            var namespaceName = this.VisitNamespaceName(context.NamespaceName);
            var aliasedAs = context.AliasedAs?.Text;
            var genericArguments = this.VisitTyhpGenericTypeArguments(context.GenericArguments);

            var result = PhpImportDeclAst.Create(
                null, // Use type will be set at higher level if needed
                namespaceName.ValueString,
                aliasedAs,
                context,
                GetCurrentLanguageMode(context)
            );

            result.AddGrammarAddon("GenericArguments", genericArguments);

            return result;
        }

        /// <summary>
        /// Overrides the use declaration grammar addon for Tyhp generics.
        ///
        /// Grammar (TyhpParser.g4):
        ///   useDeclarationGrammarAddon
        ///     : NamespaceName=legacyNamespaceName
        ///         (T_AS AliasedAs=T_STRING GenericArguments=tyhpGenericTypeArguments)
        ///     ;
        ///
        /// Syntax: `use \Foo\Bar as Alias&lt;T, U&gt;`
        ///
        /// Creates a PhpImportDeclAst with the generic arguments attached
        /// as a grammar addon under the key "GenericArguments".
        /// </summary>
        public override PhpImportDeclAst VisitUseDeclarationGrammarAddon([NotNull] TyhpParser.UseDeclarationGrammarAddonContext context)
        {
            var namespaceName = this.VisitLegacyNamespaceName(context.NamespaceName);
            var aliasedAs = context.AliasedAs?.Text;
            var genericArguments = this.VisitTyhpGenericTypeArguments(context.GenericArguments);

            var result = PhpImportDeclAst.Create(
                null, // Use type will be set at higher level if needed
                namespaceName.ValueString,
                aliasedAs,
                context,
                GetCurrentLanguageMode(context)
            );

            result.AddGrammarAddon("GenericArguments", genericArguments);

            return result;
        }
    }
}