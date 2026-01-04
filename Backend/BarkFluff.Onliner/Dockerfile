FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Onliner/BarkFluff.Onliner.csproj", "Backend/BarkFluff.Onliner/"]
RUN dotnet restore "Backend/BarkFluff.Onliner/BarkFluff.Onliner.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.Onliner"
RUN dotnet build "./BarkFluff.Onliner.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BarkFluff.Onliner.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.Onliner.dll"]
