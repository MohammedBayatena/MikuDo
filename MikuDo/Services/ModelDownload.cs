using System.IO;
using System.Net.Http;

namespace MikuDo.Services;

/// <summary>Fetches a model file, reporting progress, without leaving half a file under its real name.</summary>
public static class ModelDownload
{
    public static async Task ToFileAsync(AiModelOption option, string destination,
                                         IProgress<double> progress, CancellationToken ct)
    {
        var temp = destination + ".part";

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using (var response = await http.GetAsync(option.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? option.SizeBytes;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(temp);

            var buffer = new byte[128 * 1024];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                progress.Report(Math.Min(1.0, (double)read / total));
            }
        }

        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temp, destination);
        progress.Report(1.0);
    }
}
