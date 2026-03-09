using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace ObjectExporter
{
    internal static class DebuggerExportService
    {
        public static bool IsInBreakMode(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = package.GetServiceAsync(typeof(DTE)).ConfigureAwait(false).GetAwaiter().GetResult() as DTE;
            if (dte?.Debugger == null)
            {
                return false;
            }

            return dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode;
        }

        public static bool IsDebugVariableWindow(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return false;
            return caption == "Locals" ||
                   caption == "Autos" ||
                   caption.StartsWith("Watch", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the Name column of the selected row in a Locals/Autos/Watch window using UI Automation.
        /// Must be called while the debug window still has keyboard focus (e.g. from BeforeQueryStatus).
        /// </summary>
        public static string TryGetDebugWindowExpression()
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null) return null;

                // Walk up the automation tree to find the row (DataItem / ListItem / TreeItem).
                var current = focused;
                AutomationElement row = null;
                for (int depth = 0; depth < 10; depth++)
                {
                    var ct = current.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                    if (ct == ControlType.DataItem || ct == ControlType.ListItem || ct == ControlType.TreeItem)
                    {
                        row = current;
                        break;
                    }
                    var parent = TreeWalker.RawViewWalker.GetParent(current);
                    if (parent == null) break;
                    current = parent;
                }

                if (row == null) return null;

                // Read the first cell (Name column) specifically.
                // VS Locals/Autos DataGridCells report as ControlType.Custom in WPF UIA.
                var cell = TreeWalker.RawViewWalker.GetFirstChild(row);
                while (cell != null)
                {
                    var cellCt = cell.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                    if (cellCt == ControlType.Custom || cellCt == ControlType.Edit ||
                        cellCt == ControlType.DataItem || cellCt == ControlType.Text)
                    {
                        // ValuePattern gives the raw cell text without decoration.
                        object patternObj;
                        if (cell.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                        {
                            var val = ((ValuePattern)patternObj).Current.Value;
                            if (!string.IsNullOrWhiteSpace(val))
                                return ExtractExpressionName(val);
                        }

                        var cellName = cell.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
                        if (!string.IsNullOrWhiteSpace(cellName))
                            return ExtractExpressionName(cellName);

                        break;
                    }
                    cell = TreeWalker.RawViewWalker.GetNextSibling(cell);
                }

                // Last resort: row's own Name property (may include value/type info — clean it).
                var rowName = row.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
                return string.IsNullOrWhiteSpace(rowName) ? null : ExtractExpressionName(rowName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a valid C# identifier / member-access expression from potentially noisy UIA text.
        /// Stops at the first character that can't appear in a C# expression (space, '=', '{', ';', etc.).
        /// </summary>
        private static string ExtractExpressionName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();

            var sb = new System.Text.StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[' || c == ']')
                    sb.Append(c);
                else
                    break;
            }

            var result = sb.ToString().TrimEnd('.');
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        public static async Task ShowMessageAsync(AsyncPackage package, string title, string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        public static async Task<string> TryGetExpressionTextAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var textMgr = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            if (textMgr == null)
            {
                return null;
            }

            if (textMgr.GetActiveView(1, null, out var view) != 0 || view == null)
            {
                return null;
            }

            if (view.GetCaretPos(out var line, out var col) != 0)
            {
                return null;
            }

            if (view.GetBuffer(out var lines) != 0 || lines == null)
            {
                return null;
            }

            if (lines.GetLengthOfLine(line, out var lineLen) != 0)
            {
                return null;
            }

            lines.GetLineText(line, 0, line, lineLen, out var fullLine);
            if (string.IsNullOrEmpty(fullLine))
            {
                return null;
            }

            var start = col;
            if (start > fullLine.Length)
            {
                start = fullLine.Length;
            }

            int left = start;
            int right = start;

            bool IsIdentChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '>';

            while (left > 0 && IsIdentChar(fullLine[left - 1]))
            {
                left--;
            }

            while (right < fullLine.Length && IsIdentChar(fullLine[right]))
            {
                right++;
            }

            var expr = fullLine.Substring(left, Math.Max(0, right - left)).Trim();

            // Defensive cleanup: remove any trailing debugger format suffix if it sneaks in.
            // Example: "response, nq" -> "response"
            var comma = expr.IndexOf(',');
            if (comma > 0)
            {
                expr = expr.Substring(0, comma).Trim();
            }

            return string.IsNullOrWhiteSpace(expr) ? null : expr;
        }

        public static async Task<string> EvaluateToJsonAsync(AsyncPackage package, string expression)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE;
            if (dte?.Debugger == null)
            {
                throw new InvalidOperationException("Debugger service unavailable.");
            }

            if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            {
                throw new InvalidOperationException("Pause debugging (break mode) to export objects.");
            }

            Expression expr;
            try
            {
                expr = dte.Debugger.GetExpression(expression, true, 3000);
            }
            catch
            {
                expr = null;
            }

            if (expr == null || !expr.IsValidValue)
            {
                throw new InvalidOperationException($"Expression could not be evaluated: {expression}");
            }

            // Yield so VS can finish closing the context menu before we start the heavy traversal.
            await Task.Yield();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return SimpleJsonWriter.WriteExpression(expr);
        }

        public static async Task<string> EvaluateToCSharpAsync(AsyncPackage package, string expression)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE;
            if (dte?.Debugger == null)
            {
                throw new InvalidOperationException("Debugger service unavailable.");
            }

            if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            {
                throw new InvalidOperationException("Pause debugging (break mode) to export objects.");
            }

            var expr = dte.Debugger.GetExpression(expression, true, 3000);
            if (expr == null || !expr.IsValidValue)
            {
                throw new InvalidOperationException($"Expression could not be evaluated: {expression}");
            }

            // Yield so VS can finish closing the context menu before we start the heavy traversal.
            await Task.Yield();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return SimpleCSharpWriter.WriteExpression(expr);
        }
    }
}