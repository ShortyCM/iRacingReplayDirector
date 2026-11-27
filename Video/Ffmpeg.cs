using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace iRacingReplayDirector.Video
{
    internal static class Ffmpeg
    {
        private static readonly Regex EncoderLine = new Regex(@"^\s*([VA])\S{5}\s+([\w-]+)\s+(.+)$", RegexOptions.Multiline);
        private static readonly Regex OptionLine = new Regex(@"^\s{1,4}-([\w-]+)\s+<([^>]+)>\s+E\S*\s+([^\r\n]+)", RegexOptions.Multiline);
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        internal const string RecordingFilter = "Video files (*.mp4;*.mkv;*.avi;*.mov;*.webm;*.ts)|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.ts|All files (*.*)|*.*";
        internal static readonly string[] RecordingExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".ts" };

        internal sealed class Encoder
        {
            internal readonly string Name;
            internal readonly string Description;
            internal readonly bool IsVideo;

            internal Encoder(string name, string description, bool isVideo)
            {
                Name = name;
                Description = description;
                IsVideo = isVideo;
            }

            public override string ToString()
            {
                return Name.Length == 0 ? Description : Name + " - " + Description;
            }
        }

        internal sealed class EncoderOptions
        {
            internal readonly Dictionary<string, string> Options;
            internal readonly HashSet<string> FractionalOptions;
            internal readonly string[] PixelFormats;

            internal EncoderOptions(string help)
            {
                Options = new Dictionary<string, string>(StringComparer.Ordinal);
                FractionalOptions = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match option in OptionLine.Matches(help))
                {
                    var name = option.Groups[1].Value;
                    Options[name] = option.Groups[3].Value.Trim();
                    var type = option.Groups[2].Value;
                    if (type == "float" || type == "double")
                        FractionalOptions.Add(name);
                }

                var pixelFormats = Regex.Match(help, @"^\s*Supported pixel formats:\s*([^\r\n]+)", RegexOptions.Multiline);
                PixelFormats = pixelFormats.Success
                    ? pixelFormats.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    : new string[0];
            }
        }

        internal static Encoder[] Encoders()
        {
            var output = Query("ffmpeg", "-hide_banner", "-encoders");
            return EncoderLine.Matches(output).Cast<Match>()
                .Select(m => new Encoder(m.Groups[2].Value, m.Groups[3].Value.Trim(), m.Groups[1].Value == "V"))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static EncoderOptions Describe(string encoder)
        {
            return new EncoderOptions(Query("ffmpeg", "-hide_banner", "-h", "encoder=" + encoder));
        }

        internal static JObject Probe(string filename)
        {
            return JObject.Parse(Query("ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", filename));
        }

        internal static string Seconds(double seconds)
        {
            return seconds.ToString("0.#########", Invariant);
        }

        internal static void Encode(IEnumerable<string> arguments, Action<Stream> writeOverlay,
            Func<bool> isAborted, Action<long> progress)
        {
            using (var process = Start("ffmpeg", new[] { "-hide_banner", "-loglevel", "error", "-nostdin",
                "-nostats", "-progress", "pipe:1", "-abort_on", "empty_output_stream", "-y" }.Concat(arguments), writeOverlay != null))
            {
                var errors = process.StandardError.ReadToEndAsync();
                var report = Task.Run(() =>
                {
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        long microseconds;
                        if (progress != null && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                            && long.TryParse(line.Substring(12), NumberStyles.Integer, Invariant, out microseconds))
                            progress(Math.Max(0, microseconds) * 10);
                    }
                });

                Exception inputError = null;
                using (var cancellation = isAborted == null ? null : new Timer(_ =>
                {
                    if (isAborted())
                        Kill(process);
                }, null, 100, 100))
                {
                    try
                    {
                        if (writeOverlay != null)
                            using (var input = process.StandardInput.BaseStream)
                                writeOverlay(input);
                    }
                    catch (Exception ex)
                    {
                        inputError = ex;
                        if (!(ex is IOException))
                            Kill(process);
                    }

                    process.WaitForExit();
                    report.GetAwaiter().GetResult();
                }

                if (isAborted != null && isAborted())
                    throw new OperationCanceledException();
                if (inputError != null && !(inputError is IOException))
                    throw new InvalidOperationException("Unable to render the video overlay.", inputError);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("FFmpeg could not encode the video. " + errors.GetAwaiter().GetResult().Trim());
                if (inputError != null)
                    throw new IOException("FFmpeg stopped reading the overlay before it was complete.", inputError);
            }
        }

        private static string Query(string name, params string[] arguments)
        {
            using (var process = Start(name, arguments, false))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    Kill(process);
                    throw new TimeoutException(name + " did not respond.");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(name + " could not read the video or encoder information. " + errors.GetAwaiter().GetResult().Trim());
                return output.GetAwaiter().GetResult();
            }
        }

        private static Process Start(string name, IEnumerable<string> arguments, bool redirectInput)
        {
            var besideApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".exe");
            try
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName = File.Exists(besideApp) ? besideApp : name + ".exe",
                    Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = redirectInput,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                });
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                throw new InvalidOperationException("Could not start " + name + ".exe. Put the FFmpeg tools on PATH or next to Replay Director.", e);
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string QuoteArgument(string argument)
        {
            var quoted = new StringBuilder().Append('"');
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
                quoted.Append(character);
                backslashes = 0;
            }
            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
