using Dotto.Commands.Common;
using NetCord.Rest;
using Services.DownloaderService;

namespace Dotto.Commands.Download;

public class DownloadCommand(IDownloaderService dlService)
{
    private const long UploadLimitNoNitro = 10 << 20;
    private const long UploadLimitYesNitro = 25 << 20; 

    public async Task<T> CreateMessage<T>(Uri uri)
        where T: IMessageProperties, new()
    {
        var uploadLimit = UploadLimitNoNitro; // TODO: check server nitro level
        var message = new T();
        
        DownloadedMedia video;
        try
        {
            video = await dlService.Download(uri, false, uploadLimit);
        }
        catch (IndexOutOfRangeException)
        {
            message.WithContent("upload file limit exceeded, it's over");
            return message;
        }
        catch (ApplicationException ex)
        {
            message.WithContent(ex.Message)
                .WithEmbeds([
                    new()
                    {
                        Color = new(230, 70, 70),
                        Description = ex.InnerException!.Message
                    }
                ]);
            
            return message;
        }

        var fileName = Path.ChangeExtension(video.Metadata.Title ?? Guid.NewGuid().ToString(), "mp4");

        message.AddAttachments(new AttachmentProperties(fileName, video.Video))
            .WithContent($"-# {video.Metadata.Resolution ?? "unknown resolution"}" +
                         $" | {StringUtils.HumanReadableSize(video.Video.Length)}" +
                         $" | {StringUtils.VideoCodecToFriendlyName(video.Metadata.VideoCodec ?? "unknown codec")}");
            
        return message;
    }
}