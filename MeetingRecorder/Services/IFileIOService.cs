using System.Threading;
using System.Threading.Tasks;

namespace MeetingRecorder.Services;

public interface IFileIOService
{
    void EnsureDirectory(string directoryPath);
    Task AppendAllTextAsync(string filePath, string content, CancellationToken cancellationToken = default);
    void DeleteFile(string filePath);
}
