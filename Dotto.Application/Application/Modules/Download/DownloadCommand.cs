using System.Text;
using System.Text.RegularExpressions;
using Dotto.Application.Entities;
using Dotto.Application.InternalServices;
using Dotto.Application.InternalServices.DownloaderService;
using Dotto.Application.InternalServices.UploadService;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Application.Modules.Download;

public class DownloadCommand(IDottoDbContext dbContext,
    IDownloaderService dlService,
    IDateTimeProvider dateTimeProvider,
    IUploadService? uploadService = null)
{
    private const long UploadMinio = 100 << 20;
    private const long UploadLimitNoNitro = 10 << 20;

    public async Task<DownloadedMediaMessage<T>> CreateMessage<T>(Uri uri, long discordUploadLimit = UploadLimitNoNitro, CancellationToken ct = default)
        where T: IMessageProperties, new()
    {
        var uploadLimit = uploadService != null
            ? UploadMinio
            : UploadLimitNoNitro;

        var response = new DownloadedMediaMessage<T>()
        {
            Message = new T(),
            SourceUrl = uri,
        };

        IList<DownloadedMedia> videos;
        try
        {
            if (Regex.IsMatch(uri.Host, "\\.?instagram\\.com$"))
            {
                // instagram is a bitchcunt, has draconian ratelimits and insta-bans if you try to pass your acc's cookies to yt-dlp
                // but someone, bless their soul, runs a service which does all the hard work for us
                var bld = new UriBuilder(uri);
                bld.Host = "ddinstagram.com";
                var newLink = bld.Uri.ToString();

                response.Message.WithContent("-# " + Format.Link("instagram temporarily disabled, have a re-link instead", newLink));
                return response;
            }
            
            videos = await dlService.Download(uri, new DownloadOptions
            {
                MaxFilesize = uploadLimit,
            }, ct);
        }
        catch (IndexOutOfRangeException)
        {
            response.Message.WithContent(
                $"Upload file limit exceeded ({StringUtils.HumanReadableSize(uploadLimit)}), it's over");
            return response;
        }
        catch (InvalidOperationException)
        {
            response.Message.WithContent(
                $"Failed to pick format; perhaps there are no options under the upload limit? ({StringUtils.HumanReadableSize(uploadLimit)})");
            return response;
        }
        catch (ApplicationException ex)
        {
            var innerMessage = ex.InnerException!.Message;
            if (innerMessage.Length == 0)
            {
                innerMessage = "[none provided]\n" + ex.InnerException.StackTrace;
            }
            
            response.Message.WithContent(Format.Escape(ex.Message))
                .WithEmbeds([
                    new()
                    {
                        Color = new(230, 70, 70),
                        Description = innerMessage
                    }
                ]);
            
            return response;
        }

        if (videos.Count == 0)
        {
            response.Message.WithContent("no (eligible) videos found");
            return response;
        }
        
        var messageLines = new StringBuilder();
        
        foreach (var media in videos.OrderBy(v => v.Number))
        {
            var extension = media.GetExtension();
            var videoName = media.GetFileName();

            if (media.Video.Length > discordUploadLimit && uploadService != null)
            {
                // we can't fit the video in the discord upload limits; upload externally, if possible
                var uploadedUrl = await uploadService.UploadFile(media.Video, null, $"video/{extension}", ct);
                
                response.ExternalVideos.Add(uploadedUrl);
                videoName = Format.Link(videoName, uploadedUrl.ToString());
            }
            else
            {
                response.Message.AddAttachments(new AttachmentProperties(videoName, media.Video));
            }

            messageLines.AppendLine($"-# {videoName}" +
                                    $" | {media.GetResolution()}" +
                                    $" | {StringUtils.HumanReadableSize(media.Video.Length)}" +
                                    $" | {StringUtils.VideoCodecToFriendlyName(media.GetCodec())}");
        }
        
        response.Message.WithContent(messageLines.ToString());
        
        return response;
    }

    public static long GetMaxDiscordFileSize(Guild? guild, User? user = null)
    {
        var guildLimit = guild?.PremiumTier switch
        {
            3 => 100 << 20,
            2 => 50 << 20,
            _ => UploadLimitNoNitro
        };

        var userLimit = user?.PremiumType switch
        {
            PremiumType.NitroClassic or PremiumType.NitroBasic => 50 << 20,
            PremiumType.Nitro => 500 << 20,
            _ => UploadLimitNoNitro,
        };

        return Math.Max(userLimit, guildLimit);
    }

    public async Task LogDownloadedMedia<T>(RestMessage newMessage, DownloadedMediaMessage<T> mediaMessage, User invoker, Uri downloadedFrom)
        where T : IMessageProperties
    {
        var attachmentMedia = newMessage.Attachments
            .Select(e => new DownloadedMediaRecord()
            {
                Id = default,
                
                ChannelId = newMessage.ChannelId,
                MessageId = newMessage.Id,
                DownloadedFrom = downloadedFrom.ToString(),
                InvokerId = invoker.Id,
                MediaUrl = e.Url,
                CreatedOn = dateTimeProvider.UtcNow,
            });

        var externalMedia = mediaMessage.ExternalVideos
            .Select(e => new DownloadedMediaRecord()
            {
                Id = default,
                
                ChannelId = newMessage.ChannelId,
                MessageId = newMessage.Id,
                DownloadedFrom = downloadedFrom.ToString(),
                InvokerId = invoker.Id,
                MediaUrl = e.ToString(),
                CreatedOn = dateTimeProvider.UtcNow,
            });

        var both = attachmentMedia
            .UnionBy(externalMedia, r => r.MediaUrl)
            .ToList();
        
        if (both.IsEmpty()) return;
        
        dbContext.DownloadedMedia.AddRange(both);
        await dbContext.SaveChangesAsync();
    }
}