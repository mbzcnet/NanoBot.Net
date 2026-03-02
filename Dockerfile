FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/NanoBot.Core/NanoBot.Core.csproj NanoBot.Core/
COPY src/NanoBot.Infrastructure/NanoBot.Infrastructure.csproj NanoBot.Infrastructure/
COPY src/NanoBot.Providers/NanoBot.Providers.csproj NanoBot.Providers/
COPY src/NanoBot.Tools/NanoBot.Tools.csproj NanoBot.Tools/
COPY src/NanoBot.Channels/NanoBot.Channels.csproj NanoBot.Channels/
COPY src/NanoBot.Agent/NanoBot.Agent.csproj NanoBot.Agent/
COPY src/NanoBot.Cli/NanoBot.Cli.csproj NanoBot.Cli/
COPY src/NanoBot.WebUI/NanoBot.WebUI.csproj NanoBot.WebUI/

RUN dotnet restore NanoBot.Cli/NanoBot.Cli.csproj

COPY src/ .
WORKDIR /src/NanoBot.Cli
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl ca-certificates && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /root/.nanobot

# 暴露CLI端口和WebUI端口
EXPOSE 18790
EXPOSE 18888

ENTRYPOINT ["dotnet", "NanoBot.Cli.dll"]
CMD ["status"]
