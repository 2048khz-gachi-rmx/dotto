FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /Dotto.Bot

# Copy everything
COPY . ./

# Run the restore and cache the packages on the host for faster subsequent builds.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore

# Build and publish a release
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:9.0
COPY --from=denoland/deno:bin-2.5.6 /deno /usr/local/bin/deno
WORKDIR /app

# Get yt-dlp (and other packages while we're at it)
# yt-dlp static build has a huge startup delay, so we're using the system python install
RUN apt-get update \
	&& apt-get install -y python3 xz-utils wget curl \
	&& curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp

# Download a prebuilt, stripped-down ffmpeg, cause the one shipped by durrbian is too fucking big
RUN wget -q https://johnvansickle.com/ffmpeg/builds/ffmpeg-git-amd64-static.tar.xz \
    && tar -xf ffmpeg-git-amd64-static.tar.xz \
    && mv ffmpeg-git-*-static/ffmpeg /usr/local/bin/ \
    && mv ffmpeg-git-*-static/ffprobe /usr/local/bin/ \
    && rm -rf ffmpeg-git-*

COPY --from=build /Dotto.Bot/out .
ENTRYPOINT ["dotnet", "Dotto.Bot.dll"]