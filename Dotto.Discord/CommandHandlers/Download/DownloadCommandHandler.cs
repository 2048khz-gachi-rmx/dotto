using System.Text;
using Dotto.Application.Abstractions.Factories;
using Dotto.Application.Entities;
using Dotto.Application.InternalServices;
using Dotto.Application.InternalServices.UploadService;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Common.Exceptions;
using Dotto.Discord.Models.Download;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Download;

internal class DownloadCommandHandler(IDottoDbContext dbContext,
    IDownloaderServiceFactory downloaderFactory,
    IDateTimeProvider dateTimeProvider,
    IUploadService? uploadService = null)
{
    private const long UploadMinio = 100 << 20;
    private const long UploadLimitNoNitro = 10 << 20;

    public async Task<DownloadedMediaMessage<T>> CreateMessage<T>(Uri uri, bool audioOnly, long discordUploadLimit = UploadLimitNoNitro, CancellationToken ct = default)
        where T: IMessageProperties, new()
    {
        var uploadLimit = uploadService != null
            ? UploadMinio
            : discordUploadLimit;

        var response = new DownloadedMediaMessage<T>()
        {
            Message = new T(),
            SourceUrl = uri,
        };

        var downloaders = downloaderFactory.CreateDownloaderService(uri);

        IList<DownloadedMedia> videos = new List<DownloadedMedia>();

        foreach (var downloader in downloaders)
        {
            try
            {
                videos = await downloader.Download(uri, new DownloadOptions
                {
                    MaxFilesize = uploadLimit,
                    AudioOnly = audioOnly
                }, ct);

                if (videos.Count > 0)
                    break;
            }
            catch (ServiceUnavailableException ex)
            {
                response.Message.AddEmbeds([
                    new EmbedProperties
                    {
                        Color = new Color(235, 175, 40),
                        Description = $"Downloader service \"{ex.ServiceName}\" was unavailable..."
                    }
                ]);
            }
            catch (ApplicationException ex)
            {
                var innerMessage = ex.InnerException?.Message;
                if (innerMessage.IsNullOrWhitespace())
                {
                    innerMessage = "[none provided]";

                    if (ex.InnerException?.StackTrace != null)
                        innerMessage += "\n" + ex.InnerException?.StackTrace;
                }
                
                response.Message.AddEmbeds([
                    new()
                    {
                        Color = new(140, 55, 55),
                        Title = Format.Escape(ex.Message),
                        Description = innerMessage
                    }
                ]);
            }
        }

        if (videos.IsEmpty())
        {
            response.Message.WithContent("No (eligible) videos found or all downloaders failed");
            return response;
        }
        
        var messageLines = new StringBuilder();
        
        foreach (var media in videos.OrderBy(v => v.Number))
        {
            var extension = media.GetExtension();
            var videoName = media.GetFileName();

            if (media.FileSize > discordUploadLimit && uploadService != null)
            {
                // we can't fit the video in the discord upload limits; upload externally, if possible
                var uploadedUrl = await uploadService.UploadFile(media.Video, media.FileSize ?? discordUploadLimit, null, $"video/{extension}", ct);
                
                response.ExternalVideos.Add(uploadedUrl);
                videoName = Format.Link(videoName, uploadedUrl.ToString());

                // hack: if the message already contains embeds, discord seems to not embed external links' media
                // so if we have any embeds in the message, we turn them into regular message lines.
                var embeds = response.Message.Embeds ?? Array.Empty<EmbedProperties>();
                
                foreach (var embedProperties in embeds)
                {
                    var title = embedProperties.Title;
                    var content = embedProperties.Description;

                    if (content.IsNullOrWhitespace())
                        content = title;
                    else if (!title.IsNullOrWhitespace())
                        content = $"{title}: {content}";

                    messageLines.AppendLine($"-# {content}");
                }

                response.Message.WithEmbeds(Array.Empty<EmbedProperties>());
            }
            else
            {
                var attachment = new AttachmentProperties(videoName.SanitizeHttpHeaderValue(), media.Video);
                response.AttachedVideos.Add(attachment);
            }

            messageLines.AppendLine($"-# {videoName}" +
                                    $" | {media.GetResolution()}" +
                                    $" | {StringUtils.HumanReadableSize(media.Video.Length)}" +
                                    $" | {Format.Escape(StringUtils.VideoCodecToFriendlyName(media.GetCodec()))}");
        }

        response.Message.WithAllowedMentions(new()
            {
                Everyone = false,
                ReplyMention = true,
                AllowedUsers = []
            });
        
        response.Message.AddAttachments(response.AttachedVideos);
        response.Message.WithContent(messageLines.ToString().TrimEnd());
        
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

    public async Task LogDownloadedMedia<T>(RestMessage newMessage, DownloadedMediaMessage<T> downloadedMedia, ulong invokerUserId, Uri downloadedFrom)
        where T : IMessageProperties
    {
        var attachmentMedia = newMessage.Attachments
            .Select(e => new DownloadedMediaRecord()
            {
                Id = default,
                
                ChannelId = newMessage.ChannelId,
                MessageId = newMessage.Id,
                DownloadedFrom = downloadedFrom.ToString(),
                InvokerId = invokerUserId,
                MediaUrl = e.Url,
                CreatedOn = dateTimeProvider.UtcNow,
            });

        var externalMedia = downloadedMedia.ExternalVideos
            .Select(e => new DownloadedMediaRecord()
            {
                Id = default,
                
                ChannelId = newMessage.ChannelId,
                MessageId = newMessage.Id,
                DownloadedFrom = downloadedFrom.ToString(),
                InvokerId = invokerUserId,
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