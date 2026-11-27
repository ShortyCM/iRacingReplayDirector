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
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace iRacingReplayDirector.Video
{
    public static class VideoAttributes
    {
        public sealed class VideoDetails
        {
            public int BitRate { get; internal set; }
            public Size FrameSize { get; internal set; }
            public double FrameRate { get; internal set; }
            public int AudioSamplesPerSecond { get; internal set; }
            public int AudioAverageBytesPerSecond { get; internal set; }
            public string AudioEncoding { get; internal set; }
            public string VideoEncoding { get; internal set; }
            internal bool HasAudio;
            internal double Duration;
            internal string RateExpression;
            internal string AudioLayout;
            internal string PixelFormat;
        }

        public static VideoDetails TestFor(string videoFileName)
        {
            var metadata = Ffmpeg.Probe(videoFileName);
            var streams = metadata["streams"] as JArray ?? new JArray();
            var video = streams.FirstOrDefault(stream => (string)stream["codec_type"] == "video");
            var audio = streams.FirstOrDefault(stream => (string)stream["codec_type"] == "audio");
            if (video == null)
                throw new InvalidOperationException("No video stream in " + videoFileName);

            var width = (int)Number(video["width"]);
            var height = (int)Number(video["height"]);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("FFprobe could not determine the frame size of " + videoFileName);

            var rate = (string)video["avg_frame_rate"];
            var framesPerSecond = Fraction(rate);
            if (framesPerSecond <= 0)
            {
                rate = (string)video["r_frame_rate"];
                framesPerSecond = Fraction(rate);
            }
            if (framesPerSecond <= 0)
                throw new InvalidOperationException("FFprobe could not determine the frame rate of " + videoFileName);

            var duration = Number(video["duration"]);
            if (duration <= 0)
                duration = Number(video["duration_ts"]) * Fraction((string)video["time_base"]);
            if (duration <= 0 && video["tags"] != null)
            {
                TimeSpan taggedDuration;
                if (TimeSpan.TryParse((string)video["tags"]["DURATION"], CultureInfo.InvariantCulture, out taggedDuration))
                    duration = taggedDuration.TotalSeconds;
            }
            var format = metadata["format"];
            if (duration <= 0 && format != null)
                duration = Number(format["duration"]);
            if (duration <= 0 || double.IsInfinity(duration))
                throw new InvalidOperationException("FFprobe could not determine the duration of " + videoFileName);

            var layout = audio == null ? "" : (string)audio["channel_layout"] ?? "";
            if (!Regex.IsMatch(layout, @"^[a-zA-Z0-9.()_+-]+$"))
                layout = audio != null && Number(audio["channels"]) == 1 ? "mono" : "stereo";

            return new VideoDetails
            {
                FrameSize = new Size(width, height),
                FrameRate = framesPerSecond,
                RateExpression = rate,
                Duration = duration,
                BitRate = (int)(Number(video["bit_rate"]) / 1000000),
                VideoEncoding = (string)video["codec_name"] ?? "Unknown",
                HasAudio = audio != null,
                AudioEncoding = audio == null ? "None" : (string)audio["codec_name"] ?? "Unknown",
                AudioSamplesPerSecond = audio == null ? 0 : (int)Number(audio["sample_rate"]),
                AudioAverageBytesPerSecond = audio == null ? 0 : (int)(Number(audio["bit_rate"]) / 8),
                AudioLayout = layout,
                PixelFormat = (string)video["pix_fmt"] ?? ""
            };
        }

        public static VideoDetails TestFor(OverlayData data)
        {
            var video = data.VideoFiles.LastOrDefault();
            if (video == null)
                throw new InvalidOperationException("This replay has no recorded video files.");
            return TestFor(video.FileName);
        }

        private static double Number(JToken token)
        {
            double result;
            return double.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && !double.IsNaN(result) && !double.IsInfinity(result) ? result : 0;
        }

        private static double Fraction(string ratio)
        {
            if (string.IsNullOrEmpty(ratio) || !Regex.IsMatch(ratio, @"^[0-9]+/[0-9]+$"))
                return 0;
            var terms = ratio.Split('/');
            var numerator = double.Parse(terms[0], CultureInfo.InvariantCulture);
            var denominator = double.Parse(terms[1], CultureInfo.InvariantCulture);
            return denominator == 0 ? 0 : numerator / denominator;
        }
    }
}
