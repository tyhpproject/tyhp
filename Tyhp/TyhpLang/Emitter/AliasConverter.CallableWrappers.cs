using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    internal sealed partial class AliasConverter
    {
        private const string WrapperReceiverName = "__fccRecv";
        private const string WrapperTargetName = "__fccTarget";

        /// <summary>
        /// Rewrites a first-class callable the checker planned a wrapper for into a closure whose
        /// parameters accept the implicitly convertible types the checker admitted:
        /// <c>static fn(int|Money $m): Result =&gt; f(($m instanceof Money ? … : $m))</c>.
        /// A non-variable receiver is evaluated once, when the callable is created.
        /// </summary>
        private bool TryRewriteCallableWrapper(PhpDereferenceableAst callNode, out IBase2Ast rewritten)
        {
            rewritten = callNode;
            if (callNode.Suffix is not PhpCallAst call
                || CallableWrapperFacts.Get(call) is not { } plan)
            {
                return false;
            }

            IExpression? receiver = null;
            PhpDereferenceableAst? receiverChain = null;
            switch (callNode.Base)
            {
                case IExpression invoked when plan.IsInvokeCall:
                    receiver = invoked;
                    break;
                case PhpNameAst when !plan.IsExtensionMethod:
                    break;
                case PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst instanceAccess } chain
                    when instanceAccess.Accessor?.ValueInt64 != TyhpParser.T_NULLSAFE_OBJECT_OPERATOR
                        && chain.Base is IExpression instanceReceiver:
                    receiver = instanceReceiver;
                    receiverChain = chain;
                    break;
                case PhpDereferenceableAst
                    {
                        Suffix: PhpStaticMemberAccessAst or PhpClassConstantAccessAst,
                        Base: PhpNameAst,
                    } when !plan.IsExtensionMethod:
                    break;
                default:
                    return false;
            }

            if (plan.IsExtensionMethod && receiverChain is null)
            {
                return false;
            }

            var parameterNames = plan.CallSiteParameters
                .Select(p => p.Name.TrimStart('$'))
                .ToHashSet(StringComparer.Ordinal);

            // A generic callee is reached through its native first-class callable, so the callable
            // keeps the identity `\Tyhp\Generic::bind()` binds type arguments through; the native
            // callable also evaluates the receiver, once, when the value is created.
            var viaTarget = plan.IsGenericCallee && !plan.IsExtensionMethod;

            // How the receiver reaches the wrapper body.
            var usesThis = false;
            string? capturedVariable = null;
            var hoistReceiver = false;
            if (receiver is not null && !viaTarget)
            {
                if (receiver is PhpVariableAst { VariableExpression: null, VariableToken.ValueString: { } receiverName })
                {
                    var bare = receiverName.TrimStart('$');
                    if (bare == "this")
                    {
                        usesThis = true;
                    }
                    else if (!parameterNames.Contains(bare))
                    {
                        capturedVariable = bare;
                    }
                    else
                    {
                        hoistReceiver = true;
                    }
                }
                else
                {
                    hoistReceiver = true;
                }
            }

            // Wrapper parameters and the arguments forwarded to the callee.
            var parts = new List<CallableWrapperPart>();
            void Text(string text) => parts.Add(CallableWrapperPart.FromText(text));

            var arguments = new List<PhpArgumentAst>();
            var declarationParts = new List<CallableWrapperPart>();
            for (var index = 0; index < plan.CallSiteParameters.Count; index++)
            {
                var parameter = plan.CallSiteParameters[index];
                var name = parameter.Name.TrimStart('$');
                var planned = plan.ForIndex(index);
                var type = planned?.WidenedType
                    ?? (index < plan.WrapperParameterTypes.Count ? plan.WrapperParameterTypes[index] : null);

                if (index > 0)
                {
                    declarationParts.Add(CallableWrapperPart.FromText(", "));
                }

                if (type is not null)
                {
                    declarationParts.Add(CallableWrapperPart.FromType(type, after: " "));
                }

                declarationParts.Add(CallableWrapperPart.FromText(
                    (parameter.IsByReference ? "&" : "") + (parameter.IsVariadic ? "..." : "") + "$" + name));
                if (parameter.DefaultValue is { } defaultValue)
                {
                    declarationParts.Add(CallableWrapperPart.FromText(" = "));
                    declarationParts.Add(CallableWrapperPart.FromExpression(defaultValue, isDefault: true));
                }

                var variable = PhpVariableAst.CreateFromContext(name, callNode);
                if (parameter.IsVariadic)
                {
                    arguments.Add(PhpArgumentAst.CreateVariadicFromContext(variable, callNode));
                }
                else if (planned is not null)
                {
                    arguments.Add(PhpArgumentAst.CreateFromContext(
                        this.BuildWrapperConversion(planned, name, callNode), callNode));
                }
                else
                {
                    arguments.Add(PhpArgumentAst.CreateFromContext(variable, callNode));
                }
            }

            var argumentList = PhpArgumentListAst.Create(arguments, callNode);

            // The call the wrapper makes.
            var receiverVariableName = viaTarget
                ? WrapperTargetName
                : hoistReceiver ? WrapperReceiverName : capturedVariable;
            IExpression? body;
            switch (callNode.Base)
            {
                case var _ when viaTarget || (plan.IsInvokeCall && receiverVariableName is not null):
                    body = PhpDereferenceableAst.CreateFromContext(
                        PhpVariableAst.CreateFromContext(receiverVariableName!, callNode),
                        PhpCallAst.CreateFromContext(argumentList, callNode),
                        callNode);
                    break;
                case PhpDereferenceableAst chain when plan.IsExtensionMethod:
                    body = this.BuildWrapperExtensionCall(
                        callNode,
                        chain,
                        plan,
                        argumentList,
                        receiverVariableName is null
                            ? receiver!
                            : PhpVariableAst.CreateFromContext(receiverVariableName, callNode));
                    break;
                case PhpDereferenceableAst chain when receiver is not null && receiverVariableName is not null:
                    body = PhpDereferenceableAst.CreateFromContext(
                        PhpDereferenceableAst.CreateFromContext(
                            PhpVariableAst.CreateFromContext(receiverVariableName, callNode),
                            chain.Suffix,
                            callNode),
                        PhpCallAst.CreateFromContext(argumentList, callNode),
                        callNode);
                    break;
                default:
                    body = PhpDereferenceableAst.CreateFromContext(
                        (IDereferenceableBase)callNode.Base!,
                        PhpCallAst.CreateFromContext(argumentList, callNode),
                        callNode);
                    break;
            }

            if (body is null)
            {
                return false;
            }

            var fullClosure = plan.ReturnsNothing;
            if (viaTarget)
            {
                Text("\\Tyhp\\Generic::wrapCallable(");
                parts.Add(CallableWrapperPart.FromExpression(callNode));
                Text(", static fn(\\Closure $" + WrapperTargetName + "): \\Closure => (");
            }
            else
            {
                Text(hoistReceiver ? "((" : "(");
            }

            if (hoistReceiver)
            {
                Text("static fn($" + WrapperReceiverName + ") => ");
            }

            Text(usesThis ? "" : "static ");
            Text(fullClosure ? "function(" : "fn(");
            parts.AddRange(declarationParts);
            Text(")");
            if (fullClosure && receiverVariableName is not null)
            {
                Text(" use ($" + receiverVariableName + ")");
            }

            if (fullClosure)
            {
                var returnsNever = plan.ReturnType?.Kind == CheckedTypeKind.Never;
                Text(": " + (returnsNever ? "never" : "void") + " { ");
                parts.Add(CallableWrapperPart.FromExpression(body));
                Text("; }");
            }
            else
            {
                if (plan.ReturnType is not null)
                {
                    parts.Add(CallableWrapperPart.FromType(plan.ReturnType, before: ": "));
                }

                Text(" => ");
                parts.Add(CallableWrapperPart.FromExpression(body));
            }

            if (hoistReceiver)
            {
                Text(")(");
                parts.Add(CallableWrapperPart.FromExpression(receiver!));
                Text("))");
            }
            else
            {
                Text(viaTarget ? "))" : ")");
            }

            var selfFqn = plan.Callee is ObjectMethodSymbol method
                && this.GetOwningObjectDeclaration(method) is { } owner
                    ? this.FormatEmittedClassFqn(owner.FullyQualifiedName, owner.Name)
                    : null;
            rewritten = CallableWrapperAst.Create(parts, callNode, selfFqn);
            return true;
        }

        /// <summary>
        /// The extension-method call inside a wrapper: the extension body spliced in when it can be
        /// (single evaluation of every argument is already guaranteed — they are variables), else a
        /// static call on the extension class with the receiver first.
        /// </summary>
        private IExpression? BuildWrapperExtensionCall(
            PhpDereferenceableAst callNode,
            PhpDereferenceableAst memberChain,
            CallableWrapperPlan plan,
            PhpArgumentListAst arguments,
            IExpression receiverExpression)
        {
            if (plan.Callee is not ObjectMethodSymbol methodSymbol
                || this.GetOwningExtensionClass(methodSymbol) is not { IsExtension: true } extensionClass)
            {
                return null;
            }

            var syntheticCall = PhpDereferenceableAst.CreateFromContext(
                PhpDereferenceableAst.CreateFromContext(
                    (IDereferenceableBase)receiverExpression,
                    memberChain.Suffix,
                    callNode),
                PhpCallAst.CreateFromContext(arguments, callNode),
                callNode);

            // The wrapper body is an expression: a splice that needs a statement slot is refused.
            this._refHoistForbidden.Add(syntheticCall);
            if (this.TrySpliceExtensionCall(
                    syntheticCall,
                    receiverExpression,
                    arguments,
                    methodSymbol,
                    extensionClass,
                    out var spliced)
                && spliced is IExpression splicedExpression)
            {
                return splicedExpression;
            }

            // Compiler-generated thin mappings have no PHP backer to call.
            if (extensionClass.IsCompilerGenerated)
            {
                return null;
            }

            var extensionFqn = this.FormatEmittedClassFqn(
                extensionClass.FullyQualifiedName,
                extensionClass.Name);
            this.EnsureImport(extensionFqn);
            var staticCall = this.BuildStaticCall(
                extensionFqn,
                methodSymbol.OriginalPhpName ?? methodSymbol.Name,
                this.BuildReceiverFirstArguments(receiverExpression, arguments, callNode),
                callNode);
            staticCall.BoundSymbol = methodSymbol;
            return staticCall;
        }

        /// <summary>
        /// <c>($v instanceof S1 ? conv1($v) : ($v instanceof S2 ? conv2($v) : $v))</c>: a value of
        /// one of the parameter's convertible source types goes through its convert, any other
        /// value is forwarded unchanged (the callee then checks it).
        /// </summary>
        private IExpression BuildWrapperConversion(
            ConvertibleParameterPlan planned,
            string variableName,
            Base2Ast context)
        {
            var variableText = "$" + variableName;
            var branches = new List<(string Test, IExpression Conversion)>();

            // convert-from: one `Target::__from($v)` per class, whichever source type matched.
            var fromGroups = new List<(string ClassFqn, List<string> Tests)>();
            foreach (var source in planned.Sources.Where(s => s.Kind == ConvertibleSourceKind.ConvertFrom))
            {
                if (!this.TryResolveConvertFromClassFqn(source.Overload, source.Owner, out var classFqn)
                    || classFqn is null
                    || this.BuildSourceTypeTest(source.SourceType, variableText) is not { } test)
                {
                    continue;
                }

                var group = fromGroups.FindIndex(g => g.ClassFqn == classFqn);
                if (group < 0)
                {
                    fromGroups.Add((classFqn, [test]));
                }
                else if (!fromGroups[group].Tests.Contains(test))
                {
                    fromGroups[group].Tests.Add(test);
                }
            }

            foreach (var (classFqn, tests) in fromGroups)
            {
                var conversion = this.BuildStaticCall(
                    classFqn,
                    OperatorMethodNameGenerator.ConvertFromMethodName,
                    this.BuildSingleOperandArguments(PhpVariableAst.CreateFromContext(variableName, context), context),
                    context);
                branches.Add((string.Join(" || ", tests), conversion));
            }

            // convert-to: an instance of the source class where the scalar/named target is expected.
            foreach (var source in planned.Sources.Where(s => s.Kind == ConvertibleSourceKind.ConvertTo))
            {
                if (this.BuildSourceTypeTest(source.SourceType, variableText) is not { } test
                    || !this.TryRewriteConvertToCall(
                        PhpVariableAst.CreateFromContext(variableName, context),
                        source.Owner,
                        source.TargetKey,
                        context,
                        out var conversion)
                    || conversion is null)
                {
                    continue;
                }

                branches.Add((test, conversion));
            }

            var parts = new List<CallableWrapperPart>();
            foreach (var (test, conversion) in branches)
            {
                parts.Add(CallableWrapperPart.FromText("(" + test + " ? "));
                parts.Add(CallableWrapperPart.FromExpression(conversion));
                parts.Add(CallableWrapperPart.FromText(" : "));
            }

            parts.Add(CallableWrapperPart.FromText(variableText + new string(')', branches.Count)));
            return CallableWrapperAst.Create(parts, context);
        }

        /// <summary>The PHP test that a value of <paramref name="type"/> is held in <paramref name="variable"/>.</summary>
        private string? BuildSourceTypeTest(ICheckedType type, string variable)
        {
            switch (type)
            {
                case NullableCheckedType nullable:
                    return this.BuildSourceTypeTest(nullable.InnerType, variable);
                case GenericCheckedType generic:
                    return this.BuildSourceTypeTest(generic.BaseType, variable);
                case SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol { IsStruct: true } }:
                    return "\\is_array(" + variable + ")";
                case SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol objectSymbol }:
                    var fqn = this.FormatEmittedClassFqn(objectSymbol.FullyQualifiedName, objectSymbol.Name);
                    return string.IsNullOrEmpty(fqn) ? null : variable + " instanceof " + fqn;
                case SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol builtin }:
                    return builtin.Name.ToLowerInvariant() switch
                    {
                        "int" => "\\is_int(" + variable + ")",
                        "float" => "\\is_float(" + variable + ")",
                        "string" => "\\is_string(" + variable + ")",
                        "bool" => "\\is_bool(" + variable + ")",
                        "array" => "\\is_array(" + variable + ")",
                        "object" => "\\is_object(" + variable + ")",
                        "callable" => "\\is_callable(" + variable + ")",
                        "iterable" => "\\is_iterable(" + variable + ")",
                        _ => null,
                    };
                default:
                    return null;
            }
        }
    }
}
