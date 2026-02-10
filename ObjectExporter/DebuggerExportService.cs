using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Threading.Tasks;

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
                expr = dte.Debugger.GetExpression(expression, true, 1);
            }
            catch
            {
                expr = null;
            }

            if (expr == null)
            {
                throw new InvalidOperationException($"Expression is not valid: {expression}");
            }

            // Minimal JSON: name/value + immediate children (one level)
            // For complex objects, user can expand by editing expression and re-export.
            var json = SimpleJsonWriter.WriteExpression(expr);
            return json;
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

            var expr = dte.Debugger.GetExpression(expression, true, 1);
            if (expr == null)
            {
                throw new InvalidOperationException($"Expression is not valid: {expression}");
            }

            return SimpleCSharpWriter.WriteExpression(expr);
        }
    }
}