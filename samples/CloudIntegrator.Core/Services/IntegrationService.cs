using CloudIntegrator.Core.Interfaces;
using CloudIntegrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CloudIntegrator.Core.Services;

public class IntegrationService : IIntegrationService
{
    private readonly IEnumerable<ICloudService> _cloudServices;
    private readonly IDataTransformService _transformService;
    private readonly ILogger<IntegrationService> _logger;
    private readonly List<IntegrationResult> _history = new();

    public IntegrationService(
        IEnumerable<ICloudService> cloudServices,
        IDataTransformService transformService,
        ILogger<IntegrationService> logger)
    {
        _cloudServices = cloudServices;
        _transformService = transformService;
        _logger = logger;
    }

    public async Task<IntegrationResult> IntegrateAsync(IntegrationRequest request)
    {
        var result = new IntegrationResult { RequestId = request.Id };
        
        try
        {
            _logger.LogInformation("Starting integration {RequestId} from {Source} to {Target}", 
                request.Id, request.SourceProvider, request.TargetProvider);

            var sourceService = GetCloudService(request.SourceProvider);
            var targetService = GetCloudService(request.TargetProvider);

            // Download from source
            using var sourceData = await sourceService.DownloadAsync(request.SourcePath);
            
            // Transform if needed
            var transformedData = sourceData;
            if (request.Metadata.ContainsKey("transform"))
            {
                var transformType = request.Metadata["transform"].ToString()!;
                transformedData = await _transformService.TransformAsync(sourceData, transformType);
            }

            // Upload to target
            var uploadSuccess = await targetService.UploadAsync(request.TargetPath, transformedData);

            result.Success = uploadSuccess;
            result.Message = uploadSuccess ? "Integration completed successfully" : "Upload failed";
            
            _history.Add(result);
            _logger.LogInformation("Integration {RequestId} completed with status: {Success}", 
                request.Id, result.Success);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            _logger.LogError(ex, "Integration {RequestId} failed", request.Id);
            _history.Add(result);
        }

        return result;
    }

    public async Task<IEnumerable<IntegrationResult>> GetHistoryAsync(int limit = 100)
    {
        return await Task.FromResult(_history.TakeLast(limit).ToList());
    }

    private ICloudService GetCloudService(CloudProvider provider)
    {
        return _cloudServices.FirstOrDefault(s => s.Provider == provider)
            ?? throw new NotSupportedException($"Cloud provider {provider} is not supported");
    }
}
