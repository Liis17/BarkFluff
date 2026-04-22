# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.AdminPanel/Barkfluff.AdminPanel.csproj", "Backend/Barkfluff.AdminPanel/"]
COPY ["Shared/BarkFluff.Shared.Auth/BarkFluff.Shared.Auth.csproj", "Shared/BarkFluff.Shared.Auth/"]
COPY ["Shared/BarkFluff.Proto/BarkFluff.Proto.csproj", "Shared/BarkFluff.Proto/"]
RUN dotnet restore "Backend/Barkfluff.AdminPanel/Barkfluff.AdminPanel.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.AdminPanel"
RUN dotnet publish "./Barkfluff.AdminPanel.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
USER root
WORKDIR /app

RUN apk add --no-cache docker-cli docker-cli-compose \
    && addgroup docker \
    && adduser app docker

COPY --from=build /app/publish .
RUN mkdir -p /app/db && chown app:app /app/db
USER $APP_UID
ENTRYPOINT ["dotnet", "Barkfluff.AdminPanel.dll"]
