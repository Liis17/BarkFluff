# syntax=docker/dockerfile:1

# ──────────────────────────────────────────────────────────────────────────────
# Stage 1: Generate gRPC-Web JS bundle from .proto files
# ──────────────────────────────────────────────────────────────────────────────
FROM node:20-slim AS proto-build
ARG DEBIAN_FRONTEND=noninteractive

RUN apt-get update && \
    apt-get install -y --no-install-recommends protobuf-compiler curl ca-certificates && \
    rm -rf /var/lib/apt/lists/*

RUN curl -sSL \
    https://github.com/grpc/grpc-web/releases/download/1.5.0/protoc-gen-grpc-web-1.5.0-linux-x86_64 \
    -o /usr/local/bin/protoc-gen-grpc-web && \
    chmod +x /usr/local/bin/protoc-gen-grpc-web

WORKDIR /protobuild

# Install JS deps (google-protobuf, grpc-web, esbuild)
COPY Backend/BarkFluff.Web/scripts/package.json ./
RUN npm install --omit=dev --silent

# Copy proto sources and entry point
COPY Shared/BarkFluff.Proto ./proto/
COPY Backend/BarkFluff.Web/scripts/proto-bundle-index.js ./gen-index.js

# Generate CommonJS stubs from .proto files
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

# Copy entry point into gen/ next to the generated stubs, then bundle
RUN cp gen-index.js gen/index.js && \
    mkdir -p bundle && \
    node_modules/.bin/esbuild gen/index.js \
        --bundle \
        --format=iife \
        --global-name=_BFBundle \
        --outfile=bundle/barkfluff.bundle.js \
        --target=es2020 \
        --log-level=warning

# ──────────────────────────────────────────────────────────────────────────────
# Stage 2: Build .NET application
# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Backend/BarkFluff.Web/BarkFluff.Web.csproj", "Backend/BarkFluff.Web/"]
RUN dotnet restore "Backend/BarkFluff.Web/BarkFluff.Web.csproj"

COPY . .

# Inject the generated bundle before publish
COPY --from=proto-build /protobuild/bundle/barkfluff.bundle.js \
    /src/Backend/BarkFluff.Web/wwwroot/js/proto/barkfluff.bundle.js

WORKDIR "/src/Backend/BarkFluff.Web"
RUN dotnet publish "BarkFluff.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ──────────────────────────────────────────────────────────────────────────────
# Stage 3: Runtime image
# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
USER $APP_UID
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BarkFluff.Web.dll"]
