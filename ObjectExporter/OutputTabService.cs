using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ObjectExporter
{
    internal static class OutputTabService
    {
        public static async Task OpenOrUpdateTabAsync(AsyncPackage package, string title, string content, string extension)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var uiShellOpenDocument = await package.GetServiceAsync(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;
            if (uiShellOpenDocument == null)
            {
                throw new InvalidOperationException("Visual Studio services unavailable.");
            }

            var tempDir = System.IO.Path.GetTempPath();
            var safeName = MakeSafeFileName(title);
            var path = System.IO.Path.Combine(tempDir, $"ObjectExport_{safeName}.{extension}");

            System.IO.File.WriteAllText(path, content ?? string.Empty);

            var viewGuid = Microsoft.VisualStudio.VSConstants.LOGVIEWID_TextView;
            uiShellOpenDocument.OpenDocumentViaProject(path, ref viewGuid, out _, out _, out _, out var frame);
            if (frame != null)
            {
                frame.Show();
            }
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "export";
            }

            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Length > 80 ? name.Substring(0, 80) : name;
        }
    }
}
