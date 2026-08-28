using System.Buffers.Binary;

namespace Quizizzo.Web.Drawing;

public static class DrawingImageValidator
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPngWithDimensions(
        ReadOnlySpan<byte> content,
        int expectedWidth,
        int expectedHeight)
    {
        if (expectedWidth <= 0 || expectedHeight <= 0 ||
            content.Length < 45 || !content[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        var offset = 8;
        var sawHeader = false;
        var sawImageData = false;
        while (offset <= content.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset, 4));
            if (length > int.MaxValue || length > content.Length - offset - 12)
            {
                return false;
            }
            var dataLength = (int)length;
            var type = content.Slice(offset + 4, 4);
            var data = content.Slice(offset + 8, dataLength);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(offset + 8 + dataLength, 4));
            if (!IsChunkType(type) || ComputeCrc(type, data) != storedCrc)
            {
                return false;
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || offset != 8 || dataLength != 13 ||
                    !HasExpectedHeader(data, expectedWidth, expectedHeight))
                {
                    return false;
                }
                sawHeader = true;
            }
            else if (!sawHeader)
            {
                return false;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                sawImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                return dataLength == 0 && sawImageData && offset + 12 == content.Length;
            }
            else if (char.IsUpper((char)type[0]) && !type.SequenceEqual("PLTE"u8))
            {
                return false;
            }

            offset += 12 + dataLength;
        }
        return false;
    }

    private static bool HasExpectedHeader(
        ReadOnlySpan<byte> data,
        int expectedWidth,
        int expectedHeight)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
        var bitDepth = data[8];
        var colourType = data[9];
        var validDepth = colourType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 or 6 => bitDepth is 8 or 16,
            _ => false
        };
        return width == expectedWidth && height == expectedHeight && validDepth &&
            data[10] == 0 && data[11] == 0 && data[12] is 0 or 1;
    }

    private static bool IsChunkType(ReadOnlySpan<byte> type) =>
        type.Length == 4 && type.IndexOfAnyExceptInRange((byte)'A', (byte)'z') < 0 &&
        !type.Contains((byte)'[') && !type.Contains((byte)'\\') &&
        !type.Contains((byte)']') && !type.Contains((byte)'^') &&
        !type.Contains((byte)'_') && !type.Contains((byte)'`');

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = UpdateCrc(crc, value);
        }
        foreach (var value in data)
        {
            crc = UpdateCrc(crc, value);
        }
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }
}
