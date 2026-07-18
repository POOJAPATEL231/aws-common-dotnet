namespace Application.Common.FileProvider
{
    /// <summary>
    /// Cloud object-storage abstraction (implemented for AWS S3 by
    /// Infrastructure.Common.AWS.FileProvider.AwsS3FileProvider).
    /// Paths are of the form "container/objectKey".
    /// </summary>
    public interface IFileProvider : IDisposable
    {
        /// <summary>Deletes a single object. <paramref name="fileName"/> is "container/objectKey".</summary>
        Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default);

        /// <summary>Gets a single file (with data) by full path.</summary>
        Task<UploadedFile?> GetAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Gets multiple files (with data) by full paths.</summary>
        Task<List<UploadedFile>> GetFilesAsync(List<string> paths, CancellationToken cancellationToken = default);

        /// <summary>Gets all files (with data) in a container.</summary>
        Task<List<UploadedFile>> GetFilesAsync(string containerName, CancellationToken cancellationToken = default);

        /// <summary>Gets all files (with data) in a container filtered by key prefix.</summary>
        Task<List<UploadedFile>> GetFilesAsync(string containerName, string prefix, CancellationToken cancellationToken = default);

        /// <summary>Lists files (without data) in a container.</summary>
        Task<List<UploadedFile>> GetListAsync(string containerName, CancellationToken cancellationToken = default);

        /// <summary>Lists files (without data) in a container filtered by key prefix.</summary>
        Task<List<UploadedFile>> GetListAsync(string containerName, string prefix, CancellationToken cancellationToken = default);

        /// <summary>Replaces the metadata of an existing object.</summary>
        Task<bool> SetMetaDataAsync(string path, IDictionary<string, string> metaData, CancellationToken cancellationToken = default);

        /// <summary>Uploads raw data with a generated file name; returns the stored path or null on failure.</summary>
        Task<string?> UploadAsync(string containerName, string prefix, string contentType, byte[] data, CancellationToken cancellationToken = default);

        /// <summary>Uploads a set of files to a container.</summary>
        Task<bool> UploadAsync(string containerName, List<UploadedFile> files, CancellationToken cancellationToken = default);

        /// <summary>Uploads a set of files to a container under an optional key prefix.</summary>
        Task<bool> UploadAsync(string containerName, string? prefix, List<UploadedFile> files, CancellationToken cancellationToken = default);
    }
}
