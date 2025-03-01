using Dotto.Commands.Common;
using NetCord.Rest;
using Services.DownloaderService;

namespace Dotto.Commands.Download;

public partial class DownloadCommand(IDownloaderService dlService)
{
    private const long UploadLimitNoNitro = 10 << 20;
    private const long UploadLimitYesNitro = 25 << 20; 

    public async Task<T> HydrateMessage<T>(Uri uri, T messageProperties)
        where T: IMessageProperties
    {
        var uploadLimit = UploadLimitNoNitro; // TODO: check server nitro level
        
        DownloadedMedia video;
        try
        {
            video = await dlService.Download(uri, false, uploadLimit);
        }
        catch (IndexOutOfRangeException)
        {
            messageProperties.WithContent("upload file limit exceeded, it's over");
            return messageProperties;
        }

        var fileName = Path.ChangeExtension(video.Metadata.Title ?? Guid.NewGuid().ToString(), "mp4");

        messageProperties.AddAttachments(new AttachmentProperties(fileName, video.Video))
            .WithContent($"-# {video.Metadata.Resolution ?? "unknown resolution"}" +
                         $" | {StringUtils.HumanReadableSize(video.Video.Length)}" +
                         $" | {StringUtils.VideoCodecToFriendlyName(video.Metadata.VideoCodec ?? "unknown codec")}");
            
        return messageProperties;
    }
}