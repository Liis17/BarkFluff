# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.Developers/Barkfluff.Developers.csproj", "Backend/Barkfluff.Developers/"]
COPY ["Shared/BarkFluff.Proto/BarkFluff.Proto.csproj", "Shared/BarkFluff.Proto/"]
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Backend/Barkfluff.Developers/Barkfluff.Developers.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.Developers"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "Barkfluff.Developers.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Barkfluff.Developers.dll"]
