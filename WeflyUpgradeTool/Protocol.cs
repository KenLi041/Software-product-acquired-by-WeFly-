using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeflyUpgradeTool
{
    public class DataFrame
    {
        public byte DataType { get; set; }      // 最高位 0=FPGA, 1=MCU；低7位=记录类型
        public ushort FrameNumber { get; set; } // 0..65535
        public uint Address { get; set; }       // 高16位=偏移地址，低16位=行地址
        public byte[] Data { get; set; } = Array.Empty<byte>(); // 1..256 字节
        public byte Length => (byte)(Data?.Length ?? 0);
    }

    public interface IRecordFileParser
    {
        List<DataFrame> ParseToFrames(string path, bool isFpga);
    }

    public sealed class McsParser : IRecordFileParser
    {
        public List<DataFrame> ParseToFrames(string path, bool isFpga)
        {
            return RecordParserCore.ParseIntelLikeFile(path, isFpga, hasStartAddressRecord: false);
        }
    }

    public sealed class HexParser : IRecordFileParser
    {
        public List<DataFrame> ParseToFrames(string path, bool isFpga)
        {
            return RecordParserCore.ParseIntelLikeFile(path, isFpga, hasStartAddressRecord: true);
        }
    }

    internal static class RecordParserCore
    {
        public static List<DataFrame> ParseIntelLikeFile(string path, bool isFpga, bool hasStartAddressRecord)
        {
            // 支持 .mcs 和 .hex 的 Intel HEX 风格解析：
            // 04 扩展线性地址记录 -> 更新高16位偏移
            // 00 数据记录 -> 生成帧，数据分片到 1..256 字节
            // 01 文件结束
            // 05 启动地址（仅 .hex）可忽略
            var frames = new List<DataFrame>();
            ushort frameNumber = 0;
            ushort highOffset = 0; // 高 16 位
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith(":")) continue; // 非法行跳过

                var bytes = HexToBytes(line.Substring(1));
                if (bytes.Length < 5) continue;

                int byteCount = bytes[0];
                ushort address = (ushort)((bytes[1] << 8) | bytes[2]);
                byte recordType = bytes[3];

                if (recordType == 0x04)
                {
                    // 扩展线性地址记录：2字节，表示高16位地址
                    if (byteCount >= 2)
                    {
                        highOffset = (ushort)((bytes[4] << 8) | bytes[5]);
                    }
                    continue;
                }
                if (recordType == 0x00)
                {
                    // 数据记录
                    var data = new byte[byteCount];
                    Array.Copy(bytes, 4, data, 0, byteCount);

                    // 优化：将数据切分为最大 256 字节片段，优先使用较大的块提高效率
                    int index = 0;
                    while (index < data.Length)
                    {
                        // 尽可能使用大的数据块(256字节)，减少总帧数
                        int chunk = Math.Min(256, data.Length - index);
                        var payload = new byte[chunk];
                        Array.Copy(data, index, payload, 0, chunk);

                        uint fullAddress = (uint)((highOffset << 16) | address);
                        byte dataType = (byte)(isFpga ? 0x00 : 0x80); // 最高位
                        dataType |= 0x00; // 低 7 位与文件记录类型一致，这里为 00

                        frames.Add(new DataFrame
                        {
                            DataType = dataType,
                            FrameNumber = frameNumber++,
                            Address = fullAddress,
                            Data = payload
                        });

                        index += chunk;
                        address = (ushort)(address + chunk);
                    }
                    continue;
                }
                if (recordType == 0x01)
                {
                    break; // 文件结束
                }
                if (hasStartAddressRecord && recordType == 0x05)
                {
                    // 启动地址记录，当前协议不要求发送，可忽略
                    continue;
                }
            }
            return frames;
        }

        private static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0) hex = hex.Substring(0, hex.Length - 1);
            var data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return data;
        }
    }
}


