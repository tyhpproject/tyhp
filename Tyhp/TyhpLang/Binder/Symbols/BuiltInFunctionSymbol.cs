using System.Collections.Generic;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// A compile-time-only built-in function such as <c>nameof()</c> or <c>typeof()</c>.
    /// </summary>
    public class BuiltInFunctionSymbol :
        BaseSymbol,
        IGlobalScopeSymbol
    {
        private List<ParameterInfo>? _parameters;

        public List<ParameterInfo> Parameters
        {
            get => this._parameters ??= new List<ParameterInfo>();
            internal set => this._parameters = value;
        }

        public ITypeExpression? ReturnType { get; internal set; }

        public bool IsCompileTimeOnly { get; }

        public BuiltInFunctionSymbol(
            string name,
            IEnumerable<ParameterInfo>? parameters = null,
            ITypeExpression? returnType = null,
            bool isCompileTimeOnly = true
        )
            : base(name, SymbolType.BuiltInFunction)
        {
            this.IsCompileTimeOnly = isCompileTimeOnly;
            this.ReturnType = returnType;
            if (parameters != null)
            {
                this.Parameters = new List<ParameterInfo>(parameters);
            }
        }
    }
}
