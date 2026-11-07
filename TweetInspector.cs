using System.Text.RegularExpressions;
using OpenQA.Selenium;

namespace MediaDownloader;

internal static class TweetInspector
{
    private static readonly Regex ProfileUrl = new(@"^https?://(?:www\.)?(?:x\.com|twitter\.com)/([A-Za-z0-9_]{1,15})(?:[/?#]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool HasStatusId(IWebElement tweet, string targetId)
    {
        foreach (var link in tweet.FindElements(By.XPath(".//a[contains(@href, '/status/')]")))
        {
            var href = link.GetAttribute("href");
            if (href is not null && Regex.IsMatch(href, $@"/status/{Regex.Escape(targetId)}(?:\D|$)"))
                return true;
        }
        return false;
    }

    public static bool TryGetMetadata(IWebElement tweet, out string author, out string date, out string? tweetId)
    {
        author = string.Empty;
        date = string.Empty;
        tweetId = null;

        var profileLinks = tweet.FindElements(By.XPath(".//div[@data-testid='User-Name']//a[@href]"));
        if (profileLinks.Count == 0)
            profileLinks = tweet.FindElements(By.XPath(".//a[@href]"));

        foreach (var link in profileLinks)
        {
            var href = link.GetAttribute("href") ?? string.Empty;
            var match = ProfileUrl.Match(href);
            if (!match.Success)
                continue;

            var handle = match.Groups[1].Value;
            if (handle.Equals("home", StringComparison.OrdinalIgnoreCase) ||
                handle.Equals("search", StringComparison.OrdinalIgnoreCase) ||
                handle.Equals("explore", StringComparison.OrdinalIgnoreCase) ||
                handle.Equals("notifications", StringComparison.OrdinalIgnoreCase) ||
                handle.Equals("messages", StringComparison.OrdinalIgnoreCase))
                continue;

            author = handle;
            break;
        }

        var times = tweet.FindElements(By.TagName("time"));
        var datetime = times.Count > 0 ? times[0].GetAttribute("datetime") : null;
        if (!string.IsNullOrWhiteSpace(datetime) && datetime.Length >= 10)
            date = datetime[..10];

        foreach (var link in tweet.FindElements(By.XPath(".//a[contains(@href, '/status/')]")))
        {
            var match = Regex.Match(link.GetAttribute("href") ?? string.Empty, @"/status/(\d+)");
            if (match.Success)
            {
                tweetId = match.Groups[1].Value;
                break;
            }
        }

        if (author.Length == 0 || date.Length == 0)
        {
            ConsoleUi.Warning("Skipped a tweet with missing author or date information.");
            return false;
        }
        return true;
    }

    public static IEnumerable<string> GetImageUrls(IWebElement tweet)
    {
        foreach (var img in tweet.FindElements(By.XPath(".//img[contains(@src, 'twimg.com/media/')]")))
        {
            var src = img.GetAttribute("src");
            if (src?.StartsWith("https://pbs.twimg.com/media/", StringComparison.OrdinalIgnoreCase) == true)
                yield return src;
        }
    }

    public static IEnumerable<string> GetVideoUrls(IWebElement tweet)
    {
        foreach (var video in tweet.FindElements(By.TagName("video")))
        {
            var source = video.GetAttribute("src");
            if (IsDirectVideoUrl(source))
            {
                yield return source!;
                continue;
            }

            foreach (var element in video.FindElements(By.TagName("source")))
            {
                source = element.GetAttribute("src");
                if (IsDirectVideoUrl(source))
                {
                    yield return source!;
                    break;
                }
            }
        }
    }

    private static bool IsDirectVideoUrl(string? url) =>
        url?.StartsWith("https://video.twimg.com/", StringComparison.OrdinalIgnoreCase) == true;
}
