FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.ClientStorage/BarkFluff.ClientStorage.csproj", "Backend/BarkFluff.ClientStorage/"]
RUN dotnet restore "Backend/BarkFluff.ClientStorage/BarkFluff.ClientStorage.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.ClientStorage"
RUN dotnet publish "BarkFluff.ClientStorage.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.ClientStorage.dll"]
