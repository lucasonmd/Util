using MessagePack;
using RuntimeMonitor.Core.DTOs;
using System.Runtime.InteropServices;

namespace RuntimeMonitor.Core.Serialization;

/// <summary>
/// MessagePack 기반 패킷 직렬화/역직렬화
/// 4바이트 길이 헤더 + 데이터 프레이밍 구조 사용
/// </summary>
public static class PacketSerializer
{
    // LZ4 압축으로 네트워크 트래픽 최소화
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    /// <summary>헤더 크기 (4바이트 리틀 엔디안 int32)</summary>
    public const int HeaderSize = 4;

    /// <summary>
    /// MonitorPacket을 [4바이트 길이][데이터] 형식으로 직렬화
    /// </summary>
    public static byte[] Serialize(MonitorPacket packet)
    {
        var data = MessagePackSerializer.Serialize(packet, Options);
        var buffer = new byte[HeaderSize + data.Length];

        // 리틀 엔디안으로 길이 기록
        MemoryMarshal.Write(buffer.AsSpan(0, HeaderSize), in data.Length);
        data.CopyTo(buffer, HeaderSize);

        return buffer;
    }

    /// <summary>
    /// 수신 버퍼에서 패킷 길이를 읽음
    /// </summary>
    public static bool TryReadLength(ReadOnlySpan<byte> buffer, out int length)
    {
        if (buffer.Length < HeaderSize)
        {
            length = 0;
            return false;
        }
        length = MemoryMarshal.Read<int>(buffer);
        return length > 0;
    }

    /// <summary>
    /// 바이트 배열을 MonitorPacket으로 역직렬화
    /// </summary>
    public static MonitorPacket Deserialize(byte[] data)
    {
        return MessagePackSerializer.Deserialize<MonitorPacket>(data, Options);
    }

    /// <summary>
    /// ReadOnlyMemory 오버로드 - 복사 없이 역직렬화
    /// </summary>
    public static MonitorPacket Deserialize(ReadOnlyMemory<byte> data)
    {
        return MessagePackSerializer.Deserialize<MonitorPacket>(data, Options);
    }
}
