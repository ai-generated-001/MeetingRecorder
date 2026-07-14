using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public sealed class FileIOService : IFileIOService
{
    public void EnsureDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    public Task AppendAllTextAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        return File.AppendAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }

    public void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public long GetFileSize(string filePath)
    {
        if (File.Exists(filePath))
        {
            return new FileInfo(filePath).Length;
        }
        return 0;
    }
}
