using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PSDExtractor
{

    public static class PsdReader
    {

        public class PsdDocument
        {
            public int Width;
            public int Height;
            public List<PsdLayer> Layers = new();
        }

        public class PsdLayer
        {
            public string Name;
            public int Top, Left, Bottom, Right;
            public bool IsVisible = true;
            public bool IsGroup;
            public Color32[] Pixels;

            public int LayerWidth  => Right  - Left;
            public int LayerHeight => Bottom - Top;
        }
        

        public static PsdDocument Read(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            using var ms = new MemoryStream(data);
            using var r  = new BigEndianReader(ms);
            
            string sig = r.ReadAscii(4);
            if (sig != "8BPS") throw new Exception("Not a valid PSD file.");

            ushort version = r.ReadUInt16();
            if (version != 1) throw new Exception($"Only PSD version 1 is supported (got {version}).");

            r.Skip(6); 
            ushort channels  = r.ReadUInt16();
            int    docHeight = r.ReadInt32();
            int    docWidth  = r.ReadInt32();
            ushort depth     = r.ReadUInt16();
            ushort colorMode = r.ReadUInt16();

            if (depth != 8)        throw new Exception($"Only 8-bpc PSD files are supported (got {depth}).");
            if (colorMode != 3)    throw new Exception($"Only RGB colour mode is supported (mode {colorMode}).");
            
            uint colorDataLen = r.ReadUInt32();
            r.Skip((int)colorDataLen);
            
            uint imgResLen = r.ReadUInt32();
            r.Skip((int)imgResLen);
            
            uint layerMaskLen = r.ReadUInt32();
            long layerMaskEnd = r.Position + layerMaskLen;

            var doc = new PsdDocument { Width = docWidth, Height = docHeight };

            if (layerMaskLen > 0)
            {
                uint layerInfoLen = r.ReadUInt32();
                if ((layerInfoLen & 1) == 1) layerInfoLen++; 

                long layerInfoEnd = r.Position + layerInfoLen;

                if (layerInfoLen > 0)
                {
                    short layerCount = r.ReadInt16();
                    bool  hasMergedAlpha = layerCount < 0;
                    if (hasMergedAlpha) layerCount = (short)-layerCount;

                    var rawLayers = new List<RawLayer>();

                    for (int i = 0; i < layerCount; i++)
                    {
                        var raw = new RawLayer();
                        raw.Top     = r.ReadInt32();
                        raw.Left    = r.ReadInt32();
                        raw.Bottom  = r.ReadInt32();
                        raw.Right   = r.ReadInt32();

                        ushort numCh = r.ReadUInt16();
                        raw.Channels = new ChannelInfo[numCh];
                        for (int c = 0; c < numCh; c++)
                        {
                            raw.Channels[c].Id     = r.ReadInt16();
                            raw.Channels[c].Length = r.ReadUInt32();
                        }

                        string blendSig = r.ReadAscii(4);
                        if (blendSig != "8BIM") throw new Exception("Expected 8BIM in layer record.");

                        r.Skip(4); 
                        raw.Opacity  = r.ReadByte();
                        r.Skip(1);  
                        raw.Flags    = r.ReadByte();
                        r.Skip(1);  

                        uint extraLen = r.ReadUInt32();
                        long extraEnd = r.Position + extraLen;

                        
                        uint maskLen = r.ReadUInt32();
                        r.Skip((int)maskLen);

                        
                        uint blendRangeLen = r.ReadUInt32();
                        r.Skip((int)blendRangeLen);
                        
                        byte nameLen = r.ReadByte();
                        raw.Name = r.ReadAscii(nameLen);
                        int namePad = (4 - ((nameLen + 1) % 4)) % 4;
                        r.Skip(namePad);
                        
                        long addEnd = extraEnd;
                        while (r.Position < addEnd - 11)
                        {
                            string addSig = r.ReadAscii(4);
                            if (addSig != "8BIM" && addSig != "8B64") { r.Position = addEnd; break; }
                            string key    = r.ReadAscii(4);
                            uint   addLen = r.ReadUInt32();
                            if ((addLen & 1) == 1) addLen++;
                            long   addBlockEnd = r.Position + addLen;

                            if (key == "luni" && addLen >= 2)
                            {
                                uint unicodeLen = r.ReadUInt32();
                                var sb = new StringBuilder();
                                for (int u = 0; u < unicodeLen; u++)
                                    sb.Append((char)r.ReadUInt16());
                                raw.UnicodeName = sb.ToString();
                            }
                            else if (key == "lsct" && addLen >= 4)
                            {
                                uint sectionType = r.ReadUInt32();
                                
                                raw.SectionDividerType = (int)sectionType;
                            }

                            r.Position = addBlockEnd;
                        }

                        r.Position = extraEnd;
                        rawLayers.Add(raw);
                    }
                    
                    foreach (var raw in rawLayers)
                    {
                        int lw = raw.Right  - raw.Left;
                        int lh = raw.Bottom - raw.Top;

                        raw.ChannelData = new byte[raw.Channels.Length][];

                        for (int c = 0; c < raw.Channels.Length; c++)
                        {
                            long chStart = r.Position;
                            ushort compression = r.ReadUInt16();

                            if (lw == 0 || lh == 0)
                            {
                                
                                r.Position = chStart + raw.Channels[c].Length;
                                raw.ChannelData[c] = Array.Empty<byte>();
                                continue;
                            }

                            byte[] pixels = new byte[lw * lh];

                            if (compression == 0) 
                            {
                                int read = r.ReadBytes(pixels, 0, lw * lh);
                                if (read < lw * lh)
                                    Array.Clear(pixels, read, lw * lh - read);
                            }
                            else if (compression == 1) 
                            {
                                var rowBytes = new int[lh];
                                for (int row = 0; row < lh; row++)
                                    rowBytes[row] = r.ReadUInt16();

                                int dstOfs = 0;
                                for (int row = 0; row < lh; row++)
                                {
                                    byte[] rowData = r.ReadBytesExact(rowBytes[row]);
                                    DecompressPackBits(rowData, pixels, dstOfs, lw);
                                    dstOfs += lw;
                                }
                            }
                            else
                            {
                                r.Position = chStart + raw.Channels[c].Length;
                            }

                            raw.ChannelData[c] = pixels;
                        }

                        string layerName = string.IsNullOrEmpty(raw.UnicodeName) ? raw.Name : raw.UnicodeName;

                        bool isGroup    = (raw.SectionDividerType == 1 || raw.SectionDividerType == 2);
                        bool isSectionEnd = raw.SectionDividerType == 3;
                        bool isVisible  = (raw.Flags & 0x02) == 0;

                        if (isSectionEnd) continue; 

                        var layer = new PsdLayer
                        {
                            Name      = layerName,
                            Top       = raw.Top,
                            Left      = raw.Left,
                            Bottom    = raw.Bottom,
                            Right     = raw.Right,
                            IsVisible = isVisible,
                            IsGroup   = isGroup,
                        };

                        if (lw > 0 && lh > 0 && !isGroup)
                        {
                            layer.Pixels = BuildRgba(raw, lw, lh);
                        }
                        else
                        {
                            layer.Pixels = Array.Empty<Color32>();
                        }

                        doc.Layers.Add(layer);
                    }
                }

                r.Position = layerMaskEnd;
            }

            return doc;
        }
        

        static Color32[] BuildRgba(RawLayer raw, int w, int h)
        {
            byte[] chanR = null, chanG = null, chanB = null, chanA = null;

            for (int c = 0; c < raw.Channels.Length; c++)
            {
                switch (raw.Channels[c].Id)
                {
                    case  0: chanR = raw.ChannelData[c]; break;
                    case  1: chanG = raw.ChannelData[c]; break;
                    case  2: chanB = raw.ChannelData[c]; break;
                    case -1: chanA = raw.ChannelData[c]; break;
                }
            }

            var result = new Color32[w * h];
            for (int i = 0; i < w * h; i++)
            {
                byte r = (chanR != null && i < chanR.Length) ? chanR[i] : (byte)0;
                byte g = (chanG != null && i < chanG.Length) ? chanG[i] : (byte)0;
                byte b = (chanB != null && i < chanB.Length) ? chanB[i] : (byte)0;
                byte a = (chanA != null && i < chanA.Length) ? chanA[i] : (byte)raw.Opacity;
                result[i] = new Color32(r, g, b, a);
            }
            return result;
        }

        static void DecompressPackBits(byte[] src, byte[] dst, int dstOfs, int rowWidth)
        {
            int srcOfs = 0;
            int written = 0;
            while (srcOfs < src.Length && written < rowWidth)
            {
                sbyte header = (sbyte)src[srcOfs++];
                if (header >= 0)
                {
                    int count = header + 1;
                    for (int i = 0; i < count && written < rowWidth; i++, written++)
                        dst[dstOfs + written] = src[srcOfs++];
                }
                else if (header != -128)
                {
                    int count = -header + 1;
                    byte val = src[srcOfs++];
                    for (int i = 0; i < count && written < rowWidth; i++, written++)
                        dst[dstOfs + written] = val;
                }
            }
        }
        

        class RawLayer
        {
            public int    Top, Left, Bottom, Right;
            public ChannelInfo[] Channels;
            public byte   Opacity = 255;
            public byte   Flags;
            public string Name = "";
            public string UnicodeName;
            public int    SectionDividerType; 
            public byte[][] ChannelData;
        }

        struct ChannelInfo
        {
            public short  Id;
            public uint   Length;
        }
    }
    

    internal class BigEndianReader : IDisposable
    {
        readonly Stream _s;
        readonly byte[] _buf = new byte[8];

        public BigEndianReader(Stream s) => _s = s;

        public long Position
        {
            get => _s.Position;
            set => _s.Position = value;
        }

        public void Skip(int n) => _s.Seek(n, SeekOrigin.Current);

        public byte ReadByte()
        {
            int b = _s.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            return (byte)b;
        }

        public short ReadInt16()
        {
            Fill(2);
            return (short)((_buf[0] << 8) | _buf[1]);
        }

        public ushort ReadUInt16()
        {
            Fill(2);
            return (ushort)((_buf[0] << 8) | _buf[1]);
        }

        public int ReadInt32()
        {
            Fill(4);
            return (_buf[0] << 24) | (_buf[1] << 16) | (_buf[2] << 8) | _buf[3];
        }

        public uint ReadUInt32()
        {
            Fill(4);
            return ((uint)_buf[0] << 24) | ((uint)_buf[1] << 16) | ((uint)_buf[2] << 8) | _buf[3];
        }

        public string ReadAscii(int count)
        {
            var b = new byte[count];
            int read = _s.Read(b, 0, count);
            return Encoding.ASCII.GetString(b, 0, read);
        }

        public int ReadBytes(byte[] dst, int offset, int count) => _s.Read(dst, offset, count);

        public byte[] ReadBytesExact(int count)
        {
            var b = new byte[count];
            int total = 0;
            while (total < count)
            {
                int read = _s.Read(b, total, count - total);
                if (read == 0) break;
                total += read;
            }
            return b;
        }

        void Fill(int count) => _s.Read(_buf, 0, count);

        public void Dispose() { }
    }
}
