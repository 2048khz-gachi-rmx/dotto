using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Dotto.Common;

public static partial class StringUtils
{
    public static bool IsNullOrWhitespace([NotNullWhen(false)] this string? str)
        => string.IsNullOrWhiteSpace(str);
    
    private static readonly string[] FilesizeSuffixes = [ "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" ];
    
    public static string HumanReadableSize(long size, int decimals = 2)
    {
        if (size == 0)
            return "0" + FilesizeSuffixes[0];

        //  0 => 0  |  1 => 1024 (KiB)  |  2 => 1024*1024 (MiB)  |  ...
        int factorNum = (int)Math.Floor(Math.Log(size, 1024));
        
        double num = Math.Round(size / Math.Pow(1024, factorNum), decimals);
        return $"{num} {FilesizeSuffixes[factorNum]}";
    }

    /// <summary>
    /// .NET HTTP throws a
    /// <a href="https://github.com/dotnet/runtime/blob/d53785bfd37b316f19f5d04f12522a27b966aac3/src/libraries/System.Net.Http/src/System/Net/Http/Headers/ContentDispositionHeaderValue.cs#L404-L408">
    /// hissy fit
    /// </a> when quotes are encountered in the header value.
    /// </summary>
    public static string SanitizeHttpHeaderValue(this string value)
        => value.Replace("\"", "");
    
    public static string VideoCodecToFriendlyName(string vcodec)
    {
        if (Regex.IsMatch(vcodec, @"^(avc|h264)")) return "H264 (AVC)";
        if (Regex.IsMatch(vcodec, @"^(hevc|h265)")) return "H265 (HEVC)";
        if (Regex.IsMatch(vcodec, @"^vp0?9")) return "VP9";

	    return vcodec;
    }

    [GeneratedRegex(@"http(s)?://([\w-]+\.)+[\w-]+(/[\w- ./?%&=]*)?")]
    private static partial Regex UrlRegex();
    
    public static List<string> MatchUrls(string text)
    {
        return UrlRegex().Matches(text)
            .Select(match => match.Value)
            .ToList();
    }
}