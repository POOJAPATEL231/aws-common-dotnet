using CloudIntegrator.Core.Interfaces;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;

namespace CloudIntegrator.Core.Services;

public class DataTransformService : IDataTransformService
{
    public async Task<Stream> TransformAsync(Stream input, string transformationType)
    {
        return transformationType.ToLower() switch
        {
            "json-to-xml" => await JsonToXmlAsync(input),
            "xml-to-json" => await XmlToJsonAsync(input),
            "csv-to-json" => await CsvToJsonAsync(input),
            "uppercase" => await ToUpperCaseAsync(input),
            "lowercase" => await ToLowerCaseAsync(input),
            _ => input
        };
    }

    public async Task<T> DeserializeAsync<T>(Stream input)
    {
        using var reader = new StreamReader(input);
        var content = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<T>(content)!;
    }

    public async Task<Stream> SerializeAsync<T>(T data)
    {
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return await Task.FromResult(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private async Task<Stream> JsonToXmlAsync(Stream input)
    {
        using var reader = new StreamReader(input);
        var json = await reader.ReadToEndAsync();
        
        // Simple JSON to XML conversion (you might want to use a proper library)
        var xml = $"<root>{json}</root>";
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    private async Task<Stream> XmlToJsonAsync(Stream input)
    {
        using var reader = new StreamReader(input);
        var xml = await reader.ReadToEndAsync();
        
        // Simple XML to JSON conversion (you might want to use a proper library)
        var json = JsonConvert.SerializeObject(new { xml = xml });
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private async Task<Stream> CsvToJsonAsync(Stream input)
    {
        using var reader = new StreamReader(input);
        var lines = new List<string>();
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lines.Add(line);
        }

        if (lines.Count == 0) return new MemoryStream();

        var headers = lines[0].Split(',');
        var data = lines.Skip(1).Select(line =>
        {
            var values = line.Split(',');
            var obj = new Dictionary<string, string>();
            for (int i = 0; i < headers.Length && i < values.Length; i++)
            {
                obj[headers[i]] = values[i];
            }
            return obj;
        }).ToArray();

        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private async Task<Stream> ToUpperCaseAsync(Stream input)
    {
        using var reader = new StreamReader(input);
        var content = await reader.ReadToEndAsync();
        var upperContent = content.ToUpper();
        return new MemoryStream(Encoding.UTF8.GetBytes(upperContent));
    }

    private async Task<Stream> ToLowerCaseAsync(Stream input)
    {
        using var reader = new StreamReader(input);
        var content = await reader.ReadToEndAsync();
        var lowerContent = content.ToLower();
        return new MemoryStream(Encoding.UTF8.GetBytes(lowerContent));
    }
}
