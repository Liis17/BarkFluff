FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
WORKDIR /app

# Install Docker CLI and Docker Compose
RUN apt-get update && apt-get install -y \
    curl \
    gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian bookworm stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y docker-ce-cli docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user and add to docker group
RUN usermod -aG docker app

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER root
WORKDIR /app

# Install Docker CLI and Docker Compose
RUN apt-get update && apt-get install -y \
    curl \
    gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian bookworm stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y docker-ce-cli docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user and add to docker group
RUN usermod -aG docker app

COPY --from=build /app/publish .
RUN mkdir -p /app/db && chown app:app /app/db
USER $APP_UID
ENTRYPOINT ["dotnet", "Barkfluff.AdminPanel.dll"]
