using System;
using System.Globalization;
using System.Text;

namespace ObjectExporter
{
    public interface IExportGenerator
    {
        string Generate(TraversalResult traversalResult);
    }

    public class JsonGenerator : IExportGenerator
    {
        public string Generate(TraversalResult traversalResult)
        {
            if (traversalResult == null || !traversalResult.IsSuccessful)
            {
                return "null";
            }

            var sb = new StringBuilder();
            WriteNode(sb, traversalResult.RootNode, indent: 0);
            return sb.ToString();
        }

        private void WriteNode(StringBuilder sb, Node node, int indent)
        {
            if (node == null)
            {
                sb.Append("null");
                return;
            }

            // If it has Children, generate an object or array
            if (node.Children != null && node.Children.Count > 0)
            {
                if (node.IsCollection)
                {
                    // JSON niz
                    sb.Append("[\n");
                    bool first = true;

                    foreach (var kvp in node.Children)
                    {
                        if (!first)
                        {
                            sb.Append(",\n");
                        }
                        first = false;

                        Indent(sb, indent + 2);
                        WriteNode(sb, kvp.Value, indent + 2);
                    }

                    sb.Append("\n");
                    Indent(sb, indent);
                    sb.Append("]");
                }
                else
                {
                    // JSON objekat
                    sb.Append("{\n");
                    bool first = true;

                    foreach (var kvp in node.Children)
                    {
                        if (!first)
                        {
                            sb.Append(",\n");
                        }
                        first = false;

                        Indent(sb, indent + 2);
                        WriteJsonString(sb, kvp.Key);
                        sb.Append(": ");
                        WriteNode(sb, kvp.Value, indent + 2);
                    }

                    sb.Append("\n");
                    Indent(sb, indent);
                    sb.Append("}");
                }
            }
            else
            {
                // Leaf vrednost
                WriteJsonValue(sb, node.Value);
            }
        }

        private void WriteJsonValue(StringBuilder sb, object value)
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
                WriteJsonString(sb, valueStr);
                return;
            }

            if (value is string)
            {
                WriteJsonString(sb, (string)value);
            }
            else if (value is bool)
            {
                sb.Append(valueStr.ToLowerInvariant());
            }
            else if (value is byte || value is sbyte || value is short || value is ushort ||
                     value is int || value is uint || value is long || value is ulong ||
                     value is float || value is double || value is decimal)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is DateTime dt)
            {
                WriteJsonString(sb, dt.ToString("O"));
            }
            else
            {
                WriteJsonString(sb, valueStr);
            }
        }

        private void WriteJsonString(StringBuilder sb, string value)
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

        private void Indent(StringBuilder sb, int indent)
        {
            if (indent > 0)
            {
                sb.Append(new string(' ', indent));
            }
        }
    }
}
