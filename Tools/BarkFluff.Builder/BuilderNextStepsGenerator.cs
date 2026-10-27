using System.Text;

namespace BarkFluff.Builder;

public static class BuilderNextStepsGenerator
{
    public static string Build(BuilderModel model)
    {
        var setupUrl = !string.IsNullOrWhiteSpace(model.SetupPublicOrigin)
            ? model.SetupPublicOrigin.TrimEnd('/')
            : model.NginxMode == NginxMode.None
                ? $"http://127.0.0.1:{model.SetupHostPort}"
                : BarkFluff.Node.NodeSubdomains.BuildUrl("setup", model.PublicDomain);
        var build = model.IncludeLognara ? " --build" : "";
        var b = new StringBuilder("# BarkFluff node setup\n\n");
        b.AppendLine("1. Create the shared Docker network before starting the stack:");
        b.AppendLine();
        b.AppendLine("   ```sh");
        b.AppendLine($"   docker network create \"{model.DockerNetworkName}\"");
        b.AppendLine("   ```");
        b.AppendLine();
        b.AppendLine("2. Start the setup stack:");
        b.AppendLine();
        b.AppendLine("   ```sh");
        b.AppendLine($"   docker compose -f docker-compose.setup.yml up -d{build}");
        b.AppendLine("   ```");
        b.AppendLine();
        b.AppendLine($"3. Open [{setupUrl}]({setupUrl}) and complete setup using `secrets/setup_token`.");
        b.Append("   Use the API key and secret from `livekit/livekit.yaml` for LiveKit. ");
        b.AppendLine(model.IncludeMinio
            ? "Use the MinIO root credentials from `.env` as the initial S3 credentials."
            : "Enter the credentials issued by the external S3 provider in Setup.");
        b.AppendLine();
        b.AppendLine("The Compose project is named `barkfluff`. If the host already has a Compose project named `barkfluff` (for example, a nightly stack in a `barkfluff` directory), `docker compose up --remove-orphans` can affect its containers. Deploy this stack on a separate host and do not mix the projects.");
        b.AppendLine();
        b.AppendLine("4. Stop setup without deleting persistent data, then start the main stack:");
        b.AppendLine();
        b.AppendLine("   ```sh");
        b.AppendLine("   docker compose -f docker-compose.setup.yml down");
        b.AppendLine($"   docker compose up -d --remove-orphans{build}");
        b.AppendLine("   ```");
        b.AppendLine();
        b.AppendLine("Setup, PostgreSQL, Seq and MinIO host ports bind only to `127.0.0.1`. To reach them remotely, forward the selected `.env` ports through SSH, for example `ssh -L 7032:127.0.0.1:7032 user@host`; replace the port with `SETUP_HOST_PORT`, `POSTGRES_HOST_PORT`, `SEQ_HOST_PORT`, `MINIO_API_HOST_PORT` or `MINIO_CONSOLE_HOST_PORT`.");
        b.AppendLine();

        switch (model.NginxMode)
        {
            case NginxMode.Compose:
                b.AppendLine("Nginx runs in Compose. The generated nginx configuration and sites are in `nginx/`; the certificate and key are copied to `certs/barkfluff-crt.pem` and `certs/barkfluff-key.pem`.");
                break;
            case NginxMode.ExternalContainer:
                b.AppendLine($"Copy `nginx/sites/*.conf` into the external nginx container's conf.d directory. Make the certificate available at `{model.NginxCertificatePath}` and the key at `{model.NginxKeyPath}` inside that container.");
                b.AppendLine("The generated sites include `/etc/nginx/conf.d/01-ssl-params.conf`; copy `nginx/sites/01-ssl-params.conf` to that exact path inside the container. Check existing nginx configuration for duplicate `default_server` listeners from `00-default.conf` and duplicate `map $http_upgrade $connection_upgrade` declarations.");
                break;
            case NginxMode.Host:
                b.AppendLine($"Install `nginx/sites/*.conf` in the host nginx configuration directory. Make the certificate available at `{model.NginxCertificatePath}` and the key at `{model.NginxKeyPath}`. Backend ports are bound to loopback.");
                b.AppendLine("The generated sites include `/etc/nginx/conf.d/01-ssl-params.conf`; copy `nginx/sites/01-ssl-params.conf` to that exact path on the host. Check existing nginx configuration for duplicate `default_server` listeners from `00-default.conf` and duplicate `map $http_upgrade $connection_upgrade` declarations.");
                break;
            default:
                b.AppendLine("Nginx is disabled. Expose and secure the required service endpoints through your own ingress.");
                break;
        }

        if (model.IncludeLognara)
        {
            b.AppendLine();
            b.AppendLine("For Lognara, register each enabled agent in Core, then paste each issued relay token into the matching `*_LOGNARA_RELAY_TOKEN` entry in `.env`.");
        }

        b.AppendLine();
        b.AppendLine("Do not attach this stack to a network that also contains the older nightly stack: Compose DNS service names would collide.");
        return b.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
