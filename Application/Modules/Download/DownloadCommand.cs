using System.Text;
using Dotto.Application.InternalServices.DownloaderService;
using Dotto.Application.InternalServices.UploadService;
using Dotto.Common;
using NetCord;
using NetCord.Rest;

namespace Dotto.Application.Modules.Download;

public class DownloadCommand(IDownloaderService dlService,
    IUploadService? uploadService = null)
{
    private const long UploadMinio = 100 << 20;
    private const long UploadLimitNoNitro = 10 << 20;

    public async Task<T> CreateMessage<T>(Uri uri, CancellationToken ct = default)
        where T: IMessageProperties, new()
    {
        var uploadLimit = uploadService != null
            ? UploadMinio
            : UploadLimitNoNitro;
        
        var message = new T();
        
        IList<DownloadedMedia> videos;
        try
        {
            videos = await dlService.Download(uri, new DownloadOptions()
            {
                MaxFilesize = uploadLimit,
            }, ct);
        }
        catch (IndexOutOfRangeException)
        {
            message.WithContent(
                $"Upload file limit exceeded ({StringUtils.HumanReadableSize(uploadLimit)}), it's over");
            return message;
        }
        catch (InvalidOperationException)
        {
            message.WithContent(
                $"Failed to pick format; perhaps there are no options under the upload limit? ({StringUtils.HumanReadableSize(uploadLimit)})");
            return message;
        }
        catch (ApplicationException ex)
        {
            var innerMessage = ex.InnerException!.Message;
            if (innerMessage.Length == 0)
            {
                innerMessage = "[none provided]\n" + ex.InnerException.StackTrace;
            }
            
            message.WithContent(Format.Escape(ex.Message))
                .WithEmbeds([
                    new()
                    {
                        Color = new(230, 70, 70),
                        Description = innerMessage
                    }
                ]);
            
            return message;
        }

        if (videos.Count == 0)
        {
            message.WithContent("no (eligible) videos found");
            return message;
        }
        
        var messageLines = new StringBuilder();
        
        foreach (var media in videos.OrderBy(v => v.Number))
        {
            var extension = (media.VideoFormat ?? media.AudioFormat)!.Extension ?? "mp4";
            var fileName = (media.Metadata.Title ?? Guid.NewGuid().ToString("N")) + $".{extension}";

            var videoName = fileName;

            if (media.Video.Length > UploadLimitNoNitro && uploadService != null)
            {
                // we can't fit the video in the discord upload limits; upload externally, if possible
                var response = await uploadService.UploadFile(media.Video, null, $"video/{extension}");
                videoName = Format.Link(fileName, response.ToString());
            }
            else
            {
                message.AddAttachments(new AttachmentProperties(fileName, media.Video));
            }

            var resolutionString = media.VideoFormat?.Resolution != null
                ? media.VideoFormat?.Resolution ?? "unknown resolution"
                : media.AudioFormat?.AudioBitrate != null
                    ? $"{Math.Ceiling(media.AudioFormat.AudioBitrate.Value / 1024)}kb/s"
                    : "unknown bitrate";

            var codecString = media.VideoFormat?.VideoCodec != null
                ? (media.VideoFormat.VideoCodec ?? "unknown codec")
                : (media.AudioFormat?.AudioCodec != null)
                    ? media.AudioFormat.AudioCodec
                    : "unknown codec";
            
            messageLines.AppendLine($"-# {videoName} | {resolutionString}" +
                             $" | {StringUtils.HumanReadableSize(media.Video.Length)}" +
                             $" | {StringUtils.VideoCodecToFriendlyName(codecString)}");
        }
        
        message.WithContent(messageLines.ToString());
        return message;
    }
}