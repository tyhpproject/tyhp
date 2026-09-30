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
        /// Dispatches tyhpdefAttributedStatement labeled alternatives.
        /// Grammar: tyhpdefAttributedStatement
        ///   : Statement=tyhpdefExternClassDeclarationStatement              #tyhpdefExternClassDecl
        ///   | Statement=tyhpdefExternInterfaceDeclarationStatement          #tyhpdefExternInterfaceDecl
        ///   | Statement=tyhpdefExternEnumDeclarationStatement               #tyhpdefExternEnumDecl
        ///   | Statement=tyhpdefExternFunctionDeclarationStatement           #tyhpdefExternFunctionDecl
        ///   | Statement=tyhpdefExternConstDeclarationStatement              #tyhpdefExternConstDecl
        ///   | Statement=tyhpdefExternDeclarationStatement                   #tyhpdefExternDecl
        ///   | Statement=tyhpdefImportFunctionDeclarationStatement           #tyhpdefImportFunctionDecl
        ///   | Statement=tyhpdefImportClassDeclarationStatement              #tyhpdefImportClassDecl
        ///   | Statement=tyhpdefImportTraitDeclarationStatement              #tyhpdefImportTraitDecl
        ///   | Statement=tyhpdefImportInterfaceDeclarationStatement          #tyhpdefImportInterfaceDecl
        ///   | Statement=tyhpdefImportEnumDeclarationStatement               #tyhpdefImportEnumDecl
        /// </summary>
        public IAttributedStatement? VisitTyhpdefAttributedStatement(TyhpParser.TyhpdefAttributedStatementContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.TyhpdefExternClassDeclContext ctx => this.VisitTyhpdefExternClassDecl(ctx),
                TyhpParser.TyhpdefExternInterfaceDeclContext ctx => this.VisitTyhpdefExternInterfaceDecl(ctx),
                TyhpParser.TyhpdefExternEnumDeclContext ctx => this.VisitTyhpdefExternEnumDecl(ctx),
                TyhpParser.TyhpdefExternFunctionDeclContext ctx => this.VisitTyhpdefExternFunctionDecl(ctx),
                TyhpParser.TyhpdefExternConstDeclContext ctx => this.VisitTyhpdefExternConstDecl(ctx),
                TyhpParser.TyhpdefExternDeclContext ctx => this.VisitTyhpdefExternDecl(ctx),
                TyhpParser.TyhpdefPartialFunctionDeclContext ctx => this.VisitTyhpdefPartialFunctionDecl(ctx),
                TyhpParser.TyhpdefImportFunctionDeclContext ctx => this.VisitTyhpdefImportFunctionDecl(ctx),
                TyhpParser.TyhpdefImportClassDeclContext ctx => this.VisitTyhpdefImportClassDecl(ctx),
                TyhpParser.TyhpdefImportTraitDeclContext ctx => this.VisitTyhpdefImportTraitDecl(ctx),
                TyhpParser.TyhpdefImportInterfaceDeclContext ctx => this.VisitTyhpdefImportInterfaceDecl(ctx),
                TyhpParser.TyhpdefImportEnumDeclContext ctx => this.VisitTyhpdefImportEnumDecl(ctx),
                _ => HandleUnexpectedAlternative<IAttributedStatement>(context, "tyhpdefAttributedStatement")
            };
        }
