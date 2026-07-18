using Application.Common;
using Application.Common.FileProvider;
using Microsoft.AspNetCore.Mvc;
using Utils.Common.Crypto;

namespace AwsShowcase.API.Controllers;

/// <summary>Request wrapper so string endpoints accept a proper JSON object
/// (<c>{"value":"..."}</c>) instead of a bare quoted string body.</summary>
public record TextRequest(string Value);

/// <summary>Request wrapper for decrypt.</summary>
public record CipherRequest(string CipherText);

/// <summary>S3 file provider - every IFileProvider method.</summary>
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IFileProvider _files;

    public FilesController(IFileProvider files)
    {
        _files = files;
    }

    /// <summary>UploadAsync(container, prefix, contentType, data) - single upload with generated name.</summary>
    [HttpPost("{container}")]
    public async Task<ActionResult<string?>> Upload(string container, [FromQuery] string prefix, [FromForm] IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var path = await _files.UploadAsync(container, prefix, file.ContentType, ms.ToArray(), ct);
        return Ok(path);
    }

    /// <summary>UploadAsync(container, files) + UploadAsync(container, prefix, files) - batch uploads.</summary>
    [HttpPost("{container}/batch")]
    public async Task<ActionResult<bool>> UploadBatch(string container, [FromQuery] string? prefix, [FromBody] Dictionary<string, string> namesToText, CancellationToken ct)
    {
        var files = namesToText.Select(kvp => new UploadedFile
        {
            Name = kvp.Key,
            Data = System.Text.Encoding.UTF8.GetBytes(kvp.Value)
        }).ToList();

        var ok = prefix is null
            ? await _files.UploadAsync(container, files, ct)
            : await _files.UploadAsync(container, prefix, files, ct);
        return Ok(ok);
    }

    /// <summary>GetAsync(path) - single file with data.</summary>
    [HttpGet("content")]
    public async Task<IActionResult> Get([FromQuery] string path, CancellationToken ct)
    {
        var file = await _files.GetAsync(path, ct);
        return file?.Data is null ? NotFound() : File(file.Data, file.ContentType ?? "application/octet-stream", file.Name);
    }

    /// <summary>GetFilesAsync(paths) - multiple files with data.</summary>
    [HttpPost("content/batch")]
    public async Task<ActionResult> GetMany([FromBody] List<string> paths, CancellationToken ct)
        => Ok((await _files.GetFilesAsync(paths, ct)).Select(f => new { f.Name, f.Path, Size = f.Data?.Length ?? 0 }));

    /// <summary>GetFilesAsync(container) / GetFilesAsync(container, prefix) - with data.</summary>
    [HttpGet("{container}/download-all")]
    public async Task<ActionResult> GetAll(string container, [FromQuery] string? prefix, CancellationToken ct)
    {
        var files = prefix is null
            ? await _files.GetFilesAsync(container, ct)
            : await _files.GetFilesAsync(container, prefix, ct);
        return Ok(files.Select(f => new { f.Name, f.Path, Size = f.Data?.Length ?? 0 }));
    }

    /// <summary>GetListAsync(container) / GetListAsync(container, prefix) - listing without data.</summary>
    [HttpGet("{container}")]
    public async Task<ActionResult> List(string container, [FromQuery] string? prefix, CancellationToken ct)
    {
        var files = prefix is null
            ? await _files.GetListAsync(container, ct)
            : await _files.GetListAsync(container, prefix, ct);
        return Ok(files.Select(f => new { f.Name, f.Path }));
    }

    /// <summary>SetMetaDataAsync - replaces object metadata.</summary>
    [HttpPut("metadata")]
    public async Task<ActionResult<bool>> SetMetadata([FromQuery] string path, [FromBody] Dictionary<string, string> metadata, CancellationToken ct)
        => Ok(await _files.SetMetaDataAsync(path, metadata, ct));

    /// <summary>GetPresignedDownloadUrlAsync - time-limited direct download link.</summary>
    [HttpGet("presigned/download")]
    public async Task<ActionResult<string>> PresignedDownload([FromQuery] string path, [FromQuery] int minutes = 15, CancellationToken ct = default)
        => Ok(await _files.GetPresignedDownloadUrlAsync(path, TimeSpan.FromMinutes(minutes), ct));

    /// <summary>GetPresignedUploadUrlAsync - time-limited direct upload link.</summary>
    [HttpGet("presigned/upload")]
    public async Task<ActionResult<string>> PresignedUpload([FromQuery] string path, [FromQuery] string? contentType, [FromQuery] int minutes = 15, CancellationToken ct = default)
        => Ok(await _files.GetPresignedUploadUrlAsync(path, TimeSpan.FromMinutes(minutes), contentType, ct));

    /// <summary>DeleteAsync - removes an object.</summary>
    [HttpDelete]
    public async Task<ActionResult<bool>> Delete([FromQuery] string path, CancellationToken ct)
        => Ok(await _files.DeleteAsync(path, ct));
}

/// <summary>Distributed cache - every ICache method.</summary>
[ApiController]
[Route("api/cache")]
public class CacheController : ControllerBase
{
    private readonly ICache _cache;

    public CacheController(ICache cache)
    {
        _cache = cache;
    }

    /// <summary>SetAsync(key, value) - no expiration.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Set(string key, [FromBody] TextRequest request)
    {
        await _cache.SetAsync(key, request.Value);
        return NoContent();
    }

    /// <summary>SetAsync(key, value, relative expiration).</summary>
    [HttpPut("{key}/expires-in/{seconds:int}")]
    public async Task<IActionResult> SetWithTtl(string key, int seconds, [FromBody] TextRequest request)
    {
        await _cache.SetAsync(key, request.Value, TimeSpan.FromSeconds(seconds));
        return NoContent();
    }

    /// <summary>SetAsync(key, value, absolute expiration).</summary>
    [HttpPut("{key}/expires-at")]
    public async Task<IActionResult> SetWithAbsoluteExpiry(string key, [FromQuery] DateTimeOffset at, [FromBody] TextRequest request)
    {
        await _cache.SetAsync(key, request.Value, at);
        return NoContent();
    }

    /// <summary>SetSlidingAsync - expiry extends on each read.</summary>
    [HttpPut("{key}/sliding/{seconds:int}")]
    public async Task<IActionResult> SetSliding(string key, int seconds, [FromBody] TextRequest request)
    {
        await _cache.SetSlidingAsync(key, request.Value, TimeSpan.FromSeconds(seconds));
        return NoContent();
    }

    /// <summary>GetAsync - null when missing/expired.</summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<string>> Get(string key)
    {
        var value = await _cache.GetAsync<string>(key);
        return value is null ? NotFound() : Ok(value);
    }

    /// <summary>RemoveAsync - deletes one key.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Remove(string key)
    {
        await _cache.RemoveAsync(key);
        return NoContent();
    }

    /// <summary>RemoveAllAsync - deletes keys matching a pattern.</summary>
    [HttpDelete]
    public async Task<IActionResult> RemoveAll([FromQuery] string pattern)
    {
        await _cache.RemoveAllAsync(pattern);
        return NoContent();
    }
}

/// <summary>Crypto utilities - every ICryptoProvider method.</summary>
[ApiController]
[Route("api/crypto")]
public class CryptoController : ControllerBase
{
    private readonly ICryptoProvider _crypto;

    public CryptoController(ICryptoProvider crypto)
    {
        _crypto = crypto;
    }

    /// <summary>GenerateKey + GenerateKeyString - new AES-256 key material.</summary>
    [HttpPost("keys")]
    public ActionResult GenerateKey()
        => Ok(new { KeyBytes = _crypto.GenerateKey()?.Length, KeyBase64 = _crypto.GenerateKeyString() });

    /// <summary>EncryptString - AES with a random IV per call. Returns a JSON object so
    /// the cipher round-trips cleanly into /decrypt (bare string bodies do not).</summary>
    [HttpPost("encrypt")]
    public ActionResult Encrypt([FromBody] TextRequest request)
        => Ok(new { CipherText = _crypto.EncryptString(request.Value) });

    /// <summary>DecryptString - reverses EncryptString.</summary>
    [HttpPost("decrypt")]
    public ActionResult Decrypt([FromBody] CipherRequest request)
        => Ok(new { PlainText = _crypto.DecryptString(request.CipherText) });

    /// <summary>Base64Encode / Base64Decode round-trip.</summary>
    [HttpPost("base64")]
    public ActionResult Base64([FromBody] TextRequest request)
    {
        var encoded = _crypto.Base64Encode(request.Value);
        return Ok(new { Encoded = encoded, Decoded = _crypto.Base64Decode(encoded) });
    }

    /// <summary>HashString - SHA-256.</summary>
    [HttpPost("hash")]
    public ActionResult Hash([FromBody] TextRequest request) => Ok(new { Hash = _crypto.HashString(request.Value) });

    /// <summary>HashPassword + ValidatePassword - PBKDF2 with salt.</summary>
    [HttpPost("password/hash")]
    public ActionResult HashPassword([FromBody] TextRequest request)
    {
        var hash = _crypto.HashPassword(request.Value, 100_000, out var salt);
        var valid = _crypto.ValidatePassword(request.Value, hash, salt, 100_000);
        return Ok(new { Hash = hash, Salt = salt, RoundTripValid = valid });
    }

    /// <summary>GenerateRandomCode - numeric OTP-style code.</summary>
    [HttpGet("random-code")]
    public ActionResult<string> RandomCode([FromQuery] int min = 100000, [FromQuery] int max = 999999)
        => Ok(_crypto.GenerateRandomCode(min, max));

    /// <summary>GenerateRandomPassword - optionally with special characters.</summary>
    [HttpGet("random-password")]
    public ActionResult<string> RandomPassword([FromQuery] short length = 16, [FromQuery] bool strong = true)
        => Ok(_crypto.GenerateRandomPassword(length, strong));
}
