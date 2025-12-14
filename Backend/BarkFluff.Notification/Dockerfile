FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Notification/BarkFluff.Notification.csproj", "Backend/BarkFluff.Notification/"]
RUN dotnet restore "Backend/BarkFluff.Notification/BarkFluff.Notification.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.Notification"
RUN dotnet publish "BarkFluff.Notification.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.Notification.dll"]