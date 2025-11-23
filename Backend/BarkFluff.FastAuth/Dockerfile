FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.FastAuth/BarkFluff.FastAuth.csproj", "Backend/BarkFluff.FastAuth/"]
RUN dotnet restore "Backend/BarkFluff.FastAuth/BarkFluff.FastAuth.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.FastAuth"
RUN dotnet publish "BarkFluff.FastAuth.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.FastAuth.dll"]