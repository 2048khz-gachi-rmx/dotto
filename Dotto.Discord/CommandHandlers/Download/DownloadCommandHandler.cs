using System.Text;
using Dotto.Application.Abstractions;
using Dotto.Application.Abstractions.MediaProcessing;
using Dotto.Application.Abstractions.Upload;
using Dotto.Application.Entities;
using Dotto.Common;
using Dotto.Common.Constants;
using Dotto.Common.DateTimeProvider;
using Dotto.Discord.Models.Download;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Discord.CommandHandlers.Download;

internal class DownloadCommandHandler(IDottoDbContext dbContext,
    IMediaProcessingService mediaProcessingService,
    IDateTimeProvider dateTimeProvider,
    IUploadService? uploadService = null)
{
    private const long UploadMinio = 100 << 20;
    private const long UploadLimitNoNitro = 10 << 20;

    public async Task<DownloadMediaResult<T>> CreateMessage<T>(Uri uri, bool audioOnly, long discordUploadLimit = UploadLimitNoNitro, CancellationToken ct = default)
        where T: IMessageProperties, new()
    {
        var uploadLimit = uploadService != null
            ? UploadMinio
            : discordUploadLimit;

        var response = new DownloadMediaResult<T>
        {
            Message = new T(),
            SourceUrl = uri,
        };

        var result = await mediaProcessingService.ProcessMediaFromUrlAsync(uri, new DownloadOptions
        {
            MaxFilesize = uploadLimit,
            AudioOnly = audioOnly
        }, ct);

        var isNonFatal = result.IsSuccess;
        
        foreach (var downloadError in result.Errors)
        {
            response.Message.AddEmbeds([
                new EmbedProperties
                {
                    Color = isNonFatal ? Constants.Colors.WarningColor : Constants.Colors.ErrorColor,
                    Title = downloadError.Message,
                    Description = downloadError.Details
                }
            ]);
        }

        if (!result.IsSuccess)
        {
            if (response.Message.Embeds.IsNullOrEmpty())
                response.Message.WithContent("No (eligible) videos found or all downloaders failed");
            
            return response;
        }
        
        var messageLines = new StringBuilder();
        
        foreach (var media in result.Media.OrderBy(v => v.Number))
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

    public async Task LogDownloadedMedia<T>(RestMessage newMessage, DownloadMediaResult<T> downloadMedia, ulong invokerUserId)
        where T : IMessageProperties
    {
        var attachmentMedia = newMessage.Attachments
            .Select(e => new DownloadedMediaRecord
            {
                Id = default,
                
                ChannelId = newMessage.ChannelId,
                MessageId = newMessage.Id,
                DownloadedFrom = downloadMedia.SourceUrl.ToString(),
                InvokerId = invokerUserId,
                MediaUrl = e.Url,
                CreatedOn = dateTimeProvider.UtcNow,
            });

        var externalMedia = downloadMedia.ExternalVideos
            .Select(e => new DownloadedMediaRecord
            {
                Id = default,
                
                ChannelId = newMessage.ChannelId,
                MessageId = newMessage.Id,
                DownloadedFrom = downloadMedia.SourceUrl.ToString(),
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