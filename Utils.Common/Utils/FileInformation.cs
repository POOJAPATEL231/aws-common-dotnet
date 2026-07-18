namespace Utils.Common.Utils
{
    /// <summary>
    /// Helpers for mapping between MIME content types and file extensions.
    /// </summary>
    public static class FileInformation
    {
        private static readonly Dictionary<string, string> _extensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/json"] = ".json",
            ["application/xml"] = ".xml",
            ["text/xml"] = ".xml",
            ["text/plain"] = ".txt",
            ["text/csv"] = ".csv",
            ["text/html"] = ".html",
            ["application/pdf"] = ".pdf",
            ["application/zip"] = ".zip",
            ["application/gzip"] = ".gz",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["application/vnd.ms-excel"] = ".xls",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
            ["application/vnd.ms-powerpoint"] = ".ppt",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp",
            ["image/webp"] = ".webp",
            ["image/svg+xml"] = ".svg",
            ["image/tiff"] = ".tiff",
            ["audio/mpeg"] = ".mp3",
            ["video/mp4"] = ".mp4",
            ["application/octet-stream"] = ".bin"
        };

        /// <summary>
        /// Returns the file extension (including the leading dot) for a MIME content type,
        /// or an empty string when the content type is unknown.
        /// </summary>
        public static string GetFileExtension(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return string.Empty;
            }

            // Strip any parameters, e.g. "text/plain; charset=utf-8"
            var mimeType = contentType.Split(';')[0].Trim();
            return _extensionsByContentType.TryGetValue(mimeType, out var extension) ? extension : string.Empty;
        }
    }
}
