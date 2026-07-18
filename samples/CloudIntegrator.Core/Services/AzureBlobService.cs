using Azure.Storage.Blobs;
using CloudIntegrator.Core.Interfaces;
using CloudIntegrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CloudIntegrator.Core.Services;

public class AzureBlobService : ICloudService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobService> _logger;

    public CloudProvider Provider => CloudProvider.Azure;

    public AzureBlobService(string connectionString, ILogger<AzureBlobService> logger)
    {
        _blobServiceClient = new BlobServiceClient(connectionString);
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await _blobServiceClient.GetPropertiesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test Azure connection");
            return false;
        }
    }

    public async Task<Stream> DownloadAsync(string path)
    {
        var (containerName, blobName) = ParsePath(path);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        
        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }

    public async Task<bool> UploadAsync(string path, Stream data)
    {
        try
        {
            var (containerName, blobName) = ParsePath(path);
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();
            
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(data, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to Azure: {Path}", path);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string path)
    {
        try
        {
            var (containerName, blobName) = ParsePath(path);
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            
            await blobClient.DeleteIfExistsAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete from Azure: {Path}", path);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListAsync(string path)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(path);
        var blobs = new List<string>();
        
        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            blobs.Add(blobItem.Name);
        }
        
        return blobs;
    }

    private static (string containerName, string blobName) ParsePath(string path)
    {
        var parts = path.Split('/', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], string.Empty);
    }
}
