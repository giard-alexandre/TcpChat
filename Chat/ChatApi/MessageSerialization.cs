﻿using ChatApi.Messages;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatApi
{
    internal static class MessageSerialization
    {
        public const int LengthPrefixLength = sizeof(uint);

        public const int MessageTypeLength = sizeof(uint);
        private const int LongStringLengthPrefixLength = sizeof(ushort);
        private const int ShortStringLengthPrefixLength = sizeof(byte);

        public static int GetMessageLengthPrefixValue(IMessage message)
        {
            if (message is ChatMessage chatMessage)
            {
                return MessageTypeLength + LongStringFieldLength(chatMessage.Text);
            }
            else if (message is BroadcastMessage broadcastMessage)
            {
                return MessageTypeLength +
                    ShortStringFieldLength(broadcastMessage.From) +
                    LongStringFieldLength(broadcastMessage.Text);
            }
            else if (message is SetNicknameRequestMessage setNicknameRequestMessage)
            {
                return MessageTypeLength + ShortStringFieldLength(setNicknameRequestMessage.Nickname);
            }
            else if (message is KeepaliveMessage or AckResponseMessage or NakResponseMessage)
            {
                return MessageTypeLength;
            }
            else
            {
                throw new InvalidOperationException("Unknown message type.");
            }

            int ShortStringFieldLength(string value) =>
                ShortStringLengthPrefixLength + Encoding.UTF8.GetByteCount(value);
            int LongStringFieldLength(string value) =>
                LongStringLengthPrefixLength + Encoding.UTF8.GetByteCount(value);
        }

        public static bool TryReadMessage(ref this SequenceReader<byte> sequenceReader, uint maxMessageSize,
            out IMessage? message)
        {
            message = null;
            if (!sequenceReader.TryReadLengthPrefix(out var lengthPrefix))
                return false;

            if (lengthPrefix > maxMessageSize)
                throw new InvalidOperationException("Message size too big");

            if (sequenceReader.Remaining < lengthPrefix)
                return false;

            if (!sequenceReader.TryReadMessageType(out int messageType))
                return false;

            if (messageType == 0)
            {
                if (!sequenceReader.TryReadLongString(out var text))
                    return false;

                message = new ChatMessage(text);
                return true;
            }
            else if (messageType == 1)
            {
                if (!sequenceReader.TryReadShortString(out var from))
                    return false;

                if (!sequenceReader.TryReadLongString(out var text))
                    return false;

                message = new BroadcastMessage(from, text);
                return true;
            }
            else if (messageType == 3)
            {
                if (!sequenceReader.TryReadShortString(out var nickname))
                    return false;
                message = new SetNicknameRequestMessage(nickname);
                return true;
            }
            else if (messageType == 4)
            {
                message = new AckResponseMessage();
                return true;
            }
            else if (messageType == 5)
            {
                message = new NakResponseMessage();
                return true;
            }
            else
            {
                // `message` is `null` for unrecognized messages.
                sequenceReader.Advance(lengthPrefix - MessageTypeLength);
                return true;
            }
        }

        public static bool TryReadMessageType(ref this SequenceReader<byte> sequenceReader,
            [NotNullWhen(true)] out int value) => sequenceReader.TryReadBigEndian(out value);

        public static bool TryReadLengthPrefix(ref this SequenceReader<byte> sequenceReader,
            [NotNullWhen(true)] out uint value)
        {
            var result = sequenceReader.TryReadBigEndian(out int signedValue);
            value = (uint)signedValue;
            return result;
        }

        public static bool TryReadLongString(ref this SequenceReader<byte> sequenceReader,
            [NotNullWhen(true)] out string? value)
        {
            value = null;
            if (!sequenceReader.TryReadBigEndian(out short signedLength))
                return false;
            var length = (ushort)signedLength;

            var bytes = new byte[length];
            if (!sequenceReader.TryCopyTo(bytes))
                return false;

            // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
            sequenceReader.Advance(length);

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public static bool TryReadShortString(ref this SequenceReader<byte> sequenceReader,
            [NotNullWhen(true)] out string? value)
        {
            value = null;
            if (!sequenceReader.TryRead(out var length))
                return false;

            var bytes = new byte[length];
            if (!sequenceReader.TryCopyTo(bytes))
                return false;

            // Unlike other SequenceReader methods, TryCopyTo does *not* advance the position.
            sequenceReader.Advance(length);

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public ref struct SpanWriter
        {
            private readonly Span<byte> _span;
            private int _position;

            public SpanWriter(Span<byte> span)
            {
                _span = span;
                _position = 0;
            }

            public int Position => _position;

            public void WriteMessage(IMessage message)
            {
                if (message is ChatMessage chatMessage)
                {
                    WriteMessageType(0);
                    WriteLongString(chatMessage.Text);
                }
                else if (message is BroadcastMessage broadcastMessage)
                {
                    WriteMessageType(1);
                    WriteShortString(broadcastMessage.From);
                    WriteLongString(broadcastMessage.Text);
                }
                else if (message is SetNicknameRequestMessage setNicknameRequestMessage)
                {
                    WriteMessageType(3);
                    WriteShortString(setNicknameRequestMessage.Nickname);
                }
                else if (message is KeepaliveMessage)
                {
                    WriteMessageType(2);
                }
                else if (message is AckResponseMessage)
                {
                    WriteMessageType(4);
                }
                else if (message is NakResponseMessage)
                {
                    WriteMessageType(5);
                }
                else
                {
                    throw new InvalidOperationException("Unknown message type.");
                }
            }

            public void WriteMessageLengthPrefix(uint value) => WriteUInt32BigEndian(value);

            private void WriteMessageType(uint value) => WriteUInt32BigEndian(value);

            private void WriteUInt32BigEndian(uint value)
            {
                BinaryPrimitives.WriteUInt32BigEndian(_span.Slice(_position, sizeof(uint)), value);
                _position += sizeof(uint);
            }

            private void WriteUInt16BigEndian(ushort value)
            {
                BinaryPrimitives.WriteUInt16BigEndian(_span.Slice(_position, sizeof(ushort)), value);
                _position += sizeof(ushort);
            }

            private void WriteLongString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length > ushort.MaxValue)
                    throw new InvalidOperationException("Long string field is too big.");
                WriteUInt16BigEndian((ushort)bytes.Length);
                WriteByteArray(bytes);
            }

            private void WriteShortString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length > byte.MaxValue)
                    throw new InvalidOperationException("Short string field is too big.");
                WriteByte((byte)bytes.Length);
                WriteByteArray(bytes);
            }

            private void WriteByte(byte value) => _span[_position++] = value;

            private void WriteByteArray(ReadOnlySpan<byte> value)
            {
                value.CopyTo(_span.Slice(_position, value.Length));
                _position += value.Length;
            }
        }
    }
}
