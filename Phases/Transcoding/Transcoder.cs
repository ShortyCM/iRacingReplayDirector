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

using iRacingReplayDirector.Phases.Capturing;
using iRacingReplayDirector.Video;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace iRacingReplayDirector.Phases.Transcoding
{
    internal static class Transcoder
    {
        private sealed class Recording
        {
            internal CapturedVideoFile File;
            internal VideoAttributes.VideoDetails Media;
            internal double RaceTime;
        }

        private sealed class Segment
        {
            internal Recording Recording;
            internal double Start;
            internal long Frames;
            internal bool FadeIn;
            internal bool FadeOut;
        }

        internal static string Preview(string fileName, VideoAttributes.VideoDetails details, OutputOptions output, Action<long> progress)
        {
            var recording = new Recording
            {
                File = new CapturedVideoFile { FileName = fileName },
                Media = details
            };
            var destination = Path.ChangeExtension(fileName, ".preview." + output.Container);
            var segments = new[] { MakeSegment(recording, 0, Math.Min(10, recording.Media.Duration), recording.Media.FrameRate, true, false) };
            Encode(segments, recording.Media, destination, output, null, null,
                progress == null ? (Action<long, long>)null : (time, duration) => progress(time));
            return destination;
        }

        internal static void Replay(OverlayData data, LeaderBoard overlay, string destination, OutputOptions output,
            bool highlights, double splitGap, Func<bool> isAborted, Action<long, long> progress)
        {
            var recordings = new List<Recording>();
            double raceTime = 0;
            foreach (var file in data.VideoFiles)
            {
                if (isAborted != null && isAborted())
                    throw new OperationCanceledException();
                var recording = new Recording
                {
                    File = file,
                    Media = VideoAttributes.TestFor(file.FileName),
                    RaceTime = file.isIntroVideo ? 0 : raceTime
                };
                recordings.Add(recording);
                if (!file.isIntroVideo)
                    raceTime += recording.Media.Duration + Math.Max(0, splitGap);
            }
            var main = recordings.FirstOrDefault(recording => !recording.File.isIntroVideo);
            if (main == null)
                throw new InvalidOperationException("This replay has no race recording to export.");

            var cuts = highlights ? data.RaceEvents.GetRaceEdits().OrderBy(cut => cut.StartTime).ToArray() : new VideoEdit[0];
            if (highlights && cuts.Length == 0)
                throw new InvalidOperationException("No highlight cuts could be calculated. Try a shorter highlight duration.");
            var segments = recordings.SelectMany(recording => Segments(recording, cuts, main.Media.FrameRate)).ToArray();
            if (segments.Length == 0)
                throw new InvalidOperationException("The highlight cuts removed all recorded video.");

            destination = Path.GetFullPath(destination);
            var sources = new HashSet<string>(data.VideoFiles.Select(file => Path.GetFullPath(file.FileName)), StringComparer.OrdinalIgnoreCase);
            while (sources.Contains(destination))
                destination = Path.ChangeExtension(destination, ".full." + output.Container);
            Encode(segments, main.Media, destination, output, overlay, isAborted, progress);
        }

        private static IEnumerable<Segment> Segments(Recording recording, VideoEdit[] cuts, double frameRate)
        {
            if (recording.File.isIntroVideo)
            {
                yield return MakeSegment(recording, 0, recording.Media.Duration, frameRate, true, true);
                yield break;
            }

            var cursor = 0.0;
            var fadeIn = false;
            var length = recording.Media.Duration;
            foreach (var cut in cuts)
            {
                var cutStart = cut.StartTime - recording.RaceTime;
                var cutEnd = cut.EndTime - recording.RaceTime;
                if (cutEnd <= cursor || cutStart >= length)
                    continue;
                if (cutStart > cursor)
                    yield return MakeSegment(recording, cursor, cutStart, frameRate, fadeIn, true);
                cursor = Math.Min(length, cutEnd);
                fadeIn = true;
            }
            if (cursor < length)
                yield return MakeSegment(recording, cursor, length, frameRate, fadeIn, false);
        }

        private static Segment MakeSegment(Recording recording, double start, double end, double frameRate, bool fadeIn, bool fadeOut)
        {
            return new Segment
            {
                Recording = recording,
                Start = start,
                Frames = Math.Max(1, (long)Math.Round((end - start) * frameRate)),
                FadeIn = fadeIn,
                FadeOut = fadeOut
            };
        }

        private static void Encode(Segment[] segments, VideoAttributes.VideoDetails main, string destination, OutputOptions output,
            LeaderBoard overlay, Func<bool> isAborted, Action<long, long> progress)
        {
            var width = (main.FrameSize.Width + 1) & ~1;
            var height = (main.FrameSize.Height + 1) & ~1;
            var size = new Size(width, height);
            var audio = string.IsNullOrEmpty(output.Audio.Encoder) ? null
                : segments.Select(segment => segment.Recording.Media).FirstOrDefault(media => media.HasAudio);
            var arguments = new List<string>();
            var graph = new List<string>();
            var rate = main.RateExpression;
            var frameRate = main.FrameRate;
            var totalTicks = Ticks(segments.Sum(segment => segment.Frames) / frameRate);

            for (var index = 0; index < segments.Length; index++)
                AddSegment(arguments, graph, segments[index], index, rate, frameRate, size, audio);

            var videoLabel = "[v0]";
            var audioLabel = "[a0]";
            if (segments.Length > 1)
            {
                videoLabel = "[joinedv]";
                audioLabel = "[joineda]";
                var labels = string.Concat(Enumerable.Range(0, segments.Length).Select(index => "[v" + index + "]"
                    + (audio == null ? "" : "[a" + index + "]")));
                graph.Add(labels + "concat=n=" + segments.Length + ":v=1:a=" + (audio == null ? "0" : "1")
                    + videoLabel + (audio == null ? "" : audioLabel));
            }
            if (overlay != null)
            {
                arguments.AddRange(new[] { "-f", "rawvideo", "-pixel_format", "bgra", "-video_size",
                    width + "x" + height, "-framerate", rate, "-i", "pipe:0" });
                var format = OverlayFormat(string.IsNullOrEmpty(output.PixelFormat) ? main.PixelFormat : output.PixelFormat);
                graph.Add(videoLabel + "[" + segments.Length + ":v:0]overlay=shortest=1:format=" + format + ":alpha=straight[overlaid]");
                videoLabel = "[overlaid]";
            }
            var videoFilters = VideoFades(segments, frameRate).ToList();
            var colour = output.ColourFilter();
            if (colour.Length != 0)
                videoFilters.Add(colour);
            if (videoFilters.Count != 0)
            {
                graph.Add(videoLabel + string.Join(",", videoFilters) + "[finalv]");
                videoLabel = "[finalv]";
            }

            destination = Path.GetFullPath(destination);
            var temporary = Path.Combine(Path.GetDirectoryName(destination), ".ffmpeg-" + Guid.NewGuid().ToString("N") + "." + output.Container);
            var filterFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(filterFile, string.Join(";\n", graph), new UTF8Encoding(false));
                arguments.AddRange(new[] { "-/filter_complex", filterFile, "-map", videoLabel });
                if (audio != null)
                    arguments.AddRange(new[] { "-map", audioLabel });
                output.AddEncoderArguments(arguments, audio != null);
                arguments.AddRange(new[] { "-strict", "-2", temporary });

                Action<Stream> writeOverlay = overlay == null ? (Action<Stream>)null
                    : stream => RenderOverlay(stream, size, frameRate, segments, overlay, isAborted);
                Ffmpeg.Encode(arguments, writeOverlay, isAborted,
                    progress == null ? (Action<long>)null : ticks => progress(Math.Min(ticks, totalTicks), totalTicks));
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
                if (progress != null)
                    progress(totalTicks, totalTicks);
            }
            finally
            {
                File.Delete(filterFile);
                File.Delete(temporary);
            }
        }

        private static void AddSegment(List<string> arguments, List<string> graph, Segment segment, int index,
            string rate, double frameRate, Size size, VideoAttributes.VideoDetails audio)
        {
            var duration = segment.Frames / frameRate;
            var length = Ffmpeg.Seconds(duration);
            var tag = index.ToString(CultureInfo.InvariantCulture);
            arguments.AddRange(new[] { "-ss", Ffmpeg.Seconds(segment.Start), "-t", Ffmpeg.Seconds(duration + 1 / frameRate),
                "-i", segment.Recording.File.FileName });

            var video = "[" + tag + ":v:0]fps=" + rate + ":start_time=0"
                + ",scale=" + size.Width + ":" + size.Height + ":force_original_aspect_ratio=decrease"
                + ",pad=" + size.Width + ":" + size.Height + ":(ow-iw)/2:(oh-ih)/2,setsar=1"
                + ",tpad=stop_mode=clone:stop=" + segment.Frames + ",trim=end_frame=" + segment.Frames + ",setpts=PTS-STARTPTS";
            graph.Add(video + "[v" + tag + "]");
            if (audio == null)
                return;

            var sampleRate = audio.AudioSamplesPerSecond > 0 ? audio.AudioSamplesPerSecond : 48000;
            var audioInput = segment.Recording.Media.HasAudio
                ? "[" + tag + ":a:0]aresample=" + sampleRate + ":async=1:first_pts=0,aformat=channel_layouts=" + audio.AudioLayout + ",apad=whole_dur=" + length
                : "anullsrc=r=" + sampleRate + ":cl=" + audio.AudioLayout + ":d=" + length;
            graph.Add(audioInput + ",atrim=duration=" + length + ",asetpts=PTS-STARTPTS"
                + Fades(segment, duration, "afade") + "[a" + tag + "]");
        }

        private static string Fades(Segment segment, double duration, string filter)
        {
            var fadeLength = Math.Min(1, duration / 2);
            var length = Ffmpeg.Seconds(fadeLength);
            return (segment.FadeIn ? "," + filter + "=t=in:st=0:d=" + length : "")
                + (segment.FadeOut ? "," + filter + "=t=out:st=" + Ffmpeg.Seconds(duration - fadeLength) + ":d=" + length : "");
        }

        private static IEnumerable<string> VideoFades(IEnumerable<Segment> segments, double frameRate)
        {
            long offset = 0;
            foreach (var segment in segments)
            {
                var start = offset / frameRate;
                offset += segment.Frames;
                var end = offset / frameRate;
                var length = Math.Min(1, (end - start) / 2);

                if (segment.FadeIn)
                    yield return EnabledFade("in", start, length);
                if (segment.FadeOut)
                    yield return EnabledFade("out", end - length, length);
            }
        }

        private static string EnabledFade(string direction, double start, double length)
        {
            return "fade=t=" + direction + ":st=" + Ffmpeg.Seconds(start) + ":d=" + Ffmpeg.Seconds(length)
                + ":enable='gte(t," + Ffmpeg.Seconds(start) + ")*lt(t," + Ffmpeg.Seconds(start + length) + ")'";
        }

        private static string OverlayFormat(string pixelFormat)
        {
            if (pixelFormat.StartsWith("yuv420p10", StringComparison.Ordinal) || pixelFormat.StartsWith("p010", StringComparison.Ordinal))
                return "yuv420p10";
            if (pixelFormat.StartsWith("yuv422p10", StringComparison.Ordinal) || pixelFormat.StartsWith("p210", StringComparison.Ordinal))
                return "yuv422p10";
            if (pixelFormat.StartsWith("yuv444p10", StringComparison.Ordinal))
                return "yuv444p10";
            if (pixelFormat == "yuv420p" || pixelFormat == "yuvj420p" || pixelFormat == "nv12")
                return "yuv420";
            if (pixelFormat == "yuv422p" || pixelFormat == "yuvj422p" || pixelFormat == "nv16")
                return "yuv422";
            if (pixelFormat == "yuv444p" || pixelFormat == "yuvj444p")
                return "yuv444";
            return "auto";
        }

        private static void RenderOverlay(Stream output, Size size, double frameRate, Segment[] segments,
            LeaderBoard renderer, Func<bool> isAborted)
        {
            var rowBytes = checked(size.Width * 4);
            var buffer = new byte[checked(rowBytes * size.Height)];
            var bounds = new Rectangle(Point.Empty, size);
            using (var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
            {
                foreach (var segment in segments)
                {
                    var recording = segment.Recording;
                    for (long frame = 0; frame < segment.Frames; frame++)
                    {
                        if (isAborted != null && isAborted())
                            throw new OperationCanceledException();

                        var timestamp = recording.RaceTime + segment.Start + frame / frameRate;
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.Transparent);
                            if (recording.File.isIntroVideo)
                                renderer.Intro(graphics, Ticks(recording.Media.Duration), Ticks(timestamp));
                            else
                                DrawRaceOverlay(renderer, graphics, timestamp, segments);
                        }
                        var pixels = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            if (pixels.Stride == rowBytes)
                                Marshal.Copy(pixels.Scan0, buffer, 0, buffer.Length);
                            else
                                for (var row = 0; row < size.Height; row++)
                                    Marshal.Copy(IntPtr.Add(pixels.Scan0, row * pixels.Stride), buffer, row * rowBytes, rowBytes);
                        }
                        finally
                        {
                            bitmap.UnlockBits(pixels);
                        }
                        output.Write(buffer, 0, buffer.Length);
                    }
                }
            }
        }

        private static void DrawRaceOverlay(LeaderBoard renderer, Graphics graphics, double time, Segment[] segments)
        {
            var outro = renderer.OverlayData.TimeForOutroOverlay;
            if (!outro.HasValue || time < outro.Value)
            {
                renderer.Overlay(graphics, Ticks(time));
                return;
            }
            if (time - outro.Value > 30)
                return;
            var last = segments.Last(segment => !segment.Recording.File.isIntroVideo).Recording;
            var duration = Math.Min(30, last.RaceTime + last.Media.Duration - outro.Value);
            renderer.Outro(graphics, Ticks(duration), Ticks(time), Ticks(time - outro.Value));
        }

        private static long Ticks(double seconds)
        {
            return (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
        }
    }
}
