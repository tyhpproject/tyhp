namespace Tyhp.Domain.Services
{
    /// <summary>
    /// PHP Reflection does not report DateInterval's public handler properties, collapses
    /// DatePeriod's three constructors into one mixed 4-arg signature, and spells
    /// <c>DateTimeZone::getTransitions</c> / <c>timezone_transitions_get</c>
    /// <c>$timestampEnd</c> as <c>PHP_INT_MAX</c> on 64-bit PHP &lt; 8.5 (arginfo) while the
    /// implemented default is <c>2147483647</c>. Applied after Reflection mapping so regen
    /// does not reintroduce those gaps.
    /// </summary>
    internal static class DateExtensionReflectionFixes
    {
        internal const string TimestampEndDefault = "2147483647";

        private static readonly (string Name, string Type)[] DateIntervalProperties =
        [
            ("d", "int"),
            ("date_string", "string"),
            ("days", "int|false"),
            ("f", "float"),
            ("from_string", "bool"),
            ("h", "int"),
            ("i", "int"),
            ("invert", "int"),
            ("m", "int"),
            ("s", "int"),
            ("y", "int"),
        ];

        public static void Apply(TyhpdefFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            foreach (var function in file.GlobalFunctions ?? [])
            {
                FixTransitionsDefault(function, className: null);
            }

            foreach (var type in file.GlobalTypes ?? [])
            {
                ApplyToClass(type);
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var function in ns.Functions ?? [])
                {
                    FixTransitionsDefault(function, className: null);
                }

                // Engine Date* types live in the global namespace.
            }
        }

        private static void ApplyToClass(TyhpdefClassDeclaration type)
        {
            var name = ShortName(type.Name);
            if (name.Equals("DateInterval", StringComparison.Ordinal))
            {
                EnsureDateIntervalProperties(type);
                return;
            }

            if (name.Equals("DatePeriod", StringComparison.Ordinal))
            {
                EnsureDatePeriodConstructors(type);
                return;
            }

            if (name.Equals("DateTimeZone", StringComparison.Ordinal))
            {
                foreach (var method in type.Methods ?? [])
                {
                    FixTransitionsDefault(method, className: name);
                }
            }
        }

        private static void EnsureDateIntervalProperties(TyhpdefClassDeclaration type)
        {
            var properties = type.Properties;
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < properties.Count; i++)
            {
                var existingName = properties[i].Name?.Trim().TrimStart('$') ?? "";
                if (existingName.Length > 0 && !byName.ContainsKey(existingName))
                {
                    byName[existingName] = i;
                }
            }

            foreach (var (name, documentedType) in DateIntervalProperties)
            {
                if (byName.TryGetValue(name, out var index))
                {
                    var existing = properties[index];
                    if (IsMixedType(existing.Type))
                    {
                        properties[index] = existing with { Type = documentedType };
                    }

                    continue;
                }

                properties.Add(new TyhpdefProperty
                {
                    Name = name,
                    Type = documentedType,
                    Modifiers = ["public"],
                });
            }
        }

        private static void EnsureDatePeriodConstructors(TyhpdefClassDeclaration type)
        {
            var methods = type.Methods;
            var constructors = new List<(int Index, TyhpdefMethod Method)>();
            for (var i = 0; i < methods.Count; i++)
            {
                if (IsConstructor(methods[i].Name))
                {
                    constructors.Add((i, methods[i]));
                }
            }

            var documented = DocumentedDatePeriodConstructors();
            var hasCollapsed = constructors.Any(c => IsCollapsedDatePeriodConstructor(c.Method));
            var missingDocumented = documented
                .Where(d => constructors.All(c => !SameCtorShape(c.Method, d)))
                .ToList();
            if (!hasCollapsed && missingDocumented.Count == 0)
            {
                return;
            }

            var insertAt = constructors.Count > 0 ? constructors[0].Index : methods.Count;
            var kept = new List<TyhpdefMethod>(documented.Count + constructors.Count);
            foreach (var template in documented)
            {
                var existing = constructors
                    .Select(c => c.Method)
                    .FirstOrDefault(m => !IsCollapsedDatePeriodConstructor(m) && SameCtorShape(m, template));
                kept.Add(existing ?? template);
            }

            foreach (var (_, method) in constructors)
            {
                if (IsCollapsedDatePeriodConstructor(method))
                {
                    continue;
                }

                if (documented.Any(d => SameCtorShape(method, d)))
                {
                    continue;
                }

                kept.Add(method);
            }

            for (var i = constructors.Count - 1; i >= 0; i--)
            {
                methods.RemoveAt(constructors[i].Index);
                if (constructors[i].Index < insertAt)
                {
                    insertAt--;
                }
            }

            if (insertAt < 0)
            {
                insertAt = 0;
            }

            if (insertAt > methods.Count)
            {
                insertAt = methods.Count;
            }

            methods.InsertRange(insertAt, kept);
        }

        private static List<TyhpdefMethod> DocumentedDatePeriodConstructors()
            =>
            [
                NewConstructor(
                    [
                        Param("start", "\\DateTimeInterface"),
                        Param("interval", "\\DateInterval"),
                        Param("recurrences", "int"),
                        Param("options", "int", "0"),
                    ]),
                NewConstructor(
                    [
                        Param("start", "\\DateTimeInterface"),
                        Param("interval", "\\DateInterval"),
                        Param("end", "\\DateTimeInterface"),
                        Param("options", "int", "0"),
                    ]),
                NewConstructor(
                    [
                        Param("isostr", "string"),
                        Param("options", "int", "0"),
                    ],
                    isDeprecated: true),
            ];

        private static TyhpdefMethod NewConstructor(List<TyhpdefParameter> parameters, bool isDeprecated = false)
            => new()
            {
                Name = "__construct",
                Modifiers = ["public"],
                Parameters = parameters,
                ReturnType = "void",
                IsDeprecated = isDeprecated,
            };

        private static TyhpdefParameter Param(string name, string type, string? defaultValue = null)
            => new()
            {
                Name = name,
                Type = type,
                DefaultValue = defaultValue,
            };

        private static bool IsCollapsedDatePeriodConstructor(TyhpdefMethod method)
        {
            if (!IsConstructor(method.Name))
            {
                return false;
            }

            var parameters = method.Parameters ?? [];
            if (parameters.Count != 4)
            {
                return false;
            }

            return NameEquals(parameters[0].Name, "start")
                && NameEquals(parameters[1].Name, "interval")
                && NameEquals(parameters[2].Name, "end")
                && NameEquals(parameters[3].Name, "options")
                && parameters.All(p => IsMixedType(p.Type));
        }

        private static bool SameCtorShape(TyhpdefMethod left, TyhpdefMethod right)
        {
            var leftParams = left.Parameters ?? [];
            var rightParams = right.Parameters ?? [];
            if (leftParams.Count != rightParams.Count)
            {
                return false;
            }

            for (var i = 0; i < leftParams.Count; i++)
            {
                if (!NameEquals(leftParams[i].Name, rightParams[i].Name))
                {
                    return false;
                }

                if (!CanonicalType(leftParams[i].Type).Equals(
                        CanonicalType(rightParams[i].Type),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static void FixTransitionsDefault(TyhpdefMethod method, string? className)
        {
            if (!IsTransitionsCallable(method.Name, className))
            {
                return;
            }

            var parameters = method.Parameters;
            if (parameters is null || parameters.Count == 0)
            {
                return;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (!NameEquals(parameter.Name, "timestampEnd"))
                {
                    continue;
                }

                if (!IsPhpIntMaxDefault(parameter.DefaultValue))
                {
                    continue;
                }

                parameters[i] = parameter with { DefaultValue = TimestampEndDefault };
            }
        }

        private static bool IsTransitionsCallable(string? name, string? className)
        {
            if (string.Equals(name, "timezone_transitions_get", StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(name, "getTransitions", StringComparison.Ordinal)
                && string.Equals(className, "DateTimeZone", StringComparison.Ordinal);
        }

        private static bool IsPhpIntMaxDefault(string? value)
        {
            var trimmed = (value ?? "").Trim().TrimStart('\\');
            if (trimmed.Equals("PHP_INT_MAX", StringComparison.Ordinal))
            {
                return true;
            }

            return trimmed.Equals("9223372036854775807", StringComparison.Ordinal);
        }

        private static bool IsMixedType(string? type)
        {
            var trimmed = (type ?? "").Trim();
            return trimmed.Length == 0
                || trimmed.Equals("mixed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsConstructor(string? name)
            => string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase);

        private static bool NameEquals(string? left, string right)
            => string.Equals((left ?? "").Trim().TrimStart('$'), right, StringComparison.OrdinalIgnoreCase);

        private static string CanonicalType(string? type)
            => (type ?? "").Trim().TrimStart('\\');

        private static string ShortName(string? name)
        {
            var trimmed = (name ?? "").Trim().TrimStart('\\');
            var slash = trimmed.LastIndexOf('\\');
            return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        }
    }
}
