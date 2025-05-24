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
FROM mcr.microsoft.com/dotnet/sdk:9.0
WORKDIR /app

# Get yt-dlp. Static build has a huge startup delay...
RUN apt-get update && apt-get install -y python3 \
	&& curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp

COPY --from=build /Dotto.Bot/out .
ENTRYPOINT ["dotnet", "Bot.dll"]