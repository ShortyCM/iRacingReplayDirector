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

using iRacingReplayDirector.Video;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace iRacingReplayDirector.Phases
{
    public partial class IRacingReplay
    {
        OutputOptions outputOptions;
        string gameDataFile;

        public void _WithEncodingOf(OutputOptions output)
        {
            outputOptions = output;
        }

        public void _WithOverlayFile(string overlayFileName)
        {
            gameDataFile = overlayFileName;
        }

        public void _OverlayRaceDataOntoVideo(Action<long, long> progress, Action completed, bool highlightOnly, bool bShutdownAfterCompleted, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (!highlightOnly)
                {
                    TranscodeAndOverlayMarshaled.Apply("Full", gameDataFile, outputOptions,
                        Path.ChangeExtension(gameDataFile, outputOptions.Container), false,
                        (time, duration) => progress(time, duration * 2), token);
                }
                token.ThrowIfCancellationRequested();
                TranscodeAndOverlayMarshaled.Apply("Highlights", gameDataFile, outputOptions,
                    Path.ChangeExtension(gameDataFile, ".highlights." + outputOptions.Container), true,
                    highlightOnly ? progress : (time, duration) => progress(duration + time, duration * 2), token);
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested)
                    throw;
                return;
            }
            completed();

            //Shutdown PC after Transcoding is completed (MCooper)
            if (bShutdownAfterCompleted)
            {
                var psi = new ProcessStartInfo("shutdown", "/s /f /t 0");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
        }
    }
}
