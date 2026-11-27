using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace iRacingReplayDirector.Video
{
    [Serializable]
    public sealed class OutputOptions
    {
        internal static readonly string[] Containers = { "mp4", "mkv", "mov", "webm", "avi", "ts", "ogv", "flv", "nut", "mxf", "wmv", "asf", "3gp", "dv", "gif" };
        internal const int DefaultX265Crf = 17;
        internal const int DefaultAacBitrate = 224;

        public string Container = "mkv";
        public string PixelFormat = "";
        public string ColourMatrix = "auto";
        public string ColourRange = "auto";
        public EncoderSettings Video = new EncoderSettings { Encoder = "libx265", Mode = "CRF", Value = DefaultX265Crf };
        public EncoderSettings Audio = new EncoderSettings { Encoder = "aac", Mode = "Bitrate", Value = DefaultAacBitrate };

        internal static OutputOptions Load()
        {
            var saved = Settings.Default.FfmpegOutputOptions;
            if (!string.IsNullOrWhiteSpace(saved))
            {
                try
                {
                    var value = JsonConvert.DeserializeObject<OutputOptions>(saved);
                    if (value != null && value.Video != null && value.Audio != null
                        && Array.IndexOf(Containers, value.Container) >= 0)
                        return value;
                }
                catch (JsonException) { }
            }
            return new OutputOptions();
        }

        internal void Save()
        {
            Settings.Default.FfmpegOutputOptions = JsonConvert.SerializeObject(this);
            Settings.Default.Save();
        }

        internal void AddEncoderArguments(List<string> arguments, bool includeAudio)
        {
            Video.AddArguments(arguments, "v");
            if (includeAudio)
                Audio.AddArguments(arguments, "a");
            if (!string.IsNullOrWhiteSpace(PixelFormat))
                arguments.AddRange(new[] { "-pix_fmt", PixelFormat });
            if (ColourMatrix == "bt709" || ColourMatrix == "smpte170m" || ColourMatrix == "bt2020nc")
                arguments.AddRange(new[] { "-colorspace", ColourMatrix });
            if (ColourRange == "tv" || ColourRange == "pc")
                arguments.AddRange(new[] { "-color_range", ColourRange });
        }

        internal string ColourFilter()
        {
            var matrix = ColourMatrix == "bt709" || ColourMatrix == "smpte170m" || ColourMatrix == "bt2020nc" ? ColourMatrix : "auto";
            var range = ColourRange == "tv" || ColourRange == "pc" ? ColourRange : "auto";
            return matrix == "auto" && range == "auto" ? "" : "scale=out_color_matrix=" + matrix + ":out_range=" + range;
        }
    }

    [Serializable]
    public sealed class EncoderSettings
    {
        public string Encoder;
        public string Mode = "Encoder default";
        public decimal Value;
        public int SampleRate;
        public int Channels;
        public Dictionary<string, string> Options = new Dictionary<string, string>();

        internal static int AudioQualityMaximum(string encoder)
        {
            switch (encoder)
            {
                case "aac": return 6;
                case "libmp3lame": return 9;
                case "libvorbis": return 10;
                default: return 0;
            }
        }

        internal void AddArguments(List<string> arguments, string stream)
        {
            arguments.AddRange(new[] { "-c:" + stream, Encoder });
            var value = Value.ToString(CultureInfo.InvariantCulture);
            switch (Mode)
            {
                case "Bitrate":
                    arguments.AddRange(new[] { "-b:" + stream, value + "k" });
                    break;
                case "CRF":
                    arguments.AddRange(new[] { "-crf:" + stream, value });
                    if (Encoder.StartsWith("libvpx", StringComparison.Ordinal) || Encoder == "libaom-av1")
                        arguments.AddRange(new[] { "-b:" + stream, "0" });
                    break;
                case "CQ":
                    arguments.AddRange(new[] { "-cq:" + stream, value });
                    if (Encoder.EndsWith("_nvenc", StringComparison.Ordinal))
                        arguments.AddRange(new[] { "-rc:" + stream, "vbr", "-b:" + stream, "0" });
                    break;
                case "QP":
                    arguments.AddRange(new[] { "-qp:" + stream, value });
                    break;
                case "VBR quality":
                    arguments.AddRange(new[] { "-q:" + stream, value });
                    break;
            }
            if (Options != null)
                foreach (var option in Options)
                {
                    if (!Regex.IsMatch(option.Key, @"^[a-zA-Z0-9_-]+$"))
                        throw new InvalidOperationException("Invalid FFmpeg encoder option: " + option.Key);
                    arguments.AddRange(new[] { "-" + option.Key + ":" + stream, option.Value });
                }
            if (stream == "a" && SampleRate > 0)
                arguments.AddRange(new[] { "-ar:a", SampleRate.ToString(CultureInfo.InvariantCulture) });
            if (stream == "a" && Channels > 0)
                arguments.AddRange(new[] { "-ac:a", Channels.ToString(CultureInfo.InvariantCulture) });
        }
    }
}
