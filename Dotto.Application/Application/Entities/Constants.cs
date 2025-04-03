using System.Collections.Immutable;

namespace Dotto.Application.Entities;

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
}