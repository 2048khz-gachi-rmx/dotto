FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /Dotto.Bot

# Copy just the project files and restore first. This layer is only invalidated
# when a .csproj/.sln changes, so the (slow) restore stays cached across builds.
COPY Dotto.sln ./
COPY Dotto.Application/Dotto.Application.csproj                     Dotto.Application/
COPY Dotto.Bot/Dotto.Bot.csproj                                     Dotto.Bot/
COPY Dotto.Common/Dotto.Common.csproj                               Dotto.Common/
COPY Dotto.Database/Dotto.Database.csproj                           Dotto.Database/
COPY Dotto.Discord/Dotto.Discord.csproj                             Dotto.Discord/
COPY Dotto.Downloader/Dotto.Downloader.csproj                       Dotto.Downloader/
COPY Dotto.Downloader.Contracts/Dotto.Downloader.Contracts.csproj   Dotto.Downloader.Contracts/
COPY Dotto.Ffmpeg/Dotto.Ffmpeg.csproj                               Dotto.Ffmpeg/
COPY Dotto.Ffmpeg.Contracts/Dotto.Ffmpeg.Contracts.csproj           Dotto.Ffmpeg.Contracts/
COPY Dotto.FileUpload/Dotto.FileUpload.csproj                       Dotto.FileUpload/
COPY Dotto.Tests/Dotto.Tests.csproj                                 Dotto.Tests/

# Cache the NuGet packages in the buildkit cache so repeated builds don't re-download them.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore

# Copy everything else (only rebuilds when source actually changes).
COPY . ./

# Build and publish a release, skipping the restore we already did.
# Publish the Bot (exe) project: it pulls in every runtime dependency but leaves
# the test assemblies out of the final image.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish Dotto.Bot/Dotto.Bot.csproj -c Release -o out --no-restore

FROM mwader/static-ffmpeg AS ffmpeg

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0
COPY --from=denoland/deno:bin-2.5.6 /deno /usr/local/bin/deno
COPY --from=ffmpeg /ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg /ffprobe /usr/local/bin/ffprobe
WORKDIR /app

# Get yt-dlp (and other packages while we're at it)
# yt-dlp static build has a huge startup delay, so we're using the system python install
RUN apt-get update \
	&& apt-get install -y python3 xz-utils wget curl \
	&& curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp 

COPY --from=build /Dotto.Bot/out .
ENTRYPOINT ["dotnet", "Dotto.Bot.dll"]