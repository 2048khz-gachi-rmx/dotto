using System.Text.RegularExpressions;

namespace Dotto.Commands.Common;

public static class StringUtils
{
    private static readonly string[] _fsSuffixes = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
    
    public static string HumanReadableSize(long size, int decimals = 2)
    {
        if (size == 0)
            return "0" + _fsSuffixes[0];

        //  0 => 0  |  1 => 1024 (KiB)  |  2 => 1024*1024 (MiB)  |  ...
        int factorNum = (int)Math.Floor(Math.Log(size, 1024));
        
        double num = Math.Round(size / Math.Pow(1024, factorNum), decimals);
        return $"{num} {_fsSuffixes[factorNum]}";
    }
    
    public static string VideoCodecToFriendlyName(string vcodec)
    {
        if (Regex.IsMatch(vcodec, @"^(avc|h264)")) return "H264 (AVC)";
        if (Regex.IsMatch(vcodec, @"^(hevc|h265)")) return "H265 (HEVC)";
        if (Regex.IsMatch(vcodec, @"^vp0?9")) return "VP9";

	    return vcodec;
    }
}