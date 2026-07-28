using BarkFluff.ClientV2.WPF.Infrastructure.Storage;
using BarkFluff.ClientV2.WPF.Models;

using Microsoft.Data.Sqlite;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class SqliteApplicationDataStoreTests
{
    [Fact]
    public async Task SaveSelectedNodeAsync_PersistsNodeAndSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SqliteApplicationDataStore(new AppDataPaths(directory));
            await store.InitializeAsync();
            await store.MarkWelcomeSeenAsync();
            await store.SaveLanguageAsync("ru");
            await store.SaveSelectedNodeAsync(new NodeProfile("https://node.example.com", "Node", "Description"));

            Assert.True(await store.HasSeenWelcomeAsync());
            Assert.Equal("ru", await store.GetLanguageAsync());
            Assert.Equal("Node", (await store.GetSelectedNodeAsync())!.Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
