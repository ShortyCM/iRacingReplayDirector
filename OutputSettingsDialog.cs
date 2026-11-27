using iRacingReplayDirector.Video;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace iRacingReplayDirector
{
    internal sealed class OutputSettingsDialog : Form
    {
        private readonly ComboBox container = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox colourMatrix = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox colourRange = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly EncoderEditor video;
        private readonly EncoderEditor audio;

        internal OutputOptions Output { get; private set; }

        internal OutputSettingsDialog(OutputOptions current)
        {
            Text = "Output settings";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 435);
            MinimumSize = new Size(680, 420);
            AutoScaleMode = AutoScaleMode.Font;
            ShowInTaskbar = false;

            var encoders = Ffmpeg.Encoders();
            video = new EncoderEditor("Video", encoders.Where(e => e.IsVideo).ToArray(), current.Video, current.PixelFormat);
            var audioEncoders = new[] { new Ffmpeg.Encoder("", "No audio", false) }.Concat(encoders.Where(e => !e.IsVideo)).ToArray();
            audio = new EncoderEditor("Audio", audioEncoders, current.Audio, "");
            container.Items.AddRange(OutputOptions.Containers);
            container.SelectedItem = current.Container;
            if (container.SelectedIndex < 0)
                container.SelectedIndex = 0;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var format = new TableLayoutPanel { ColumnCount = 6, Dock = DockStyle.Fill, AutoSize = true };
            colourMatrix.Items.AddRange(new object[] { "Automatic", "BT.709", "BT.601", "BT.2020" });
            colourRange.Items.AddRange(new object[] { "Automatic", "Limited", "Full" });
            colourMatrix.SelectedIndex = Math.Max(0, Array.IndexOf(Matrices, current.ColourMatrix));
            colourRange.SelectedIndex = Math.Max(0, Array.IndexOf(Ranges, current.ColourRange));
            AddFormatField(format, 0, "Container", container);
            AddFormatField(format, 2, "Colour matrix", colourMatrix);
            AddFormatField(format, 4, "Colour range", colourRange);
            root.Controls.Add(format, 0, 0);
            root.SetColumnSpan(format, 2);
            root.Controls.Add(video, 0, 1);
            root.Controls.Add(audio, 1, 1);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            save.Click += SaveOutput;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            root.Controls.Add(buttons, 0, 2);
            root.SetColumnSpan(buttons, 2);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static readonly string[] Matrices = { "auto", "bt709", "smpte170m", "bt2020nc" };
        private static readonly string[] Ranges = { "auto", "tv", "pc" };

        private static void AddFormatField(TableLayoutPanel panel, int column, string name, Control control)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            panel.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = name }, column, 0);
            panel.Controls.Add(control, column + 1, 0);
        }

        private void SaveOutput(object sender, EventArgs e)
        {
            try
            {
                Output = new OutputOptions
                {
                    Container = (string)container.SelectedItem,
                    Video = video.GetSettings(),
                    Audio = audio.GetSettings(),
                    PixelFormat = video.PixelFormat,
                    ColourMatrix = Matrices[colourMatrix.SelectedIndex],
                    ColourRange = Ranges[colourRange.SelectedIndex]
                };
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private sealed class EncoderEditor : GroupBox
        {
            private static readonly string[] X264AndX265Presets =
            {
                "ultrafast", "superfast", "veryfast", "faster", "fast",
                "medium", "slow", "slower", "veryslow", "placebo"
            };

            private readonly ComboBox encoder = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly ComboBox preset = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly Label presetLabel = new Label { Text = "Preset" };
            private readonly ComboBox mode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly ComboBox pixelFormat = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly NumericUpDown value = new NumericUpDown { Dock = DockStyle.Fill };
            private readonly NumericUpDown sampleRate = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 384000, Increment = 1000 };
            private readonly NumericUpDown channels = new NumericUpDown { Dock = DockStyle.Fill, Maximum = 32 };
            private readonly ComboBox option = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly TextBox options = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
            private readonly Label valueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            private readonly Label optionHelp = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Height = 35 };
            private readonly ToolTip toolTip = new ToolTip();
            private readonly bool isVideo;
            private Ffmpeg.EncoderOptions capabilities;

            internal EncoderEditor(string title, Ffmpeg.Encoder[] encoders, EncoderSettings current, string currentPixelFormat)
            {
                isVideo = title == "Video";
                Text = title;
                Dock = DockStyle.Fill;
                Padding = new Padding(8);
                var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                Controls.Add(fields);

                AddRow(fields, new Label { Text = "Encoder" }, encoder);
                if (isVideo)
                    AddRow(fields, presetLabel, preset);
                AddRow(fields, new Label { Text = "Rate control" }, mode);
                AddRow(fields, valueLabel, value);
                if (isVideo)
                    AddRow(fields, new Label { Text = "Pixel format" }, pixelFormat);
                else
                {
                    AddRow(fields, new Label { Text = "Sample rate Hz (0 = auto)" }, sampleRate);
                    AddRow(fields, new Label { Text = "Channels (0 = auto)" }, channels);
                }

                var addOption = new Button { Text = "Add", Dock = DockStyle.Right, Width = 55 };
                var optionPanel = new Panel { Dock = DockStyle.Fill, Height = option.PreferredHeight + 4 };
                optionPanel.Controls.Add(option);
                optionPanel.Controls.Add(addOption);
                AddRow(fields, new Label { Text = "Encoder option" }, optionPanel);
                AddRow(fields, new Label(), optionHelp);

                var optionsRow = fields.RowCount++;
                fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                fields.Controls.Add(options, 0, optionsRow);
                fields.SetColumnSpan(options, 2);
                toolTip.SetToolTip(options, "One encoder option per line, as name=value.");
                addOption.Click += (sender, e) => InsertOption();
                mode.SelectedIndexChanged += (sender, e) => UpdateRateControl();
                option.SelectedIndexChanged += (sender, e) =>
                {
                    var key = option.SelectedItem as string;
                    optionHelp.Text = key == null ? "" : capabilities.Options[key];
                    toolTip.SetToolTip(optionHelp, optionHelp.Text);
                };

                encoder.Items.AddRange(encoders);
                var selected = encoders.FirstOrDefault(item => item.Name == current.Encoder);
                encoder.SelectedItem = selected ?? encoders.FirstOrDefault();
                RefreshEncoder();
                if (selected != null)
                {
                    if (mode.Items.Contains(current.Mode))
                    {
                        mode.SelectedItem = current.Mode;
                        value.Value = Math.Max(value.Minimum, Math.Min(value.Maximum, current.Value));
                    }
                    if (isVideo && pixelFormat.Items.Contains(currentPixelFormat))
                        pixelFormat.SelectedItem = currentPixelFormat;
                    if (current.Options != null)
                    {
                        string savedPreset;
                        if (preset.Items.Count != 0 && current.Options.TryGetValue("preset", out savedPreset) && !string.IsNullOrWhiteSpace(savedPreset))
                        {
                            if (!preset.Items.Contains(savedPreset))
                                preset.Items.Add(savedPreset);
                            preset.SelectedItem = savedPreset;
                        }

                        options.Lines = current.Options.Where(pair => preset.Items.Count == 0 || pair.Key != "preset")
                            .Select(pair => pair.Key + "=" + pair.Value).ToArray();
                    }
                    sampleRate.Value = Math.Max(0, Math.Min(sampleRate.Maximum, current.SampleRate));
                    channels.Value = Math.Max(0, Math.Min(channels.Maximum, current.Channels));
                }

                encoder.SelectedIndexChanged += (sender, e) =>
                {
                    try { RefreshEncoder(); }
                    catch (Exception ex)
                    {
                        capabilities = null;
                        mode.Items.Clear();
                        option.Items.Clear();
                        optionHelp.Text = ex.Message;
                    }
                };
            }

            private static void AddRow(TableLayoutPanel fields, Label label, Control control)
            {
                label.AutoSize = true;
                label.Anchor = AnchorStyles.Left;
                var row = fields.RowCount++;
                fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                fields.Controls.Add(label, 0, row);
                fields.Controls.Add(control, 1, row);
            }

            private void RefreshEncoder()
            {
                var selected = encoder.SelectedItem as Ffmpeg.Encoder;
                preset.Items.Clear();
                presetLabel.Visible = preset.Visible = false;
                capabilities = selected == null || selected.Name.Length == 0 ? null : Ffmpeg.Describe(selected.Name);
                sampleRate.Enabled = capabilities != null;
                channels.Enabled = capabilities != null;
                sampleRate.Value = 0;
                channels.Value = 0;
                options.Clear();
                option.Items.Clear();
                optionHelp.Text = "";
                mode.Items.Clear();
                pixelFormat.Items.Clear();
                pixelFormat.Items.Add("Automatic");
                pixelFormat.SelectedIndex = 0;
                if (capabilities == null)
                    return;

                if (capabilities.Options.ContainsKey("preset") &&
                    (selected.Name == "libx265" || selected.Name == "libx264" || selected.Name == "libx264rgb"))
                {
                    preset.Items.Add("Encoder default");
                    preset.Items.AddRange(X264AndX265Presets);
                    preset.SelectedIndex = 0;
                    presetLabel.Visible = preset.Visible = true;
                }
                option.Items.AddRange(capabilities.Options.Keys.Where(name => preset.Items.Count == 0 || name != "preset")
                    .OrderBy(name => name).Cast<object>().ToArray());
                pixelFormat.Items.AddRange(capabilities.PixelFormats);
                mode.Items.Add("Encoder default");
                if (!Regex.IsMatch(selected.Name, @"^(pcm_|adpcm_|ffv1|alac$|flac$|huffyuv$|rawvideo$|utvideo$)"))
                    mode.Items.Add("Bitrate");
                foreach (var rateMode in new[] { "CRF", "CQ", "QP" })
                    if (capabilities.Options.ContainsKey(rateMode.ToLowerInvariant()))
                        mode.Items.Add(rateMode);
                if (EncoderSettings.AudioQualityMaximum(selected.Name) > 0)
                    mode.Items.Add("VBR quality");
                mode.SelectedItem = mode.Items.Contains("CRF") ? "CRF"
                    : selected.Name == "aac" ? "Bitrate" : "Encoder default";
            }

            private void UpdateRateControl()
            {
                var rateMode = mode.SelectedItem as string;
                var selected = encoder.SelectedItem as Ffmpeg.Encoder;
                value.Enabled = rateMode != null && rateMode != "Encoder default";
                valueLabel.Text = rateMode == "Bitrate" ? "Bitrate (kb/s)" : value.Enabled ? rateMode : "";
                value.DecimalPlaces = value.Enabled && (rateMode == "VBR quality"
                    || capabilities.FractionalOptions.Contains(rateMode.ToLowerInvariant())) ? 1 : 0;
                value.Minimum = 0;
                value.Maximum = rateMode == "Bitrate" ? 1000000 : 100;
                if (rateMode == "Bitrate")
                {
                    value.Minimum = 1;
                    value.Value = isVideo ? 15000 : selected.Name == "aac" ? OutputOptions.DefaultAacBitrate : 128;
                }
                else if (rateMode == "VBR quality")
                {
                    var name = selected.Name;
                    value.Minimum = name == "aac" ? 0.1m : name == "libvorbis" ? -1 : 0;
                    value.Maximum = EncoderSettings.AudioQualityMaximum(name);
                    value.Value = name == "aac" ? 1 : name == "libvorbis" ? 3 : 2;
                    toolTip.SetToolTip(value, name == "libmp3lame" ? "Lower values give higher quality." : "Higher values give higher quality.");
                }
                else if (value.Enabled)
                {
                    var name = selected.Name;
                    var max = name == "libx264" || name == "libx265" || name.EndsWith("_nvenc") ? 51 : 63;
                    var description = capabilities.Options[rateMode.ToLowerInvariant()];
                    var range = Regex.Match(description, @"\(from (-?\d+(\.\d+)?) to (\d+(\.\d+)?)\)");
                    decimal minimum, maximum;
                    if (range.Success && decimal.TryParse(range.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out minimum)
                        && decimal.TryParse(range.Groups[3].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out maximum))
                    {
                        value.Maximum = maximum;
                        value.Minimum = Math.Max(0, minimum);
                    }
                    else
                        value.Maximum = max;
                    var initialQuality = name == "libx265" && rateMode == "CRF" ? OutputOptions.DefaultX265Crf : 23;
                    value.Value = Math.Max(value.Minimum, Math.Min(value.Maximum, initialQuality));
                }
                if (rateMode != "VBR quality")
                    toolTip.SetToolTip(value, "");
            }

            private void InsertOption()
            {
                if (option.SelectedItem == null)
                    return;
                if (options.TextLength > 0 && !options.Text.EndsWith("\n", StringComparison.Ordinal))
                    options.AppendText(Environment.NewLine);
                options.AppendText(option.SelectedItem + "=");
                options.Focus();
            }

            internal string PixelFormat
            {
                get { return pixelFormat.SelectedIndex > 0 ? (string)pixelFormat.SelectedItem : ""; }
            }

            internal EncoderSettings GetSettings()
            {
                var selected = encoder.SelectedItem as Ffmpeg.Encoder;
                if (selected != null && !isVideo && selected.Name.Length == 0)
                    return new EncoderSettings { Encoder = "" };
                if (selected == null || capabilities == null)
                    throw new ArgumentException("Choose an available " + Text.ToLowerInvariant() + " encoder.");

                var settings = new EncoderSettings
                {
                    Encoder = selected.Name, Mode = (string)mode.SelectedItem, Value = value.Value,
                    SampleRate = (int)sampleRate.Value, Channels = (int)channels.Value
                };
                if (preset.SelectedIndex > 0)
                    settings.Options.Add("preset", (string)preset.SelectedItem);
                foreach (var line in options.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                {
                    var separator = line.IndexOf('=');
                    var key = separator < 0 ? "" : line.Substring(0, separator).Trim();
                    if (key == "preset" && preset.Items.Count != 0)
                        throw new ArgumentException("Set the encoder preset using the Preset drop-down.");
                    if (!capabilities.Options.ContainsKey(key) || string.IsNullOrWhiteSpace(line.Substring(separator + 1)))
                        throw new ArgumentException("Use a supported " + Text.ToLowerInvariant() + " encoder option as name=value: " + line);
                    if (settings.Options.ContainsKey(key))
                        throw new ArgumentException("Duplicate encoder option: " + key);
                    settings.Options.Add(key, line.Substring(separator + 1).Trim());
                }
                return settings;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    toolTip.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
