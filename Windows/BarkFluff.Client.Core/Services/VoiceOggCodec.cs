using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;

namespace BarkFluff.Client.Core.Services;

public static class VoiceOggCodec
{
    public const int SampleRate = 48_000;
    private const int FrameSamples = 960;
    private const uint RiffId = 0x46464952;
    private const uint WaveId = 0x45564157;
    private const uint FormatChunkId = 0x20746d66;
    private const uint DataChunkId = 0x61746164;
    private static readonly uint[] CrcTable = CreateCrcTable();

    static VoiceOggCodec() => OpusCodecFactory.AttemptToUseNativeLibrary = false;

    public static bool EncodeWave(Stream wave, Stream ogg, int minimumSamples, int maximumSamples, CancellationToken token)
    {
        wave.Position = 0;
        using var reader = new BinaryReader(wave, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != RiffId)
            throw new InvalidDataException();
        reader.ReadUInt32();
        if (reader.ReadUInt32() != WaveId)
            throw new InvalidDataException();

        bool pcm = false;
        while (wave.Length - wave.Position >= 8)
        {
            var chunk = reader.ReadUInt32();
            var size = reader.ReadUInt32();
            var next = wave.Position + size + (size & 1);
            if (wave.Position + size > wave.Length)
                throw new InvalidDataException();
            if (chunk == FormatChunkId && size >= 16)
            {
                var format = reader.ReadUInt16();
                var channels = reader.ReadUInt16();
                var rate = reader.ReadUInt32();
                reader.ReadUInt32();
                var alignment = reader.ReadUInt16();
                var bits = reader.ReadUInt16();
                pcm = (format == 1 || (format == 0xfffe && size >= 40 && ReadPcmSubformat(reader)))
                    && channels == 1 && rate == SampleRate && alignment == 2 && bits == 16;
            }
            if (chunk == DataChunkId && pcm)
            {
                int count = Math.Min((int)(size / 2), maximumSamples);
                if (count < minimumSamples)
                    return false;
                EncodePcm(wave, count, ogg, token);
                return true;
            }
            wave.Position = next;
        }
        throw new InvalidDataException();
    }

    private static bool ReadPcmSubformat(BinaryReader reader)
    {
        reader.ReadUInt16(); // Размер расширения
        reader.ReadUInt16(); // Число значащих бит
        reader.ReadUInt32(); // Маска каналов
        return new Guid(reader.ReadBytes(16)) == new Guid("00000001-0000-0010-8000-00aa00389b71");
    }

    private static void EncodePcm(Stream pcm, int sampleCount, Stream ogg, CancellationToken token)
    {
        using var encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = 24_000;
        int preSkip = encoder.Lookahead;
        Span<byte> head = stackalloc byte[19];
        head.Clear();
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(head[10..], (ushort)preSkip);
        BinaryPrimitives.WriteInt32LittleEndian(head[12..], SampleRate);
        int serial = Random.Shared.Next();
        WritePage(ogg, head, 2, 0, serial, 0);
        WritePage(ogg, "OpusTags\u0009\0\0\0BarkFluff\0\0\0\0"u8, 0, 0, serial, 1);

        var samples = new short[FrameSamples];
        var packet = new byte[1275];
        int finalGranule = sampleCount + preSkip;
        int sequence = 2;
        for (int start = 0; start < finalGranule; start += FrameSamples)
        {
            token.ThrowIfCancellationRequested();
            samples.AsSpan().Clear();
            int read = Math.Clamp(sampleCount - start, 0, FrameSamples);
            pcm.ReadExactly(MemoryMarshal.AsBytes(samples.AsSpan(0, read)));
            int length = encoder.Encode(samples, FrameSamples, packet, packet.Length);
            int end = Math.Min(start + FrameSamples, finalGranule);
            WritePage(ogg, packet.AsSpan(0, length), end == finalGranule ? (byte)4 : (byte)0, end, serial, sequence++);
        }
    }

    // Один пакет на странице. Последняя granule position включает pre-skip, но исключает дополнение кадра.
    private static void WritePage(Stream output, ReadOnlySpan<byte> packet, byte flags, long granule, int serial, int sequence)
    {
        int segments = packet.Length / 255 + 1;
        Span<byte> page = stackalloc byte[27 + segments + packet.Length];
        page[..27].Clear();
        "OggS"u8.CopyTo(page);
        page[5] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(page[6..], granule);
        BinaryPrimitives.WriteInt32LittleEndian(page[14..], serial);
        BinaryPrimitives.WriteInt32LittleEndian(page[18..], sequence);
        page[26] = (byte)segments;
        page.Slice(27, segments - 1).Fill(255);
        page[26 + segments] = (byte)(packet.Length % 255);
        packet.CopyTo(page[(27 + segments)..]);
        uint crc = 0;
        foreach (byte value in page)
            crc = (crc << 8) ^ CrcTable[(crc >> 24) ^ value];
        BinaryPrimitives.WriteUInt32LittleEndian(page[22..], crc);
        output.Write(page);
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (int index = 0; index < table.Length; index++)
        {
            uint value = (uint)index << 24;
            for (int bit = 0; bit < 8; bit++)
                value = (value << 1) ^ ((value & 0x80000000) == 0 ? 0 : 0x04c11db7u);
            table[index] = value;
        }
        return table;
    }

    public static void DecodeWave(Stream ogg, Stream wave, CancellationToken token)
    {
        Span<byte> page = stackalloc byte[28];
        ogg.ReadExactly(page);
        if (!page[..4].SequenceEqual("OggS"u8) || page[26] != 1 || page[27] < 19)
            throw new InvalidDataException();
        Span<byte> head = stackalloc byte[19];
        ogg.ReadExactly(head);
        int channels = head[9];
        if (!head[..8].SequenceEqual("OpusHead"u8) || channels is not (1 or 2) || head[18] != 0)
            throw new InvalidDataException();
        int skip = BinaryPrimitives.ReadUInt16LittleEndian(head[10..]) * channels;
        double gain = Math.Pow(10, BinaryPrimitives.ReadInt16LittleEndian(head[16..]) / (256d * 20));
        ogg.Position = 0;

        using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, channels);
        var input = new OpusOggReadStream(decoder, ogg);
        long totalSamples = input.GranuleCount * channels - skip;
        if (totalSamples <= 0 || totalSamples > (long)SampleRate * channels * 3600)
            throw new InvalidDataException();

        WriteWaveHeader(wave, channels, checked((int)totalSamples * 2));
        long remaining = totalSamples;
        while (remaining > 0 && input.HasNextPacket)
        {
            token.ThrowIfCancellationRequested();
            var samples = input.DecodeNextPacket() ?? throw new InvalidDataException();
            int discard = Math.Min(skip, samples.Length);
            skip -= discard;
            var audible = samples.AsSpan(discard, (int)Math.Min(remaining, samples.Length - discard));
            if (gain != 1)
            {
                for (int i = 0; i < audible.Length; i++)
                    audible[i] = (short)Math.Clamp(Math.Round(audible[i] * gain), short.MinValue, short.MaxValue);
            }
            wave.Write(MemoryMarshal.AsBytes(audible));
            remaining -= audible.Length;
        }
        if (remaining != 0 || input.LastError is not null)
            throw new InvalidDataException();
    }

    internal static void WriteWaveHeader(Stream wave, int channels, int dataBytes)
    {
        using var writer = new BinaryWriter(wave, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(RiffId);
        writer.Write(36 + dataBytes);
        writer.Write(WaveId);
        writer.Write(FormatChunkId);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(DataChunkId);
        writer.Write(dataBytes);
    }
}
