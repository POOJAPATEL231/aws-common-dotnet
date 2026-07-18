namespace Application.Common.FileProvider
{
    /// <summary>
    /// Represents a file stored in (or being uploaded to) cloud object storage
    /// such as AWS S3 or Azure Blob Storage.
    /// </summary>
    public class UploadedFile
    {
        /// <summary>File name (object key without the container/bucket segment).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Full path in the form "container/objectKey". Set after a successful upload.</summary>
        public string? Path { get; set; }

        /// <summary>Raw file content. Null when only listing without data.</summary>
        public byte[]? Data { get; set; }

        /// <summary>Optional content type (MIME) of the file.</summary>
        public string? ContentType { get; set; }

        /// <summary>Custom metadata stored alongside the object.</summary>
        public Dictionary<string, string>? MetaData { get; set; }

        /// <summary>Optional time-to-live in days, applied as an object tag for lifecycle rules.</summary>
        public int? TtlDays { get; set; }
    }
}
