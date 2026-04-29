using System.Collections.Immutable;
using System.Text.RegularExpressions;
using NetCord;

namespace Dotto.Common.Constants;

public static class Constants
{
    public static class ChannelFlags
    {
        public const int MaxLength = 255;
        public const int MaxFlagsInChannel = 16;

        public static class FunctionalFlags
        {
            public const string VideoRecompress = "video_recompress";
            public const string LinkAutodownload = "link_autodownload";
        }
        
        public static readonly ImmutableList<string> FunctionalFlagsList =
        [
            FunctionalFlags.VideoRecompress,
            FunctionalFlags.LinkAutodownload
        ];
    }

    public static class Compression
    {
        public const string TempDirName = "dotto_ffmpeg";

        public static class Regexes
        {
            public static readonly Regex DiscordCdn = new(
                @"https?:\/\/(?:media|cdn)\.discord(?:app)?\.(?:net|com)\/attachments\/(\d{17,20}\/\d{17,20})\/([\w.-]+\.\w{3,4})(?:\?[\w=&-]+)?(?=[^\w=&-]|$)[\n\s]?",
                RegexOptions.Compiled | RegexOptions.Multiline);
            
            public static readonly Regex VideoExts = new(@"\.(mov|mp4|webm)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    public static class Colors
    {
        public static Color WarningColor = new(235, 175, 40);
        public static Color ErrorColor = new(230, 50, 50);
    }
}