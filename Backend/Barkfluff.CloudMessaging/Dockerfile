FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj", "Backend/Barkfluff.CloudMessaging/"]
RUN dotnet restore "Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.CloudMessaging"
RUN dotnet publish "Barkfluff.CloudMessaging.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Barkfluff.CloudMessaging.dll"]
