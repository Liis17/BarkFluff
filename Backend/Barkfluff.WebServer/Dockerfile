# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/Barkfluff.WebServer/Barkfluff.WebServer.csproj", "Backend/Barkfluff.WebServer/"]
COPY ["Backend/BarkFluff.GrpcServer/BarkFluff.GrpcServer.csproj", "Backend/BarkFluff.GrpcServer/"]
COPY ["Shared/BarkFluff.Shared.Auth/BarkFluff.Shared.Auth.csproj", "Shared/BarkFluff.Shared.Auth/"]
COPY ["Shared/BarkFluff.Shared.Exceptions/BarkFluff.Shared.Exceptions.csproj", "Shared/BarkFluff.Shared.Exceptions/"]
COPY ["Shared/BarkFluff.Shared.Identity/BarkFluff.Shared.Identity.csproj", "Shared/BarkFluff.Shared.Identity/"]
COPY ["Shared/BarkFluff.Proto/BarkFluff.Proto.csproj", "Shared/BarkFluff.Proto/"]
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Backend/Barkfluff.WebServer/Barkfluff.WebServer.csproj"
COPY . .
WORKDIR "/src/Backend/Barkfluff.WebServer"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "Barkfluff.WebServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Barkfluff.WebServer.dll"]
