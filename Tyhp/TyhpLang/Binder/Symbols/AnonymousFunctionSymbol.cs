using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using System.Collections.Generic;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class AnonymousFunctionSymbol :
        BaseSymbol
    {
        private List<VariableSymbol>? _capturedVariables;
        private List<ParameterInfo>? _parameters;
        private List<GenericTypeParameterSymbol>? _genericParameters;

        public List<VariableSymbol> CapturedVariables => _capturedVariables ??= new List<VariableSymbol>();
        public List<ParameterInfo> Parameters => _parameters ??= new List<ParameterInfo>();

        public ITypeExpression? ReturnType { get; internal set; }

        public List<GenericTypeParameterSymbol> GenericParameters => _genericParameters ??= new List<GenericTypeParameterSymbol>();

        public AnonymousFunctionSymbol(
            string name,
            string? sourceFile = null
        )
            : base(name, SymbolType.AnonymousFunctionDeclaration, sourceFile: sourceFile ?? string.Empty)
        {
        }
    }
}