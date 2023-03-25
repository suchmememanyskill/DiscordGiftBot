# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /source

# copy csproj and restore as distinct layers
COPY *.sln .
COPY DiscordGiftBot/*.csproj ./DiscordGiftBot/
RUN dotnet restore

# copy everything else and build app
COPY DiscordGiftBot/. ./DiscordGiftBot/
WORKDIR /source/DiscordGiftBot
RUN dotnet publish -c release -o /app

# final stage/image
FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /app ./

ENTRYPOINT ["dotnet", "DiscordGiftBot.dll"]
