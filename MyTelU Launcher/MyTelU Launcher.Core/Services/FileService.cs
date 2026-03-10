using System.Text;
using System.Text.Json;

using MyTelU_Launcher.Core.Contracts.Services;
using MyTelU_Launcher.Core.Helpers;

namespace MyTelU_Launcher.Core.Services;

public class FileService : IFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public T Read<T>(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (File.Exists(path))
        {
            // Retry logic for file locking issues
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var streamReader = new StreamReader(fileStream);
                    var json = streamReader.ReadToEnd();
                    return JsonSerializer.Deserialize<T>(json, JsonOptions);
                }
                catch (IOException)
                {
                    retries--;
                    if (retries == 0) throw;
                    Thread.Sleep(100);
                }
            }
        }

        return default;
    }

    public void Save<T>(string folderPath, string fileName, T content)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var fileContent = JsonSerializer.Serialize(content, JsonOptions);
        var path = Path.Combine(folderPath, fileName);
        
        // Retry logic for file locking issues
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                File.WriteAllText(path, fileContent, Encoding.UTF8);
                break;
            }
            catch (IOException)
            {
                retries--;
                if (retries == 0) throw;
                Thread.Sleep(100);
            }
        }
    }

    public void Delete(string folderPath, string fileName)
    {
        if (fileName != null && File.Exists(Path.Combine(folderPath, fileName)))
        {
            File.Delete(Path.Combine(folderPath, fileName));
        }
    }
}
