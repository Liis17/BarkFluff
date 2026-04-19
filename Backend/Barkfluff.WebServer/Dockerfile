FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.WebServer/Barkfluff.WebServer.csproj", "Backend/Barkfluff.WebServer/"]
COPY ["Shared/BarkFluff.Proto/", "Shared/BarkFluff.Proto/"]
RUN dotnet restore "Backend/Barkfluff.WebServer/Barkfluff.WebServer.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.WebServer"
RUN dotnet publish "Barkfluff.WebServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Barkfluff.WebServer.dll"]
