using EnvDTE;
using System;
using System.Globalization;
using System.Text;

namespace ObjectExporter
{
    internal static class SimpleJsonWriter
    {
        // Safety limits to avoid huge/cyclic graphs from debugger evaluation.
        private const int DefaultMaxDepth = 20;
        private const int DefaultMaxNodes = 20000;

        public static string WriteExpression(Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (expr == null)
            {
                return "null";
            }

            var sb = new StringBuilder();
            var ctx = new WriteContext(DefaultMaxDepth, DefaultMaxNodes);
            WriteValue(sb, expr, depth: 0, ctx: ctx, indent: 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, Expression expr, int depth, WriteContext ctx, int indent)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (expr == null)
            {
                sb.Append("null");
                return;
            }

            if (!ctx.TryEnter())
            {
                sb.Append("null");
                return;
            }

            if (depth >= ctx.MaxDepth)
            {
                // Stop expanding further.
                WriteJsonString(sb, expr.Value);
                return;
            }

            // If the debugger already provides a concrete value, prefer it and do NOT expand members.
            // This avoids dumping framework/internal properties for types like DateTime.
            if (ShouldTreatAsScalar(expr))
            {
                WriteJsonScalar(sb, expr);
                return;
            }

            // Otherwise, emit JSON object with properties.
            var members = SafeDataMembers(expr);
            if (members == null || members.Count == 0)
            {
                // Leaf value.
                WriteJsonScalar(sb, expr);
                return;
            }

            // If members look like indexed items ([0], [1], ...), serialize as JSON array.
            if (LooksLikeArray(members))
            {
                sb.Append("[\n");
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

                    Indent(sb, indent + 2);
                    WriteValue(sb, m, depth + 1, ctx, indent + 2);
                }

                sb.Append("\n");
                Indent(sb, indent);
                sb.Append("]");
                return;
            }

            sb.Append("{\n");

            // First pass: collect member names so we can filter noise like "XField"/"XFields" duplicates.
            var nameSet = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= members.Count; i++)
            {
                var m0 = SafeItem(members, i);
                if (m0?.Name != null)
                {
                    nameSet.Add(m0.Name);
                }
            }

            bool first = true;
            for (int i = 1; i <= members.Count; i++)
            {
                var m = SafeItem(members, i);
                if (m == null || string.IsNullOrEmpty(m.Name))
                {
                    continue;
                }

                // Skip compiler/debugger generated backing-field-like duplicates.
                if (ShouldSkipMember(m.Name, nameSet))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(",\n");
                }
                first = false;

                Indent(sb, indent + 2);
                WriteJsonString(sb, m.Name);
                sb.Append(": ");
                WriteValue(sb, m, depth + 1, ctx, indent + 2);
            }

            sb.Append("\n");
            Indent(sb, indent);
            sb.Append("}");
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

            // If a decent portion of members are [n], treat it as an array.
            return indexCount >= 1 && indexCount >= (checkedCount / 2);
        }

        private static bool IsIndexName(string name)
        {
            return name.Length >= 3 && name[0] == '[' && name[name.Length - 1] == ']';
        }

        private static bool ShouldSkipMember(string memberName, System.Collections.Generic.HashSet<string> allNames)
        {
            if (string.IsNullOrEmpty(memberName))
            {
                return true;
            }

            // If we have both "airportCode" and "airportCodeField(s)", keep the base property only.
            if (memberName.EndsWith("fields", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = memberName.Substring(0, memberName.Length - "fields".Length);
                if (!string.IsNullOrEmpty(baseName) && allNames.Contains(baseName))
                {
                    return true;
                }
            }

            if (memberName.EndsWith("field", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = memberName.Substring(0, memberName.Length - "field".Length);
                if (!string.IsNullOrEmpty(baseName) && allNames.Contains(baseName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldTreatAsScalar(Expression expr)
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

            if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Common scalar types.
            var type = expr.Type ?? string.Empty;
            if (type.IndexOf("System.String", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Char", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.Boolean", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("System.DateTime", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Numbers (best-effort) - debugger formats them as text.
            double number;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }

            // Heuristic: many complex objects show value like "{Namespace.Type}" or "Namespace.Type".
            // If it looks like a type name/placeholder, expand instead.
            if (value == type || (value.Length >= 2 && value[0] == '{' && value[value.Length - 1] == '}'))
            {
                return false;
            }

            // Debugger shows collections as "Count = N" or "Length = N" — must expand, not scalar.
            if (IsCollectionSummaryValue(value))
            {
                return false;
            }

            // If value is already short and informative, keep it scalar.
            // This also prevents dumping internal implementation details.
            return value.Length <= 200;
        }

        /// <summary>
        /// Returns true when the debugger value string is a collection size summary like "Count = 2".
        /// Such values must be expanded via DataMembers rather than serialised as strings.
        /// </summary>
        private static bool IsCollectionSummaryValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.StartsWith("Count = ", StringComparison.Ordinal) ||
                value.StartsWith("Length = ", StringComparison.Ordinal))
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

        private static void Indent(StringBuilder sb, int indent)
        {
            if (indent > 0)
            {
                sb.Append(new string(' ', indent));
            }
        }

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

        private static Expressions SafeDataMembers(Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return expr.DataMembers;
            }
            catch
            {
                return null;
            }
        }

        // Note: EnvDTE.Expression doesn't expose collection items in a stable way across VS versions.
        // We rely on DataMembers for expansion.

        private static void WriteJsonString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void WriteJsonScalar(StringBuilder sb, Expression expr)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            var raw = expr?.Value;
            if (raw == null)
            {
                sb.Append("null");
                return;
            }

            raw = raw.Trim();

            if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("null");
                return;
            }

            // Booleans
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(raw.ToLowerInvariant());
                return;
            }

            // Numbers (emit as JSON number)
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            {
                // Preserve integer formatting when possible
                if (raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0)
                {
                    if (long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var lint))
                    {
                        sb.Append(lint.ToString(CultureInfo.InvariantCulture));
                        return;
                    }
                }

                sb.Append(num.ToString(CultureInfo.InvariantCulture));
                return;
            }

            // Debugger often returns strings already quoted: "LX". Unwrap one level.
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
            {
                raw = raw.Substring(1, raw.Length - 2);
            }

            // Debugger often wraps many values in braces: {03/12/2026 00:00:00}
            if (raw.Length >= 2 && raw[0] == '{' && raw[raw.Length - 1] == '}')
            {
                raw = raw.Substring(1, raw.Length - 2);
            }

            WriteJsonString(sb, raw);
        }
    }
}
