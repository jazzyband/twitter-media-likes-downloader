using System.Net;

namespace MediaDownloader;

internal sealed class MediaDownloadService : IDisposable
{
    private const int Retries = 3;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _client = new();

    public MediaDownloadService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36");
    }

    public async Task<bool> DownloadAsync(string url, string filename, string directory)
    {
        var filePath = Path.Combine(directory, filename);
        if (File.Exists(filePath))
            return false;

        for (var attempt = 1; attempt <= Retries; attempt++)
        {
            var createdFile = false;
            try
            {
                using var timeout = new CancellationTokenSource(RequestTimeout);
                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (response.StatusCode == HttpStatusCode.NotFound &&
                    url.Contains("format=jpg", StringComparison.OrdinalIgnoreCase))
                {
                    var pngUrl = url.Replace("format=jpg", "format=png", StringComparison.OrdinalIgnoreCase);
                    var pngFilename = Path.ChangeExtension(filename, ".png");
                    ConsoleUi.Warning($"JPG not found. Retrying as PNG: {pngUrl}");
                    return await DownloadAsync(pngUrl, pngFilename, directory);
                }

                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var destination = new FileStream(
                    filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
                createdFile = true;

                var buffer = new byte[81920];
                while (true)
                {
                    timeout.CancelAfter(RequestTimeout);
                    var read = await source.ReadAsync(buffer, timeout.Token);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    timeout.CancelAfter(RequestTimeout);
                }

                await destination.FlushAsync(timeout.Token);
                return true;
            }
            catch (IOException) when (!createdFile && File.Exists(filePath))
            {
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                if (createdFile)
                {
                    try { File.Delete(filePath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                ConsoleUi.Error($"Failed to download {url}: {ex.Message}");
                if (attempt < Retries)
                {
                    ConsoleUi.Warning($"Retrying in 2 seconds... (Attempt {attempt + 1}/{Retries})");
                    await Task.Delay(2000);
                }
                else
                {
                    ConsoleUi.Error("Giving up.");
                }
            }
        }
        return false;
    }

    public void Dispose() => _client.Dispose();
}
