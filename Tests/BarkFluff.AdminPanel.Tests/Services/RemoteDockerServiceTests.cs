using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Logging.Abstractions;

using System.Text.Json;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class RemoteDockerServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"barkfluff-remote-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task CreateServerAsync_PersistsServerAndDoesNotExposePassword()
    {
        using var db = new RemoteDockerDbContext(_dbPath);
        var ssh = new FakeSshClient();
        var service = CreateService(db, ssh);

        var result = await service.CreateServerAsync(new SaveRemoteServerRequest
        {
            Name = "MSK", Host = "141.105.69.30", Port = 22, Username = "root", Password = "secret"
        });

        Assert.True(ssh.ConnectionWasTested);
        Assert.True(result.IsPasswordConfigured);
        Assert.Equal("MSK", db.Servers.FindById(result.Id)!.Name);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(document.RootElement.TryGetProperty("Password", out _));
    }

    [Fact]
    public async Task CreateServerAsync_RejectsDuplicateName()
    {
        using var db = new RemoteDockerDbContext(_dbPath);
        var service = CreateService(db, new FakeSshClient());
        var request = new SaveRemoteServerRequest { Name = "Node", Host = "host", Port = 22, Username = "root", Password = "secret" };

        await service.CreateServerAsync(request);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateServerAsync(request));
    }

    [Fact]
    public void DeleteServer_RemovesItsTrackedContainers()
    {
        using var db = new RemoteDockerDbContext(_dbPath);
        var server = new RemoteServer { Name = "Node", Host = "host", Username = "root", Password = "secret" };
        db.Servers.Insert(server);
        db.Containers.Insert(new RemoteContainer { ServerId = server.Id, ContainerName = "web" });
        var service = CreateService(db, new FakeSshClient());

        Assert.True(service.DeleteServer(server.Id));
        Assert.Empty(db.Containers.FindAll());
    }

    [Fact]
    public async Task DiscoverContainersAsync_ReturnsComposeCapabilityFromLabels()
    {
        using var db = new RemoteDockerDbContext(_dbPath);
        var server = new RemoteServer { Name = "Node", Host = "host", Username = "root", Password = "secret" };
        db.Servers.Insert(server);
        var ssh = new FakeSshClient
        {
            DockerPsOutput = """{"ID":"abc","Image":"barkfluff/web","Names":"web","State":"running","Status":"Up 1 minute"}""",
            LabelsOutput = """{"com.docker.compose.service":"web","com.docker.compose.project.config_files":"/srv/docker-compose.yml","com.docker.compose.project.working_dir":"/srv"}"""
        };
        var service = CreateService(db, ssh);

        var result = await service.DiscoverContainersAsync(server.Id);

        var container = Assert.Single(result);
        Assert.Equal("web", container.ContainerName);
        Assert.True(container.IsComposeManaged);
    }

    [Fact]
    public async Task ExecuteActionAsync_RejectsUpdateForNonComposeContainer()
    {
        using var db = new RemoteDockerDbContext(_dbPath);
        var server = new RemoteServer { Name = "Node", Host = "host", Username = "root", Password = "secret" };
        db.Servers.Insert(server);
        var container = new RemoteContainer { ServerId = server.Id, ContainerName = "plain-docker" };
        db.Containers.Insert(container);
        var ssh = new FakeSshClient();
        var service = CreateService(db, ssh);

        var result = await service.ExecuteActionAsync(server.Id, container.Id, "pull");

        Assert.False(result.Success);
        Assert.Empty(ssh.Commands);
    }

    [Fact]
    public async Task ExecuteActionAsync_UpdatesComposeContainerUsingDiscoveredMetadata()
    {
        using var db = new RemoteDockerDbContext(_dbPath);
        var server = new RemoteServer { Name = "Node", Host = "host", Username = "root", Password = "secret" };
        db.Servers.Insert(server);
        var container = new RemoteContainer
        {
            ServerId = server.Id,
            ContainerName = "web",
            ComposeServiceName = "web",
            ComposeFiles = "/srv/docker-compose.yml",
            ComposeWorkingDirectory = "/srv"
        };
        db.Containers.Insert(container);
        var ssh = new FakeSshClient();
        var service = CreateService(db, ssh);

        var result = await service.ExecuteActionAsync(server.Id, container.Id, "pull");

        Assert.True(result.Success);
        Assert.Contains("docker compose -f '/srv/docker-compose.yml' pull 'web'", Assert.Single(ssh.Commands));
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static RemoteDockerService CreateService(RemoteDockerDbContext db, FakeSshClient ssh) =>
        new(db, ssh, NullLogger<RemoteDockerService>.Instance);

    private sealed class FakeSshClient : IRemoteSshClient
    {
        public bool ConnectionWasTested { get; private set; }
        public string DockerPsOutput { get; init; } = string.Empty;
        public string LabelsOutput { get; init; } = "{}";
        public List<string> Commands { get; } = [];

        public Task TestConnectionAsync(RemoteServer server, CancellationToken cancellationToken = default)
        {
            ConnectionWasTested = true;
            return Task.CompletedTask;
        }

        public Task<RemoteSshCommandResult> RunAsync(RemoteServer server, string command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            var output = command.StartsWith("docker ps", StringComparison.Ordinal) ? DockerPsOutput
                : command.StartsWith("docker inspect", StringComparison.Ordinal) ? LabelsOutput
                : string.Empty;
            return Task.FromResult(new RemoteSshCommandResult(output, string.Empty, 0));
        }
    }
}
