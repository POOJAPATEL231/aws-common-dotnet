using Microsoft.AspNetCore.Mvc;
using Utils.Common.Utils;

namespace AwsShowcase.API.Controllers;

/// <summary>
/// Utility helpers - transcript endpoints that call every JsonUtil, StringUtil,
/// NumberUtil, ByteArrayUtil and FileInformation method with sample inputs so the
/// behavior of each is visible in one response.
/// </summary>
[ApiController]
[Route("api/utils")]
public class UtilsController : ControllerBase
{
    public record SamplePayload(string Name, int Value);

    /// <summary>Every JsonUtil method.</summary>
    [HttpGet("json")]
    public ActionResult JsonDemo()
    {
        var sample = new SamplePayload("widget", 42);
        var json = sample.Serialize();

        using var stream = JsonUtil.SerializeJsonIntoStream(sample);
        var jsonStream = json.GetJsonStream("/PartitionKey", "demo", id: "1", eTag: "etag-1");

        var tempPath = Path.Combine(Path.GetTempPath(), "utils-demo.json");
        System.IO.File.WriteAllText(tempPath, json);

        return Ok(new
        {
            Serialize = json,
            TryParseTo = json.TryParseTo<SamplePayload>(),
            ToDictionary = sample.ToDictionary(),
            SerializeJsonIntoStreamBytes = stream.Length,
            GetJsonStreamBytes = jsonStream?.Length,
            ParseJsonFile = JsonUtil.ParseJsonFile<SamplePayload>(tempPath)
        });
    }

    /// <summary>Every StringUtil method.</summary>
    [HttpGet("strings")]
    public ActionResult StringDemo([FromQuery] string sample = "alpha,beta,gamma")
        => Ok(new
        {
            SplitCsv = sample.SplitCsv(),
            SplitToList = sample.SplitToList(','),
            JoinChar = StringUtil.Join('-', "a", "b", "c"),
            JoinString = StringUtil.Join(" | ", "x", "y"),
            Truncate = "a-very-long-string".Truncate(6),
            IsNullOrWhitespace = "  ".IsNullOrWhitespace(),
            Segment = "part1:part2:part3".Segment(':', 1),
            ToGuid = "6f1c8a7e-2b3d-4c5e-9f0a-1b2c3d4e5f6a".ToGuid(),
            ToNullableGuid = "not-a-guid".ToNullableGuid(),
            ToInt = "123".ToInt(),
            ToNullableInt = "abc".ToNullableInt(),
            ToShort = "7".ToShort(),
            ToNullableShort = "9".ToNullableShort(),
            ToAlphaNumeric = "he!!o-w@rld_42".ToAlphaNumeric(),
            ToByteArrayLength = "hello".ToByteArray().Length,
            ConvertTo = "3.14".ConvertTo<double>(),
            FormatPhoneNumber = StringUtil.FormatPhoneNumber("9876543210"),
            IsRegexMatch = "abc123".IsRegexMatch("^[a-z]+\\d+$"),
            IsUrl = "https://example.com".IsUrl(),
            DecodeBase64String = "aGVsbG8=".DecodeBase64String(),
            ProperCase = "hello world".ProperCase()
        });

    /// <summary>Every NumberUtil method.</summary>
    [HttpGet("numbers")]
    public ActionResult NumberDemo()
        => Ok(new
        {
            GenerateRandomNumber = NumberUtil.GenerateRandomNumber(1, 100),
            ValidateTerm = NumberUtil.ValidateTerm(24, 12, 60),
            ValidateDecimal = NumberUtil.ValidateDecimal(150.5m, 0, 100),
            ValueOrNull = 42m.ValueOrNull(0, 40),
            ValueOrZero = 42m.ValueOrZero(0, 40)
        });

    /// <summary>ByteArrayUtil + FileInformation.</summary>
    [HttpGet("bytes-and-files")]
    public ActionResult BytesDemo()
    {
        var payload = new SamplePayload("roundtrip", 7);
        var bytes = payload.ToByteArray();

        return Ok(new
        {
            ToByteArrayLength = bytes.Length,
            FromByteArray = bytes.FromByteArray<SamplePayload>(),
            GetFileExtension_Json = FileInformation.GetFileExtension("application/json"),
            GetFileExtension_WithCharset = FileInformation.GetFileExtension("text/plain; charset=utf-8"),
            GetFileExtension_Unknown = FileInformation.GetFileExtension("application/x-mystery")
        });
    }
}
