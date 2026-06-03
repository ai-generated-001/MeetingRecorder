using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Util.Store;

namespace MeetingRecorder.Services;

public sealed class DpapiFileDataStore : IDataStore
{
    private readonly string _folderPath;

    public DpapiFileDataStore(string folderPath)
    {
        _folderPath = folderPath;
        if (!Directory.Exists(_folderPath))
        {
            Directory.CreateDirectory(_folderPath);
        }
    }

    private string GetFilePath(string key)
    {
        return Path.Combine(_folderPath, $"dpapi_{key}.dat");
    }

    public Task StoreAsync<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key must have a value", nameof(key));
        }

        var filePath = GetFilePath(key);
        var jsonString = JsonSerializer.Serialize(value);
        var plaintextBytes = Encoding.UTF8.GetBytes(jsonString);
        var ciphertextBytes = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(filePath, ciphertextBytes);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key must have a value", nameof(key));
        }

        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key must have a value", nameof(key));
        }

        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            return Task.FromResult(default(T));
        }

        try
        {
            var ciphertextBytes = File.ReadAllBytes(filePath);
            var plaintextBytes = ProtectedData.Unprotect(ciphertextBytes, null, DataProtectionScope.CurrentUser);
            var jsonString = Encoding.UTF8.GetString(plaintextBytes);
            var value = JsonSerializer.Deserialize<T>(jsonString);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error decrypting or deserializing token for key '{key}': {ex.Message}");
            return Task.FromResult(default(T));
        }
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_folderPath))
        {
            foreach (var file in Directory.GetFiles(_folderPath, "dpapi_*.dat"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore delete errors
                }
            }
        }
        return Task.CompletedTask;
    }
}
