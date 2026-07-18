using CloudIntegrator.Core.Interfaces;
using CloudIntegrator.Core.Models;
using CloudIntegrator.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CloudIntegrator.Tests;

public class IntegrationServiceTests
{
    private readonly Mock<ICloudService> _mockCloudService;
    private readonly Mock<IDataTransformService> _mockTransformService;
    private readonly Mock<ILogger<IntegrationService>> _mockLogger;
    private readonly IntegrationService _integrationService;

    public IntegrationServiceTests()
    {
        _mockCloudService = new Mock<ICloudService>();
        _mockTransformService = new Mock<IDataTransformService>();
        _mockLogger = new Mock<ILogger<IntegrationService>>();
        
        _mockCloudService.Setup(x => x.Provider).Returns(CloudProvider.Azure);
        
        _integrationService = new IntegrationService(
            new[] { _mockCloudService.Object },
            _mockTransformService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task IntegrateAsync_ShouldReturnSuccess_WhenIntegrationCompletes()
    {
        // Arrange
        var request = new IntegrationRequest
        {
            SourceProvider = CloudProvider.Azure,
            TargetProvider = CloudProvider.Azure,
            SourcePath = "source/file.txt",
            TargetPath = "target/file.txt"
        };

        var sourceData = new MemoryStream();
        _mockCloudService.Setup(x => x.DownloadAsync(request.SourcePath))
            .ReturnsAsync(sourceData);
        _mockCloudService.Setup(x => x.UploadAsync(request.TargetPath, It.IsAny<Stream>()))
            .ReturnsAsync(true);

        // Act
        var result = await _integrationService.IntegrateAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(request.Id, result.RequestId);
    }

    [Fact]
    public async Task GetHistoryAsync_ShouldReturnEmptyList_Initially()
    {
        // Act
        var history = await _integrationService.GetHistoryAsync();

        // Assert
        Assert.Empty(history);
    }
}
