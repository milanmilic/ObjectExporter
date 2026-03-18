using EnvDTE;
using Microsoft.VisualStudio.Shell;
using ObjectExporter.Models;
using ObjectExporter.Views;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ObjectExporter
{
    public class ExportService
    {
        private readonly IObjectTraverser _objectTraverser;
        private readonly IExportGenerator _jsonGenerator;
        private readonly IExportGenerator _csharpGenerator;

        // Default timeout of 3 seconds before showing the dialog
        private readonly TimeSpan _dialogShowDelay = TimeSpan.FromSeconds(3);

        public ExportService()
        {
            _objectTraverser = new ObjectTraverser();
            _jsonGenerator = new JsonGenerator();
            _csharpGenerator = new CSharpGenerator();
        }

        // Methods for EnvDTE.Expression (uses existing writers directly)
        public async Task<ExportResult> ExportExpressionToJsonAsync(
            Expression expression,
            CancellationToken cancellationToken = default)
        {
            return await ExportExpressionAsync(expression, ExportType.Json, cancellationToken);
        }

        public async Task<ExportResult> ExportExpressionToCSharpAsync(
            Expression expression,
            CancellationToken cancellationToken = default)
        {
            return await ExportExpressionAsync(expression, ExportType.CSharp, cancellationToken);
        }

        // Methods for generic .NET objects (uses ObjectTraverser + generators)
        public async Task<ExportResult> ExportObjectToJsonAsync(
            object obj,
            CancellationToken cancellationToken = default)
        {
            return await ExportObjectAsync(obj, ExportType.Json, cancellationToken);
        }

        public async Task<ExportResult> ExportObjectToCSharpAsync(
            object obj,
            CancellationToken cancellationToken = default)
        {
            return await ExportObjectAsync(obj, ExportType.CSharp, cancellationToken);
        }

        // Export for EnvDTE.Expression - uses existing writers directly
        // NOTE: SimpleWriter/CSharpWriter BLOCK the UI thread and do not support cancellation
        // Timeout dialog is not possible until writers are refactored to be async
        private async Task<ExportResult> ExportExpressionAsync(
            Expression expression,
            ExportType exportType,
            CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (expression == null || !expression.IsValidValue)
            {
                return ExportResult.Error("Expression is not valid.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Direct call - blocks UI thread until complete
                // TODO: Refactor SimpleWriter to support async operations
                string result;
                if (exportType == ExportType.Json)
                {
                    result = SimpleJsonWriter.WriteExpression(expression);
                }
                else if (exportType == ExportType.CSharp)
                {
                    result = SimpleCSharpWriter.WriteExpression(expression);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(exportType));
                }

                return ExportResult.CreateSuccess(result, exportType);
            }
            catch (OperationCanceledException)
            {
                return ExportResult.Cancelled("Operation was cancelled by the user.");
            }
            catch (Exception ex)
            {
                return ExportResult.Error($"Error during export: {ex.Message}");
            }
        }

        // Export for generic .NET objects - uses ObjectTraverser + generators
        private async Task<ExportResult> ExportObjectAsync(
            object obj,
            ExportType exportType,
            CancellationToken cancellationToken)
        {
            if (obj == null)
            {
                return ExportResult.Error("Object is null.");
            }

            CancellationTokenSource dialogCts = null;

            try
            {
                // Create CTS for internal timeout management
                dialogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                // Start the export operation (uses ObjectTraverser + generators)
                var exportTask = Task.Run(async () =>
                {
                    await Task.Yield();
                    
                    cancellationToken.ThrowIfCancellationRequested();

                    // 1. Traverse the object
                    var traversalResult = await _objectTraverser.TraverseAsync(obj, cancellationToken);

                    if (!traversalResult.IsSuccessful)
                    {
                        throw new InvalidOperationException($"Traversal error: {traversalResult.Error}");
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // 2. Generate output
                    string result;
                    if (exportType == ExportType.Json)
                    {
                        result = _jsonGenerator.Generate(traversalResult);
                    }
                    else if (exportType == ExportType.CSharp)
                    {
                        result = _csharpGenerator.Generate(traversalResult);
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(exportType));
                    }

                    return result;
                }, cancellationToken);

                return await WaitForTaskWithDialog(exportTask, exportType, dialogCts, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return ExportResult.Cancelled("Operation was cancelled by the user.");
            }
            catch (Exception ex)
            {
                return ExportResult.Error($"Error during export: {ex.Message}");
            }
            finally
            {
                if (dialogCts != null)
                {
                    dialogCts.Dispose();
                }
            }
        }

        // Helper method for waiting on a task with a timeout dialog
        private async Task<ExportResult> WaitForTaskWithDialog(
            Task<string> exportTask,
            ExportType exportType,
            CancellationTokenSource dialogCts,
            CancellationToken cancellationToken)
        {
            // FIX: Use SwitchToMainThreadAsync to ensure we are on the UI thread
            // for dialog operations
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Wait briefly before showing the dialog
            var delayTask = Task.Delay(_dialogShowDelay, cancellationToken);
            var completedTask = await Task.WhenAny(exportTask, delayTask);

            // If the export completed quickly, return the result
            if (completedTask == exportTask)
            {
                var content = await exportTask;
                return ExportResult.CreateSuccess(content, exportType);
            }

            // Export is taking long - show the dialog on the UI thread
            bool userWantsToWait = await ShowTimeoutDialogAsync(dialogCts);

            if (!userWantsToWait)
            {
                // User wants to cancel
                dialogCts.Cancel();
                
                // NOTE: SimpleWriter/CSharpWriter DO NOT support cancellation,
                // so the operation may continue running, but we mark it as cancelled
                return ExportResult.Cancelled("Operation was cancelled by the user. Note: Conversion may continue in the background because writers do not support cancellation.");
            }

            // User wants to wait - wait for completion
            try
            {
                var content2 = await exportTask;
                return ExportResult.CreateSuccess(content2, exportType);
            }
            catch (Exception ex)
            {
                return ExportResult.Error($"Error: {ex.Message}");
            }
        }

        private async Task<bool> ShowTimeoutDialogAsync(CancellationTokenSource dialogCts)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dialog = new Views.TimeoutDialog(
                "The export operation is taking longer than expected.\nWould you like to wait or cancel?",
                "Cancel now",
                "Wait"
            );

            var result = dialog.ShowDialog();

            // true = Cancel, false = Wait
            // Return the opposite: true if user wants to wait
            return result != true;
        }
    }
}