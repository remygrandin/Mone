using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Mone.Contracts.Models;

namespace Mone.Infrastructure.Services;

public sealed record ResolvedNodeIdentity(Guid Id, string Name, ExecutorRole Role);

public static class NodeIdentity
{
    /// <summary>
    /// Resolves this executor's node identity. <c>Mone:Node:Id</c> / <c>Mone:Node:Name</c> override
    /// the defaults; when absent, a deterministic GUID derived from the machine name + role keeps the
    /// identity stable across restarts with zero configuration.
    /// </summary>
    public static ResolvedNodeIdentity Resolve(IConfiguration config, ExecutorRole role)
    {
        var machineName = Environment.MachineName;

        var id = Guid.TryParse(config["Mone:Node:Id"], out var parsed)
            ? parsed
            : Deterministic(machineName, role);

        var name = config["Mone:Node:Name"];
        if (string.IsNullOrWhiteSpace(name))
            name = $"{machineName}-{role.ToString().ToLowerInvariant()}";

        return new ResolvedNodeIdentity(id, name, role);
    }

    public static Guid Deterministic(string machineName, ExecutorRole role)
    {
        var input = $"mone-node|{machineName}|{(int)role}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
}
