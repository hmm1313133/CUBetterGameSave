using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterGameSave.Core;

internal static class WorldSnapshotStore
{
    private const int Magic = 0x42575331; // BWS1
    private const int FormatVersion = 1;
    private static readonly FieldInfo? WorldBlocksField = AccessTools.Field(typeof(WorldGeneration), "worldBlocks");

    internal static byte[]? Capture()
    {
        var world = WorldGeneration.world;
        var blocks = world == null ? null : WorldBlocksField?.GetValue(world) as ushort[,];
        if (world == null || blocks == null)
            return null;

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            var width = blocks.GetLength(0);
            var height = blocks.GetLength(1);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(width);
            writer.Write(height);
            writer.Write(world.biomeDepth);
            writer.Write(world.totalTraveled);
            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                writer.Write(blocks[x, y]);
        }
        return output.ToArray();
    }

    internal static bool Restore(byte[] data)
    {
        var world = WorldGeneration.world;
        var blocks = world == null ? null : WorldBlocksField?.GetValue(world) as ushort[,];
        if (world == null || blocks == null || data.Length == 0)
            return false;

        using var input = new MemoryStream(data, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (reader.ReadInt32() != Magic)
            throw new InvalidDataException("Unknown world snapshot format.");
        if (reader.ReadInt32() != FormatVersion)
            throw new InvalidDataException("Unsupported world snapshot version.");

        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        if (width <= 0 || height <= 0 || width != blocks.GetLength(0) || height != blocks.GetLength(1))
            throw new InvalidDataException($"World size mismatch: snapshot {width}x{height}, runtime {blocks.GetLength(0)}x{blocks.GetLength(1)}.");

        var biomeDepth = reader.ReadInt32();
        var totalTraveled = reader.ReadInt32();
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            blocks[x, y] = reader.ReadUInt16();
        if (gzip.ReadByte() != -1)
            throw new InvalidDataException("World snapshot contains trailing block data.");

        world.biomeDepth = biomeDepth;
        world.totalTraveled = totalTraveled;
        world.currentTempCurve = biomeDepth;
        world.UpdateBiomePostProcess();
        return true;
    }

    internal static IEnumerator RefreshChunks()
    {
        var world = WorldGeneration.world;
        if (world == null)
            yield break;

        var refreshed = 0;
        for (var x = 0; x < world.chunkWidth; x++)
        for (var y = 0; y < world.chunkHeight; y++)
        {
            world.UpdateChunk(new Vector2Int(x, y));
            if (++refreshed % 8 == 0)
                yield return null;
        }
    }
}
