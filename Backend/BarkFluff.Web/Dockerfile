# syntax=docker/dockerfile:1

FROM node:20-slim AS proto-build
ARG DEBIAN_FRONTEND=noninteractive
ARG PROTOC_VERSION=3.20.3

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl ca-certificates unzip && \
    rm -rf /var/lib/apt/lists/*

RUN curl -sSL \
    "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-x86_64.zip" \
    -o /tmp/protoc.zip && \
    unzip /tmp/protoc.zip -d /usr/local && \
    rm /tmp/protoc.zip

RUN curl -sSL \
    https://github.com/grpc/grpc-web/releases/download/1.5.0/protoc-gen-grpc-web-1.5.0-linux-x86_64 \
    -o /usr/local/bin/protoc-gen-grpc-web && \
    chmod +x /usr/local/bin/protoc-gen-grpc-web

WORKDIR /protobuild

COPY Backend/BarkFluff.Web/scripts/package.json ./
RUN npm install --omit=dev --silent

COPY Shared/BarkFluff.Proto ./proto/
COPY Backend/BarkFluff.Web/scripts/proto-bundle-index.js ./gen-index.js

RUN mkdir -p gen && cd proto && protoc \
    --proto_path=. \
    --js_out="import_style=commonjs,binary:/protobuild/gen" \
    --grpc-web_out="import_style=commonjs,mode=grpcwebtext:/protobuild/gen" \
    shared.proto \
    identity_api.proto \
    users_api.proto \
    messages_api.proto \
    files_api.proto \
    updates_api.proto \
    onliner_api.proto

RUN cp gen-index.js gen/index.js && \
    mkdir -p bundle && \
    node_modules/.bin/esbuild gen/index.js \
        --bundle \
        --format=iife \
        --global-name=_BFBundle \
        --outfile=bundle/barkfluff.bundle.js \
        --target=es2020 \
        --log-level=warning

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Backend/BarkFluff.Web/BarkFluff.Web.csproj", "Backend/BarkFluff.Web/"]
COPY ["Backend/BarkFluff.GrpcServer/BarkFluff.GrpcServer.csproj", "Backend/BarkFluff.GrpcServer/"]
COPY ["Shared/BarkFluff.Shared.Auth/BarkFluff.Shared.Auth.csproj", "Shared/BarkFluff.Shared.Auth/"]
COPY ["Shared/BarkFluff.Shared.Exceptions/BarkFluff.Shared.Exceptions.csproj", "Shared/BarkFluff.Shared.Exceptions/"]
COPY ["Shared/BarkFluff.Shared.Identity/BarkFluff.Shared.Identity.csproj", "Shared/BarkFluff.Shared.Identity/"]
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Backend/BarkFluff.Web/BarkFluff.Web.csproj"
COPY . .
COPY --from=proto-build /protobuild/bundle/barkfluff.bundle.js \
    /src/Backend/BarkFluff.Web/wwwroot/js/proto/barkfluff.bundle.js
WORKDIR "/src/Backend/BarkFluff.Web"
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "BarkFluff.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Web остаётся на Alpine: healthcheck требует wget (busybox), которого нет в chiseled
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/logs && chown $APP_UID /app/logs
USER $APP_UID
EXPOSE 7016
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget -qO /dev/null http://localhost:7016/health || exit 1
ENTRYPOINT ["dotnet", "BarkFluff.Web.dll"]
