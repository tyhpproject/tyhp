using System.Globalization;
using System.Text.Json;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Maps schema-version-1 Reflection JSON onto <see cref="TyhpdefFile"/> IR.
    /// </summary>
    public static class PhpReflectionMapper
    {
        public static TyhpdefFile Map(
            string json,
            TyhpdefGenerationOptions options,
            PhpRuntimeInfo runtime)
            => Map(PhpReflectionJson.Deserialize(json), options, runtime);

        internal static TyhpdefFile Map(
            PhpReflectionDumpDto dump,
            TyhpdefGenerationOptions options,
            PhpRuntimeInfo runtime)
        {
            ArgumentNullException.ThrowIfNull(dump);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(runtime);

            var file = new TyhpdefFile
            {
                Header = BuildHeader(dump, runtime),
            };

            var namespaces = new Dictionary<string, TyhpdefNamespace>(StringComparer.Ordinal);
            var extensionName = ResolveExtensionName(options, dump);

            foreach (var constant in dump.Constants ?? [])
            {
                if (!ShouldInclude(constant.Deprecated, constant.DocComment, options))
                {
                    continue;
                }

                AddConstant(file, namespaces, constant);
            }

            foreach (var function in dump.Functions ?? [])
            {
                if (!ShouldInclude(function.Deprecated, function.DocComment, options))
                {
                    continue;
                }

                AddFunction(file, namespaces, function);
            }

            foreach (var classLike in dump.Classes ?? [])
            {
                if (classLike.IsAnonymous)
                {
                    continue;
                }

                if (!ShouldInclude(classLike.Deprecated, classLike.DocComment, options))
                {
                    continue;
                }

                AddClass(file, namespaces, classLike, options, extensionName);
            }

            foreach (var ns in namespaces.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                file.Namespaces.Add(ns);
            }

            DateExtensionReflectionFixes.Apply(file);
            return file;
        }

        internal static string MapType(PhpReflectionTypeDto? type)
        {
            if (type is null)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(type.Text))
            {
                return type.Text.Trim();
            }

            return (type.Kind ?? "none").ToLowerInvariant() switch
            {
                "none" => "",
                "named" => type.Name?.Trim() ?? "",
                "nullable" => type.Types.Count > 0
                    ? "?" + MapType(type.Types[0]).TrimStart('?')
                    : (type.Name is { Length: > 0 } ? "?" + type.Name.TrimStart('?') : ""),
                "union" => string.Join("|", type.Types.Select(MapType).Where(t => t.Length > 0)),
                "intersection" => string.Join("&", type.Types.Select(MapType).Where(t => t.Length > 0)),
                _ => type.Name?.Trim() ?? "",
            };
        }

        internal static string? MapValue(PhpReflectionValueDto? value)
        {
            if (value is null)
            {
                return null;
            }

            return (value.Kind ?? "").ToLowerInvariant() switch
            {
                "null" => "null",
                "bool" => MapBool(value.Value) ? "true" : "false",
                "int" => MapNumber(value.Value, integer: true),
                "float" => MapNumber(value.Value, integer: false),
                "string" => MapString(value.Value),
                "array" => MapArray(value.Value),
                "const" => string.IsNullOrWhiteSpace(value.ConstName) ? null : value.ConstName.Trim(),
                "nan" => "NAN",
                "inf" => "INF",
                "neginf" => "-INF",
                "unavailable" => null,
                _ => null,
            };
        }

        private static void AddConstant(
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            PhpReflectionConstantDto constant)
        {
            var mapped = MapConstant(constant, isClassMember: false);
            var (ns, shortName) = SplitName(constant.Name);
            mapped = mapped with { Name = shortName };
            if (ns.Length == 0)
            {
                file.GlobalConstants.Add(mapped);
                return;
            }

            GetNamespace(namespaces, ns).Constants.Add(mapped);
        }

        private static void AddFunction(
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            PhpReflectionFunctionDto function)
        {
            var mapped = MapFunction(function);
            var (ns, shortName) = SplitName(function.Name);
            mapped = mapped with { Name = shortName };
            if (ns.Length == 0)
            {
                file.GlobalFunctions.Add(mapped);
                return;
            }

            GetNamespace(namespaces, ns).Functions.Add(mapped);
        }

        private static void AddClass(
            TyhpdefFile file,
            Dictionary<string, TyhpdefNamespace> namespaces,
            PhpReflectionClassDto classLike,
            TyhpdefGenerationOptions options,
            string extensionName)
        {
            var fqn = string.IsNullOrWhiteSpace(classLike.Fqn) ? classLike.Name : classLike.Fqn;
            var (ns, shortName) = SplitName(fqn);
            var mapped = MapClass(classLike, shortName, options, extensionName);
            if (IsEmptyPdoDriverHost(mapped, classLike.Fqn, classLike.Name))
            {
                return;
            }

            if (ns.Length == 0)
            {
                file.GlobalTypes.Add(mapped);
                return;
            }

            GetNamespace(namespaces, ns).Classes.Add(mapped);
        }

        private static TyhpdefConstant MapConstant(PhpReflectionConstantDto constant, bool isClassMember)
        {
            var value = MapValue(constant.Value);
            var type = MapType(constant.Type);
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "mixed";
            }

            return new TyhpdefConstant
            {
                Name = constant.Name,
                Type = type,
                Value = value,
                UsesCoalesce = value is not null,
                Modifiers = isClassMember ? NormalizeModifiers(constant.Modifiers) : [],
                DocComment = constant.DocComment,
                IsDeprecated = constant.Deprecated,
            };
        }

        private static TyhpdefFunction MapFunction(PhpReflectionFunctionDto function)
        {
            return new TyhpdefFunction
            {
                Name = function.Name,
                Modifiers = [],
                Attributes = MapAttributes(function.Attributes),
                Parameters = MapParameters(function.Params),
                ReturnType = TyhpdefRequiredTypes.ReturnType(function.Name, MapType(function.ReturnType)),
                DocComment = function.DocComment,
                IsDeprecated = function.Deprecated,
                ReturnsReference = function.ReturnByRef,
            };
        }

        private static TyhpdefClassDeclaration MapClass(
            PhpReflectionClassDto classLike,
            string shortName,
            TyhpdefGenerationOptions options,
            string extensionName)
        {
            var kind = (classLike.Kind ?? "class").Trim().ToLowerInvariant();
            if (kind is not ("class" or "interface" or "trait" or "enum"))
            {
                kind = "class";
            }

            var isPdoClass = PdoDriverClassConstants.IsPdoClass(classLike.Fqn, classLike.Name);
            var isPdoDriverHost = isPdoClass && PdoDriverClassConstants.IsPdoDriverExtension(extensionName);

            var methods = new List<TyhpdefMethod>();
            if (!isPdoDriverHost)
            {
                foreach (var method in classLike.Methods ?? [])
                {
                    if (!ShouldInclude(method.Deprecated, method.DocComment, options))
                    {
                        continue;
                    }

                    methods.Add(MapMethod(method));
                }
            }

            var properties = new List<TyhpdefProperty>();
            if (!isPdoDriverHost)
            {
                foreach (var property in classLike.Properties ?? [])
                {
                    if (!ShouldInclude(property.Deprecated, property.DocComment, options))
                    {
                        continue;
                    }

                    properties.Add(MapProperty(property));
                }
            }

            // Pre-isEnumCase() snapshots (and PHP's getReflectionConstants()) list every
            // enum case again under constants. Cases are already mapped from enumCases;
            // keeping both emits `case` plus `public const mixed` and fails bind (TYHP8002)
            // or parse-check for reserved names such as Default.
            var enumCaseNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enumCase in classLike.EnumCases ?? [])
            {
                if (!string.IsNullOrEmpty(enumCase.Name))
                {
                    enumCaseNames.Add(enumCase.Name);
                }
            }

            var constants = new List<TyhpdefConstant>();
            foreach (var constant in classLike.Constants ?? [])
            {
                if (!string.IsNullOrEmpty(constant.Name) && enumCaseNames.Contains(constant.Name))
                {
                    continue;
                }

                if (isPdoClass
                    && !PdoDriverClassConstants.ShouldIncludePdoClassConstant(constant.Name, extensionName))
                {
                    continue;
                }

                if (!ShouldInclude(constant.Deprecated, constant.DocComment, options))
                {
                    continue;
                }

                constants.Add(MapConstant(constant, isClassMember: true));
            }

            return new TyhpdefClassDeclaration
            {
                Kind = kind,
                Name = shortName,
                IsPartial = isPdoDriverHost,
                Modifiers = NormalizeModifiers(classLike.Modifiers),
                Attributes = MapAttributes(classLike.Attributes),
                Extends = isPdoDriverHost ? null : EmptyToNull(classLike.Extends),
                Implements = isPdoDriverHost
                    ? []
                    : (classLike.Implements ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
                Uses = isPdoDriverHost
                    ? []
                    : (classLike.Uses ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
                BackingType = EmptyToNull(classLike.BackingType),
                Constants = constants,
                Properties = properties,
                Methods = methods,
                EnumCases = (classLike.EnumCases ?? [])
                    .Select(c => new TyhpdefEnumCase
                    {
                        Name = c.Name,
                        BackingValue = MapValue(c.Backing),
                        DocComment = c.DocComment,
                    })
                    .ToList(),
                DocComment = classLike.DocComment,
                IsDeprecated = classLike.Deprecated,
            };
        }

        private static string ResolveExtensionName(
            TyhpdefGenerationOptions options,
            PhpReflectionDumpDto dump)
        {
            if (!string.IsNullOrWhiteSpace(options.ExtensionName))
            {
                return options.ExtensionName.Trim();
            }

            return (dump.Extension ?? "").Trim();
        }

        private static bool IsEmptyPdoDriverHost(
            TyhpdefClassDeclaration mapped,
            string? fqn,
            string? shortName)
        {
            if (!mapped.IsPartial || !PdoDriverClassConstants.IsPdoClass(fqn, shortName))
            {
                return false;
            }

            return mapped.Constants.Count == 0
                && mapped.Methods.Count == 0
                && mapped.Properties.Count == 0
                && mapped.EnumCases.Count == 0;
        }

        private static TyhpdefMethod MapMethod(PhpReflectionFunctionDto method)
        {
            return new TyhpdefMethod
            {
                Name = method.Name,
                Modifiers = NormalizeModifiers(method.Modifiers),
                Attributes = MapAttributes(method.Attributes),
                Parameters = MapParameters(method.Params),
                ReturnType = TyhpdefRequiredTypes.ReturnType(method.Name, MapType(method.ReturnType)),
                DocComment = method.DocComment,
                IsDeprecated = method.Deprecated,
                ReturnsReference = method.ReturnByRef,
            };
        }

        private static TyhpdefProperty MapProperty(PhpReflectionPropertyDto property)
        {
            var modifiers = NormalizeModifiers(property.Modifiers);
            if (modifiers.Count == 0)
            {
                modifiers = ["public"];
            }

            var type = MapType(property.Type);
            if (string.IsNullOrWhiteSpace(type))
            {
                type = "mixed";
            }

            return new TyhpdefProperty
            {
                Name = property.Name,
                Type = type,
                Modifiers = modifiers,
                CoalesceValue = property.HasDefault ? MapValue(property.Default) : null,
                DocComment = property.DocComment,
                IsDeprecated = property.Deprecated,
                Hooks = MapPropertyHooks(property.Hooks),
            };
        }

        private static List<TyhpdefPropertyHook> MapPropertyHooks(
            IEnumerable<PhpReflectionPropertyHookDto>? hooks)
        {
            var result = new List<TyhpdefPropertyHook>();
            foreach (var hook in hooks ?? [])
            {
                var name = hook.Name?.Trim() ?? "";
                if (name.Length == 0)
                {
                    continue;
                }

                result.Add(new TyhpdefPropertyHook
                {
                    Name = name,
                    ReturnsRef = hook.ReturnsRef,
                    Modifiers = NormalizeModifiers(hook.Modifiers),
                    Attributes = MapAttributes(hook.Attributes),
                });
            }

            return result;
        }

        private static List<TyhpdefParameter> MapParameters(IEnumerable<PhpReflectionParameterDto>? parameters)
        {
            var result = new List<TyhpdefParameter>();
            foreach (var parameter in parameters ?? [])
            {
                var mappedType = MapType(parameter.Type);
                result.Add(new TyhpdefParameter
                {
                    Name = parameter.Name,
                    Type = TyhpdefRequiredTypes.ParameterType(mappedType),
                    DefaultValue = parameter.Default is null ? null : MapValue(parameter.Default),
                    IsVariadic = parameter.Variadic,
                    IsByReference = parameter.ByRef,
                    IsPromoted = parameter.Promoted,
                    Attributes = MapAttributes(parameter.Attributes),
                });
            }

            return result;
        }

        private static List<TyhpdefAttribute> MapAttributes(IEnumerable<PhpReflectionAttributeDto>? attributes)
        {
            var result = new List<TyhpdefAttribute>();
            foreach (var attribute in attributes ?? [])
            {
                if (string.IsNullOrWhiteSpace(attribute.Name))
                {
                    continue;
                }

                var name = attribute.Name.Trim();
                result.Add(new TyhpdefAttribute
                {
                    Name = name,
                    Arguments = MapAttributeArguments(name, attribute.Args),
                });
            }

            return result;
        }

        private static List<string> MapAttributeArguments(
            string attributeName,
            IEnumerable<PhpReflectionValueDto>? args)
        {
            var mapped = new List<(string? Name, string Value)>();
            foreach (var arg in args ?? [])
            {
                var value = MapValue(arg);
                if (value is null)
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(arg.ArgName) ? null : arg.ArgName.Trim();
                if (name is { Length: > 0 } && name[0] == '$')
                {
                    name = name[1..];
                }

                mapped.Add((string.IsNullOrEmpty(name) ? null : name, value));
            }

            if (IsDeprecatedAttributeName(attributeName))
            {
                return SpellDeprecatedArguments(mapped);
            }

            return mapped
                .Select(a => string.IsNullOrEmpty(a.Name) ? a.Value : a.Name + ": " + a.Value)
                .ToList();
        }

        private static bool IsDeprecatedAttributeName(string name)
            => name.Equals("Deprecated", StringComparison.OrdinalIgnoreCase)
                || name.Equals("\\Deprecated", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("\\Deprecated", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Spell <c>#[\Deprecated]</c> with named <c>message:</c> / <c>since:</c> matching
        /// the PHP ctor. Unnamed args whose first literal looks like a PHP minor
        /// (<c>'8.1'</c>) are treated as <c>$since</c> — PHP stubs write
        /// <c>since:</c> first, and flattening those names used to swap the pair.
        /// </summary>
        private static List<string> SpellDeprecatedArguments(
            List<(string? Name, string Value)> mapped)
        {
            string? message = null;
            string? since = null;
            var unnamed = new List<string>();
            foreach (var (name, value) in mapped)
            {
                if (string.Equals(name, "message", StringComparison.OrdinalIgnoreCase))
                {
                    message = value;
                }
                else if (string.Equals(name, "since", StringComparison.OrdinalIgnoreCase))
                {
                    since = value;
                }
                else
                {
                    unnamed.Add(value);
                }
            }

            foreach (var value in unnamed)
            {
                if (since is null && LooksLikePhpVersionLiteral(value))
                {
                    since = value;
                }
                else if (message is null)
                {
                    message = value;
                }
                else
                {
                    since ??= value;
                }
            }

            var result = new List<string>();
            if (message is not null)
            {
                result.Add("message: " + message);
            }

            if (since is not null)
            {
                result.Add("since: " + since);
            }

            return result;
        }

        private static bool LooksLikePhpVersionLiteral(string spelled)
        {
            if (spelled.Length < 5 || spelled[0] != '\'' || spelled[^1] != '\'')
            {
                return false;
            }

            return Version.TryParse(spelled[1..^1], out _);
        }

        private static List<string> NormalizeModifiers(IEnumerable<string>? modifiers)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var modifier in modifiers ?? [])
            {
                var trimmed = modifier?.Trim() ?? "";
                if (trimmed.Length == 0 || !seen.Add(trimmed))
                {
                    continue;
                }

                result.Add(trimmed.ToLowerInvariant());
            }

            return result;
        }

        private static bool ShouldInclude(bool deprecated, string? docComment, TyhpdefGenerationOptions options)
        {
            if (deprecated && !options.IncludeDeprecated)
            {
                return false;
            }

            if (!options.IncludeInternal && IsInternal(docComment))
            {
                return false;
            }

            return true;
        }

        private static bool IsInternal(string? docComment)
        {
            if (string.IsNullOrWhiteSpace(docComment))
            {
                return false;
            }

            return docComment.Contains("@internal", StringComparison.OrdinalIgnoreCase);
        }

        private static TyhpdefNamespace GetNamespace(
            Dictionary<string, TyhpdefNamespace> namespaces,
            string name)
        {
            if (!namespaces.TryGetValue(name, out var ns))
            {
                ns = new TyhpdefNamespace { Name = name };
                namespaces[name] = ns;
            }

            return ns;
        }

        private static (string Namespace, string ShortName) SplitName(string name)
        {
            var trimmed = (name ?? "").Trim().TrimStart('\\');
            var last = trimmed.LastIndexOf('\\');
            if (last < 0)
            {
                return ("", trimmed);
            }

            return (trimmed[..last], trimmed[(last + 1)..]);
        }

        private static string BuildHeader(PhpReflectionDumpDto dump, PhpRuntimeInfo runtime)
        {
            var extVersion = string.IsNullOrWhiteSpace(dump.ExtensionVersion)
                ? ""
                : " v" + dump.ExtensionVersion.Trim();
            var mode = runtime.IsManaged ? "managed PHP" : "user PHP binary";
            return string.Join(
                "\n",
                [
                    $"Generated from PHP {dump.PhpVersion}, extension {dump.Extension}{extVersion}",
                    $"Runtime: {mode} ({runtime.Path})",
                ]);
        }

        private static string? EmptyToNull(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool MapBool(JsonElement? element)
        {
            if (element is null)
            {
                return false;
            }

            var json = element.Value;
            return json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => json.TryGetInt64(out var n) && n != 0,
                JsonValueKind.String => bool.TryParse(json.GetString(), out var b) && b,
                _ => false,
            };
        }

        private static string MapNumber(JsonElement? element, bool integer)
        {
            if (element is null)
            {
                return integer ? "0" : "0.0";
            }

            var json = element.Value;
            if (json.ValueKind == JsonValueKind.String)
            {
                return json.GetString() ?? (integer ? "0" : "0.0");
            }

            if (json.ValueKind != JsonValueKind.Number)
            {
                return integer ? "0" : "0.0";
            }

            if (integer && json.TryGetInt64(out var i))
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }

            if (json.TryGetDouble(out var d))
            {
                return d.ToString("0.0################", CultureInfo.InvariantCulture);
            }

            return json.GetRawText();
        }

        private static string MapString(JsonElement? element)
        {
            var text = "";
            if (element is not null && element.Value.ValueKind == JsonValueKind.String)
            {
                text = element.Value.GetString() ?? "";
            }
            else if (element is not null && element.Value.ValueKind != JsonValueKind.Null
                     && element.Value.ValueKind != JsonValueKind.Undefined)
            {
                text = element.Value.ToString();
            }

            return "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal)
                .Replace("\0", "\\0", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal) + "'";
        }

        private static string? MapArray(JsonElement? element)
        {
            if (element is null)
            {
                return "[]";
            }

            var json = element.Value;
            if (json.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var item in json.EnumerateArray())
                {
                    var mapped = MapNestedValue(item);
                    if (mapped is null)
                    {
                        return null;
                    }

                    items.Add(mapped);
                }

                return "[" + string.Join(", ", items) + "]";
            }

            if (json.ValueKind == JsonValueKind.Object)
            {
                var items = new List<string>();
                foreach (var property in json.EnumerateObject())
                {
                    var mapped = MapNestedValue(property.Value);
                    if (mapped is null)
                    {
                        return null;
                    }

                    items.Add(MapString(JsonSerializer.SerializeToElement(property.Name)) + " => " + mapped);
                }

                return "[" + string.Join(", ", items) + "]";
            }

            return "[]";
        }

        private static string? MapNestedValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("kind", out var kindEl))
            {
                var nested = new PhpReflectionValueDto
                {
                    Kind = kindEl.GetString() ?? "unavailable",
                    Value = element.TryGetProperty("value", out var valueEl) ? valueEl : null,
                    ConstName = element.TryGetProperty("constName", out var constEl)
                        ? constEl.GetString()
                        : null,
                };
                return MapValue(nested);
            }

            return element.ValueKind switch
            {
                JsonValueKind.Null => "null",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.String => MapString(element),
                JsonValueKind.Array => MapArray(element),
                JsonValueKind.Object => MapArray(element),
                _ => null,
            };
        }
    }
}
