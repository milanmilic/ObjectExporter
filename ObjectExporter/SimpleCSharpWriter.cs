using EnvDTE;
using System.Text;

namespace ObjectExporter
{
    internal static class SimpleCSharpWriter
    {
        private const int DefaultMaxDepth = 20;
        private const int DefaultMaxNodes = 20000;

        public static string WriteExpression(Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var sb = new StringBuilder();
            if (expr == null)
            {
                return "// No expression";
            }

            // Build a minimal set of POCO class definitions based on the debugger object graph.
            var ctx = new WriteContext(DefaultMaxDepth, DefaultMaxNodes);
            var rootName = ToPascalIdentifier(string.IsNullOrWhiteSpace(expr.Type) ? "Root" : expr.Type);
            var created = new System.Collections.Generic.Dictionary<string, ClassDef>(System.StringComparer.Ordinal);

            BuildClassForExpression(rootName, expr, depth: 0, ctx: ctx, created: created);

            sb.AppendLine("// Generated from debugger evaluation");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();

            // Emit in a stable order.
            foreach (var kvp in created)
            {
                EmitClass(sb, kvp.Value);
                sb.AppendLine();
            }

            // Emit object graph values (nested initializers)
            sb.AppendLine("// Values:");
            var initCtx = new WriteContext(DefaultMaxDepth, DefaultMaxNodes);
            sb.Append("var root = ");
            WriteInitializer(sb, expr, typeNameHint: rootName, parentNameHint: "Root", depth: 0, ctx: initCtx, indent: 0);
            sb.AppendLine(";");

            return sb.ToString().TrimEnd();
        }

        private static void WriteInitializer(StringBuilder sb, Expression expr, string typeNameHint, string parentNameHint, int depth, WriteContext ctx, int indent)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (expr == null)
            {
                sb.Append("null");
                return;
            }

            if (!ctx.TryEnter() || depth >= ctx.MaxDepth)
            {
                sb.Append("null");
                return;
            }

            // Prefer scalar values if possible.
            var scalar = TryFormatScalarLiteral(expr);
            if (scalar != null)
            {
                sb.Append(scalar);
                return;
            }

            var members = SafeDataMembers(expr);
            if (members == null || members.Count == 0)
            {
                sb.Append("null");
                return;
            }

            // Collection detection: members like [0], [1]
            if (LooksLikeArray(members))
            {
                // Best-effort infer element type name from parent hint
                var elemType = ToPascalIdentifier(Singularize(parentNameHint));
                sb.Append("new List<");
                sb.Append(elemType);
                sb.Append(">\n");
                Indent(sb, indent);
                sb.Append("{\n");

                var firstItem = true;
                for (int i = 1; i <= members.Count; i++)
                {
                    var m = SafeItem(members, i);
                    if (m?.Name == null || !IsIndexName(m.Name))
                    {
                        continue;
                    }

                    if (!firstItem)
                    {
                        sb.Append(",\n");
                    }
                    firstItem = false;

                    Indent(sb, indent + 4);
                    WriteInitializer(sb, m, typeNameHint: elemType, parentNameHint: elemType, depth: depth + 1, ctx: ctx, indent: indent + 4);
                }

                sb.Append("\n");
                Indent(sb, indent);
                sb.Append("}");
                return;
            }

            // Object initializer
            sb.Append("new ");
            sb.Append(ToPascalIdentifier(typeNameHint));
            sb.Append("\n");
            Indent(sb, indent);
            sb.Append("{\n");

            // Filter Field/Fields duplicates
            var nameSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= members.Count; i++)
            {
                var m0 = SafeItem(members, i);
                if (m0?.Name != null)
                {
                    nameSet.Add(m0.Name);
                }
            }

            var first = true;
            for (int i = 1; i <= members.Count; i++)
            {
                var m = SafeItem(members, i);
                if (m == null || string.IsNullOrWhiteSpace(m.Name))
                {
                    continue;
                }

                if (ShouldSkipMember(m.Name, nameSet))
                {
                    continue;
                }

                var propName = ToPascalIdentifier(m.Name);
                var propType = InferCSharpType(m, parentNameHint: propName, out var nestedType);

                if (!first)
                {
                    sb.Append(",\n");
                }
                first = false;

                Indent(sb, indent + 4);
                sb.Append(propName);
                sb.Append(" = ");

                // If it is a collection property, we want List<Elem> initializer.
                if (propType.StartsWith("List<", System.StringComparison.Ordinal) && nestedType != null)
                {
                    WriteInitializer(sb, m, typeNameHint: nestedType, parentNameHint: propName, depth: depth + 1, ctx: ctx, indent: indent + 4);
                }
                else if (nestedType != null)
                {
                    WriteInitializer(sb, m, typeNameHint: nestedType, parentNameHint: propName, depth: depth + 1, ctx: ctx, indent: indent + 4);
                }
                else
                {
                    var lit = TryFormatScalarLiteral(m);
                    if (lit != null)
                    {
                        sb.Append(lit);
                    }
                    else
                    {
                        // TryFormatScalarLiteral returned null (e.g. unrecognised complex/collection type).
                        // Let WriteInitializer try to expand it rather than emitting null.
                        WriteInitializer(sb, m, typeNameHint: propName, parentNameHint: propName, depth: depth + 1, ctx: ctx, indent: indent + 4);
                    }
                }
            }

            sb.Append("\n");
            Indent(sb, indent);
            sb.Append("}");
        }

        private static string TryFormatScalarLiteral(Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (expr == null)
            {
                return "null";
            }

            // Mirror JSON behavior: if Value is concrete/informative, emit scalar even if type isn't a known scalar.
            // This avoids many "null"s for values the debugger already formatted.
            if (!ShouldTreatAsScalarLikeJson(expr))
            {
                return null;
            }

            var type = expr.Type ?? string.Empty;

            var raw = expr.Value;
            if (raw == null)
            {
                return "null";
            }

            raw = raw.Trim();
            if (string.Equals(raw, "null", System.StringComparison.OrdinalIgnoreCase))
            {
                return "null";
            }

            // Unwrap debugger formatting: "LX" or {03/12/2026 00:00:00}
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
            {
                raw = raw.Substring(1, raw.Length - 2);
            }
            if (raw.Length >= 2 && raw[0] == '{' && raw[raw.Length - 1] == '}')
            {
                raw = raw.Substring(1, raw.Length - 2);
            }

            if (type.IndexOf("System.Boolean", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "bool", System.StringComparison.OrdinalIgnoreCase))
            {
                return raw.Equals("true", System.StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            }

            if (type.IndexOf("System.Int16", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Int32", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Int64", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.UInt16", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.UInt32", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.UInt64", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Byte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.SByte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "int", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "long", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "short", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "byte", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "uint", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "ulong", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "ushort", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "sbyte", System.StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(raw, out var l))
                {
                    return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (type.IndexOf("System.Single", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Double", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Decimal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "float", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "double", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "decimal", System.StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (type.IndexOf("System.DateTime", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Keep it simple and robust
                return $"DateTime.Parse(\"{EscapeString(raw)}\")";
            }

            // Default: string literal
            return $"\"{EscapeString(raw)}\"";
        }

        private static bool ShouldTreatAsScalarLikeJson(Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (expr == null)
            {
                return true;
            }

            var value = expr.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, "null", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var type = expr.Type ?? string.Empty;
            if (IsScalarType(type) ||
                type.IndexOf("System.String", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.DateTime", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Numeric-looking values
            if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return true;
            }

            // If it looks like a type placeholder, don't treat as scalar.
            var trimmed = value.Trim();
            if (trimmed == type || (trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}'))
            {
                return false;
            }

            // Debugger shows collections as "Count = N" or "Length = N" — must expand, not scalar.
            if (IsCollectionSummaryValue(trimmed))
            {
                return false;
            }

            return trimmed.Length <= 200;
        }

        /// <summary>
        /// Returns true when the debugger value string is a collection size summary like "Count = 2".
        /// Such values must be expanded via DataMembers rather than serialised as string literals.
        /// </summary>
        private static bool IsCollectionSummaryValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.StartsWith("Count = ", System.StringComparison.Ordinal) ||
                value.StartsWith("Length = ", System.StringComparison.Ordinal))
            {
                var eqIdx = value.IndexOf('=');
                if (eqIdx > 0)
                {
                    var rest = value.Substring(eqIdx + 1).Trim();
                    return int.TryParse(rest, System.Globalization.NumberStyles.None,
                                        System.Globalization.CultureInfo.InvariantCulture, out _);
                }
            }
            return false;
        }

        private static string EscapeString(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool LooksLikeArray(Expressions members)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (members == null || members.Count == 0)
            {
                return false;
            }

            int indexCount = 0;
            int checkedCount = 0;
            for (int i = 1; i <= members.Count && checkedCount < 20; i++)
            {
                var m = SafeItem(members, i);
                if (m?.Name == null)
                {
                    continue;
                }

                checkedCount++;
                if (IsIndexName(m.Name))
                {
                    indexCount++;
                }
            }

            return indexCount >= 1 && indexCount >= (checkedCount / 2);
        }

        private static void Indent(StringBuilder sb, int indent)
        {
            if (indent > 0)
            {
                sb.Append(new string(' ', indent));
            }
        }

        private static void BuildClassForExpression(string className, Expression expr, int depth, WriteContext ctx, System.Collections.Generic.Dictionary<string, ClassDef> created)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (expr == null)
            {
                return;
            }

            if (!ctx.TryEnter() || depth >= ctx.MaxDepth)
            {
                return;
            }

            if (created.ContainsKey(className))
            {
                return;
            }

            var members = SafeDataMembers(expr);
            if (members == null || members.Count == 0)
            {
                return;
            }

            var def = new ClassDef(className);
            created[className] = def;

            // Collect member names to filter noise like "XField"/"XFields" duplicates.
            var nameSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= members.Count; i++)
            {
                var m0 = SafeItem(members, i);
                if (m0?.Name != null)
                {
                    nameSet.Add(m0.Name);
                }
            }

            // Detect indexed members like "[0]" or "Leg[0]" so we can model them as a collection.
            var collectionName = TryDetectCollectionMemberBaseName(members);
            if (collectionName != null)
            {
                // Infer item type from first element.
                var elementExpr = TryGetFirstIndexedMember(members, collectionName);
                if (elementExpr != null)
                {
                    var itemType = InferCSharpType(elementExpr, parentNameHint: Singularize(collectionName), out var nested);
                    def.Properties.Add(new PropertyDef($"List<{itemType}>", ToPascalIdentifier(collectionName)));
                    if (nested != null)
                    {
                        BuildClassForExpression(nested, elementExpr, depth + 1, ctx, created);
                    }
                }
                return;
            }

            for (int i = 1; i <= members.Count; i++)
            {
                var m = SafeItem(members, i);
                if (m == null || string.IsNullOrWhiteSpace(m.Name))
                {
                    continue;
                }

                if (ShouldSkipMember(m.Name, nameSet))
                {
                    continue;
                }

                var propName = ToPascalIdentifier(m.Name);
                var propType = InferCSharpType(m, parentNameHint: propName, out var nestedClassName);

                def.Properties.Add(new PropertyDef(propType, propName));

                if (nestedClassName != null)
                {
                    BuildClassForExpression(nestedClassName, m, depth + 1, ctx, created);
                }
            }
        }

        private static string InferCSharpType(Expression expr, string parentNameHint, out string nestedClassName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            nestedClassName = null;

            if (expr == null)
            {
                return "object";
            }

            // Treat well-known scalar types as scalars even if debugger exposes DataMembers.
            var type = expr.Type ?? string.Empty;
            if (IsScalarType(type))
            {
                return MapScalarType(type);
            }

            // If it has members, treat as complex POCO
            var members = SafeDataMembers(expr);
            if (members != null && members.Count > 0)
            {
                // If this expression represents an indexed element ([0]) it may have Name="0".
                // Use the parent name hint (e.g. "Leg") for a meaningful class name.
                var proposed = ToPascalIdentifier(string.IsNullOrWhiteSpace(expr.Name) ? parentNameHint : expr.Name);
                if (IsNumericIdentifier(proposed))
                {
                    proposed = ToPascalIdentifier(string.IsNullOrWhiteSpace(parentNameHint) ? "Item" : parentNameHint);
                }

                nestedClassName = proposed;
                return proposed;
            }

            // Simple heuristics based on debugger value/type
            var value = expr.Value;

            if (string.Equals(value, "null", System.StringComparison.OrdinalIgnoreCase))
            {
                return "object";
            }

            if (type.IndexOf("System.String", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "string";
            }

            if (type.IndexOf("System.DateTime", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "DateTime";
            }

            if (type.IndexOf("System.Boolean", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "bool";
            }

            // Try numbers
            if (int.TryParse(value, out _))
            {
                return "int";
            }

            if (double.TryParse(value, out _))
            {
                return "double";
            }

            return "string";
        }

        private static bool ShouldSkipMember(string memberName, System.Collections.Generic.HashSet<string> allNames)
        {
            if (string.IsNullOrEmpty(memberName))
            {
                return true;
            }

            if (memberName.EndsWith("fields", System.StringComparison.OrdinalIgnoreCase))
            {
                var baseName = memberName.Substring(0, memberName.Length - "fields".Length);
                if (!string.IsNullOrEmpty(baseName) && allNames.Contains(baseName))
                {
                    return true;
                }
            }

            if (memberName.EndsWith("field", System.StringComparison.OrdinalIgnoreCase))
            {
                var baseName = memberName.Substring(0, memberName.Length - "field".Length);
                if (!string.IsNullOrEmpty(baseName) && allNames.Contains(baseName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNumericIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Singularize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Item";
            }

            if (name.EndsWith("ies", System.StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            {
                return name.Substring(0, name.Length - 3) + "y";
            }

            if (name.EndsWith("s", System.StringComparison.OrdinalIgnoreCase) && name.Length > 1)
            {
                return name.Substring(0, name.Length - 1);
            }

            return name;
        }

        private static bool IsScalarType(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return false;
            }

            // Full .NET type names
            if (type.IndexOf("System.String", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Char", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Boolean", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.DateTime", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Int16", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Int32", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Int64", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.UInt16", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.UInt32", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.UInt64", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Byte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.SByte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Single", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Double", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Decimal", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // C# keyword aliases that the VS debugger often reports directly
            switch (type.ToLowerInvariant())
            {
                case "int": case "long": case "short": case "byte":
                case "uint": case "ulong": case "ushort": case "sbyte":
                case "float": case "double": case "decimal":
                case "bool": case "string": case "char":
                    return true;
            }

            return false;
        }

        private static string MapScalarType(string type)
        {
            if (type.IndexOf("System.String", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "string", System.StringComparison.OrdinalIgnoreCase))
            {
                return "string";
            }

            if (type.IndexOf("System.Char", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "char", System.StringComparison.OrdinalIgnoreCase))
            {
                return "char";
            }

            if (type.IndexOf("System.Boolean", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "bool", System.StringComparison.OrdinalIgnoreCase))
            {
                return "bool";
            }

            if (type.IndexOf("System.DateTime", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "System.DateTime";
            }

            if (type.IndexOf("System.Int64", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "long", System.StringComparison.OrdinalIgnoreCase))
            {
                return "long";
            }

            if (type.IndexOf("System.Int16", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "short", System.StringComparison.OrdinalIgnoreCase))
            {
                return "short";
            }

            if (type.IndexOf("System.Int32", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "int", System.StringComparison.OrdinalIgnoreCase))
            {
                return "int";
            }

            if (type.IndexOf("System.UInt64", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "ulong", System.StringComparison.OrdinalIgnoreCase))
            {
                return "ulong";
            }

            if (type.IndexOf("System.UInt32", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "uint", System.StringComparison.OrdinalIgnoreCase))
            {
                return "uint";
            }

            if (type.IndexOf("System.UInt16", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "ushort", System.StringComparison.OrdinalIgnoreCase))
            {
                return "ushort";
            }

            if (type.IndexOf("System.SByte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "sbyte", System.StringComparison.OrdinalIgnoreCase))
            {
                return "sbyte";
            }

            if (type.IndexOf("System.Byte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "byte", System.StringComparison.OrdinalIgnoreCase))
            {
                return "byte";
            }

            if (type.IndexOf("System.Single", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "float", System.StringComparison.OrdinalIgnoreCase))
            {
                return "float";
            }

            if (type.IndexOf("System.Decimal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "decimal", System.StringComparison.OrdinalIgnoreCase))
            {
                return "decimal";
            }

            if (type.IndexOf("System.Double", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "double", System.StringComparison.OrdinalIgnoreCase))
            {
                return "double";
            }

            return "string";
        }

        private static string TryDetectCollectionMemberBaseName(Expressions members)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (members == null || members.Count <= 0)
            {
                return null;
            }

            // If we see "[0]" assume the class itself is a collection.
            for (int i = 1; i <= members.Count; i++)
            {
                var m = SafeItem(members, i);
                if (m?.Name == null)
                {
                    continue;
                }

                if (IsIndexName(m.Name))
                {
                    return "items";
                }
            }

            // Or property-like name with index e.g. "Leg[0]".
            for (int i = 1; i <= members.Count; i++)
            {
                var m = SafeItem(members, i);
                if (m?.Name == null)
                {
                    continue;
                }

                if (TryGetIndexedBaseName(m.Name, out var baseName))
                {
                    return baseName;
                }
            }

            return null;
        }

        private static Expression TryGetFirstIndexedMember(Expressions members, string baseName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (members == null)
            {
                return null;
            }

            for (int i = 1; i <= members.Count; i++)
            {
                var m = SafeItem(members, i);
                if (m?.Name == null)
                {
                    continue;
                }

                if (baseName == "items" && IsIndexName(m.Name))
                {
                    return m;
                }

                if (baseName != "items" && TryGetIndexedBaseName(m.Name, out var bn) && string.Equals(bn, baseName, System.StringComparison.Ordinal))
                {
                    return m;
                }
            }

            return null;
        }

        private static bool IsIndexName(string name)
        {
            // "[0]", "[1]", ...
            return name.Length >= 3 && name[0] == '[' && name[name.Length - 1] == ']';
        }

        private static bool TryGetIndexedBaseName(string name, out string baseName)
        {
            baseName = null;
            var idx = name.IndexOf('[');
            if (idx <= 0)
            {
                return false;
            }

            if (name[name.Length - 1] != ']')
            {
                return false;
            }

            baseName = name.Substring(0, idx);
            return !string.IsNullOrWhiteSpace(baseName);
        }

        private static Expressions SafeDataMembers(Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return expr?.DataMembers;
            }
            catch
            {
                return null;
            }
        }

        // Note: EnvDTE.Expression doesn't expose collection items in a stable way across VS versions.

        private static Expression SafeItem(Expressions expressions, int index)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return expressions.Item(index);
            }
            catch
            {
                return null;
            }
        }

        private sealed class WriteContext
        {
            public int MaxDepth { get; }
            public int MaxNodes { get; }
            private int _nodes;

            public WriteContext(int maxDepth, int maxNodes)
            {
                MaxDepth = maxDepth;
                MaxNodes = maxNodes;
            }

            public bool TryEnter()
            {
                _nodes++;
                return _nodes <= MaxNodes;
            }
        }

        private static void EmitClass(StringBuilder sb, ClassDef def)
        {
            sb.AppendLine($"public class {def.Name}");
            sb.AppendLine("{");
            for (int i = 0; i < def.Properties.Count; i++)
            {
                var p = def.Properties[i];
                sb.AppendLine($"    public {p.Type} {p.Name} {{ get; set; }}");
            }
            sb.AppendLine("}");
        }

        private static string ToPascalIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Item";
            }

            var cleaned = CleanIdentifier(name);
            if (cleaned.Length == 0)
            {
                return "Item";
            }

            return char.ToUpperInvariant(cleaned[0]) + cleaned.Substring(1);
        }

        private static string ToCamelIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "value";
            }

            var cleaned = CleanIdentifier(name);
            if (cleaned.Length == 0)
            {
                return "value";
            }

            return char.ToLowerInvariant(cleaned[0]) + cleaned.Substring(1);
        }

        private static string CleanIdentifier(string name)
        {
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (char.IsLetterOrDigit(ch) || ch == '_')
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private sealed class ClassDef
        {
            public string Name { get; }
            public System.Collections.Generic.List<PropertyDef> Properties { get; } = new System.Collections.Generic.List<PropertyDef>();

            public ClassDef(string name)
            {
                Name = name;
            }
        }

        private sealed class PropertyDef
        {
            public string Type { get; }
            public string Name { get; }

            public PropertyDef(string type, string name)
            {
                Type = type;
                Name = name;
            }
        }
    }
}
