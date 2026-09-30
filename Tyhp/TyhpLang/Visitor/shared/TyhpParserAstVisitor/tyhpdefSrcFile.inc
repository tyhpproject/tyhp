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
        /// Entry point for parsing Tyhpdef source files.
        /// Grammar: tyhpdefSrcFile
        ///   : TyhpdefBlock=tyhpdefBlock EOF   #tyhpdefFile
        /// </summary>
        public override TyhpdefSrcFileAst VisitTyhpdefFile([NotNull] TyhpParser.TyhpdefFileContext context)
        {
            // Missing/invalid open tag leaves TyhpdefBlock null after recovery — do not NRE.
            if (context.TyhpdefBlock == null)
            {
                return TyhpdefSrcFileAst.Create(this._filename, this._fileHash, null, null, null);
            }

            var block = this.VisitTyhpdefBlock(context.TyhpdefBlock);
            return TyhpdefSrcFileAst.Create(
                this._filename,
                this._fileHash,
                null,
                block != null ? [block] : null,
                null
            );
        }

        /// <summary>
        /// Visits a tyhpdef block (tyhpdef open tag followed by statement list).
        /// Grammar: tyhpdefBlock
        ///   : T_TYHPDEF_OPEN_TAG StatementList=tyhpdefTopStatementList
        /// </summary>
        public override PhpTopStatementListAst VisitTyhpdefBlock([NotNull] TyhpParser.TyhpdefBlockContext context)
        {
            if (context.StatementList == null)
            {
                return PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefTopStatementList(context.StatementList);
        }

        /// <summary>
        /// Entry point for parsing tagless Tyhpdef source files (source.tagless enabled).
        /// Grammar: tyhpdefTaglessSrcFile
        ///   : T_TYHPDEF_OPEN_TAG? StatementList=tyhpdefTopStatementList EOF   #tyhpdefTaglessFile
        /// The open tag is optional; the whole file is a single Tyhpdef block.
        /// </summary>
        public override TyhpdefSrcFileAst VisitTyhpdefTaglessFile([NotNull] TyhpParser.TyhpdefTaglessFileContext context)
        {
            var block = context.StatementList != null
                ? this.VisitTyhpdefTopStatementList(context.StatementList)
                : PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            return TyhpdefSrcFileAst.Create(
                this._filename,
                this._fileHash,
                null,
                block != null ? [block] : null,
                null
            );
        }
