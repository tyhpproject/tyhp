using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpImportDeclListAst : NodeListAst<PhpImportDeclAst, PhpImportDeclListAst>, IStatement
    {
        private const short IS_GLOBAL_FLAG = -30;

        /// <summary>
        /// <c>global use</c> (C# <c>global using</c>): applies to the entire compilation.
        /// </summary>
        public bool IsGlobal
        {
            get => HasFlag(IS_GLOBAL_FLAG);
            set => SetFlag(IS_GLOBAL_FLAG, value);
        }

        public PhpImportDeclListAst MarkGlobal()
        {
            IsGlobal = true;
            foreach (var import in GetAllNotNull())
            {
                import.IsGlobal = true;
            }

            return this;
        }
    }
} 