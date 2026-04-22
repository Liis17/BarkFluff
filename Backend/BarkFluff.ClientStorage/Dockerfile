# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.ClientStorage/BarkFluff.ClientStorage.csproj", "Backend/BarkFluff.ClientStorage/"]
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Backend/BarkFluff.ClientStorage/BarkFluff.ClientStorage.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.ClientStorage"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "BarkFluff.ClientStorage.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
USER root
RUN mkdir -p /app/data && chown $APP_UID /app/data
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BarkFluff.ClientStorage.dll"]
