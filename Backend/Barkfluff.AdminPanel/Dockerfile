FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.AdminPanel/Barkfluff.AdminPanel.csproj", "Backend/Barkfluff.AdminPanel/"]
RUN dotnet restore "Backend/Barkfluff.AdminPanel/Barkfluff.AdminPanel.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.AdminPanel"
RUN dotnet publish "./Barkfluff.AdminPanel.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Barkfluff.AdminPanel.dll"]
