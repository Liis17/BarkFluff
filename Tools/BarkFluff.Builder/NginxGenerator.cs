using System.Text;
using BarkFluff.Node;

namespace BarkFluff.Builder;

public static class NginxGenerator
{
    private static readonly string[] CloudflareCidrs =
    [
        "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "104.16.0.0/13",
        "104.24.0.0/14", "108.162.192.0/18", "131.0.72.0/22", "141.101.64.0/18",
        "162.158.0.0/15", "172.64.0.0/13", "173.245.48.0/20", "188.114.96.0/20",
        "190.93.240.0/20", "197.234.240.0/22", "198.41.128.0/17",
        "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32",
        "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32"
    ];

    public static IReadOnlyDictionary<string, string> BuildFiles(BuilderModel model)
    {
        if (model.NginxMode == NginxMode.None)
            return new Dictionary<string, string>();

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nginx/sites/00-default.conf"] = BuildDefault(model),
            ["nginx/sites/00-rate-limits.conf"] = BuildRateLimits(),
            ["nginx/sites/01-ssl-params.conf"] = BuildSslParams(model)
        };

        if (model.BehindCloudflare)
            files["nginx/sites/00-real-ip.conf"] = BuildRealIp();
        if (model.NginxMode == NginxMode.Compose)
            files["nginx/nginx.conf"] = BuildNginxConf();

        AddSites(files, model);
        return files;
    }

    private static void AddSites(IDictionary<string, string> files, BuilderModel model)
    {
        var dockerDns = model.NginxMode is NginxMode.Compose or NginxMode.ExternalContainer;
        var upstreamHost = dockerDns ? null : "127.0.0.1";

        files["nginx/sites/identity.conf"] = GrpcSite(model, "identity", upstreamHost, "identity", "7000", "limit_req_status 429;\n        limit_req zone=identity_anon burst=20 nodelay;", "300s");
        files["nginx/sites/users.conf"] = GrpcSite(model, "users", upstreamHost, "users", "7001", timeout: "300s");
        files["nginx/sites/messages.conf"] = GrpcSite(model, "messages", upstreamHost, "messages", "7007", timeout: "3600s");
        files["nginx/sites/files.conf"] = FilesSite(model, upstreamHost);
        files["nginx/sites/files-media.conf"] = FilesMediaSite(model, upstreamHost);
        files["nginx/sites/updates.conf"] = GrpcSite(model, "updates", upstreamHost, "updates", "7015", timeout: "3600s");
        files["nginx/sites/onliner.conf"] = GrpcSite(model, "onliner", upstreamHost, "onliner", "7009", timeout: "3600s");
        files["nginx/sites/fast-auth.conf"] = GrpcSite(model, "fast-auth", upstreamHost, "fast-auth", "7008",
            "limit_req zone=fastauth_anon burst=5 nodelay;\n        limit_conn fastauth_streams 10;", "300s");
        files["nginx/sites/federation.conf"] = GrpcSite(model, "federation", upstreamHost, "federation", "7030",
            "limit_req zone=federation_s2s burst=20 nodelay;", "3600s");
        files["nginx/sites/beacon.conf"] = GrpcSite(model, "beacon", upstreamHost, "beacon", "7002",
            "limit_req zone=beacon_anon burst=10 nodelay;", "300s");
        files["nginx/sites/bots.conf"] = BotsSite(model, upstreamHost);
        files["nginx/sites/calls.conf"] = GrpcSite(model, "calls", upstreamHost, "calls", "7025", timeout: "3600s");
        files["nginx/sites/livekit.conf"] = LiveKitSite(model, upstreamHost);
        files["nginx/sites/web.conf"] = WebSite(model, upstreamHost);
        files["nginx/sites/developers.conf"] = DevelopersSite(model, upstreamHost);
        files["nginx/sites/admin-panel.conf"] = AdminPanelSite(model, upstreamHost);
        var setupPort = model.NginxMode == NginxMode.Host ? model.SetupHostPort : "7032";
        files["nginx/sites/setup.conf"] = HttpSite(model, "setup", upstreamHost, "setup", setupPort, "location /", "proxy_read_timeout 300s;\n    proxy_send_timeout 300s;");
    }

    private static string GrpcSite(
        BuilderModel model,
        string site,
        string? upstreamHost,
        string service,
        string port,
        string? locationPrefix = null,
        string timeout = "300s")
    {
        var backend = Upstream("grpc", upstreamHost, service, port);
        var prefix = string.IsNullOrWhiteSpace(locationPrefix) ? "" : $"        {locationPrefix.Replace("\n", "\n        ", StringComparison.Ordinal)}\n";
        var location = $$"""
            location / {
        {{prefix}}set $grpc_backend {{backend}};
        grpc_pass $grpc_backend;
        grpc_set_header Host $host;
        grpc_set_header X-Real-IP $remote_addr;
        grpc_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        grpc_set_header X-Forwarded-Proto $scheme;
        grpc_read_timeout {{timeout}};
        grpc_send_timeout {{timeout}};
    }
    """;
        return TlsSite(model, site, "http2", location);
    }

    private static string FilesSite(BuilderModel model, string? upstreamHost)
    {
        var grpc = Upstream("grpc", upstreamHost, "files", "7005");
        var http = Upstream("http", upstreamHost, "files", "7006");
        var locations = $$"""
            location / {
        set $grpc_backend {{grpc}};
        grpc_pass $grpc_backend;
        grpc_set_header Host $host;
        grpc_set_header X-Real-IP $remote_addr;
        grpc_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        grpc_set_header X-Forwarded-Proto $scheme;
        grpc_read_timeout 300s;
        grpc_send_timeout 300s;
    }

    location /web/ {
        set $http_backend {{http}};
        rewrite ^/web/(.*) /$1 break;
        proxy_pass $http_backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 512m;
        proxy_request_buffering off;
        proxy_buffering off;
        proxy_read_timeout 600s;
        proxy_send_timeout 600s;
        client_body_timeout 600s;
    }
    """;
        return TlsSite(model, "files", "http2", locations);
    }

    private static string FilesMediaSite(BuilderModel model, string? upstreamHost)
    {
        var http = Upstream("http", upstreamHost, "files", "7006");
        var location = $$"""
            location /web/ {
        if ($request_method = OPTIONS) {
            add_header Access-Control-Allow-Origin '*' always;
            add_header Access-Control-Allow-Methods 'GET, POST, OPTIONS' always;
            add_header Access-Control-Allow-Headers 'Content-Type, Authorization' always;
            add_header Access-Control-Max-Age 86400 always;
            return 204;
        }
        set $http_backend {{http}};
        rewrite ^/web/(.*) /$1 break;
        proxy_pass $http_backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        add_header Access-Control-Allow-Origin '*' always;
        add_header Access-Control-Expose-Headers 'Retry-After' always;
        client_max_body_size 512m;
        proxy_request_buffering off;
        proxy_buffering off;
        proxy_read_timeout 600s;
        proxy_send_timeout 600s;
        client_body_timeout 600s;
    }
    """;
        return TlsSite(model, "files-media", "http2", location);
    }

    private static string BotsSite(BuilderModel model, string? upstreamHost)
    {
        var http = Upstream("http", upstreamHost, "bots", "7028");
        var grpc = Upstream("grpc", upstreamHost, "bots", "7027");
        var locations = $$"""
            location ~ ^/bot/ {
        set $http_backend {{http}};
        proxy_pass $http_backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        set $grpc_backend {{grpc}};
        grpc_pass $grpc_backend;
        grpc_set_header Host $host;
        grpc_set_header X-Real-IP $remote_addr;
        grpc_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        grpc_set_header X-Forwarded-Proto $scheme;
        grpc_read_timeout 3600s;
        grpc_send_timeout 3600s;
    }
    """;
        return TlsSite(model, "bots", "http2", locations);
    }

    private static string LiveKitSite(BuilderModel model, string? upstreamHost)
    {
        var backend = Upstream("http", upstreamHost, "livekit", "7880");
        var location = $$"""
            location / {
        set $lk_backend {{backend}};
        proxy_pass $lk_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
    """;
        var map = "map $http_upgrade $connection_upgrade {\n    default upgrade;\n    '' close;\n}\n\n";
        return map + TlsSite(model, "livekit", "", location);
    }

    private static string WebSite(BuilderModel model, string? upstreamHost)
    {
        var backend = Upstream("http", upstreamHost, "web", "7016");
        var locations = $$"""
            location /api/files/upload/ {
        set $backend {{backend}};
        proxy_pass $backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 512m;
        proxy_request_buffering off;
        proxy_read_timeout 600s;
        proxy_send_timeout 600s;
        client_body_timeout 600s;
    }

    location ~ ^/barkfluff\.(updates|onliner)\. {
        set $backend {{backend}};
        proxy_pass $backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_set_header Accept-Encoding "";
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
        proxy_request_buffering off;
        proxy_buffering off;
        proxy_cache off;
        chunked_transfer_encoding on;
    }

    location / {
        set $backend {{backend}};
        proxy_pass $backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
    """;
        return TlsSite(model, "web", "http2", locations);
    }

    private static string DevelopersSite(BuilderModel model, string? upstreamHost)
    {
        var identity = Upstream("grpc", upstreamHost, "identity", "7000");
        var api = Upstream("http", upstreamHost, "developers", "7020");
        var web = Upstream("http", upstreamHost, "developers", "7021");
        var locations = $$"""
            location /grpc/barkfluff.identity.IdentityApi/ {
        limit_req zone=identity_anon burst=20 nodelay;
        set $grpc_backend {{identity}};
        rewrite ^/grpc/(.*)$ /$1 break;
        grpc_pass $grpc_backend;
        grpc_set_header Host $host;
        grpc_set_header X-Real-IP $remote_addr;
        grpc_set_header X-Forwarded-For $remote_addr;
        grpc_set_header X-Forwarded-Proto $scheme;
        grpc_read_timeout 300s;
        grpc_send_timeout 300s;
    }

    location /grpc/barkfluff.developers.DevelopersApi/ {
        set $backend {{api}};
        rewrite ^/grpc/(.*)$ /$1 break;
        proxy_pass $backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Connection "";
        proxy_buffering off;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }

    location ~ ^/(ping(?:/|$)|health/) {
        set $backend {{api}};
        proxy_pass $backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /grpc/ {
        return 404;
    }

    location / {
        set $backend {{web}};
        proxy_pass $backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
        proxy_send_timeout 60s;
    }
    """;
        return TlsSite(model, "developers", "http2", locations,
            "limit_req_status 429;\n    add_header X-Content-Type-Options \"nosniff\" always;\n    add_header X-Frame-Options \"DENY\" always;\n    add_header Referrer-Policy \"strict-origin-when-cross-origin\" always;");
    }

    private static string AdminPanelSite(BuilderModel model, string? upstreamHost)
    {
        var backend = Upstream("http", upstreamHost, "admin-panel", "51888");
        var locations = $$"""
            location /api/remote/ {
        set $backend {{backend}};
        proxy_pass $backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }

    location / {
        set $backend {{backend}};
        proxy_pass $backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
    """;
        return TlsSite(model, "admin-panel", "", locations);
    }

    private static string HttpSite(
        BuilderModel model,
        string site,
        string? upstreamHost,
        string service,
        string port,
        string locationPrefix,
        string? extra = null)
    {
        var backend = Upstream("http", upstreamHost, service, port);
        var location = $$"""
            {{locationPrefix}} {
        set $backend {{backend}};
        proxy_pass $backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        {{extra}}
    }
    """;
        return TlsSite(model, site, "", location);
    }

    private static string TlsSite(
        BuilderModel model,
        string site,
        string http2,
        string locations,
        string? serverDirectives = null)
    {
        var host = $"{NodeSubdomains.Get(site)}.{model.PublicDomain}";
        var listen = http2.Length == 0 ? "443 ssl" : $"443 ssl {http2}";
        var resolver = model.NginxMode is NginxMode.Compose or NginxMode.ExternalContainer
            ? "    resolver 127.0.0.11 valid=30s ipv6=off;\n"
            : "";
        var directives = string.IsNullOrWhiteSpace(serverDirectives) ? "" : "    " + serverDirectives.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n";
        return $$"""
        # {{host}} node endpoint
        server {
            listen 80;
            server_name {{host}};
            return 301 https://$host$request_uri;
        }

        server {
            listen {{listen}};
            server_name {{host}};
            include /etc/nginx/conf.d/01-ssl-params.conf;
        {{resolver}}{{directives}}{{Indent(locations, 4)}}
        }
        """;
    }

    private static string Upstream(string scheme, string? host, string service, string port) =>
        $"{scheme}://{host ?? service}:{port}";

    private static string Indent(string value, int spaces)
    {
        var indentation = new string(' ', spaces);
        return string.Join('\n', value.Trim().Split('\n').Select(line => indentation + line.TrimEnd('\r')));
    }

    private static string BuildDefault(BuilderModel model) => $$"""
    server {
        listen 80 default_server;
        listen 443 ssl default_server;
        server_name _;
        ssl_certificate {{CertificatePath(model)}};
        ssl_certificate_key {{KeyPath(model)}};
        return 444;
    }
    """;

    private static string BuildRateLimits() => """
limit_req_zone $binary_remote_addr zone=beacon_anon:10m rate=5r/s;
limit_req_zone $binary_remote_addr zone=fastauth_anon:10m rate=2r/s;
limit_conn_zone $binary_remote_addr zone=fastauth_streams:10m;
limit_req_zone $binary_remote_addr zone=federation_s2s:10m rate=30r/s;
limit_req_zone $binary_remote_addr zone=federation_wellknown:10m rate=5r/s;
limit_req_zone $binary_remote_addr zone=identity_anon:10m rate=10r/s;
""";

    private static string BuildSslParams(BuilderModel model) => $$"""
ssl_certificate {{CertificatePath(model)}};
ssl_certificate_key {{KeyPath(model)}};
ssl_protocols TLSv1.2 TLSv1.3;
ssl_ciphers HIGH:!aNULL:!MD5;
ssl_session_cache shared:SSL:10m;
ssl_session_timeout 10m;
ssl_prefer_server_ciphers on;
""";

    private static string BuildRealIp() =>
        string.Join("\n", CloudflareCidrs.Select(cidr => $"set_real_ip_from {cidr};"))
        + "\nreal_ip_header CF-Connecting-IP;\nreal_ip_recursive off;\n";

    private static string BuildNginxConf() => """
user nginx;
worker_processes auto;
error_log /var/log/nginx/error.log warn;
pid /var/run/nginx.pid;

events {
    worker_connections 1024;
}

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;
    log_format main '$remote_addr - $remote_user [$time_local] "$request" '
                    '$status $body_bytes_sent "$http_referer" '
                    '"$http_user_agent" "$http_x_forwarded_for"';
    access_log /var/log/nginx/access.log main;
    sendfile on;
    keepalive_timeout 65;
    include /etc/nginx/conf.d/*.conf;
}
""";

    private static string CertificatePath(BuilderModel model) => model.NginxMode == NginxMode.Compose
        ? "/etc/nginx/certs/barkfluff-crt.pem"
        : model.NginxCertificatePath;

    private static string KeyPath(BuilderModel model) => model.NginxMode == NginxMode.Compose
        ? "/etc/nginx/certs/barkfluff-key.pem"
        : model.NginxKeyPath;
}
