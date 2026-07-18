using CloudIntegrator.Core.Models;

namespace CloudIntegrator.Core.Interfaces;

public interface ICloudService
{
    CloudProvider Provider { get; }
    Task<bool> TestConnectionAsync();
    Task<Stream> DownloadAsync(string path);
    Task<bool> UploadAsync(string path, Stream data);
    Task<bool> DeleteAsync(string path);
    Task<IEnumerable<string>> ListAsync(string path);
}

public interface IIntegrationService
{
    Task<IntegrationResult> IntegrateAsync(IntegrationRequest request);
    Task<IEnumerable<IntegrationResult>> GetHistoryAsync(int limit = 100);
}

public interface IDataTransformService
{
    Task<Stream> TransformAsync(Stream input, string transformationType);
    Task<T> DeserializeAsync<T>(Stream input);
    Task<Stream> SerializeAsync<T>(T data);
}

public interface IConfigurationService
{
    CloudConfiguration GetConfiguration(CloudProvider provider);
    Task SaveConfigurationAsync(CloudProvider provider, CloudConfiguration config);
}
