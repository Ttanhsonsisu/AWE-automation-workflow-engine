using System.Reflection;
using System.Runtime.Loader;

namespace AWE.Infrastructure.Plugins;

/// <summary>
/// Context cô lập để load plugin.
/// Cho phép load các dependency nằm cùng thư mục với plugin chính.
/// Có khả năng Unload để giải phóng memory và file lock.
/// </summary>
public class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Cố gắng resolve đường dẫn file .dll dependency từ logic của plugin
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // 2. Nếu không tìm thấy, trả về null để Host (Core Engine) tự xử lý 
        // (Ví dụ: load các thư viện chuẩn của .NET Core hoặc Shared SDK)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
