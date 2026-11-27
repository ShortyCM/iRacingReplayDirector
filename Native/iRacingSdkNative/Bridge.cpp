#include "IrsdkContext.h"

#include <new>
#include <vector>

unsigned int irsdk_getBroadcastMsgID();

namespace
{
    struct Connection
    {
        IrsdkContext sdk;
        std::vector<char> telemetry;
    };

    struct Sample
    {
        const char* telemetry;
        const irsdk_varHeader* variables;
        const char* sessionInfo;
        int telemetryLength;
        int variableCount;
        int tickCount;
        int sessionInfoLength;
        int sessionInfoUpdate;
    };

    static_assert(sizeof(irsdk_varHeader) == 144, "Update the managed telemetry header layout.");
    static_assert(sizeof(Sample) == 48, "Update the managed sample layout.");

    class UseConnection
    {
    public:
        explicit UseConnection(IrsdkContext& context)
            : previous(activeIrsdkContext)
        {
            activeIrsdkContext = &context;
        }

        ~UseConnection()
        {
            activeIrsdkContext = previous;
        }

    private:
        IrsdkContext* previous;
    };
}

extern "C" __declspec(dllexport) Connection* __cdecl OpenConnection()
{
    return new (std::nothrow) Connection();
}

extern "C" __declspec(dllexport) void __cdecl CloseConnection(Connection* connection)
{
    UseConnection scope(connection->sdk);
    irsdk_shutdown();
    delete connection;
}

extern "C" __declspec(dllexport) int __cdecl ReadSample(
    Connection* connection, int timeoutMilliseconds, Sample* sample)
{
    UseConnection scope(connection->sdk);
    *sample = {};

    if (!connection->sdk.initialized)
    {
        if (!irsdk_startup())
        {
            irsdk_shutdown();
            Sleep(timeoutMilliseconds);
            return -1;
        }

        connection->sdk.lastValidTime = time(nullptr);
        connection->sdk.lastTickCount = INT_MIN;
    }

    const auto* header = irsdk_getHeader();
    if (header->ver != IRSDK_VER)
    {
        irsdk_shutdown();
        return -2;
    }

    if ((header->status & irsdk_stConnected) == 0)
    {
        irsdk_shutdown();
        Sleep(timeoutMilliseconds);
        return -1;
    }

    const int telemetryLength = header->bufLen;
    if (telemetryLength <= 0 || !connection->sdk.Contains(0, telemetryLength))
    {
        Sleep(timeoutMilliseconds);
        return 0;
    }

    try
    {
        connection->telemetry.resize(telemetryLength);
    }
    catch (const std::bad_alloc&)
    {
        return -3;
    }

    connection->sdk.sampleCapacity = telemetryLength;
    if (!irsdk_waitForDataReady(timeoutMilliseconds, connection->telemetry.data()))
    {
        if (irsdk_isConnected())
            return 0;

        irsdk_shutdown();
        return -1;
    }

    if (header->bufLen != telemetryLength)
        return 0;

    const int variableCount = header->numVars;
    const int variableOffset = header->varHeaderOffset;
    if (variableCount <= 0
        || !connection->sdk.Contains(variableOffset, sizeof(irsdk_varHeader) * variableCount))
        return 0;

    sample->telemetry = connection->telemetry.data();
    sample->variables = reinterpret_cast<const irsdk_varHeader*>(connection->sdk.sharedMemory + variableOffset);
    sample->telemetryLength = telemetryLength;
    sample->variableCount = variableCount;
    sample->tickCount = connection->sdk.lastTickCount;
    sample->sessionInfoUpdate = irsdk_getSessionInfoStrUpdate();
    const int sessionLength = header->sessionInfoLen;
    const int sessionOffset = header->sessionInfoOffset;
    if (sessionLength > 0 && connection->sdk.Contains(sessionOffset, sessionLength))
    {
        sample->sessionInfo = connection->sdk.sharedMemory + sessionOffset;
        sample->sessionInfoLength = sessionLength;
    }
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl GetSessionInfoUpdate(Connection* connection)
{
    UseConnection scope(connection->sdk);
    return irsdk_getSessionInfoStrUpdate();
}

extern "C" __declspec(dllexport) int __cdecl GetBroadcastMessageId()
{
    return irsdk_getBroadcastMsgID();
}

extern "C" __declspec(dllexport) void __cdecl SendBroadcast(int message, int value, int payload)
{
    irsdk_broadcastMsg(static_cast<irsdk_BroadcastMsg>(message), value, payload);
}
