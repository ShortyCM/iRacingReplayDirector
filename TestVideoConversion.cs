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

using iRacingReplayDirector.Support;
using iRacingReplayDirector.Video;
using iRacingReplayDirector.Phases.Transcoding;
using iRacingSDK.Support;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace iRacingReplayDirector
{
    public partial class TestVideoConversion : Form
    {
        MyListener listener;
        Task task;

        public TestVideoConversion()
        {
            InitializeComponent();
            WindowPlacement.Remember(this);
        }

        void TestVideoCapture_Load(object sender, EventArgs e)
        {
            sourceFilenameTextBox.Text = "";

            listener = new MyListener(this.TraceMessageTextBox);
            Trace.Listeners.Add(listener);

            sourceFilenameTextBox_TextChanged(null, null);
        }

        void TestVideoCapture_FormClosed(object sender, FormClosedEventArgs e)
        {
            Trace.Listeners.Remove(listener);
        }

        void workingFolderButton_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog();

            dlg.InitialDirectory = sourceFilenameTextBox.Text;
            dlg.Filter = Ffmpeg.RecordingFilter;

            if (dlg.ShowDialog() == DialogResult.OK)
                sourceFilenameTextBox.Text = dlg.FileName;
        }

        void testVideoCaptureButton_Click(object sender, EventArgs e)
        {
            testVideoCaptureButton.Enabled = false;

            if (task != null)
            {
                task.Wait(1.Second());

                task.Dispose();
            }

            var workingFolder = sourceFilenameTextBox.Text;
            var context = SynchronizationContext.Current;

            task = new Task(() => RunTest(workingFolder, context));

            TraceMessageTextBox.Text = "";

            task.Start();
        }

        void RunTest(string filename, SynchronizationContext context)
        {
            try
            {
                TranscodeVideoTest(filename);
            }
            catch (Exception e)
            {
                TraceError.WriteLine(e.Message);
                TraceError.WriteLine(e.StackTrace);
            }
            finally
            {
                context.Post(ignored => testVideoCaptureButton.Enabled = true, null);
            }
        }

        void TranscodeVideoTest(string filename)
        {
            var details = VideoAttributes.TestFor(filename);
            TraceInfo.WriteLine("Video: {0} {1}x{2} at {3:0.###} fps. Audio: {4}.",
                details.VideoEncoding, details.FrameSize.Width, details.FrameSize.Height,
                details.FrameRate, details.AudioEncoding);
            var lastSecond = -1L;
            var destination = Transcoder.Preview(filename, details, OutputOptions.Load(), ticks =>
            {
                var seconds = ticks / TimeSpan.TicksPerSecond;
                if (seconds != lastSecond)
                    TraceInfo.WriteLine("Encoded {0} seconds", seconds);
                lastSecond = seconds;
            });
            TraceInfo.WriteLine("Video converted. Review {0} to check the result.", destination);
        }


        private void sourceFilenameTextBox_TextChanged(object sender, EventArgs e)
        {
            var exists = File.Exists(sourceFilenameTextBox.Text);
            this.testVideoCaptureButton.Enabled = exists;
        }
    }
}
