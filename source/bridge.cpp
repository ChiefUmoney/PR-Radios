#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sddl.h>
#include <string>
#include <sstream>
#include <mutex>
#include <cmath>
#include <algorithm>
#include "ts3_functions.h"

#define API extern "C" __declspec(dllexport)
static TS3Functions ts;
static HANDLE stopEvent = NULL, worker = NULL, pipeHandle = INVALID_HANDLE_VALUE;
static std::mutex messageMutex;
static std::string pluginId, lastError, pendingReturnCode;
static ULONGLONG errorUntil = 0;

static std::string quote(const std::string& s) {
    std::string r = "\"";
    for (unsigned char c : s) {
        if(c == '"' || c == '\\') { r += '\\'; r += c; }
        else if(c < 32) { char b[8]; sprintf_s(b, "\\u%04x", c); r += b; }
        else r += c;
    }
    return r + "\"";
}
static void remember(const std::string& s) {
    std::lock_guard<std::mutex> lock(messageMutex);
    lastError = s; errorUntil = GetTickCount64() + 7000;
}
static void error(unsigned int code) {
    if(!code) return;
    char* s = NULL;
    if(!ts.getErrorMessage(code, &s) && s) { remember(s); ts.freeMemory(s); }
    else remember("TeamSpeak returned error " + std::to_string(code));
}
static std::string channelName(uint64 server, uint64 channel) {
    char* s = NULL; std::string name;
    if(!ts.getChannelVariableAsString(server, channel, CHANNEL_NAME, &s) && s) { name = s; ts.freeMemory(s); }
    return name;
}
static std::string snapshot(const std::string& command) {
    uint64 server = ts.getCurrentServerConnectionHandlerID();
    int status = 0; anyID me = 0; uint64 channel = 0;
    if(!server || ts.getConnectionStatus(server, &status) || status != STATUS_CONNECTION_ESTABLISHED ||
       ts.getClientID(server, &me) || ts.getChannelOfClient(server, me, &channel))
        return "{\"connected\":false,\"message\":\"Join a server in TeamSpeak 3\"}\n";
    std::istringstream in(command); std::string action; uint64 targetServer = 0; in >> action;
    if(action != "STATE") {
        in >> targetServer;
        if(!in || targetServer != server) remember("Server changed. Try again.");
        else if(action == "JOIN") {
            uint64 target = 0; in >> target;
            if(in && target) {
                char code[128] = {};
                { std::lock_guard<std::mutex> lock(messageMutex);
                  if(!pluginId.empty()) ts.createReturnCode(pluginId.c_str(), code, sizeof(code)); pendingReturnCode=code; }
                error(ts.requestClientMove(server, me, target, "", code[0] ? code : NULL));
            }
        } else if(action == "VOLUME") {
            float delta = 0, current = 0; in >> delta;
            if(in && std::isfinite(delta) && fabs(delta) <= 2) {
                unsigned int e = ts.getPlaybackConfigValueAsFloat(server, "volume_modifier", &current);
                if(!e) { char v[32]; sprintf_s(v, "%.1f", (double)(std::max)(-40.0f, (std::min)(20.0f,current + delta)));
                    e = ts.setPlaybackConfigValue(server, "volume_modifier", v); }
                error(e);
            }
        }
    }
    std::string speaker; bool transmitting = false, receiving = false;
    anyID* clients = NULL;
    if(!ts.getChannelClientList(server, channel, &clients) && clients) {
        for(int i=0; clients[i]; ++i) {
            int talking=0;
            if(!ts.getClientVariableAsInt(server, clients[i], CLIENT_FLAG_TALKING, &talking) && talking) {
                if(clients[i] == me) transmitting = true;
                else { receiving = true; char* name = NULL;
                    if(speaker.empty() && !ts.getClientVariableAsString(server, clients[i], CLIENT_NICKNAME, &name) && name) {
                        speaker = name; ts.freeMemory(name);
                    }
                }
            }
        }
        ts.freeMemory(clients);
    }
    float volume = 0; ts.getPlaybackConfigValueAsFloat(server, "volume_modifier", &volume);
    if(!std::isfinite(volume)) volume = 0;
    std::ostringstream out;
    out << "{\"connected\":true,\"server\":" << quote(std::to_string(server))
        << ",\"channel\":" << quote(std::to_string(channel))
        << ",\"name\":" << quote(channelName(server,channel))
        << ",\"speaker\":" << quote(speaker) << ",\"tx\":" << (transmitting ? "true":"false")
        << ",\"rx\":" << (receiving ? "true":"false") << ",\"volume\":" << volume << ",\"message\":";
    { std::lock_guard<std::mutex> lock(messageMutex); out << quote(GetTickCount64() < errorUntil ? lastError : ""); }
    out << ",\"channels\":[";
    uint64* channels = NULL;
    if(!ts.getChannelList(server, &channels) && channels) {
        for(int i=0; channels[i] && i<128; ++i) {
            if(i) out << ',';
            out << "{\"id\":" << quote(std::to_string(channels[i])) << ",\"name\":" << quote(channelName(server, channels[i])) << '}';
        }
        ts.freeMemory(channels);
    }
    out << "]}\n";
    return out.str();
}
static DWORD WINAPI run(void*) {
    bool connected = false; ULONGLONG since=0;
    while(WaitForSingleObject(stopEvent, 25) == WAIT_TIMEOUT) {
        if(!connected) {
            if(ConnectNamedPipe(pipeHandle,NULL) || GetLastError() == ERROR_PIPE_CONNECTED) { connected = true; since = GetTickCount64(); }
            continue;
        }
        char buffer[512] = {}; DWORD count=0;
        if(ReadFile(pipeHandle,buffer,sizeof(buffer)-1,&count,NULL) && count) {
            std::string response;
            try { response = snapshot(std::string(buffer,count)); }
            catch(...) { response = "{\"connected\":false,\"message\":\"Bridge error\"}\n"; }
            DWORD sent=0;
            WriteFile(pipeHandle,response.data(),(DWORD)response.size(),&sent,NULL);
            // Keep the response available until the client closes its end.
            since=GetTickCount64();
        } else {
            DWORD e=GetLastError();
            if(e == ERROR_BROKEN_PIPE || e == ERROR_NO_DATA || GetTickCount64()-since > 2000) {
                // ERROR_NO_DATA also means a connected nonblocking pipe is simply idle.
                if(e == ERROR_NO_DATA && GetTickCount64()-since <= 2000) continue;
                DisconnectNamedPipe(pipeHandle); connected=false;
            }
        }
    }
    DisconnectNamedPipe(pipeHandle);
    return 0;
}
API const char* ts3plugin_name() { return "PR Radios Bridge"; }
API const char* ts3plugin_version() { return "0.1.0"; }
API int ts3plugin_apiVersion() { return 26; }
API const char* ts3plugin_author() { return "PR Radios project"; }
API const char* ts3plugin_description() { return "Connects a local radio overlay to TeamSpeak 3. Does not access Roblox."; }
API void ts3plugin_setFunctionPointers(const TS3Functions functions) { ts=functions; }
API void ts3plugin_registerPluginID(const char* id) { std::lock_guard<std::mutex> lock(messageMutex); pluginId=id; }
API unsigned int ts3plugin_onServerErrorEvent(uint64, const char* message, unsigned int, const char* returnCode, const char*) {
    bool ours=false;
    { std::lock_guard<std::mutex> lock(messageMutex); ours=returnCode && !pendingReturnCode.empty() && pendingReturnCode==returnCode; }
    if(ours) remember(message ? message : "Channel change failed. Check TeamSpeak.");
    return 0;
}
API int ts3plugin_init() {
    HANDLE token=NULL; DWORD length=0; std::string sid;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return 1;
    GetTokenInformation(token,TokenUser,NULL,0,&length);
    char* data = new char[length]; LPSTR text=NULL;
    if(GetTokenInformation(token,TokenUser,data,length,&length) && ConvertSidToStringSidA(((TOKEN_USER*)data)->User.Sid,&text)) {
        sid=text; LocalFree(text);
    }
    delete[] data; CloseHandle(token); if(sid.empty()) return 1;
    std::string descriptor="D:P(A;;GA;;;"+sid+")";
    PSECURITY_DESCRIPTOR sd=NULL;
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorA(descriptor.c_str(),SDDL_REVISION_1,&sd,NULL)) return 1;
    SECURITY_ATTRIBUTES sa={sizeof(sa),sd,FALSE};
    std::string path="\\\\.\\pipe\\PRRadios-"+sid;
    pipeHandle=CreateNamedPipeA(path.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_NOWAIT|PIPE_REJECT_REMOTE_CLIENTS,1,131072,1024,0,&sa);
    LocalFree(sd); if(pipeHandle == INVALID_HANDLE_VALUE) return 1;
    stopEvent=CreateEvent(NULL,TRUE,FALSE,NULL);
    if(stopEvent) worker=CreateThread(NULL,0,run,NULL,0,NULL);
    if(!worker) { if(stopEvent) CloseHandle(stopEvent); CloseHandle(pipeHandle); return 1; }
    return 0;
}
API void ts3plugin_shutdown() {
    if(worker) { SetEvent(stopEvent); WaitForSingleObject(worker,INFINITE); CloseHandle(worker); CloseHandle(stopEvent); CloseHandle(pipeHandle); worker=NULL; }
}
