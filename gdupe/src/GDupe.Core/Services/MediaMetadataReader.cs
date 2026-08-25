using System.Buffers.Binary;
using GDupe.Core.Abstractions;
using GDupe.Core.Models;

namespace GDupe.Core.Services;

public sealed class MediaMetadataReader : IMediaMetadataReader
{
    public Task<MediaMetadata> ReadAsync(string path, MediaKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => kind == MediaKind.Image ? ReadImage(path) : ReadVideo(path), cancellationToken);
    }

    private static MediaMetadata ReadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[32];
        var read = stream.Read(header);

        if (read >= 24 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return new(BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]), null);
        }

        if (read >= 10 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
        {
            return new(BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]), null);
        }

        if (read >= 26 && header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            return new(BinaryPrimitives.ReadInt32LittleEndian(header[18..22]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(header[22..26])), null);
        }

        stream.Position = 0;
        return TryReadJpeg(stream) ?? new(null, null, null);
    }

    private static MediaMetadata? TryReadJpeg(Stream stream)
    {
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
        {
            return null;
        }

        Span<byte> lengthBytes = stackalloc byte[2];
        Span<byte> dimensions = stackalloc byte[5];
        while (stream.Position + 4 < stream.Length)
        {
            if (stream.ReadByte() != 0xFF)
            {
                continue;
            }

            int marker;
            do { marker = stream.ReadByte(); } while (marker == 0xFF);
            if (marker < 0 || marker is 0xD8 or 0xD9)
            {
                continue;
            }

            if (stream.Read(lengthBytes) != 2)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (stream.Read(dimensions) != 5)
                {
                    return null;
                }

                return new(BinaryPrimitives.ReadUInt16BigEndian(dimensions[3..5]), BinaryPrimitives.ReadUInt16BigEndian(dimensions[1..3]), null);
            }

            stream.Seek(length - 2, SeekOrigin.Current);
        }

        return null;
    }

    private static MediaMetadata ReadVideo(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase))
        {
            return ReadIsoBaseMedia(path);
        }

        return new(null, null, null);
    }

    private static MediaMetadata ReadIsoBaseMedia(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int? width = null;
        int? height = null;
        long? durationMs = null;
        ParseBoxes(stream, stream.Length, ref width, ref height, ref durationMs, 0);
        return new(width, height, durationMs);
    }

    private static void ParseBoxes(Stream stream, long end, ref int? width, ref int? height, ref long? durationMs, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        Span<byte> header = stackalloc byte[16];
        Span<byte> movieHeader = stackalloc byte[32];
        while (stream.Position + 8 <= end)
        {
            var start = stream.Position;
            if (stream.Read(header[..8]) != 8)
            {
                return;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            var type = System.Text.Encoding.ASCII.GetString(header[4..8]);
            var headerSize = 8;
            if (size == 1)
            {
                if (stream.Read(header[..8]) != 8) return;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[..8]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = end - start;
            }

            if (size < headerSize || start + size > end)
            {
                return;
            }

            var boxEnd = start + size;
            if (type == "mvhd" && size >= headerSize + 20)
            {
                var needed = (int)Math.Min(movieHeader.Length, boxEnd - stream.Position);
                if (stream.Read(movieHeader[..needed]) == needed)
                {
                    var version = movieHeader[0];
                    if (version == 0 && needed >= 20)
                    {
                        var timescale = BinaryPrimitives.ReadUInt32BigEndian(movieHeader[12..16]);
                        var duration = BinaryPrimitives.ReadUInt32BigEndian(movieHeader[16..20]);
                        if (timescale > 0) durationMs = (long)Math.Round(duration * 1000d / timescale);
                    }
                    else if (version == 1 && needed >= 32)
                    {
                        var timescale = BinaryPrimitives.ReadUInt32BigEndian(movieHeader[20..24]);
                        var duration = BinaryPrimitives.ReadUInt64BigEndian(movieHeader[24..32]);
                        if (timescale > 0) durationMs = (long)Math.Round(duration * 1000d / timescale);
                    }
                }
            }
            else if (type == "tkhd" && size >= headerSize + 84)
            {
                var payloadLength = (int)Math.Min(100, boxEnd - stream.Position);
                var payload = new byte[payloadLength];
                if (stream.Read(payload) == payloadLength)
                {
                    var version = payload[0];
                    var offset = version == 1 ? 88 : 76;
                    if (payloadLength >= offset + 8)
                    {
                        var candidateWidth = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4)) >> 16);
                        var candidateHeight = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 4, 4)) >> 16);
                        if (candidateWidth > 0 && candidateHeight > 0)
                        {
                            width = Math.Max(width ?? 0, candidateWidth);
                            height = Math.Max(height ?? 0, candidateHeight);
                        }
                    }
                }
            }
            else if (type is "moov" or "trak" or "mdia" or "minf" or "stbl")
            {
                ParseBoxes(stream, boxEnd, ref width, ref height, ref durationMs, depth + 1);
            }

            stream.Position = boxEnd;
        }
    }
}
