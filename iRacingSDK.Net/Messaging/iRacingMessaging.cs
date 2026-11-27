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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace iRacingSDK
{
    public class iRacingMessaging
    {
        protected readonly int messageId;
        readonly object messageQueueLock = new object();
        DateTime lastMessagePostedTime = DateTime.Now;
        Task currentMessageTask = Task.CompletedTask;
        const double MessageThrottleTime = 1000;

        protected iRacingMessaging()
        {
            messageId = NativeSdk.GetBroadcastMessageId();
        }

        protected virtual void SendMessage(BroadcastMessage message, short var1 = 0, int var2 = 0)
        {
            lock (messageQueueLock)
            {
                currentMessageTask = currentMessageTask.ContinueWith(previous =>
                {
                    previous.GetAwaiter().GetResult();
                    var elapsed = DateTime.Now - lastMessagePostedTime;
                    var throttleTime = (int)(MessageThrottleTime - elapsed.TotalMilliseconds);
                    if (throttleTime > 0)
                    {
                        Trace.WriteLine(string.Format("Throttle message {0} delivery to iRacing by {1} millisecond", message, throttleTime), "DEBUG");
                        Thread.Sleep(throttleTime);
                    }
                    lastMessagePostedTime = DateTime.Now;
                    NativeSdk.SendBroadcast(message, var1, var2);
                }, TaskScheduler.Default);
            }
        }

        protected void SendMessage(BroadcastMessage message, short var1, short var2, short var3)
        {
            var var23 = FromShorts(var2, var3);
            SendMessage(message, var1, var23);
        }
        
        protected static int FromShorts(short lowPart, short highPart)
        {
            return ((int)highPart << 16) | (ushort)lowPart;
        }

        public virtual void Wait()
        {
            Task pendingMessages;
            lock (messageQueueLock)
                pendingMessages = currentMessageTask;
            pendingMessages.Wait();
        }
    }
}
