﻿using System.Text;
using Dotto.Application.Abstractions.Factories;
using Dotto.Application.Entities;
using Dotto.Application.InternalServices;
using Dotto.Application.InternalServices.UploadService;
using Dotto.Common;
using Dotto.Common.DateTimeProvider;
using Dotto.Infrastructure.Downloader.Contracts.Interfaces;
using Dotto.Infrastructure.Downloader.Contracts.Models;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Dotto.Application.Modules.Download;

public class DownloadCommand(IDottoDbContext dbContext,
    IDownloaderServiceFactory downloaderFactory,
    IDateTimeProvider dateTimeProvider,
    IUploadService? uploadService = null)
{
    private const long UploadMinio = 100 << 20;
    private const long UploadLimitNoNitro = 10 << 20;

    public async Task<DownloadedMediaMessage<T>> CreateMessage<T>(Uri uri, bool audioOnly = false, long discordUploadLimit = UploadLimitNoNitro, CancellationToken ct = default)
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

        var downloader = downloaderFactory.CreateDownloaderService(uri);

        IList<DownloadedMedia> videos;
        try
        {
            videos = await downloader.Download(uri, new DownloadOptions
            {
                MaxFilesize = uploadLimit,
                AudioOnly = audioOnly
            }, ct);
        }
        catch (IndexOutOfRangeException)
        {
            response.Message.WithContent(
                $"Upload file limit exceeded ({StringUtils.HumanReadableSize(uploadLimit)}), it's over");
            return response;
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
            
            response.Message
                .WithEmbeds([
                    new()
                    {
                        Color = new(140, 55, 55),
                        Title = Format.Escape(ex.Message),
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

            if (media.FileSize > discordUploadLimit && uploadService != null)
            {
                // we can't fit the video in the discord upload limits; upload externally, if possible
                var uploadedUrl = await uploadService.UploadFile(media.Video, media.FileSize ?? discordUploadLimit, null, $"video/{extension}", ct);
                
                response.ExternalVideos.Add(uploadedUrl);
                videoName = Format.Link(videoName, uploadedUrl.ToString());
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