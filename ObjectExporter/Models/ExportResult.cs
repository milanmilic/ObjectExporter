namespace ObjectExporter.Models
{
    public class ExportResult
    {
        public bool IsSuccess { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsTimeout { get; set; }
        public string Content { get; set; }
        public ExportType ExportType { get; set; }
        public string Message { get; set; }

        public bool Success => IsSuccess;

        public static ExportResult CreateSuccess(string content, ExportType exportType) => new ExportResult
        {
            IsSuccess = true,
            Content = content,
            ExportType = exportType,
            Message = "Successfully exported."
        };

        public static ExportResult Cancelled(string message) => new ExportResult
        {
            IsCancelled = true,
            Message = message
        };

        public static ExportResult Timeout(string message) => new ExportResult
        {
            IsTimeout = true,
            Message = message
        };

        public static ExportResult Error(string message) => new ExportResult
        {
            IsSuccess = false,
            Message = message
        };
    }

    public enum ExportType
    {
        Json,
        CSharp
    }
}