# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj", "Backend/Barkfluff.CloudMessaging/"]
COPY ["Backend/BarkFluff.GrpcServer/BarkFluff.GrpcServer.csproj", "Backend/BarkFluff.GrpcServer/"]
COPY ["Shared/BarkFluff.Shared.Auth/BarkFluff.Shared.Auth.csproj", "Shared/BarkFluff.Shared.Auth/"]
COPY ["Shared/BarkFluff.Shared.Exceptions/BarkFluff.Shared.Exceptions.csproj", "Shared/BarkFluff.Shared.Exceptions/"]
COPY ["Shared/BarkFluff.Shared.Identity/BarkFluff.Shared.Identity.csproj", "Shared/BarkFluff.Shared.Identity/"]
COPY ["Shared/BarkFluff.Shared.Queue/BarkFluff.Shared.Queue.csproj", "Shared/BarkFluff.Shared.Queue/"]
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.CloudMessaging"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "Barkfluff.CloudMessaging.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN mkdir -p /app/publish/logs

FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app
COPY --chown=1654:1654 --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Barkfluff.CloudMessaging.dll"]
