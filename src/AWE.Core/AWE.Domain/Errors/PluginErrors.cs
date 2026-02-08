using AWE.Shared.Primitives;

namespace AWE.Domain.Errors;

public static class PluginErrors
{
    public static class Package
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("PluginPackage.NotFound", $"Plugin package with ID '{id}' was not found.");

        public static Error AlreadyExists(string uniqueName) =>
            Error.Conflict("PluginPackage.AlreadyExists", $"Plugin package '{uniqueName}' already exists.");
    }

    public static class Version
    {
        public static Error NotFound(Guid id) =>
            Error.NotFound("PluginVersion.NotFound", $"Plugin version '{id}' not found.");

        public static Error AlreadyExists(string version) =>
            Error.Conflict("PluginVersion.AlreadyExists", $"Version '{version}' already exists for this package.");

        public static Error EmptyDll =>
            Error.Validation("PluginVersion.EmptyDll", "The provided DLL stream is empty.");

        public static Error InvalidAssembly(string reason) =>
            Error.Validation("PluginVersion.InvalidAssembly", $"Invalid plugin assembly: {reason}");

        public static Error MissingInterface =>
            Error.Validation("PluginVersion.MissingInterface", "DLL does not implement required 'IWorkflowPlugin' interface from AWE.Sdk.");

        public static Error UploadFailed(string detail) =>
            Error.Failure("PluginVersion.UploadFailed", $"Failed to upload to storage: {detail}");
    }
}
