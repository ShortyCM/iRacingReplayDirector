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
using System.Threading;
using System.Diagnostics;

namespace iRacingSDK
{
    public class iRacingConnection : IDisposable
    {
        readonly CrossThreadEvents connected = new CrossThreadEvents();
        readonly CrossThreadEvents disconnected = new CrossThreadEvents();
        readonly CrossThreadEvents<DataSample> newSessionData = new CrossThreadEvents<DataSample>();
        readonly object dataFeedLock = new object();
        DataFeed dataFeed;
        bool disposed;
        
        int isRunning;
        internal bool IsRunning { get { return Volatile.Read(ref isRunning) != 0; } }

        long processingTime;
        public long ProcessingTime { get { return processingTime * 1000000L / Stopwatch.Frequency; } }

        long waitingTime;
        public long WaitingTime { get { return waitingTime * 1000000L / Stopwatch.Frequency; } }

        long yieldTime;
        public long YieldTime { get { return (yieldTime * 1000000L / Stopwatch.Frequency) ; } }

        public readonly Replay Replay;
        public readonly PitCommand PitCommand;

        public bool IsConnected { get; private set; }

        public event Action Connected
        {
            add { connected.Event += value; }
            remove { connected.Event -= value; }
        }

        public event Action Disconnected
        {
            add { disconnected.Event += value; }
            remove { disconnected.Event -= value; }
        }

        public event Action<DataSample> NewSessionData
        {
            add { newSessionData.Event += value; }
            remove { newSessionData.Event -= value; }
        }

        public iRacingConnection()
        {
            this.Replay = new Replay(this);
            this.PitCommand = new PitCommand();
        }

        public IEnumerable<DataSample> GetDataFeed(bool logging = true)
        {
            return GetRawDataFeed(logging).WithLastSample().WithEvents(connected, disconnected, newSessionData);
        }

        internal IEnumerable<DataSample> GetRawDataFeed(bool logging = true, Func<bool> keepReading = null)
        {
            if (Interlocked.CompareExchange(ref isRunning, 1, 0) != 0)
                throw new InvalidOperationException("Cannot enumerate the same iRacing connection concurrently.");
            try
            {
                foreach (var sample in AllSamples(logging, keepReading))
                    yield return sample;
            }
            finally
            {
                Volatile.Write(ref isRunning, 0);
            }
        }

        private IEnumerable<DataSample> AllSamples(bool logging, Func<bool> keepReading)
        {
            int? nextTickCount = null;
            var lastTickTime = DateTime.Now;
            var stopwatch = new Stopwatch();

            while (keepReading == null || keepReading())
            {
                stopwatch.Restart();
                DataSample data;
                lock (dataFeedLock)
                {
                    if (disposed)
                        throw new ObjectDisposedException(nameof(iRacingConnection));
                    if (dataFeed == null)
                        dataFeed = new DataFeed();
                    data = dataFeed.Read(out waitingTime);
                    if (data != null)
                        IsConnected = data.IsConnected;
                }
                processingTime = stopwatch.ElapsedTicks - waitingTime;
                if (data == null)
                    continue;

                if (data.IsConnected)
                {
                    var tickCount = data.Telemetry.TickCount;
                    if (logging && nextTickCount.HasValue && tickCount > nextTickCount.Value)
                        Debug.WriteLine("Dropped DataSample from {0} to {1}. Over time of {2}",
                            nextTickCount, tickCount - 1, (DateTime.Now - lastTickTime).ToString(@"s\.fff"), "WARN");

                    nextTickCount = tickCount + 1;
                    lastTickTime = DateTime.Now;
                }
                else
                {
                    nextTickCount = null;
                }

                stopwatch.Restart();
                yield return data;
                yieldTime = stopwatch.ElapsedTicks;
            }
        }

        public void Dispose()
        {
            lock (dataFeedLock)
            {
                disposed = true;
                IsConnected = false;
                if (dataFeed != null)
                {
                    dataFeed.Dispose();
                    dataFeed = null;
                }
            }
        }
    }
}
