using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ObjectExporter
{
    public class CSharpGenerator : IExportGenerator
    {
        public string Generate(TraversalResult traversalResult)
        {
            if (traversalResult == null || !traversalResult.IsSuccessful)
            {
                return "// Error generating C# code";
            }

            var sb = new StringBuilder();
            sb.AppendLine("// Generated C# object structure");
            sb.AppendLine();
            
            WriteNode(sb, traversalResult.RootNode, "root", indent: 0);
            
            return sb.ToString();
        }

        private void WriteNode(StringBuilder sb, Node node, string varName, int indent)
        {
            if (node == null)
            {
                Indent(sb, indent);
                sb.AppendLine($"var {varName} = null;");
                return;
            }

            // If it has Children, generate initialization
            if (node.Children != null && node.Children.Count > 0)
            {
                if (node.IsCollection)
                {
                    // Generate as List or Array
                    Indent(sb, indent);
                    sb.AppendLine($"var {varName} = new[]");
                    Indent(sb, indent);
                    sb.AppendLine("{");

                    bool first = true;
                    foreach (var kvp in node.Children.OrderBy(x => x.Key))
                    {
                        if (!first)
                        {
                            sb.AppendLine(",");
                        }
                        first = false;

                        if (kvp.Value.Children != null && kvp.Value.Children.Count > 0)
                        {
                            // Kompleksni objekat u nizu
                            Indent(sb, indent + 4);
                            sb.Append("new { ");
                            WriteObjectInitializer(sb, kvp.Value, indent + 4);
                            sb.Append(" }");
                        }
                        else
                        {
                            // Prosta vrednost
                            Indent(sb, indent + 4);
                            WriteCSharpValue(sb, kvp.Value.Value);
                        }
                    }

                    sb.AppendLine();
                    Indent(sb, indent);
                    sb.AppendLine("};");
                }
                else
                {
                    // Objekat
                    Indent(sb, indent);
                    sb.Append($"var {varName} = new");
                    sb.AppendLine();
                    Indent(sb, indent);
                    sb.AppendLine("{");
                    WriteObjectProperties(sb, node, indent + 4);
                    Indent(sb, indent);
                    sb.AppendLine("};");
                }
            }
            else
            {
                // Leaf vrednost
                Indent(sb, indent);
                sb.Append($"var {varName} = ");
                WriteCSharpValue(sb, node.Value);
                sb.AppendLine(";");
            }
        }

        private void WriteObjectInitializer(StringBuilder sb, Node node, int indent)
        {
            bool first = true;
            foreach (var kvp in node.Children.OrderBy(x => x.Key))
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                first = false;

                sb.Append(SanitizePropertyName(kvp.Key));
                sb.Append(" = ");

                if (kvp.Value.Children != null && kvp.Value.Children.Count > 0)
                {
                    sb.Append("new { ");
                    WriteObjectInitializer(sb, kvp.Value, indent);
                    sb.Append(" }");
                }
                else
                {
                    WriteCSharpValue(sb, kvp.Value.Value);
                }
            }
        }

        private void WriteObjectProperties(StringBuilder sb, Node node, int indent)
        {
            bool first = true;
            foreach (var kvp in node.Children.OrderBy(x => x.Key))
            {
                if (!first)
                {
                    sb.AppendLine(",");
                }
                first = false;

                Indent(sb, indent);
                sb.Append(SanitizePropertyName(kvp.Key));
                sb.Append(" = ");

                if (kvp.Value.Children != null && kvp.Value.Children.Count > 0)
                {
                    if (kvp.Value.IsCollection)
                    {
                        sb.Append("new[] { ");
                        WriteArrayItems(sb, kvp.Value);
                        sb.Append(" }");
                    }
                    else
                    {
                        sb.Append("new { ");
                        WriteObjectInitializer(sb, kvp.Value, indent);
                        sb.Append(" }");
                    }
                }
                else
                {
                    WriteCSharpValue(sb, kvp.Value.Value);
                }
            }
            sb.AppendLine();
        }

        private void WriteArrayItems(StringBuilder sb, Node node)
        {
            bool first = true;
            foreach (var kvp in node.Children.OrderBy(x => x.Key))
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                first = false;

                if (kvp.Value.Children != null && kvp.Value.Children.Count > 0)
                {
                    sb.Append("new { ");
                    WriteObjectInitializer(sb, kvp.Value, 0);
                    sb.Append(" }");
                }
                else
                {
                    WriteCSharpValue(sb, kvp.Value.Value);
                }
            }
        }

        private void WriteCSharpValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            var valueStr = value.ToString();

            // Proveri da li je string tipa "[Circular Reference]" ili "[Error: ...]"
            if (valueStr.StartsWith("[") && valueStr.EndsWith("]"))
            {
                WriteCSharpString(sb, valueStr);
                return;
            }

            if (value is string)
            {
                WriteCSharpString(sb, (string)value);
            }
            else if (value is bool)
            {
                sb.Append(valueStr.ToLowerInvariant());
            }
            else if (value is byte || value is sbyte || value is short || value is ushort ||
                     value is int || value is uint)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is long l)
            {
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                sb.Append("L");
            }
            else if (value is ulong ul)
            {
                sb.Append(ul.ToString(CultureInfo.InvariantCulture));
                sb.Append("UL");
            }
            else if (value is float f)
            {
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                sb.Append("f");
            }
            else if (value is double d)
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                sb.Append("d");
            }
            else if (value is decimal dec)
            {
                sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                sb.Append("m");
            }
            else if (value is DateTime dt)
            {
                sb.Append("DateTime.Parse(");
                WriteCSharpString(sb, dt.ToString("O"));
                sb.Append(")");
            }
            else
            {
                WriteCSharpString(sb, valueStr);
            }
        }

        private void WriteCSharpString(StringBuilder sb, string value)
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

        private string SanitizePropertyName(string name)
        {
            // Ukloni [0], [1] za nizove i make valid C# identifier
            if (name.StartsWith("[") && name.EndsWith("]"))
            {
                return "Item" + name.Substring(1, name.Length - 2);
            }

            // If it starts with a number or has invalid characters
            if (!char.IsLetter(name[0]) && name[0] != '_')
            {
                return "_" + name;
            }

            return name;
        }

        private void Indent(StringBuilder sb, int indent)
        {
            if (indent > 0)
            {
                sb.Append(new string(' ', indent));
            }
        }
    }
}
