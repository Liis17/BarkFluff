FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Users/BarkFluff.Users.csproj", "Backend/BarkFluff.Users/"]
RUN dotnet restore "Backend/BarkFluff.Users/BarkFluff.Users.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.Users"
RUN dotnet publish "BarkFluff.Users.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.Users.dll"]