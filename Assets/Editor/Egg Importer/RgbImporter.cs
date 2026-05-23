using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using System.IO;
using POTCO.Editor;

[ScriptedImporter(1, "rgb")]
public class RgbImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        try
        {
            byte[] sgiData = File.ReadAllBytes(ctx.assetPath);
            
            if (sgiData == null || sgiData.Length < 512)
            {
                DebugLogger.LogWarningEggImporter($"Invalid SGI .rgb file: {ctx.assetPath}");
                return;
            }

            int width;
            int height;
            Color[] pixels;
            string decodeError;

            if (!TryDecodeSgi(sgiData, out width, out height, out pixels, out decodeError))
            {
                DebugLogger.LogWarningEggImporter($"{decodeError}: {ctx.assetPath}");
                return;
            }

            // Create texture  
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            texture.SetPixels(pixels);
            texture.Apply();
            
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear; // Use bilinear filtering for smooth alpha blending
            
            ctx.AddObjectToAsset("Texture", texture);
            ctx.SetMainObject(texture);
            
            DebugLogger.LogEggImporter($"Imported SGI texture: {ctx.assetPath} as {width}x{height}");
        }
        catch (System.Exception ex)
        {
            DebugLogger.LogErrorEggImporter($"Failed to import SGI file {ctx.assetPath}: {ex.Message}");
        }
    }

    public static bool TryDecodeSgi(byte[] data, out int width, out int height, out Color[] pixels, out string error)
    {
        width = 0;
        height = 0;
        pixels = null;
        error = null;

        if (data == null || data.Length < 512)
        {
            error = "Invalid SGI .rgb file";
            return false;
        }

        ushort magic = ReadU16(data, 0);
        if (magic != 0x01DA)
        {
            error = "Invalid SGI magic number";
            return false;
        }

        byte storage = data[2]; // 0 = uncompressed, 1 = RLE compressed
        byte bpc = data[3]; // bytes per channel (1 or 2)
        ushort dimension = ReadU16(data, 4);
        width = ReadU16(data, 6);
        height = ReadU16(data, 8);
        ushort channels = ReadU16(data, 10);

        DebugLogger.LogEggImporter($"SGI texture: {width}x{height}, {channels} channels, storage={storage}, bpc={bpc}");

        if ((bpc != 1 && bpc != 2) || dimension < 2 || width == 0 || height == 0 || channels == 0)
        {
            error = "Unsupported SGI format";
            return false;
        }

        pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0, 0, 0, 1);
        }

        if (storage == 0)
        {
            return DecodeSgiUncompressed(data, pixels, width, height, channels, bpc, out error);
        }

        if (storage == 1)
        {
            return DecodeSgiRLE(data, pixels, width, height, channels, bpc, out error);
        }

        error = "Unsupported SGI storage mode";
        return false;
    }

    private static bool DecodeSgiUncompressed(byte[] data, Color[] pixels, int width, int height, int channels, int bpc, out string error)
    {
        error = null;
        int channelCount = Mathf.Min(channels, 4);
        int bytesPerRow = width * bpc;

        for (int channel = 0; channel < channelCount; channel++)
        {
            for (int y = 0; y < height; y++)
            {
                int rowOffset = 512 + ((channel * height + y) * bytesPerRow);
                if (rowOffset + bytesPerRow > data.Length)
                {
                    error = "Truncated SGI pixel data";
                    return false;
                }

                int targetY = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int sampleOffset = rowOffset + (x * bpc);
                    int pixelIndex = targetY * width + x;
                    SetChannelValue(ref pixels[pixelIndex], channel, ReadSample(data, sampleOffset, bpc), channels);
                }
            }
        }

        return true;
    }

    private static bool DecodeSgiRLE(byte[] data, Color[] pixels, int width, int height, int channels, int bpc, out string error)
    {
        error = null;
        int tableLength = height * channels;
        int startTableOffset = 512;
        int lengthTableOffset = startTableOffset + tableLength * 4;
        int pixelDataOffset = lengthTableOffset + tableLength * 4;

        if (pixelDataOffset > data.Length)
        {
            error = "Truncated SGI RLE tables";
            return false;
        }

        uint[] startTable = new uint[tableLength];
        uint[] lengthTable = new uint[tableLength];

        for (int i = 0; i < tableLength; i++)
        {
            startTable[i] = ReadU32(data, startTableOffset + i * 4);
            lengthTable[i] = ReadU32(data, lengthTableOffset + i * 4);
        }

        int channelCount = Mathf.Min(channels, 4);
        for (int channel = 0; channel < channelCount; channel++)
        {
            for (int y = 0; y < height; y++)
            {
                int rowIndex = channel * height + y;
                int rowStart = (int)startTable[rowIndex];
                int rowEnd = rowStart + (int)lengthTable[rowIndex];

                if (rowStart < 0 || rowStart >= data.Length)
                {
                    error = "Invalid SGI RLE row offset";
                    return false;
                }

                if (rowEnd <= rowStart || rowEnd > data.Length)
                {
                    rowEnd = data.Length;
                }

                DecodeRLERow(data, pixels, rowStart, rowEnd, width, height - 1 - y, channel, channels, bpc);
            }
        }

        return true;
    }

    private static void DecodeRLERow(byte[] data, Color[] pixels, int offset, int endOffset, int width, int y, int channel, int totalChannels, int bpc)
    {
        int x = 0;
        while (x < width && offset < endOffset)
        {
            byte countByte = data[offset++];
            if (countByte == 0)
            {
                break;
            }

            int count = countByte & 0x7F;
            if ((countByte & 0x80) != 0)
            {
                for (int i = 0; i < count && x < width && offset + bpc <= endOffset; i++, x++)
                {
                    int pixelIndex = y * width + x;
                    SetChannelValue(ref pixels[pixelIndex], channel, ReadSample(data, offset, bpc), totalChannels);
                    offset += bpc;
                }
            }
            else
            {
                if (offset + bpc > endOffset) break;
                float value = ReadSample(data, offset, bpc);
                offset += bpc;

                for (int i = 0; i < count && x < width; i++, x++)
                {
                    int pixelIndex = y * width + x;
                    SetChannelValue(ref pixels[pixelIndex], channel, value, totalChannels);
                }
            }
        }
    }

    private static void SetChannelValue(ref Color pixel, int channel, float value, int totalChannels)
    {
        if (channel == 0 && totalChannels < 3)
        {
            pixel.r = value;
            pixel.g = value; 
            pixel.b = value;
            return;
        }

        if (totalChannels == 2 && channel == 1)
        {
            pixel.a = value;
            return;
        }

        switch (channel)
        {
            case 0: pixel.r = value; break;
            case 1: pixel.g = value; break;
            case 2: pixel.b = value; break;
            case 3: pixel.a = value; break;
        }
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) |
                      (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) |
                      data[offset + 3]);
    }

    private static float ReadSample(byte[] data, int offset, int bpc)
    {
        if (bpc == 1)
        {
            return data[offset] / 255.0f;
        }

        ushort value = ReadU16(data, offset);
        return value / 65535.0f;
    }
}
