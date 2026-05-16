# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Notification/BarkFluff.Notification.csproj", "Backend/BarkFluff.Notification/"]
COPY ["Backend/BarkFluff.GrpcServer/BarkFluff.GrpcServer.csproj", "Backend/BarkFluff.GrpcServer/"]
COPY ["Shared/BarkFluff.Shared.Auth/BarkFluff.Shared.Auth.csproj", "Shared/BarkFluff.Shared.Auth/"]
COPY ["Shared/BarkFluff.Shared.Exceptions/BarkFluff.Shared.Exceptions.csproj", "Shared/BarkFluff.Shared.Exceptions/"]
COPY ["Shared/BarkFluff.Shared.Identity/BarkFluff.Shared.Identity.csproj", "Shared/BarkFluff.Shared.Identity/"]
COPY ["Shared/BarkFluff.Shared.Queue/BarkFluff.Shared.Queue.csproj", "Shared/BarkFluff.Shared.Queue/"]
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Backend/BarkFluff.Notification/BarkFluff.Notification.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.Notification"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "BarkFluff.Notification.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN mkdir -p /app/publish/logs

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --chown=1654:1654 --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BarkFluff.Notification.dll"]
