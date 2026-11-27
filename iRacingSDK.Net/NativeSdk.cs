using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;

namespace iRacingSDK
{
    internal static class NativeSdk
    {
        private const string Library = "ReplayDirector.iRacingNative.dll";

        internal enum ReadResult
        {
            OutOfMemory = -3,
            UnsupportedProtocol = -2,
            Disconnected = -1,
            NoSample = 0,
            SampleReady = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Sample
        {
            public IntPtr Telemetry;
            public IntPtr Variables;
            public IntPtr SessionInfo;
            public int TelemetryLength;
            public int VariableCount;
            public int TickCount;
            public int SessionInfoLength;
            public int SessionInfoUpdate;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern ConnectionHandle OpenConnection();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void CloseConnection(IntPtr connection);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern ReadResult ReadSample(ConnectionHandle connection, int timeoutMilliseconds, out Sample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetSessionInfoUpdate(ConnectionHandle connection);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetBroadcastMessageId();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SendBroadcast(BroadcastMessage message, int value, int payload);

        internal sealed class ConnectionHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private ConnectionHandle() : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                CloseConnection(handle);
                return true;
            }
        }
    }
}
