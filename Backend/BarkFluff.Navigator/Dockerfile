FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Navigator/BarkFluff.Navigator.csproj", "Backend/BarkFluff.Navigator/"]
RUN dotnet restore "Backend/BarkFluff.Navigator/BarkFluff.Navigator.csproj"
COPY . .
WORKDIR "/src/Backend/BarkFluff.Navigator"
RUN dotnet build "BarkFluff.Navigator.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BarkFluff.Navigator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.Navigator.dll"]
