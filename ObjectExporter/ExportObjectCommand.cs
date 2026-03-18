using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;

namespace ObjectExporter
{
    internal enum ExportFormat
    {
        Json = 1,
        CSharp = 2,
    }

    internal sealed class ExportObjectCommand
    {
        public const int ExportToJsonCommandId = 0x0100;
        public const int ExportToCSharpCommandId = 0x0101;

        public static readonly Guid CommandSet = new Guid("4c70fd0b-f459-4e4b-a566-3c42d185a2d1");

        private readonly AsyncPackage package;

        private string _capturedWindowCaption;
        private string _capturedDebugExpression;

        private ExportObjectCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));

            var exportJsonCommandId = new CommandID(CommandSet, ExportToJsonCommandId);
            var exportJson = new OleMenuCommand(async (s, e) => await ExecuteAsync(ExportFormat.Json), exportJsonCommandId);
            exportJson.BeforeQueryStatus += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                UpdateVisibility((OleMenuCommand)s);
            };
            commandService.AddCommand(exportJson);

            var exportCSharpCommandId = new CommandID(CommandSet, ExportToCSharpCommandId);
            var exportCSharp = new OleMenuCommand(async (s, e) => await ExecuteAsync(ExportFormat.CSharp), exportCSharpCommandId);
            exportCSharp.BeforeQueryStatus += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                UpdateVisibility((OleMenuCommand)s);
            };
            commandService.AddCommand(exportCSharp);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            _ = new ExportObjectCommand(package, commandService);
        }

        private void UpdateVisibility(OleMenuCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var debugger = package.GetServiceAsync(typeof(SVsShellDebugger)).GetAwaiter().GetResult() as IVsDebugger;
            if (debugger == null)
            {
                command.Visible = false;
                command.Enabled = false;
                return;
            }

            DBGMODE[] mode = new DBGMODE[1];
            if (ErrorHandler.Failed(debugger.GetMode(mode)))
            {
                command.Visible = false;
                command.Enabled = false;
                return;
            }

            var isDebugging = mode[0] != DBGMODE.DBGMODE_Design;
            command.Visible = isDebugging;
            command.Enabled = isDebugging;

            // Pre-capture the selected expression while the debug window still has focus.
            // BeforeQueryStatus fires before the menu renders, so FocusedElement is still the row.
            if (isDebugging)
            {
                try
                {
                    var dte = package.GetServiceAsync(typeof(EnvDTE.DTE)).GetAwaiter().GetResult() as EnvDTE.DTE;
                    _capturedWindowCaption = dte?.ActiveWindow?.Caption ?? string.Empty;
                    _capturedDebugExpression = DebuggerExportService.IsDebugVariableWindow(_capturedWindowCaption)
                        ? DebuggerExportService.TryGetDebugWindowExpression()
                        : null;
                }
                catch
                {
                    _capturedWindowCaption = string.Empty;
                    _capturedDebugExpression = null;
                }
            }
        }

        private async Task ExecuteAsync(ExportFormat format)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string expr;
            if (DebuggerExportService.IsDebugVariableWindow(_capturedWindowCaption))
            {
                expr = _capturedDebugExpression;
                if (string.IsNullOrWhiteSpace(expr))
                {
                    await DebuggerExportService.ShowMessageAsync(package, "Export Object",
                        "Could not determine the selected variable. Right-click directly on a variable row in the debug window.");
                    return;
                }
            }
            else
            {
                expr = await DebuggerExportService.TryGetExpressionTextAsync(package);
                if (string.IsNullOrWhiteSpace(expr))
                {
                    await DebuggerExportService.ShowMessageAsync(package, "Export Object", "No expression found under caret.");
                    return;
                }
            }

            string content;
            string title;

            try
            {
                if (format == ExportFormat.Json)
                {
                    title = $"{expr} (JSON)";
                    content = await DebuggerExportService.EvaluateToJsonAsync(package, expr);
                }
                else
                {
                    title = $"{expr} (C#)";
                    content = await DebuggerExportService.EvaluateToCSharpAsync(package, expr);
                }
            }
            catch (OperationCanceledException)
            {
                await DebuggerExportService.ShowMessageAsync(package, "Export Object", "Conversion was cancelled by the user.");
                return;
            }
            catch (Exception ex)
            {
                await DebuggerExportService.ShowMessageAsync(package, "Export Object", ex.Message);
                return;
            }

            await OutputTabService.OpenOrUpdateTabAsync(package, title, content, format == ExportFormat.Json ? "json" : "cs");
        }
    }
}
