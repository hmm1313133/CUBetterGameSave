using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace BetterGameSave.Core;

/// <summary>Handles JSON metadata and the checksummed binary provider container.</summary>
internal static class SaveSerializer
{
    private const int ProviderMagic = 0x42504731; // BPG1
    private const int ProviderFormatVersion = 1;
    private const int MaxProviderCount = 4096;
    private const int MaxProviderDataLength = 256 * 1024 * 1024;
    private static readonly JsonSerializerSettings _settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public static string Serialize(object data)
    {
        return JsonConvert.SerializeObject(data, _settings);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, _settings)!;
    }

    public static byte[] SerializeProviders(Dictionary<string, BinaryProviderData> providers)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(ProviderMagic);
            writer.Write(ProviderFormatVersion);
            writer.Write(providers.Count);
            foreach (var entry in providers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var data = entry.Value.Data ?? Array.Empty<byte>();
                writer.Write(entry.Key);
                writer.Write(entry.Value.DataVersion);
                writer.Write(data.Length);
                writer.Write(Hash(data));
                writer.Write(data);
            }
        }
        return output.ToArray();
    }

    public static Dictionary<string, BinaryProviderData> DeserializeProviders(byte[] container)
    {
        using var input = new MemoryStream(container, writable: false);
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: false);
        if (reader.ReadInt32() != ProviderMagic)
            throw new InvalidDataException("Unknown provider save format.");
        if (reader.ReadInt32() != ProviderFormatVersion)
            throw new InvalidDataException("Unsupported provider save version.");
        var count = reader.ReadInt32();
        if (count < 0 || count > MaxProviderCount)
            throw new InvalidDataException("Invalid provider count.");

        var result = new Dictionary<string, BinaryProviderData>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var modId = reader.ReadString();
            var dataVersion = reader.ReadInt32();
            var length = reader.ReadInt32();
            if (string.IsNullOrWhiteSpace(modId) || dataVersion < 1 || length < 0 || length > MaxProviderDataLength)
                throw new InvalidDataException("Invalid provider entry header.");
            var expectedHash = ReadExactly(reader, 32);
            var data = ReadExactly(reader, length);
            if (!Hash(data).SequenceEqual(expectedHash))
                continue;
            result[modId] = new BinaryProviderData { DataVersion = dataVersion, Data = data };
        }
        if (input.Position != input.Length)
            throw new InvalidDataException("Provider save contains trailing data.");
        return result;
    }

    private static byte[] Hash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    private static byte[] ReadExactly(BinaryReader reader, int length)
    {
        var data = reader.ReadBytes(length);
        if (data.Length != length)
            throw new EndOfStreamException("Provider save ended unexpectedly.");
        return data;
    }

    internal sealed class BinaryProviderData
    {
        public int DataVersion { get; set; }
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }

}
