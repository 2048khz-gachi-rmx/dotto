FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /Dotto.Bot

# Copy everything
COPY . ./

# Run the restore and cache the packages on the host for faster subsequent builds.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore

# Build and publish a release
RUN dotnet publish -c Release -o out

FROM mwader/static-ffmpeg AS ffmpeg

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0
COPY --from=denoland/deno:bin-2.5.6 /deno /usr/local/bin/deno
COPY --from=denoland/deno:bin-2.5.6 /deno /usr/local/bin/deno
COPY --from=ffmpeg /ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg /ffprobe /usr/local/bin/ffprobe
WORKDIR /app

# Get yt-dlp (and other packages while we're at it)
# yt-dlp static build has a huge startup delay, so we're using the system python install
RUN apt-get update \
	&& apt-get install -y python3 xz-utils wget curl \
	&& curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp 

COPY --from=build /Dotto.Bot/out .
ENTRYPOINT ["dotnet", "Dotto.Bot.dll"]