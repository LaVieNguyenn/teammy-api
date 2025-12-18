using System;
using System.Security.Cryptography;
using System.Text;

namespace Teammy.Infrastructure.Ai;

public static class AiPointId
{
    public static string Stable(string type, Guid entityId)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Type is required.", nameof(type));

        var payload = Encoding.UTF8.GetBytes($"{type}:{entityId}");
        Span<byte> hash = stackalloc byte[16];
        using var md5 = MD5.Create();
        if (!md5.TryComputeHash(payload, hash, out _))
            throw new InvalidOperationException("Failed to compute point id hash.");

        return new Guid(hash).ToString("N");
    }
}
