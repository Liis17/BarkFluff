FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.Developers/Barkfluff.Developers.csproj", "Backend/Barkfluff.Developers/"]
RUN dotnet restore "Backend/Barkfluff.Developers/Barkfluff.Developers.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.Developers"
RUN dotnet publish "Barkfluff.Developers.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Barkfluff.Developers.dll"]
