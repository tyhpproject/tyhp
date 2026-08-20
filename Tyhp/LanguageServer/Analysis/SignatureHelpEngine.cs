namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Resolution;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using Tyhp.TyhpLang.Enum;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Resolves the call under the cursor and builds LSP signature help.
    /// </summary>
    internal static class SignatureHelpEngine
    {
        /// <summary>
        /// Signature help for the innermost call whose argument list contains
        /// <paramref name="position"/>, or null when the cursor is not in a call.
        /// </summary>
        public static SignatureHelp? Provide(
            string content,
            Position position,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? symbolTree,
            SymbolFinder finder)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(position);
            ArgumentNullException.ThrowIfNull(finder);

            int offset = PositionUtilities.GetOffset(content, position);
            CallSite? site = FindCallSiteInText(content, offset);
            if (site is null && ast is not null)
            {
                site = FindCallSiteInAst(ast, content, position, offset);
            }
            if (site is null)
            {
                return null;
            }

            CallSite call = site;

            var (line, column) = PositionUtilities.FromLspPosition(
                PositionUtilities.GetPosition(content, call.CalleeOffset));
            BaseSymbol? callee = null;
            if (ast is not null)
            {
                callee = finder.LookupAtPosition(ast, globalScope, symbolTree, line, column)?.Symbol;
            }

            callee ??= call.BoundSymbol as BaseSymbol;
            callee ??= ResolveCalleeByName(content, call, ast, globalScope, symbolTree, finder);
            if (callee is null)
            {
                return null;
            }

            // A source-text call site cannot tell a call apart from the callee's own
            // declaration header (e.g. `function calculate(int $a, |)`) — both look like
            // "identifier immediately followed by (". Once the callee resolves, reject the
            // match if the cursor is inside that very declaration's own parameter list rather
            // than a call to it.
            if (callee is FunctionDeclarationSymbol or ObjectMethodSymbol
                && IsWithinOwnParameterList(callee, position))
            {
                return null;
            }

            IReadOnlyList<BaseSymbol> signatures = ExpandSignatures(callee, call.IsConstructor, globalScope, symbolTree);
            if (signatures.Count == 0)
            {
                return null;
            }

            int activeParameter = Math.Max(0, call.ActiveParameter);
            int activeSignature = SelectActiveSignature(signatures, call.ArgumentCount);
            return new SignatureHelp
            {
                Signatures = [.. signatures.Select(ToSignatureInformation)],
                ActiveSignature = activeSignature,
                ActiveParameter = activeParameter,
            };
        }

        private static BaseSymbol? ResolveCalleeByName(
            string content,
            CallSite call,
            SrcFileAst? ast,
            GlobalScope? globalScope,
            SymbolTree? symbolTree,
            SymbolFinder finder)
        {
            string name = ReadIdentifier(content, call.CalleeOffset);
            if (string.IsNullOrEmpty(name) || globalScope is null)
            {
                return null;
            }

            var (line, column) = PositionUtilities.FromLspPosition(
                PositionUtilities.GetPosition(content, Math.Max(0, call.CalleeOffset)));
            IBaseScope? fromScope = ast is not null
                ? finder.FindScopeAtPosition(ast, globalScope, line, column)
                : globalScope;
            fromScope ??= globalScope;

            var diagnostics = new DiagnosticBag();
            NameResolver resolver = symbolTree is not null
                ? symbolTree.CreateNameResolver(diagnostics)
                : new NameResolver(globalScope, diagnostics);
            if (call.IsConstructor)
            {
                if (resolver.ResolveSymbol(name, fromScope) is ObjectDeclarationSymbol type)
                {
                    return type;
                }

                return resolver.ResolveRelativeName([name], fromScope) as ObjectDeclarationSymbol;
            }

            if (resolver.ResolveSymbol(name, fromScope) is BaseSymbol symbol)
            {
                return symbol;
            }

            return resolver.ResolveRelativeName([name], fromScope) as BaseSymbol;
        }

        private static string ReadIdentifier(string content, int offset)
        {
            if (offset < 0 || offset >= content.Length)
            {
                return string.Empty;
            }

            int start = offset;
            if (content[start] == '$')
            {
                start++;
            }

            int end = start;
            while (end < content.Length && IdentifierSyntax.IsIdentifierChar(content[end]))
            {
                end++;
            }

            return start < end ? content[start..end] : string.Empty;
        }

        private static CallSite? FindCallSiteInAst(
            SrcFileAst ast,
            string content,
            Position position,
            int cursorOffset)
        {
            var (line, column) = PositionUtilities.FromLspPosition(position);
            var path = new List<IBase2Ast>();
            if (!TryCollectPath(ast, line, column, path) || path.Count == 0)
            {
                return null;
            }

            for (int i = path.Count - 1; i >= 0; i--)
            {
                IBase2Ast node = path[i];
                if (node is PhpNewAst created)
                {
                    int openParen = FindOpenParen(content, created, cursorOffset);
                    if (openParen < 0)
                    {
                        continue;
                    }

                    return new CallSite(
                        CalleeOffset(content, created.ClassName as IBase2Ast ?? created),
                        CountActiveParameter(content, openParen, cursorOffset),
                        CountCommas(content, openParen, cursorOffset) + 1,
                        IsConstructor: true,
                        created.BoundSymbol);
                }

                if (node is PhpCallAst call)
                {
                    int openParen = FindOpenParen(content, call, cursorOffset);
                    if (openParen < 0)
                    {
                        continue;
                    }

                    IBase2Ast calleeNode = FindCallCallee(path, i, call);
                    return new CallSite(
                        CalleeOffset(content, calleeNode),
                        CountActiveParameter(content, openParen, cursorOffset),
                        CountCommas(content, openParen, cursorOffset) + 1,
                        IsConstructor: false,
                        calleeNode.BoundSymbol ?? call.BoundSymbol);
                }

                if (node is PhpArgumentListAst args
                    && i > 0
                    && path[i - 1] is PhpDereferenceableAst)
                {
                    int openParen = FindOpenParen(content, args, cursorOffset);
                    if (openParen < 0)
                    {
                        continue;
                    }

                    IBase2Ast calleeNode = FindCallCallee(path, i, args);
                    return new CallSite(
                        CalleeOffset(content, calleeNode),
                        CountActiveParameter(content, openParen, cursorOffset),
                        CountCommas(content, openParen, cursorOffset) + 1,
                        IsConstructor: false,
                        calleeNode.BoundSymbol);
                }
            }

            return null;
        }

        private static IBase2Ast FindCallCallee(IReadOnlyList<IBase2Ast> path, int callIndex, IBase2Ast callNode)
        {
            if (callIndex > 0 && path[callIndex - 1] is PhpDereferenceableAst deref)
            {
                if (deref.Base is PhpDereferenceableAst inner
                    && inner.Suffix is PhpInstanceMemberAccessAst or PhpStaticMemberAccessAst)
                {
                    return inner.Suffix as IBase2Ast ?? inner;
                }

                if (deref.Suffix is PhpInstanceMemberAccessAst or PhpStaticMemberAccessAst)
                {
                    return deref.Suffix as IBase2Ast ?? deref;
                }

                return deref.Base as IBase2Ast ?? deref;
            }

            return callNode;
        }

        private static CallSite? FindCallSiteInText(string content, int cursorOffset)
        {
            int openParen = FindInnermostOpenParen(content, cursorOffset);
            if (openParen < 0)
            {
                return null;
            }

            int nameEnd = SkipWhitespaceBack(content, openParen - 1);
            if (nameEnd < 0 || !IdentifierSyntax.IsIdentifierChar(content[nameEnd]))
            {
                return null;
            }

            int nameStart = nameEnd;
            while (nameStart > 0 && IdentifierSyntax.IsIdentifierChar(content[nameStart - 1]))
            {
                nameStart--;
            }

            bool isConstructor = IsNewKeywordBefore(content, nameStart);
            return new CallSite(
                nameStart,
                CountActiveParameter(content, openParen, cursorOffset),
                CountCommas(content, openParen, cursorOffset) + 1,
                isConstructor,
                BoundSymbol: null);
        }

        private static bool IsNewKeywordBefore(string content, int nameStart)
        {
            int i = SkipWhitespaceBack(content, nameStart - 1);
            if (i < 2)
            {
                return false;
            }

            if (i >= 2
                && content[i - 2] == 'n'
                && content[i - 1] == 'e'
                && content[i] == 'w'
                && (i == 2 || !IdentifierSyntax.IsIdentifierChar(content[i - 3])))
            {
                return true;
            }

            return i >= 2
                && content.AsSpan(i - 2, 3).Equals("new", StringComparison.Ordinal)
                && (i < 3 || !IdentifierSyntax.IsIdentifierChar(content[i - 3]));
        }

        /// <summary>
        /// True when <paramref name="position"/> falls inside <paramref name="callee"/>'s own
        /// declared parameter list (a function or method being defined, not called).
        /// </summary>
        private static bool IsWithinOwnParameterList(BaseSymbol callee, Position position)
        {
            PhpParameterListAst? parameters = callee.DeclaringAstNode switch
            {
                PhpFunctionDeclAst function => function.Parameters,
                PhpMethodDeclAst method => method.Parameters,
                _ => null,
            };
            if (parameters is null)
            {
                return false;
            }

            ProtocolRange range = PositionUtilities.ToLspRange(parameters);
            return range.Start is not null
                && range.End is not null
                && !IsBeforePosition(position, range.Start)
                && !IsBeforePosition(range.End, position);
        }

        private static bool IsBeforePosition(Position left, Position right)
        {
            return left.Line != right.Line ? left.Line < right.Line : left.Character < right.Character;
        }

        private static IReadOnlyList<BaseSymbol> ExpandSignatures(
            BaseSymbol callee,
            bool preferConstructor,
            GlobalScope? globalScope,
            SymbolTree? symbolTree)
        {
            if (preferConstructor)
            {
                if (callee is ObjectDeclarationSymbol type)
                {
                    BaseSymbol? ctor = FindConstructor(type, globalScope, symbolTree);
                    return ctor is not null ? CollectMethodSignatures(ctor) : [type];
                }

                if (callee is ObjectMethodSymbol { SymbolType: SymbolType.ObjectConstructor })
                {
                    return CollectMethodSignatures(callee);
                }
            }

            return CollectMethodSignatures(callee);
        }

        private static IReadOnlyList<BaseSymbol> CollectMethodSignatures(BaseSymbol callee)
        {
            if (callee is FunctionDeclarationSymbol function)
            {
                if (function.Overloads.Count == 0)
                {
                    return [function];
                }

                var all = new List<BaseSymbol>(function.Overloads.Count + 1) { function };
                all.AddRange(function.Overloads);
                return all;
            }

            return [callee];
        }

        /// <summary>
        /// The constructor for <paramref name="type"/>, walking up the <c>extends</c> chain
        /// (via the same inherited-member resolution hover/completion use) when the type does
        /// not declare its own <c>__construct</c>.
        /// </summary>
        private static BaseSymbol? FindConstructor(
            ObjectDeclarationSymbol type,
            GlobalScope? globalScope,
            SymbolTree? symbolTree)
        {
            foreach (IBaseSymbol member in type.EnumerateMembersAndConstants())
            {
                if (member is ObjectMethodSymbol method
                    && (method.SymbolType == SymbolType.ObjectConstructor
                        || string.Equals(method.Name, "__construct", StringComparison.OrdinalIgnoreCase)))
                {
                    return method;
                }
            }

            if (type.Members.TryGetValue("__construct", out IBaseSymbol? found)
                && found is BaseSymbol ctor)
            {
                return ctor;
            }

            NameResolver? resolver = CreateResolver(globalScope, symbolTree);
            if (resolver?.ResolveMember("__construct", type) is BaseSymbol inherited)
            {
                return inherited;
            }

            return null;
        }

        private static NameResolver? CreateResolver(GlobalScope? globalScope, SymbolTree? symbolTree)
        {
            var diagnostics = new DiagnosticBag();
            if (symbolTree is not null)
            {
                return symbolTree.CreateNameResolver(diagnostics);
            }

            if (globalScope is not null)
            {
                return new NameResolver(globalScope, diagnostics);
            }

            return null;
        }

        private static int SelectActiveSignature(IReadOnlyList<BaseSymbol> signatures, int argumentCount)
        {
            for (int i = 0; i < signatures.Count; i++)
            {
                if (AcceptsArgumentCount(signatures[i], argumentCount))
                {
                    return i;
                }
            }

            return 0;
        }

        private static bool AcceptsArgumentCount(BaseSymbol symbol, int argumentCount)
        {
            IReadOnlyList<ParameterInfo> parameters = GetParameters(symbol);
            int required = 0;
            bool variadic = false;
            foreach (ParameterInfo parameter in parameters)
            {
                if (parameter.IsVariadic)
                {
                    variadic = true;
                    continue;
                }

                if (parameter.DefaultValue is null)
                {
                    required++;
                }
            }

            if (argumentCount < required)
            {
                return false;
            }

            if (variadic)
            {
                return true;
            }

            return argumentCount <= parameters.Count;
        }

        private static SignatureInformation ToSignatureInformation(BaseSymbol symbol)
        {
            IReadOnlyList<ParameterInfo> parameters = GetParameters(symbol);
            string label = SymbolFormatter.FormatSignature(symbol);
            string? documentation = SymbolFormatter.FormatDocumentation(symbol);
            return new SignatureInformation
            {
                Label = label,
                Documentation = string.IsNullOrEmpty(documentation) ? null : documentation,
                Parameters = [.. parameters.Select(parameter => new ParameterInformation
                {
                    Label = SymbolFormatter.FormatParameterLabel(parameter),
                })],
            };
        }

        private static IReadOnlyList<ParameterInfo> GetParameters(BaseSymbol symbol)
        {
            return symbol switch
            {
                FunctionDeclarationSymbol function => function.Parameters,
                ObjectMethodSymbol method => method.Parameters,
                BuiltInFunctionSymbol builtIn => builtIn.Parameters,
                _ => [],
            };
        }

        private static int FindOpenParen(string content, IBase2Ast node, int cursorOffset)
        {
            int start = OffsetOf(content, node);
            if (start < 0 || start >= content.Length)
            {
                return FindInnermostOpenParen(content, cursorOffset);
            }

            int searchEnd = Math.Min(cursorOffset, content.Length);
            for (int i = start; i < searchEnd; i++)
            {
                if (content[i] == '(' && !IsInsideStringOrComment(content, i))
                {
                    return i;
                }
            }

            return FindInnermostOpenParen(content, cursorOffset);
        }

        /// <summary>
        /// Innermost still-open <c>(</c> before <paramref name="cursorOffset"/>, skipping
        /// strings and comments. Uses an explicit stack of open positions so a fully-closed
        /// nested call (e.g. the inner call in <c>outer(inner(1, 2), |</c>) does not leave a
        /// stale offset behind once its own <c>)</c> has been consumed.
        /// </summary>
        private static int FindInnermostOpenParen(string content, int cursorOffset)
        {
            var open = new Stack<int>();
            var state = new ScanState();
            int limit = Math.Min(cursorOffset, content.Length);
            for (int i = 0; i < limit; i++)
            {
                if (state.Consume(content, i))
                {
                    continue;
                }

                char c = content[i];
                if (c == '(')
                {
                    open.Push(i);
                }
                else if (c == ')' && open.Count > 0)
                {
                    open.Pop();
                }
            }

            return open.Count > 0 ? open.Peek() : -1;
        }

        private static int CountActiveParameter(string content, int openParen, int cursorOffset)
            => CountCommas(content, openParen, cursorOffset);

        private static int CountCommas(string content, int openParen, int cursorOffset)
        {
            int depth = 0;
            int commas = 0;
            var state = new ScanState();
            int end = Math.Min(cursorOffset, content.Length);
            for (int i = openParen; i < end; i++)
            {
                if (state.Consume(content, i))
                {
                    continue;
                }

                char c = content[i];
                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                    continue;
                }

                if (c == ')' || c == ']' || c == '}')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (c == ',' && depth == 1)
                {
                    commas++;
                }
            }

            return commas;
        }

        private static int CalleeOffset(string content, IBase2Ast node)
            => OffsetOf(content, SymbolFinder.PreferIdentifierNode(node));

        private static int OffsetOf(string content, IBase2Ast node)
        {
            if (node.Line < 1)
            {
                return 0;
            }

            return PositionUtilities.GetOffset(
                content,
                PositionUtilities.ToLspPosition(node.Line, Math.Max(0, node.Column)));
        }

        private static int SkipWhitespaceBack(string content, int index)
        {
            int i = index;
            while (i >= 0 && char.IsWhiteSpace(content[i]))
            {
                i--;
            }

            return i;
        }

        private static bool TryCollectPath(IBase2Ast root, int line, int column, List<IBase2Ast> path)
        {
            if (!SymbolFinder.ContainsPosition(root, line, column))
            {
                return false;
            }

            path.Add(root);
            foreach (IBase2Ast? child in root.AstChildren)
            {
                if (child is null)
                {
                    continue;
                }

                if (TryCollectPath(child, line, column, path))
                {
                    return true;
                }
            }

            return true;
        }

        private static bool IsInsideStringOrComment(string content, int index)
        {
            var state = new ScanState();
            for (int i = 0; i < index; i++)
            {
                state.Consume(content, i);
            }

            return state.InNonCode;
        }

        private sealed record CallSite(
            int CalleeOffset,
            int ActiveParameter,
            int ArgumentCount,
            bool IsConstructor,
            IBaseSymbol? BoundSymbol);

        private struct ScanState
        {
            private enum Mode
            {
                Code,
                SingleQuote,
                DoubleQuote,
                LineComment,
                BlockComment,
            }

            private Mode _mode;

            public bool InNonCode => this._mode != Mode.Code;

            public bool Consume(string content, int index)
            {
                char c = content[index];
                switch (this._mode)
                {
                    case Mode.SingleQuote:
                        if (c == '\\' && index + 1 < content.Length)
                        {
                            return true;
                        }

                        if (c == '\'')
                        {
                            this._mode = Mode.Code;
                        }

                        return true;
                    case Mode.DoubleQuote:
                        if (c == '\\' && index + 1 < content.Length)
                        {
                            return true;
                        }

                        if (c == '"')
                        {
                            this._mode = Mode.Code;
                        }

                        return true;
                    case Mode.LineComment:
                        if (c is '\n' or '\r')
                        {
                            this._mode = Mode.Code;
                            return false;
                        }

                        return true;
                    case Mode.BlockComment:
                        if (c == '*' && index + 1 < content.Length && content[index + 1] == '/')
                        {
                            this._mode = Mode.Code;
                        }

                        return true;
                    default:
                        if (c == '\'')
                        {
                            this._mode = Mode.SingleQuote;
                            return true;
                        }

                        if (c == '"')
                        {
                            this._mode = Mode.DoubleQuote;
                            return true;
                        }

                        if (c == '/' && index + 1 < content.Length && content[index + 1] == '/')
                        {
                            this._mode = Mode.LineComment;
                            return true;
                        }

                        if (c == '#' && (index + 1 >= content.Length || content[index + 1] != '['))
                        {
                            this._mode = Mode.LineComment;
                            return true;
                        }

                        if (c == '/' && index + 1 < content.Length && content[index + 1] == '*')
                        {
                            this._mode = Mode.BlockComment;
                            return true;
                        }

                        return false;
                }
            }
        }
    }
}
