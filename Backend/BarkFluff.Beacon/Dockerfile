FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Beacon/BarkFluff.Beacon.csproj", "Backend/BarkFluff.Beacon/"]
RUN dotnet restore "Backend/BarkFluff.Beacon/BarkFluff.Beacon.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.Beacon"
RUN dotnet publish "BarkFluff.Beacon.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.Beacon.dll"]