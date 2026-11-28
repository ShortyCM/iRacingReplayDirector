// This file is part of iRacingReplayDirector.
//
// Copyright 2014 Dean Netherton
// https://github.com/vipoo/iRacingReplayDirector.net
//
// iRacingReplayDirector is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// iRacingReplayDirector is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with iRacingReplayDirector.  If not, see <http://www.gnu.org/licenses/>.
//

using iRacingReplayDirector.Phases.Capturing;
using iRacingReplayDirector.Phases.Transcoding;
using iRacingSDK.Support;
using iRacingReplayDirector.Video;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace iRacingReplayDirector.Phases
{
    public class TranscodeAndOverlayMarshaled
    {
        public static void Apply(string name, string gameDataFile, OutputOptions output, string destFile, bool highlights, Action<long, long> progressReporter, CancellationToken token)
        {
            var domain = AppDomain.CreateDomain(name, null, new AppDomainSetup());
            try
            {
                var hostArgs = new TranscodeAndOverlayArguments(progressReporter, () => token.IsCancellationRequested);

                var arg = (TranscodeAndOverlayArguments)domain.CreateInstanceFromAndUnwrap(
                    typeof(TranscodeAndOverlayArguments).Assembly.Location,
                    typeof(TranscodeAndOverlayArguments).FullName,
                    false,
                    BindingFlags.CreateInstance,
                    null,
                    new object[] { gameDataFile, output, destFile, highlights, (Action<long, long>)hostArgs.ProgressReporter, (Func<bool>)hostArgs.IsAborted, hostArgs.LogRepeater, Settings.Default.PluginName },
                    null,
                    null);
                arg.Apply();
            }
            catch (Exception e)
            {
                TraceError.WriteLine(e.Message);
                TraceError.WriteLine(e.StackTrace);
                throw;
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }
    }

    public class SubDomainLogListener : TraceListener
    {
        LogRepeater logRepeater;
        public SubDomainLogListener(LogRepeater logRepeater)
        {
            this.logRepeater = logRepeater;
        }

        public override void Write(string message)
        {
            logRepeater.Write(message);
        }

        public override void WriteLine(string message)
        {
            logRepeater.WriteLine(message);
        }
    }

    public class LogRepeater : MarshalByRefObject
    {
        public void Write(string message)
        {
            Trace.Write(message);
        }

        public void WriteLine(string message)
        {
            Trace.WriteLine(message);
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }
    }

    public class TranscodeAndOverlayArguments : MarshalByRefObject
    {
        private string destFile;
        private string gameDataFile;
        private bool highlights;
        private Func<bool> _isAborted;
        private Action<long, long> _progressReporter;
        private OutputOptions output;
        private LogRepeater logRepeater;
        private readonly string pluginName;

        public TranscodeAndOverlayArguments(Action<long, long> progressReporter, Func<bool> isAborted)
        {
            //Constructed in main/host AppDomain
            this._progressReporter = progressReporter;
            this._isAborted = isAborted;
            this.logRepeater = new LogRepeater();
        }

        public TranscodeAndOverlayArguments(string gameDataFile, OutputOptions output, string destFile, bool highlights, Action<long, long> progressReporter, Func<bool> isAborted, LogRepeater logRepeater, string pluginName)
        {
            //Constructed in subdomain
            Program.MakePortable(Settings.Default);

            this.gameDataFile = gameDataFile;
            this.output = output;
            this.destFile = destFile;
            this.highlights = highlights;
            this._progressReporter = progressReporter;
            this._isAborted = isAborted;
            this.pluginName = pluginName;

            var logger = new SubDomainLogListener(logRepeater);
            Trace.Listeners.Add(logger);
        }

        public bool IsAborted()
        {
            return _isAborted();
        }

        public void ProgressReporter(long a, long b)
        {
            if (_progressReporter != null)
                _progressReporter(a, b);
        }

        public LogRepeater LogRepeater { get { return logRepeater; } }

        public void Apply()
        {
            TranscodeAndOverlay.Apply(gameDataFile, output, destFile, highlights, ProgressReporter, IsAborted, pluginName);
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }
    }

    public class TranscodeAndOverlay
    {
        public static void Apply(string gameDataFile, OutputOptions output, string destination, bool highlights,
            Action<long, long> progressReporter, Func<bool> isAborted, string pluginName)
        {
            var data = OverlayData.FromFile(gameDataFile);
            var overlay = new LeaderBoard { OverlayData = data, PluginName = pluginName };
            if (string.Equals(pluginName, "F1-style", StringComparison.OrdinalIgnoreCase) && data.Broadcast.Timing.Count == 0)
                TraceInfo.WriteLine("This capture has no saved F1-style timing samples for gaps or speeds.");

            TraceInfo.WriteLine("Encoding {0}", destination);
            Transcoder.Replay(data, overlay, destination, output, highlights, Settings.Default.VideoSplitGap, isAborted, progressReporter);
            TraceInfo.WriteLine("Finished encoding {0}", destination);
        }
    }
}
