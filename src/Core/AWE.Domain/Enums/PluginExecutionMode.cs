namespace AWE.Domain.Enums;

public enum PluginExecutionMode
{
    BuiltIn = 0,    // Chạy các node hệ thống (Log, Delay, Join...)
    DynamicDll = 1, // Load file .dll bằng ALC
    RemoteGrpc = 2  // Bắn gRPC sang môi trường độc lập (Python/Nodejs...)
}
