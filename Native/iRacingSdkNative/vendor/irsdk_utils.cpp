/*
Copyright (c) 2013, iRacing.com Motorsport Simulations, LLC.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * Neither the name of iRacing.com Motorsport Simulations nor the
      names of its contributors may be used to endorse or promote products
      derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#define MIN_WIN_VER 0x0501

#ifndef WINVER
#	define WINVER			MIN_WIN_VER
#endif

#ifndef _WIN32_WINNT
#	define _WIN32_WINNT		MIN_WIN_VER 
#endif

#include <windows.h>
#include <stdio.h>
#include <time.h>
#include <limits.h>

#ifdef _MSC_VER
#include <crtdbg.h>
#endif

// memory read barrier, keeps compiler from reordering our memory reads
#include <intrin.h>
#pragma intrinsic(_ReadBarrier)

#include "irsdk_defines.h"

// for timeBeginPeriod()
#pragma comment(lib, "Winmm")
// for RegisterWindowMessage() and SendMessage()
#pragma comment(lib, "User32")

// Replay Director supplies one context per managed data feed.
#include "../IrsdkContext.h"

thread_local IrsdkContext* activeIrsdkContext = nullptr;
static const double timeout = 30.0; // timeout after 30 seconds with no communication


// Function Implementations

bool irsdk_startup()
{
	if(!activeIrsdkContext->memoryMap)
	{
		activeIrsdkContext->memoryMap = OpenFileMapping( FILE_MAP_READ, FALSE, IRSDK_MEMMAPFILENAME);
		activeIrsdkContext->lastTickCount = INT_MAX;
	}

	if(activeIrsdkContext->memoryMap)
	{
		if(!activeIrsdkContext->sharedMemory)
		{
			activeIrsdkContext->sharedMemory = (const char *)MapViewOfFile(activeIrsdkContext->memoryMap, FILE_MAP_READ, 0, 0, 0);
			MEMORY_BASIC_INFORMATION mapping = {};
			if (!activeIrsdkContext->sharedMemory
				|| !VirtualQuery(activeIrsdkContext->sharedMemory, &mapping, sizeof(mapping))
				|| mapping.RegionSize < sizeof(irsdk_header))
				return false;
			activeIrsdkContext->mappedLength = mapping.RegionSize;
			activeIrsdkContext->header = (const irsdk_header *)activeIrsdkContext->sharedMemory;
			activeIrsdkContext->lastTickCount = INT_MAX;
		}

		if(activeIrsdkContext->sharedMemory)
		{
			if(!activeIrsdkContext->dataValidEvent)
			{
				activeIrsdkContext->dataValidEvent = OpenEvent(SYNCHRONIZE, false, IRSDK_DATAVALIDEVENTNAME);
				activeIrsdkContext->lastTickCount = INT_MAX;
			}

			if(activeIrsdkContext->dataValidEvent)
			{
				activeIrsdkContext->initialized = true;
				return activeIrsdkContext->initialized;
			}
			//else printf("Error opening event: %d\n", GetLastError()); 
		}
		//else printf("Error mapping file: %d\n", GetLastError()); 
	}
	//else printf("Error opening file: %d\n", GetLastError()); 

	activeIrsdkContext->initialized = false;
	return activeIrsdkContext->initialized;
}

void irsdk_shutdown()
{
	if(activeIrsdkContext->dataValidEvent)
		CloseHandle(activeIrsdkContext->dataValidEvent);

	if(activeIrsdkContext->sharedMemory)
		UnmapViewOfFile(activeIrsdkContext->sharedMemory);

	if(activeIrsdkContext->memoryMap)
		CloseHandle(activeIrsdkContext->memoryMap);

	activeIrsdkContext->dataValidEvent = NULL;
	activeIrsdkContext->sharedMemory = NULL;
	activeIrsdkContext->header = NULL;
	activeIrsdkContext->memoryMap = NULL;
	activeIrsdkContext->mappedLength = 0;

	activeIrsdkContext->initialized = false;
	activeIrsdkContext->lastTickCount = INT_MAX;
}

bool irsdk_getNewData(char *data)
{
	if(activeIrsdkContext->initialized || irsdk_startup())
	{
#ifdef _MSC_VER
		_ASSERTE(NULL != activeIrsdkContext->header);
#endif

		// if sim is not active, then no new data
		if(!(activeIrsdkContext->header->status & irsdk_stConnected))
		{
			activeIrsdkContext->lastTickCount = INT_MAX;
			return false;
		}

		const int bufferCount = activeIrsdkContext->header->numBuf;
		if (bufferCount <= 0 || bufferCount > IRSDK_MAX_BUFS)
			return false;

		// Use curBuf to find the latest sample, or catch up if the next tick is still buffered.
		int latest = activeIrsdkContext->header->curBuf;
		if(latest >= bufferCount)
			latest = 0;
		const int previousTick = activeIrsdkContext->lastTickCount;
		if (previousTick != INT_MIN && previousTick != INT_MAX)
		{
			for (int index = 0; index < bufferCount; ++index)
			{
				if (activeIrsdkContext->header->varBuf[index].tickCount == previousTick + 1)
				{
					latest = index;
					break;
				}
			}
		}

		// if newer than last recieved, than report new data
		if(activeIrsdkContext->lastTickCount < activeIrsdkContext->header->varBuf[latest].tickCount)
		{
			// if asked to retrieve the data
			if(data)
			{
				// try twice to get the data out
				for(int count = 0; count < 2; count++)
				{
					// Read tickCount (updated AFTER write completes)
					int curTickCount = activeIrsdkContext->header->varBuf[latest].tickCount;

					_ReadBarrier();

					const int sampleLength = activeIrsdkContext->header->bufLen;
					const int sampleOffset = activeIrsdkContext->header->varBuf[latest].bufOffset;
					if (sampleLength <= 0 || sampleLength > activeIrsdkContext->sampleCapacity
						|| !activeIrsdkContext->Contains(sampleOffset, sampleLength))
						return false;
					memcpy(data, activeIrsdkContext->sharedMemory + sampleOffset, sampleLength);

					_ReadBarrier();

					// Read tickCountBegin (updated BEFORE write starts)
					// If they match, no write was in progress during our read
					if(curTickCount == activeIrsdkContext->header->varBuf[latest].tickCountBegin)
					{
						activeIrsdkContext->lastTickCount = curTickCount;
						activeIrsdkContext->lastValidTime = time(NULL);
						return true;
					}
				}
				// if here, the data changed out from under us.
				return false;
			}
			else
			{
				activeIrsdkContext->lastTickCount =  activeIrsdkContext->header->varBuf[latest].tickCount;
				activeIrsdkContext->lastValidTime = time(NULL);
				return true;
			}
		}
		// if older than last recieved, than reset, we probably disconnected
		else if(activeIrsdkContext->lastTickCount >  activeIrsdkContext->header->varBuf[latest].tickCount)
		{
			activeIrsdkContext->lastTickCount =  activeIrsdkContext->header->varBuf[latest].tickCount;
			return false;
		}
		// else the same, and nothing changed this tick
	}

	return false;
}


bool irsdk_waitForDataReady(int timeOut, char *data)
{
#ifdef _MSC_VER
	_ASSERTE(timeOut >= 0);
#endif

	if(activeIrsdkContext->initialized || irsdk_startup())
	{
		// just to be sure, check before we sleep
		if(irsdk_getNewData(data))
			return true;

		// sleep till signaled
		WaitForSingleObject(activeIrsdkContext->dataValidEvent, timeOut);

		// we woke up, so check for data
		if(irsdk_getNewData(data))
			return true;
		else
			return false;
	}

	// sleep if error
	if(timeOut > 0)
		Sleep(timeOut);

	return false;
}

bool irsdk_isConnected()
{
	if(activeIrsdkContext->initialized)
	{
		int elapsed = (int)difftime(time(NULL), activeIrsdkContext->lastValidTime);
		return (activeIrsdkContext->header->status & irsdk_stConnected) > 0 && elapsed < timeout;
	}

	return false;
}

const irsdk_header *irsdk_getHeader()
{
	if(activeIrsdkContext->initialized)
	{
		return activeIrsdkContext->header;
	}

	return NULL;
}

// direct access to the data buffer
// Warnign! This buffer is volitile so read it out fast!
// Use the cached copy from irsdk_waitForDataReady() or irsdk_getNewData() instead
const char *irsdk_getData(int index)
{
	if(activeIrsdkContext->initialized)
	{
		return activeIrsdkContext->sharedMemory + activeIrsdkContext->header->varBuf[index].bufOffset;
	}

	return NULL;
}

const char *irsdk_getSessionInfoStr()
{
	if(activeIrsdkContext->initialized)
	{
		return activeIrsdkContext->sharedMemory + activeIrsdkContext->header->sessionInfoOffset;
	}
	return NULL;
}

int irsdk_getSessionInfoStrUpdate()
{
	if(activeIrsdkContext->initialized)
	{
		return activeIrsdkContext->header->sessionInfoUpdate;
	}
	return -1;
}

const irsdk_varHeader *irsdk_getVarHeaderPtr()
{
	if(activeIrsdkContext->initialized)
	{
		return ((irsdk_varHeader*)(activeIrsdkContext->sharedMemory + activeIrsdkContext->header->varHeaderOffset));
	}
	return NULL;
}

const irsdk_varHeader *irsdk_getVarHeaderEntry(int index)
{
	if(activeIrsdkContext->initialized)
	{
		if(index >= 0 && index < activeIrsdkContext->header->numVars)
		{
			return &((irsdk_varHeader*)(activeIrsdkContext->sharedMemory + activeIrsdkContext->header->varHeaderOffset))[index];
		}
	}
	return NULL;
}

// Note: this is a linear search, so cache the results
int irsdk_varNameToIndex(const char *name)
{
	const irsdk_varHeader *pVar;

	if(name)
	{
		for(int index=0; index<activeIrsdkContext->header->numVars; index++)
		{
			pVar = irsdk_getVarHeaderEntry(index);
			if(pVar && 0 == strncmp(name, pVar->name, IRSDK_MAX_STRING))
			{
				return index;
			}
		}
	}

	return -1;
}

int irsdk_varNameToOffset(const char *name)
{
	const irsdk_varHeader *pVar;

	if(name)
	{
		for(int index=0; index<activeIrsdkContext->header->numVars; index++)
		{
			pVar = irsdk_getVarHeaderEntry(index);
			if(pVar && 0 == strncmp(name, pVar->name, IRSDK_MAX_STRING))
			{
				return pVar->offset;
			}
		}
	}

	return -1;
}

unsigned int irsdk_getBroadcastMsgID()
{
	static unsigned int msgId = RegisterWindowMessage(IRSDK_BROADCASTMSGNAME); 

	return msgId;
}

void irsdk_broadcastMsg(irsdk_BroadcastMsg msg, int var1, int var2, int var3)
{
	irsdk_broadcastMsg(msg, var1, (int)MAKELONG(var2, var3));
}

void irsdk_broadcastMsg(irsdk_BroadcastMsg msg, int var1, float var2)
{
	// multiply by 2^16-1 to move fractional part to the integer part
	int real = (int)(var2 * 65536.0f);

	irsdk_broadcastMsg(msg, var1, real);
}

void irsdk_broadcastMsg(irsdk_BroadcastMsg msg, int var1, int var2)
{
	static unsigned int msgId = irsdk_getBroadcastMsgID();

	if(msgId && msg >= 0 && msg < irsdk_BroadcastLast)
	{
		SendNotifyMessage(HWND_BROADCAST, msgId, MAKELONG(msg, var1), var2);
	}
}

int irsdk_padCarNum(int num, int zero)
{
	int retVal = num;
	int numPlace = 1;
	if(num > 99)
		numPlace = 3;
	else if(num > 9)
		numPlace = 2;
	if(zero)
	{
		numPlace += zero;
		retVal = num + 1000*numPlace;
	}

	return retVal;
}
