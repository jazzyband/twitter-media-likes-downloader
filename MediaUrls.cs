using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaDownloader;

internal static class MediaUrls
{
    private static readonly Regex ImageSizeSuffix = new(@"_(?:thumb|small|medium|large|orig|4096x4096)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string GetFullQualityImageUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("pbs.twimg.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
            return sourceUrl;

        var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        var queryFormat = GetQueryValue(uri.Query, "format")?.ToLowerInvariant();
        var format = IsImageExtension(queryFormat) ? queryFormat! : IsImageExtension(extension) ? extension : "jpg";

        var path = uri.AbsolutePath;
        var pathExtension = Path.GetExtension(path);
        if (pathExtension.Length > 0)
        {
            var withoutExtension = path[..^pathExtension.Length];
            path = ImageSizeSuffix.Replace(withoutExtension, string.Empty) + pathExtension;
        }
        else
        {
            path = ImageSizeSuffix.Replace(path, string.Empty);
        }

        return new UriBuilder(uri)
        {
            Path = path,
            Query = $"format={format}&name=orig"
        }.Uri.AbsoluteUri;
    }

    public static string CreateFilename(string mediaUrl, string author, string date, string defaultExtension, string? tweetId)
    {
        var uri = new Uri(mediaUrl);
        var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        var format = GetQueryValue(uri.Query, "format")?.ToLowerInvariant();
        if (IsImageExtension(format))
            extension = format!;
        if (extension is not ("jpg" or "jpeg" or "png" or "gif" or "mp4"))
            extension = defaultExtension;

        var mediaMatch = Regex.Match(uri.AbsolutePath, @"/media/([^/.?]+)");
        string mediaId;
        if (mediaMatch.Success)
        {
            mediaId = mediaMatch.Groups[1].Value;
        }
        else
        {
            var videoMatch = Regex.Match(uri.AbsolutePath, @"/(?:ext_tw_video|amplify_video|tweet_video)/(\w+)");
            var prefix = videoMatch.Success ? videoMatch.Groups[1].Value : tweetId ?? "media";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mediaUrl));
            mediaId = $"{prefix}_{Convert.ToHexString(hash.AsSpan(0, 5)).ToLowerInvariant()}";
        }

        return $"{Sanitize(date)}_@{Sanitize(author)}_{Sanitize(mediaId)}.{extension}";
    }

    private static bool IsImageExtension(string? extension) => extension is "jpg" or "jpeg" or "png" or "gif";

    private static string? GetQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0].Equals(key, StringComparison.OrdinalIgnoreCase) && parts.Length == 2)
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static string Sanitize(string text) => Regex.Replace(text, @"[^a-zA-Z0-9_-]", "_");
}
