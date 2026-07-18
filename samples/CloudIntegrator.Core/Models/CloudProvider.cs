namespace CloudIntegrator.Core.Models;

public enum CloudProvider
{
    Azure,
    AWS,
    GoogleCloud
}

public class CloudConfiguration
{
    public CloudProvider Provider { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new();
}

public class IntegrationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CloudProvider SourceProvider { get; set; }
    public CloudProvider TargetProvider { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class IntegrationResult
{
    public Guid RequestId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
