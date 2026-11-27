// This file is part of iRacingSDK.
//
// Copyright 2014 Dean Netherton
// https://github.com/vipoo/iRacingSDK.Net
//
// iRacingSDK is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// iRacingSDK is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with iRacingSDK.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace iRacingSDK
{
    internal sealed class DataFeed : IDisposable
    {
        private const int WaitMilliseconds = 17;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        private static readonly int VariableHeaderSize = Marshal.SizeOf(typeof(VariableHeader));

        private readonly NativeSdk.ConnectionHandle connection;
        private readonly IDeserializer deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        private SessionData sessionData;
        private int? sessionUpdate;
        private IntPtr variableHeaderAddress;
        private VariableHeader[] variableHeaders = new VariableHeader[0];
        private Dictionary<string, string> descriptions;
        private byte[] telemetryBytes = new byte[0];

        internal DataFeed()
        {
            connection = NativeSdk.OpenConnection();
            if (connection.IsInvalid)
                throw new OutOfMemoryException("Unable to allocate an iRacing SDK connection.");
        }

        internal DataSample Read(out long waitingTime)
        {
            NativeSdk.Sample sample;
            var startTime = Stopwatch.GetTimestamp();
            var result = NativeSdk.ReadSample(connection, WaitMilliseconds, out sample);
            waitingTime = Stopwatch.GetTimestamp() - startTime;
            switch (result)
            {
                case NativeSdk.ReadResult.NoSample:
                    return null;
                case NativeSdk.ReadResult.Disconnected:
                    sessionData = null;
                    sessionUpdate = null;
                    variableHeaderAddress = IntPtr.Zero;
                    return new DataSample { IsConnected = false };
                case NativeSdk.ReadResult.UnsupportedProtocol:
                    throw new NotSupportedException("The simulator uses an unsupported iRacing telemetry header version.");
                case NativeSdk.ReadResult.OutOfMemory:
                    throw new OutOfMemoryException("Unable to allocate an iRacing telemetry buffer.");
                case NativeSdk.ReadResult.SampleReady:
                    break;
                default:
                    throw new InvalidOperationException("Unexpected result from the iRacing SDK.");
            }

            ReadSessionInfo(sample);
            if (sessionData == null)
                return new DataSample { IsConnected = false };

            if (sample.Variables != variableHeaderAddress || sample.VariableCount != variableHeaders.Length)
                ReadVariableHeaders(sample);

            if (telemetryBytes.Length != sample.TelemetryLength)
                telemetryBytes = new byte[sample.TelemetryLength];
            Marshal.Copy(sample.Telemetry, telemetryBytes, 0, telemetryBytes.Length);

            var telemetry = new Telemetry { Descriptions = descriptions, SessionData = sessionData };
            foreach (var variable in variableHeaders)
            {
                var value = variable.Read(telemetryBytes);
                if (value != null)
                    telemetry[variable.Name] = value;
            }
            telemetry["TickCount"] = sample.TickCount;
            return new DataSample
            {
                IsConnected = true,
                SessionData = sessionData,
                Telemetry = telemetry,
                TelemetryDescription = descriptions
            };
        }

        private void ReadSessionInfo(NativeSdk.Sample sample)
        {
            if (sessionUpdate == sample.SessionInfoUpdate || sample.SessionInfoLength <= 0)
                return;

            var bytes = new byte[sample.SessionInfoLength];
            Marshal.Copy(sample.SessionInfo, bytes, 0, bytes.Length);
            if (sample.SessionInfoUpdate != NativeSdk.GetSessionInfoUpdate(connection))
                return;

            var length = Array.IndexOf(bytes, (byte)0);
            if (length <= 0)
                return;

            string yaml;
            try
            {
                yaml = Utf8.GetString(bytes, 0, length);
            }
            catch (DecoderFallbackException)
            {
                yaml = Latin1.GetString(bytes, 0, length);
            }

            sessionUpdate = sample.SessionInfoUpdate;
            try
            {
                yaml = yaml.Replace(": *", ": ");
                var parsed = deserializer.Deserialize<SessionData>(yaml);
                if (parsed == null)
                    return;

                parsed.Raw = yaml.Replace("\n", "\r\n");
                parsed.InfoUpdate = sample.SessionInfoUpdate;
                sessionData = parsed;
            }
            catch (YamlException exception)
            {
                Trace.WriteLine("Error decoding iRacing session YAML: " + exception.Message, "DEBUG");
            }
        }

        private void ReadVariableHeaders(NativeSdk.Sample sample)
        {
            variableHeaderAddress = sample.Variables;
            variableHeaders = new VariableHeader[sample.VariableCount];
            descriptions = new Dictionary<string, string>(sample.VariableCount);
            for (var index = 0; index < variableHeaders.Length; index++)
            {
                var address = IntPtr.Add(sample.Variables, index * VariableHeaderSize);
                var variable = Marshal.PtrToStructure<VariableHeader>(address);
                variableHeaders[index] = variable;
                descriptions[variable.Name] = variable.Description;
            }
        }

        public void Dispose()
        {
            connection.Dispose();
        }

        private enum VariableType
        {
            Character,
            Boolean,
            Integer,
            BitField,
            Float,
            Double
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct VariableHeader
        {
            private static readonly Func<byte[], int, byte> ReadCharacter = (bytes, offset) => bytes[offset];
            private static readonly Func<byte[], int, bool> ReadBoolean = BitConverter.ToBoolean;
            private static readonly Func<byte[], int, int> ReadInteger = BitConverter.ToInt32;
            private static readonly Func<byte[], int, float> ReadFloat = BitConverter.ToSingle;
            private static readonly Func<byte[], int, double> ReadDouble = BitConverter.ToDouble;

            public VariableType Type;
            public int Offset;
            public int Count;
            public int CountAsTimeAndPadding;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Defines.MaxString)]
            public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Defines.MaxDesc)]
            public string Description;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Defines.MaxString)]
            public string Unit;

            public object Read(byte[] data)
            {
                switch (Type)
                {
                    case VariableType.Character:
                        return ReadValue(data, sizeof(byte), ReadCharacter);
                    case VariableType.Boolean:
                        return ReadValue(data, sizeof(bool), ReadBoolean);
                    case VariableType.Integer:
                    case VariableType.BitField:
                        return ReadValue(data, sizeof(int), ReadInteger);
                    case VariableType.Float:
                        return ReadValue(data, sizeof(float), ReadFloat);
                    case VariableType.Double:
                        return ReadValue(data, sizeof(double), ReadDouble);
                    default:
                        return null;
                }
            }

            private object ReadValue<T>(byte[] data, int width, Func<byte[], int, T> readScalar)
                where T : struct
            {
                if (Offset < 0 || Offset > data.Length || Count <= 0 || Count > (data.Length - Offset) / width)
                    return null;
                if (Count == 1)
                    return readScalar(data, Offset);

                var values = new T[Count];
                Buffer.BlockCopy(data, Offset, values, 0, Count * width);
                return values;
            }
        }
    }
}
