#pragma once

#include <windows.h>
#include <climits>
#include <cstddef>
#include <ctime>

#include "vendor/irsdk_defines.h"

struct IrsdkContext
{
    HANDLE dataValidEvent = nullptr;
    HANDLE memoryMap = nullptr;
    const char* sharedMemory = nullptr;
    const irsdk_header* header = nullptr;
    std::size_t mappedLength = 0;
    int lastTickCount = INT_MAX;
    bool initialized = false;
    time_t lastValidTime = 0;
    int sampleCapacity = 0;

    bool Contains(int offset, std::size_t length) const
    {
        return offset >= 0 && static_cast<std::size_t>(offset) <= mappedLength
            && length <= mappedLength - static_cast<std::size_t>(offset);
    }
};

extern thread_local IrsdkContext* activeIrsdkContext;
