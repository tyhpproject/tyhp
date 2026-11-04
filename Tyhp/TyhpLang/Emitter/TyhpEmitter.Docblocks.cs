using Tyhp.Config;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        /// <summary>
        /// Attaches an author docblock, and when <c>emit.docblocks</c> is not <c>off</c>, the
        /// generated analysis tags for a declaration. Statements keep the author-only path.
        /// </summary>
        private EmitItem ApplyDocComment(IBase2Ast node, EmitItem item)
        {
            if (IsDocblockDeclaration(node))
            {
                return this.ApplyAnalysisDocblock(node, item);
            }

            return this.AttachAuthorDocComment(node, item);
        }

        private EmitItem AttachAuthorDocComment(IBase2Ast node, EmitItem item)
        {
            if (!this._context.Config.IncludeComments || string.IsNullOrWhiteSpace(node.DocComment))
            {
                return item;
            }

            return EmitItem.AttachDocComment(node.DocComment, item);
        }

        private static bool IsDocblockDeclaration(IBase2Ast node) =>
            node is PhpObjectTypeDeclAst
                or PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpConstDeclAst
                or PhpPropertyAst
                or TyhpExtensionDeclAst;

        private EmitItem ApplyAnalysisDocblock(IBase2Ast node, EmitItem item)
        {
            if (this._context.Config.DocblockMode == DocblockEmitMode.Off)
            {
                return this.AttachAuthorDocComment(node, item);
            }

            var generated = this.BuildGeneratedDocTags(node);
            if (generated.Count == 0 && this._context.Config.IncludeComments)
            {
                return this.AttachAuthorDocComment(node, item);
            }

            var merged = AnalysisDocblockMerger.Merge(
                node.DocComment,
                generated,
                includeProse: this._context.Config.IncludeComments);
            if (string.IsNullOrWhiteSpace(merged))
            {
                return item;
            }

            return EmitItem.AttachDocComment(merged, item);
        }

        private List<GeneratedDocTag> BuildGeneratedDocTags(IBase2Ast node)
        {
            var tags = new List<GeneratedDocTag>();
            switch (node)
            {
                case PhpObjectTypeDeclAst objectDecl:
                    this.AddObjectTags(objectDecl, tags);
                    break;
                case TyhpExtensionDeclAst extension:
                    this.AddInternalTag(extension.BoundSymbol, tags);
                    this.AddSymbolTemplateTags(extension.BoundSymbol as ObjectDeclarationSymbol, tags);
                    break;
                case PhpFunctionDeclAst function:
                    this.AddInternalTag(function.BoundSymbol, tags);
                    this.AddCallableTags(
                        function.BoundSymbol as FunctionDeclarationSymbol,
                        function.Parameters,
                        function.ReturnType,
                        function,
                        tags);
                    break;
                case PhpMethodDeclAst method:
                    this.AddInternalTag(method.BoundSymbol, tags);
                    this.AddCallableTags(
                        method.BoundSymbol as ObjectMethodSymbol,
                        method.Parameters,
                        method.ReturnType,
                        method,
                        tags);
                    break;
                case PhpConstDeclAst constant:
                    this.AddInternalTag(constant.BoundSymbol, constant.Modifiers, tags);
                    this.AddVarTag(constant.Type, constant, tags);
                    break;
                case PhpPropertyAst property:
                    this.AddInternalTag(this.PropertySymbol(property), tags);
                    this.AddVarTag(this.PropertyDeclaredType(property), property, tags);
                    break;
            }

            return tags;
        }

        private void AddObjectTags(PhpObjectTypeDeclAst objectDecl, List<GeneratedDocTag> tags)
        {
            var symbol = objectDecl.BoundSymbol as ObjectDeclarationSymbol;
            this.AddInternalTag(symbol, objectDecl.Modifiers, tags);
            this.AddSymbolTemplateTags(symbol, tags);

            var declType = objectDecl.DeclType?.ValueString ?? "class";
            var isInterface = string.Equals(declType, "interface", StringComparison.OrdinalIgnoreCase);
            var isTrait = string.Equals(declType, "trait", StringComparison.OrdinalIgnoreCase);

            // The binder records `use SomeTrait;` names in `ImplementsTypes` too. They are
            // `@use` targets, not `@implements` / `@extends` ones.
            var usedTraits = new List<(string Identifier, IBase2Ast Name)>();
            if (objectDecl.Body is not null)
            {
                foreach (var member in objectDecl.Body.GetAllNotNull())
                {
                    if (member is not PhpTraitUseAst traitUse)
                    {
                        continue;
                    }

                    foreach (var traitName in traitUse.TraitNames?.GetAllNotNull() ?? [])
                    {
                        usedTraits.Add((WrittenName(traitName), traitName));
                    }
                }
            }

            // Classify by the resolved symbol first (an alias such as `use X as T1` is expanded
            // in the emitted body but not in the binder's copy), then by written name.
            bool IsTraitReference(ITypeExpression type) =>
                ResolvedObjectKind(type) == PhpTypeDeclType.Trait
                || usedTraits.Exists(trait => IsSameTypeName(trait.Identifier, WrittenName(type)));

            var implemented = (symbol?.ImplementsTypes ?? [])
                .Where(type => !IsTraitReference(type))
                .ToList();
            var boundTraits = (symbol?.ImplementsTypes ?? []).Where(IsTraitReference).ToList();
            if (!isTrait)
            {
                if (isInterface)
                {
                    foreach (var parent in implemented)
                    {
                        this.AddHeritageTag(parent, "extends", tags);
                    }
                }
                else
                {
                    this.AddHeritageTag(symbol?.ExtendsType, "extends", tags);
                    foreach (var implementedType in implemented)
                    {
                        this.AddHeritageTag(implementedType, "implements", tags);
                    }
                }
            }

            // The binder's copy in `ImplementsTypes` has its symbol and alias resolved; the raw
            // `use` name may not.
            foreach (var trait in boundTraits)
            {
                this.AddHeritageTag(trait, "use", tags);
            }

            if (boundTraits.Count == 0)
            {
                foreach (var (_, traitName) in usedTraits)
                {
                    this.AddHeritageTag(traitName, "use", tags);
                }
            }
        }

        private static PhpTypeDeclType? ResolvedObjectKind(IBase2Ast? type)
        {
            var symbol = type switch
            {
                PhpNamedTypeAst named => named.BoundSymbol ?? named.Name?.BoundSymbol,
                null => null,
                _ => type.BoundSymbol,
            };
            return (symbol as ObjectDeclarationSymbol)?.ObjectKind;
        }

        /// <summary>The name as written in source, without type arguments or resolution.</summary>
        private static string WrittenName(IBase2Ast? node)
        {
            switch (node)
            {
                case null:
                    return "";
                case PhpNamedTypeAst named:
                    return WrittenName(named.Name);
                case PhpNameAst name:
                    return name.ValueString ?? name.Identifier ?? "";
                default:
                    return node.Identifier ?? "";
            }
        }

        private static bool IsSameTypeName(string? left, string? right) =>
            !string.IsNullOrEmpty(left)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private void AddSymbolTemplateTags(ObjectDeclarationSymbol? symbol, List<GeneratedDocTag> tags)
        {
            if (symbol is null)
            {
                return;
            }

            foreach (var param in symbol.GenericParameters)
            {
                this.AddTemplateTag(param, tags);
            }
        }

        private void AddCallableTags(
            IReadOnlyList<GenericTypeParameterSymbol>? genericParameters,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            IBase2Ast host,
            List<GeneratedDocTag> tags)
        {
            if (genericParameters is not null)
            {
                foreach (var param in genericParameters)
                {
                    this.AddTemplateTag(param, tags);
                }
            }

            foreach (var parameter in parameters?.GetAllNotNull() ?? [])
            {
                this.AddParameterTags(parameter, host, tags);
            }

            this.AddReturnTag(returnType, host, tags);
        }

        private void AddCallableTags(
            FunctionDeclarationSymbol? function,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            IBase2Ast host,
            List<GeneratedDocTag> tags) =>
            this.AddCallableTags(function?.GenericParameters, parameters, returnType, host, tags);

        private void AddCallableTags(
            ObjectMethodSymbol? method,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            IBase2Ast host,
            List<GeneratedDocTag> tags) =>
            this.AddCallableTags(method?.GenericParameters, parameters, returnType, host, tags);

        private void AddTemplateTag(GenericTypeParameterSymbol param, List<GeneratedDocTag> tags)
        {
            var tag = param.Variance switch
            {
                TypeVariance.Covariant => "@template-covariant",
                TypeVariance.Contravariant => "@template-contravariant",
                _ => "@template",
            };
            var text = tag + " " + param.Name;
            if (param.Constraint is not null)
            {
                var constraint = this.SpellDocType(param.Constraint);
                if (IsPublicDocType(constraint)
                    && !string.Equals(constraint, "mixed", StringComparison.OrdinalIgnoreCase))
                {
                    text += " of " + constraint;
                }
            }

            if (param.DefaultType is not null)
            {
                var defaultType = this.SpellDocType(param.DefaultType);
                if (IsPublicDocType(defaultType))
                {
                    text += " = " + defaultType;
                }
            }

            tags.Add(new GeneratedDocTag("template:" + param.Name, text));
        }

        private void AddHeritageTag(IBase2Ast? name, string tagName, List<GeneratedDocTag> tags)
        {
            if (name is null)
            {
                return;
            }

            var spelled = DocblockTypeSpellingHelper.SpellClassReference(
                name,
                this._context.TypeAliasMap,
                this._context.GlobalScope,
                this._context.Config.NamespacePrefix);
            if (!IsPublicDocType(spelled))
            {
                return;
            }

            if (this._context.Config.DocblockMode == DocblockEmitMode.LossyOnly
                && !spelled.Contains('<', StringComparison.Ordinal))
            {
                return;
            }

            var shortName = AnalysisDocblockMerger.HeritageShortName(spelled);
            tags.Add(new GeneratedDocTag(tagName + ":" + shortName, "@" + tagName + " " + spelled));
        }

        private void AddParameterTags(PhpParameterAst parameter, IBase2Ast host, List<GeneratedDocTag> tags)
        {
            if (parameter.Type is null)
            {
                return;
            }

            var spelled = this.SpellDocType(parameter.Type);
            if (!this.ShouldEmitTypeTag(parameter.Type, host, spelled))
            {
                return;
            }

            var name = FormatDocVariable(parameter.Name);
            if (name.Length == 0)
            {
                return;
            }

            var variadic = parameter.IsVariadic ? "..." : "";
            tags.Add(new GeneratedDocTag("param:" + name, "@param " + spelled + " " + variadic + name));
            if (parameter.IsRef)
            {
                tags.Add(new GeneratedDocTag("param-out:" + name, "@param-out " + spelled + " " + name));
            }
        }

        private void AddReturnTag(ITypeExpression? returnType, IBase2Ast host, List<GeneratedDocTag> tags)
        {
            if (returnType is null)
            {
                return;
            }

            var spelled = this.SpellDocType(returnType);
            if (!this.ShouldEmitTypeTag(returnType, host, spelled))
            {
                return;
            }

            tags.Add(new GeneratedDocTag("return", "@return " + spelled));
        }

        private void AddVarTag(ITypeExpression? type, IBase2Ast host, List<GeneratedDocTag> tags)
        {
            if (type is null)
            {
                return;
            }

            var spelled = this.SpellDocType(type);
            if (!this.ShouldEmitTypeTag(type, host, spelled))
            {
                return;
            }

            tags.Add(new GeneratedDocTag("var", "@var " + spelled));
        }

        private void AddInternalTag(IBaseSymbol? symbol, List<GeneratedDocTag> tags) =>
            this.AddInternalTag(symbol, modifiers: null, tags);

        private void AddInternalTag(IBaseSymbol? symbol, PhpModifierListAst? modifiers, List<GeneratedDocTag> tags)
        {
            var fromSymbol = symbol is BaseSymbol baseSymbol && (baseSymbol.Visibility & MemberModifier.Internal) != 0;
            var fromModifiers = modifiers?.Modifiers.Contains(PhpModifier.Internal) == true;
            if (!fromSymbol && !fromModifiers)
            {
                return;
            }

            tags.Add(new GeneratedDocTag("internal", "@internal"));
        }

        private bool ShouldEmitTypeTag(ITypeExpression type, IBase2Ast host, string spelled)
        {
            if (!IsPublicDocType(spelled))
            {
                return false;
            }

            if (this._context.Config.DocblockMode == DocblockEmitMode.All)
            {
                return true;
            }

            var native = this.SpellEmittedType(type, host, inheritedHint: null);
            return IsLossyDocType(native, spelled);
        }

        /// <summary>
        /// Compiler utility names such as <c>__Awaited</c> are not a PHPStan or Psalm type.
        /// A generated tag uses them only when the spelling has a public form.
        /// </summary>
        private static bool IsPublicDocType(string? spelled) =>
            !string.IsNullOrWhiteSpace(spelled) && !spelled.Contains("__", StringComparison.Ordinal);

        /// <summary>
        /// True when <paramref name="doc"/> carries information the native PHP hint dropped:
        /// type arguments, shapes, <c>class-string</c>, callable signatures, or a name the
        /// native hint widened (<c>T</c> to <c>mixed</c>, a struct to <c>array</c>).
        /// A fully qualified class name and its short name in the same namespace are the same type.
        /// </summary>
        internal static bool IsLossyDocType(string? native, string doc)
        {
            if (string.IsNullOrWhiteSpace(doc))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(native))
            {
                return true;
            }

            if (TypesEquivalent(native, doc))
            {
                return false;
            }

            if (doc.Contains('<', StringComparison.Ordinal)
                || doc.Contains('{', StringComparison.Ordinal)
                || doc.StartsWith("callable(", StringComparison.Ordinal)
                || doc.Contains("class-string", StringComparison.Ordinal)
                || doc.Contains("callable-string", StringComparison.Ordinal))
            {
                return true;
            }

            var nativeNorm = NormalizeType(native);
            if (nativeNorm is "mixed" or "array" or "object" or "callable" or "string" or "iterable" or "resource")
            {
                return !string.Equals(nativeNorm, NormalizeType(doc), StringComparison.OrdinalIgnoreCase);
            }

            return !SameShortName(native, doc);
        }

        private static bool TypesEquivalent(string native, string doc)
        {
            var left = NormalizeType(native);
            var right = NormalizeType(doc);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Member order and `?T` vs `T|null` do not change the type.
            return DocblockTypeSpellingHelper.UnionMembers(native)
                .SetEquals(DocblockTypeSpellingHelper.UnionMembers(doc));
        }

        private static bool SameShortName(string native, string doc)
        {
            if (native.Contains('<', StringComparison.Ordinal) || doc.Contains('<', StringComparison.Ordinal)
                || native.Contains('{', StringComparison.Ordinal) || doc.Contains('{', StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
                AnalysisDocblockMerger.HeritageShortName(native),
                AnalysisDocblockMerger.HeritageShortName(doc),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeType(string type) =>
            type.Replace(" ", "", StringComparison.Ordinal).Trim().TrimStart('\\');

        private string SpellDocType(ITypeExpression type)
        {
            if (!DocblockTypeSpellingHelper.ContainsRelativeType(type)
                && this._context.ExpressionTypes.TryGetValue(type, out var checkedType)
                && checkedType.Kind != Tyhp.TyhpLang.Checker.CheckedTypeKind.Unresolved)
            {
                var fromChecked = DocblockTypeSpellingHelper.Spell(
                    checkedType,
                    this._context.Config.NamespacePrefix);
                if (!string.IsNullOrWhiteSpace(fromChecked))
                {
                    return fromChecked;
                }
            }

            return DocblockTypeSpellingHelper.SpellTypeExpression(
                type,
                this._context.TypeAliasMap,
                this._context.GlobalScope,
                this._context.Config.NamespacePrefix);
        }

        private ITypeExpression? PropertyDeclaredType(PhpPropertyAst property) =>
            this.PropertySymbol(property)?.DeclaredType;

        private ObjectPropertySymbol? PropertySymbol(PhpPropertyAst property)
        {
            if (property.BoundSymbol is ObjectPropertySymbol bound)
            {
                return bound;
            }

            var obj = this._currentObjectSymbol;
            if (obj is null)
            {
                return null;
            }

            var name = property.Identifier ?? "";
            if (obj.Members.TryGetValue(name, out var member) && member is ObjectPropertySymbol found)
            {
                return found;
            }

            var alternate = name.StartsWith('$') ? name[1..] : "$" + name;
            if (obj.Members.TryGetValue(alternate, out member) && member is ObjectPropertySymbol alternateFound)
            {
                return alternateFound;
            }

            return null;
        }

        private static string FormatDocVariable(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var trimmed = name.Trim();
            return trimmed.StartsWith('$') ? trimmed : "$" + trimmed;
        }
    }
}
