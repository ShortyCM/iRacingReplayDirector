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

using iRacingReplayDirector.Phases.Direction;
using iRacingReplayDirector.Support;
using iRacingReplayDirector.Video;
using iRacingReplayDirector.Phases.Transcoding;
using iRacingSDK.Support;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace iRacingReplayDirector
{
    public partial class TestVideoCapture : Form
    {
        MyListener listener;
        Task task;

        public TestVideoCapture()
        {
            InitializeComponent();
        }

        void TestVideoCapture_Load(object sender, EventArgs e)
        {
            workingFolderTextBox.Text = Settings.Default.WorkingFolder;

            listener = new MyListener(this.TraceMessageTextBox);
            Trace.Listeners.Add(listener);
        }

        void TestVideoCapture_FormClosed(object sender, FormClosedEventArgs e)
        {
            Trace.Listeners.Remove(listener);
        }

        void workingFolderButton_Click(object sender, EventArgs e)
        {
            var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = workingFolderTextBox.Text;

            if (fbd.ShowDialog() == DialogResult.OK)
                workingFolderTextBox.Text = fbd.SelectedPath;
        }

        void workingFolderTextBox_TextChanged(object sender, EventArgs e)
        {
            Settings.Default.WorkingFolder = workingFolderTextBox.Text;
            Settings.Default.Save();
        }

        void testVideoCaptureButton_Click(object sender, EventArgs e)
        {
            testVideoCaptureButton.Enabled = false;

            if (task != null)
            {
                task.Wait(1.Second());

                task.Dispose();
            }

            var workingFolder = workingFolderTextBox.Text;
            var context = SynchronizationContext.Current;

            task = new Task(() => RunTest(workingFolder, context));

            TraceMessageTextBox.Text = "";

            task.Start();
        }

        void RunTest(string workingFolder, SynchronizationContext context)
        {
            try
            {
                TraceInfo.WriteLine("Switching to iRacing ....");

                var hwnd = Win32.Messages.FindWindow(null, "iRacing.com Simulator");
                Win32.Messages.ShowWindow(hwnd, Win32.Messages.SW_SHOWNORMAL);
                Win32.Messages.SetForegroundWindow(hwnd);
                Thread.Sleep(2000);

                TraceInfo.WriteLine("Begining Test....");
                var videoCapture = new VideoCapture();

                TraceInfo.WriteLine("Broadcasting keypress {0} to activate your video capture software", Settings.Default.strHotKeyStopStart);
                videoCapture.Activate(workingFolder);

                TraceInfo.WriteLine("Expecting video file to be written in folder: {0}", workingFolder);

                TraceInfo.WriteLine("Waiting for 5 seconds");

                for (var i = 5; i >= 0; i--)
                {
                    Thread.Sleep(1.Seconds());
                    TraceInfo.WriteLine("{0} Seconds...", i);
                }

                TraceInfo.WriteLine("Broadcasting keypress {0} to deactivate your video capture software", Settings.Default.strHotKeyStopStart);
                var filenames = videoCapture.Deactivate();


                TraceInfo.WriteLine("Minimising iRacing");

                AltTabBackToApp();

                if (filenames.Count == 0)
                {
                    TraceInfo.WriteLine("\nFailure - Did not find any video files");
                    return;
                }

                if (filenames.Count != 1)
                {
                    TraceInfo.WriteLine("\nFailure - Found more than 1 video file!");
                    return;
                }

                var filename = filenames[0];

                if (filename != null)
                {
                    TraceInfo.WriteLine("");
                    TraceInfo.WriteLine("Found your video file {0}.", filename);

                    TranscodeVideoTest(filename.FileName);
                }
                else
                {
                    TraceInfo.WriteLine("");
                    TraceInfo.WriteLine("Failure!");
                }
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

        private static void AltTabBackToApp()
        {
            KeyboardEmulator.SendHotkey(KeyboardEmulator.Win32VirtualKeyCodes.VK_TAB, KeyboardEmulator.Win32VirtualKeyCodes.VK_MENU);
            //Keyboard.keybd_event(Keyboard.VK_MENU, 0, 0, UIntPtr.Zero);
            //Thread.Sleep(200);
            //Keyboard.keybd_event(Keyboard.VK_TAB, 0, 0, UIntPtr.Zero);
            //Thread.Sleep(200);
            //Keyboard.keybd_event(Keyboard.VK_TAB, 0, Keyboard.KEYEVENTF_KEYUP, UIntPtr.Zero);
            //Thread.Sleep(200);
            //Keyboard.keybd_event(Keyboard.VK_MENU, 0, Keyboard.KEYEVENTF_KEYUP, UIntPtr.Zero);
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


    }
}
