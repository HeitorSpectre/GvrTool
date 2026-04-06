using GvrTool.Gvr.ImageDataFormats;
using GvrTool.Gvr.PaletteDataFormats;
using System;
using System.Collections.Generic;
using System.IO;
using TGASharpLib;

namespace GvrTool.Gvr
{
    public class GVR
    {
        public ushort Width { get; private set; }
        public ushort Height { get; private set; }

        public uint GlobalIndex { get; private set; }
        public uint Unknown1 { get; private set; }

        public GvrPixelFormat PixelFormat { get; private set; }
        public GvrDataFlags DataFlags { get; private set; }
        public GvrDataFormat DataFormat { get; private set; }

        public GvrPixelFormat PalettePixelFormat { get; private set; }
        public ushort PaletteEntryCount { get; private set; }

        public byte ExternalPaletteUnknown1 { get; private set; }
        public ushort ExternalPaletteUnknown2 { get; private set; }
        public ushort ExternalPaletteUnknown3 { get; private set; }

        public byte[] Pixels { get; private set; }
        public byte[] Palette { get; private set; }

        // Helpers
        public bool HasExternalPalette => (DataFlags & GvrDataFlags.ExternalPalette) != 0;
        public bool HasInternalPalette => (DataFlags & GvrDataFlags.InternalPalette) != 0;
        public bool HasPalette => (DataFlags & GvrDataFlags.Palette) != 0;
        public bool HasMipmaps => (DataFlags & GvrDataFlags.Mipmaps) != 0;

        bool isLoaded;

        const uint GCIX_MAGIC = 0x58494347;
        const uint GVRT_MAGIC = 0x54525647;
        const uint GVPL_MAGIC = 0x4c505647;

        const bool BIG_ENDIAN = true;
        const byte TGA_COLOR_MAP_TYPE = 0x01;
        const byte TGA_IMAGE_TYPE_UNCOMPRESSED_COLOR_MAPPED = 0x01;
        const byte TGA_IMAGE_DESCRIPTOR_TOP_LEFT = 0x20;
        const byte TGA_IMAGE_DESCRIPTOR_BOTTOM_LEFT = 0x00;

        public GVR()
        {
            isLoaded = false;
        }

        public void LoadFromGvrFile(string gvrPath)
        {
            if (string.IsNullOrWhiteSpace(gvrPath))
            {
                throw new ArgumentNullException(nameof(gvrPath));
            }

            if (!File.Exists(gvrPath))
            {
                throw new FileNotFoundException($"GVR file has not been found: {gvrPath}.");
            }

            using (FileStream fs = File.OpenRead(gvrPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                uint gcixMagic = br.ReadUInt32();
                if (gcixMagic != GCIX_MAGIC)
                {
                    throw new InvalidDataException($"\"{gvrPath}\" is not a valid GCIX/GVRT file.");
                }

                fs.Position = 0x10;

                uint gvrtMagic = br.ReadUInt32();
                if (gvrtMagic != GVRT_MAGIC)
                {
                    throw new InvalidDataException($"\"{gvrPath}\" is not a valid GCIX/GVRT file.");
                }

                fs.Position = 0x8;
                GlobalIndex = br.ReadUInt32Endian(BIG_ENDIAN);
                Unknown1 = br.ReadUInt32Endian(BIG_ENDIAN);

                fs.Position = 0x1A;
                byte pixelFormatAndFlags = br.ReadByte();
                PixelFormat = (GvrPixelFormat)(pixelFormatAndFlags >> 4);
                DataFlags = (GvrDataFlags)(pixelFormatAndFlags & 0x0F);
                DataFormat = (GvrDataFormat)br.ReadByte();
                Width = br.ReadUInt16Endian(BIG_ENDIAN);
                Height = br.ReadUInt16Endian(BIG_ENDIAN);

                if (HasMipmaps)
                {
                    throw new NotImplementedException($"Textures with mip maps are not supported.");
                }

                GvrImageDataFormat format = GvrImageDataFormat.Get(Width, Height, DataFormat);
                Pixels = format.Decode(fs);
            }

            if (HasExternalPalette)
            {
                string gvpPath = Path.ChangeExtension(gvrPath, ".gvp");

                if (!File.Exists(gvpPath))
                {
                    throw new FileNotFoundException($"External GVP palette has not been found: {gvpPath}.");
                }

                using (FileStream fs = File.OpenRead(gvpPath))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    uint gvplMagic = br.ReadUInt32();
                    if (gvplMagic != GVPL_MAGIC)
                    {
                        throw new InvalidDataException($"\"{gvpPath}\" is not a valid GVPL file.");
                    }

                    fs.Position = 0x8;
                    ExternalPaletteUnknown1 = br.ReadByte();
                    PalettePixelFormat = (GvrPixelFormat)br.ReadByte();
                    ExternalPaletteUnknown2 = br.ReadUInt16Endian(BIG_ENDIAN);
                    ExternalPaletteUnknown3 = br.ReadUInt16Endian(BIG_ENDIAN);
                    PaletteEntryCount = br.ReadUInt16Endian(BIG_ENDIAN);

                    GvrPaletteDataFormat format = GvrPaletteDataFormat.Get(PaletteEntryCount, PalettePixelFormat);
                    Palette = format.Decode(fs);
                }
            }

            isLoaded = true;
        }

        public void LoadFromTgaFile(string tgaFilePath)
        {
            if (string.IsNullOrWhiteSpace(tgaFilePath))
            {
                throw new ArgumentNullException(nameof(tgaFilePath));
            }

            if (!File.Exists(tgaFilePath))
            {
                throw new FileNotFoundException($"\"{tgaFilePath}\" TGA file has not been found.");
            }

            if (!Path.GetExtension(tgaFilePath).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{tgaFilePath} is not a valid TGA file.");
            }

            GVRMetadata metadata = GVRMetadata.LoadMetadataFromJson(Path.ChangeExtension(tgaFilePath, ".json"));

            GlobalIndex = metadata.GlobalIndex;
            Unknown1 = metadata.Unknown1;
            PixelFormat = metadata.PixelFormat;
            DataFlags = metadata.DataFlags;
            DataFormat = metadata.DataFormat;
            PalettePixelFormat = metadata.PalettePixelFormat;
            PaletteEntryCount = metadata.PaletteEntryCount;
            ExternalPaletteUnknown1 = metadata.ExternalPaletteUnknown1;
            ExternalPaletteUnknown2 = metadata.ExternalPaletteUnknown2;
            ExternalPaletteUnknown3 = metadata.ExternalPaletteUnknown3;
            Palette = metadata.ReferencePalette;

            if (HasPalette && TryLoadColorMappedTgaFile(tgaFilePath, out ushort width, out ushort height, out byte[] pixels, out byte[] palette))
            {
                Width = width;
                Height = height;
                Pixels = pixels;
                Palette = palette;
            }
            else
            {
                TGA tga = new TGA(tgaFilePath);

                Width = tga.Width;
                Height = tga.Height;

                switch (tga.Header.ImageSpec.ImageDescriptor.ImageOrigin)
                {
                    case TgaImgOrigin.TopLeft:

                        Pixels = tga.ImageOrColorMapArea.ImageData;
                        break;

                    case TgaImgOrigin.BottomLeft:

                        Pixels = Utils.FlipImageY(tga.ImageOrColorMapArea.ImageData, Width, Height, (byte)tga.Header.ImageSpec.PixelDepth >> 3);
                        break;

                    default:

                        throw new NotImplementedException($"TGA file ImageOrigin mode not supported: {tga.Header.ImageSpec.ImageDescriptor.ImageOrigin}.");
                }

                if (HasExternalPalette)
                {
                    Palette = tga.ImageOrColorMapArea.ColorMapData;
                }
            }

            if (HasPalette && Palette == null && TryLoadSiblingPalette(tgaFilePath, out byte[] siblingPalette))
            {
                Palette = siblingPalette;
            }

            if (HasPalette && Palette == null)
            {
                ConvertTrueColorPixelsToIndexed();
            }
            else if (HasPalette && Pixels != null && Pixels.Length != Width * Height)
            {
                ConvertTrueColorPixelsToIndexed();
            }

            isLoaded = true;
        }

        public void SaveToTgaFile(string tgaFilePath)
        {
            if (!isLoaded)
            {
                throw new Exception($"GVR was not successfully initialized. Cannot proceed.");
            }

            if (string.IsNullOrWhiteSpace(tgaFilePath))
            {
                throw new ArgumentNullException(nameof(tgaFilePath));
            }

            if (HasPalette)
            {
                SaveColorMappedTgaFile(tgaFilePath);
            }
            else
            {
                GvrImageDataFormat imageFormat = GvrImageDataFormat.Get(Width, Height, DataFormat);

                TGA tga = new TGA(Width, Height, imageFormat.TgaPixelDepth, imageFormat.TgaImageType);
                tga.Header.ImageSpec.ImageDescriptor.ImageOrigin = TgaImgOrigin.TopLeft;
                tga.Header.ImageSpec.ImageDescriptor.AlphaChannelBits = imageFormat.TgaAlphaChannelBits;
                tga.Header.ImageSpec.Y_Origin = Height;
                tga.ImageOrColorMapArea.ImageData = Pixels;
                tga.Save(tgaFilePath);
            }

            GVRMetadata metadata = new GVRMetadata()
            {
                GlobalIndex = GlobalIndex,
                Unknown1 = Unknown1,
                PixelFormat = PixelFormat,
                DataFlags = DataFlags,
                DataFormat = DataFormat,
                PalettePixelFormat = PalettePixelFormat,
                PaletteEntryCount = PaletteEntryCount,
                ExternalPaletteUnknown1 = ExternalPaletteUnknown1,
                ExternalPaletteUnknown2 = ExternalPaletteUnknown2,
                ExternalPaletteUnknown3 = ExternalPaletteUnknown3,
                ReferencePalette = HasPalette ? (byte[])Palette?.Clone() : null
            };

            GVRMetadata.SaveMetadataToJson(metadata, Path.ChangeExtension(tgaFilePath, ".json"));
        }

        public void SaveToGvrFile(string gvrFilePath)
        {
            if (!isLoaded)
            {
                throw new Exception($"GVR was not successfully initialized. Cannot proceed.");
            }

            if (string.IsNullOrWhiteSpace(gvrFilePath))
            {
                throw new ArgumentNullException(nameof(gvrFilePath));
            }

            GvrImageDataFormat format = GvrImageDataFormat.Get(Width, Height, DataFormat);

            using (FileStream fs = File.Create(gvrFilePath))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(GCIX_MAGIC);
                bw.Write((uint)0x8); // GCIX data size
                bw.WriteEndian(GlobalIndex, BIG_ENDIAN);
                bw.WriteEndian(Unknown1, BIG_ENDIAN);

                bw.Write(GVRT_MAGIC);
                bw.Write(format.EncodedDataLength + 8);
                bw.WriteEndian((ushort)0x0, BIG_ENDIAN); //TODO: ???
                bw.Write((byte)(((byte)PixelFormat << 4) | ((byte)DataFlags & 0xF)));
                bw.Write((byte)DataFormat);
                bw.WriteEndian(Width, BIG_ENDIAN);
                bw.WriteEndian(Height, BIG_ENDIAN);

                byte[] gvrtPixels = format.Encode(Pixels);
                fs.Write(gvrtPixels, 0, gvrtPixels.Length);
            }

            if (HasExternalPalette)
            {
                string gvpFilePath = Path.ChangeExtension(gvrFilePath, ".gvp");

                GvrPaletteDataFormat paletteFormat = GvrPaletteDataFormat.Get(PaletteEntryCount, PalettePixelFormat);

                using (FileStream fs = File.Create(gvpFilePath))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(GVPL_MAGIC);
                    bw.Write(paletteFormat.EncodedDataLength + 8);
                    bw.Write(ExternalPaletteUnknown1);
                    bw.Write((byte)PalettePixelFormat);
                    bw.WriteEndian(ExternalPaletteUnknown2, BIG_ENDIAN);
                    bw.WriteEndian(ExternalPaletteUnknown3, BIG_ENDIAN);
                    bw.WriteEndian(PaletteEntryCount, BIG_ENDIAN);

                    byte[] palette = paletteFormat.Encode(Palette);
                    fs.Write(palette, 0, palette.Length);
                }
            }
        }

        void SaveColorMappedTgaFile(string tgaFilePath)
        {
            GvrPaletteDataFormat paletteFormat = GvrPaletteDataFormat.Get(PaletteEntryCount, PalettePixelFormat);
            byte tgaPaletteEntrySize = (byte)paletteFormat.TgaColorMapEntrySize;
            byte alphaBits = tgaPaletteEntrySize == (byte)TgaColorMapEntrySize.A8R8G8B8 ? (byte)8 : (byte)0;

            using (FileStream fs = File.Create(tgaFilePath))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((byte)0x00); // ID length
                bw.Write(TGA_COLOR_MAP_TYPE);
                bw.Write(TGA_IMAGE_TYPE_UNCOMPRESSED_COLOR_MAPPED);
                bw.Write((ushort)0x0000); // First color map entry
                bw.Write(PaletteEntryCount);
                bw.Write(tgaPaletteEntrySize);
                bw.Write((ushort)0x0000); // X origin
                bw.Write((ushort)0x0000); // Y origin
                bw.Write(Width);
                bw.Write(Height);
                bw.Write((byte)0x08); // Indexed pixels
                bw.Write((byte)(TGA_IMAGE_DESCRIPTOR_TOP_LEFT | alphaBits));
                bw.Write(Palette);
                bw.Write(Pixels);
            }
        }

        bool TryLoadColorMappedTgaFile(string tgaFilePath, out ushort width, out ushort height, out byte[] pixels, out byte[] palette)
        {
            width = 0;
            height = 0;
            pixels = null;
            palette = null;

            using (FileStream fs = File.OpenRead(tgaFilePath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                if (fs.Length < 18)
                {
                    return false;
                }

                byte idLength = br.ReadByte();
                byte colorMapType = br.ReadByte();
                byte imageType = br.ReadByte();
                ushort firstEntryIndex = br.ReadUInt16();
                ushort colorMapLength = br.ReadUInt16();
                byte colorMapEntrySize = br.ReadByte();
                br.ReadUInt16(); // X origin
                br.ReadUInt16(); // Y origin
                width = br.ReadUInt16();
                height = br.ReadUInt16();
                byte pixelDepth = br.ReadByte();
                byte imageDescriptor = br.ReadByte();

                if (colorMapType != TGA_COLOR_MAP_TYPE ||
                    imageType != TGA_IMAGE_TYPE_UNCOMPRESSED_COLOR_MAPPED ||
                    pixelDepth != 8)
                {
                    return false;
                }

                if (firstEntryIndex != 0)
                {
                    throw new NotImplementedException($"TGA color maps with a non-zero first entry index are not supported.");
                }

                GvrPaletteDataFormat paletteFormat = GvrPaletteDataFormat.Get(PaletteEntryCount, PalettePixelFormat);
                byte expectedColorMapEntrySize = (byte)paletteFormat.TgaColorMapEntrySize;
                if (colorMapEntrySize != expectedColorMapEntrySize)
                {
                    throw new InvalidDataException($"TGA file ColorMapEntrySize is {colorMapEntrySize} but {expectedColorMapEntrySize} is expected.");
                }

                if (colorMapLength != PaletteEntryCount)
                {
                    throw new InvalidDataException($"TGA file ColorMapLength is {colorMapLength} but {PaletteEntryCount} is expected.");
                }

                fs.Position += idLength;

                palette = br.ReadBytes((int)paletteFormat.DecodedDataLength);
                if (palette.Length != paletteFormat.DecodedDataLength)
                {
                    throw new EndOfStreamException($"TGA palette data is truncated.");
                }

                pixels = br.ReadBytes(width * height);
                if (pixels.Length != width * height)
                {
                    throw new EndOfStreamException($"TGA pixel data is truncated.");
                }

                byte imageOrigin = (byte)(imageDescriptor & 0x30);
                if (imageOrigin == TGA_IMAGE_DESCRIPTOR_BOTTOM_LEFT)
                {
                    pixels = Utils.FlipImageY(pixels, width, height, 1);
                }
                else if (imageOrigin != TGA_IMAGE_DESCRIPTOR_TOP_LEFT)
                {
                    throw new NotImplementedException($"TGA file ImageOrigin mode not supported: 0x{imageOrigin:X2}.");
                }

                return true;
            }
        }

        bool TryLoadSiblingPalette(string tgaFilePath, out byte[] palette)
        {
            palette = null;

            string gvpPath = Path.ChangeExtension(tgaFilePath, ".gvp");
            if (!File.Exists(gvpPath))
            {
                return false;
            }

            using (FileStream fs = File.OpenRead(gvpPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                uint gvplMagic = br.ReadUInt32();
                if (gvplMagic != GVPL_MAGIC)
                {
                    return false;
                }

                fs.Position = 0x8;
                br.ReadByte();
                br.ReadByte();
                br.ReadUInt16Endian(BIG_ENDIAN);
                br.ReadUInt16Endian(BIG_ENDIAN);
                ushort paletteEntryCount = br.ReadUInt16Endian(BIG_ENDIAN);

                if (paletteEntryCount != PaletteEntryCount)
                {
                    return false;
                }

                GvrPaletteDataFormat format = GvrPaletteDataFormat.Get(PaletteEntryCount, PalettePixelFormat);
                palette = format.Decode(fs);
                return palette != null && palette.Length == format.DecodedDataLength;
            }
        }

        void ConvertTrueColorPixelsToIndexed()
        {
            if (Pixels == null)
            {
                throw new InvalidDataException($"TGA pixel data is missing.");
            }

            int totalPixels = Width * Height;
            if (totalPixels <= 0)
            {
                throw new InvalidDataException($"TGA dimensions are invalid.");
            }

            if (Pixels.Length % totalPixels != 0)
            {
                throw new InvalidDataException($"TGA pixel data size is not valid for {Width}x{Height}.");
            }

            int bytesPerPixel = Pixels.Length / totalPixels;
            if (bytesPerPixel != 3 && bytesPerPixel != 4)
            {
                throw new NotImplementedException($"TGA true-color pixel depth not supported for palettized textures: {bytesPerPixel * 8} bpp.");
            }

            GvrPaletteDataFormat paletteFormat = GvrPaletteDataFormat.Get(PaletteEntryCount, PalettePixelFormat);
            int paletteBytesPerEntry = (int)(paletteFormat.DecodedDataLength / PaletteEntryCount);

            Dictionary<uint, int> histogram = new Dictionary<uint, int>();
            uint[] sourceColors = new uint[totalPixels];

            for (int pixelIndex = 0; pixelIndex < totalPixels; pixelIndex++)
            {
                int offset = pixelIndex * bytesPerPixel;
                byte r = Pixels[offset + 0];
                byte g = Pixels[offset + 1];
                byte b = Pixels[offset + 2];
                byte a = bytesPerPixel == 4 ? Pixels[offset + 3] : (byte)255;
                uint colorKey = (uint)(r | (g << 8) | (b << 16) | (a << 24));

                sourceColors[pixelIndex] = colorKey;
                histogram[colorKey] = histogram.TryGetValue(colorKey, out int count) ? count + 1 : 1;
            }

            List<uint> paletteColors = new List<uint>(PaletteEntryCount);
            byte[] paletteBytes = Palette;

            if (paletteBytes != null && paletteBytes.Length == paletteFormat.DecodedDataLength)
            {
                for (int offset = 0; offset < paletteBytes.Length; offset += paletteBytesPerEntry)
                {
                    byte r = paletteBytes[offset + 0];
                    byte g = paletteBytes[offset + 1];
                    byte b = paletteBytes[offset + 2];
                    byte a = paletteBytesPerEntry == 4 ? paletteBytes[offset + 3] : (byte)255;
                    paletteColors.Add((uint)(r | (g << 8) | (b << 16) | (a << 24)));
                }
            }
            else
            {
                paletteBytes = BuildPaletteFromHistogram(histogram, sourceColors, PaletteEntryCount, (int)paletteFormat.DecodedDataLength, paletteBytesPerEntry, out paletteColors);
            }

            Dictionary<uint, byte> paletteLookup = new Dictionary<uint, byte>();
            for (int i = 0; i < paletteColors.Count; i++)
            {
                paletteLookup[paletteColors[i]] = (byte)i;
            }

            byte[] indexedPixels = new byte[totalPixels];
            for (int pixelIndex = 0; pixelIndex < totalPixels; pixelIndex++)
            {
                uint color = sourceColors[pixelIndex];
                if (!paletteLookup.TryGetValue(color, out byte paletteIndex))
                {
                    paletteIndex = FindNearestPaletteIndex(color, paletteColors);
                }

                indexedPixels[pixelIndex] = paletteIndex;
            }

            Pixels = indexedPixels;
            Palette = paletteBytes;
        }

        static byte[] BuildPaletteFromHistogram(Dictionary<uint, int> histogram, uint[] sourceColors, int paletteEntryCount, int decodedPaletteLength, int paletteBytesPerEntry, out List<uint> paletteColors)
        {
            paletteColors = new List<uint>(paletteEntryCount);
            if (histogram.Count <= paletteEntryCount)
            {
                foreach (uint color in sourceColors)
                {
                    if (!paletteColors.Contains(color))
                    {
                        paletteColors.Add(color);
                    }
                }
            }
            else
            {
                List<KeyValuePair<uint, int>> rankedColors = new List<KeyValuePair<uint, int>>(histogram);
                rankedColors.Sort((left, right) =>
                {
                    byte leftAlpha = (byte)(left.Key >> 24);
                    byte rightAlpha = (byte)(right.Key >> 24);

                    bool leftTransparent = leftAlpha == 0;
                    bool rightTransparent = rightAlpha == 0;
                    if (leftTransparent != rightTransparent)
                    {
                        return leftTransparent ? -1 : 1;
                    }

                    int frequency = right.Value.CompareTo(left.Value);
                    if (frequency != 0)
                    {
                        return frequency;
                    }

                    return left.Key.CompareTo(right.Key);
                });

                for (int i = 0; i < paletteEntryCount && i < rankedColors.Count; i++)
                {
                    paletteColors.Add(rankedColors[i].Key);
                }
            }

            List<byte> paletteBytes = new List<byte>((int)decodedPaletteLength);
            for (int i = 0; i < paletteColors.Count; i++)
            {
                uint color = paletteColors[i];
                paletteBytes.Add((byte)(color & 0xFF));
                paletteBytes.Add((byte)((color >> 8) & 0xFF));
                paletteBytes.Add((byte)((color >> 16) & 0xFF));

                if (paletteBytesPerEntry == 4)
                {
                    paletteBytes.Add((byte)((color >> 24) & 0xFF));
                }
            }

            while (paletteBytes.Count < decodedPaletteLength)
            {
                paletteBytes.Add(0);
            }

            return paletteBytes.ToArray();
        }

        static byte FindNearestPaletteIndex(uint color, List<uint> paletteColors)
        {
            byte bestIndex = 0;
            long bestDistance = long.MaxValue;

            byte r = (byte)(color & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)((color >> 16) & 0xFF);
            byte a = (byte)((color >> 24) & 0xFF);

            for (byte i = 0; i < paletteColors.Count; i++)
            {
                uint candidate = paletteColors[i];
                int dr = r - (byte)(candidate & 0xFF);
                int dg = g - (byte)((candidate >> 8) & 0xFF);
                int db = b - (byte)((candidate >> 16) & 0xFF);
                int da = a - (byte)((candidate >> 24) & 0xFF);

                long distance = (dr * dr) + (dg * dg) + (db * db) + (da * da * 2L);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
    }
}
