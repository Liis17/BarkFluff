using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

internal sealed class OutgoingStaging(string root)
{
    private string ScopeDirectory(OutgoingScope scope) =>
        Path.Combine(root, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scope))));

    private string OperationDirectory(OutgoingScope scope, Guid id) => Path.Combine(ScopeDirectory(scope), id.ToString("N"));

    internal string FilePath(OutgoingScope scope, Guid id, OutgoingFile file) =>
        Path.Combine(OperationDirectory(scope, id), file.Id.ToString("N") + file.Extension);

    internal async Task CopyAsync(OutgoingScope scope, OutgoingMessage message,
        IReadOnlyList<AttachmentSourceFile> sources, CancellationToken token)
    {
        if (sources.Count == 0)
            return;

        Directory.CreateDirectory(ScopeDirectory(scope));
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var security = new DirectorySecurity();
        // Вложения принадлежат интерактивному пользователю. Службы не читают staging,
        // поэтому наследуемый доступ, в том числе для SYSTEM, здесь не нужен.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(OperationDirectory(scope, message.Id)).Create(security);

        for (var i = 0; i < sources.Count; i++)
        {
            await using var source = new FileStream(sources[i].Path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var target = new FileStream(FilePath(scope, message.Id, message.Files[i]), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(target, token).ConfigureAwait(false);
            target.Flush(flushToDisk: true);
        }
    }

    internal void Remove(OutgoingScope scope, Guid id) => RemoveDirectory(OperationDirectory(scope, id));

    private static void RemoveDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal void Sweep(OutgoingScope scope, HashSet<Guid> keep)
        => SweepDirectory(ScopeDirectory(scope), keep);

    internal void SweepAll(IReadOnlyList<(OutgoingScope Scope, Guid Id)> operations, HashSet<Guid> copying)
    {
        var keep = operations.ToLookup(operation => ScopeDirectory(operation.Scope), operation => operation.Id,
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
            return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
                SweepDirectory(directory, keep[directory].Concat(copying).ToHashSet());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void SweepDirectory(string scopeDirectory, HashSet<Guid> keep)
    {
        if (!Directory.Exists(scopeDirectory))
            return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(scopeDirectory))
                if (Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) && !keep.Contains(id))
                    RemoveDirectory(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

