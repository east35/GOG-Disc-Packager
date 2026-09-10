namespace GogDisc.Companion;

internal static class OperationLease
{
    public static IDisposable Acquire(string id)
    {
        var root = CompanionPaths.PackageRoot(id);
        Directory.CreateDirectory(root);
        try { return new FileStream(Path.Combine(root, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("This game already has an operation running. Close it before trying again.", ex); }
    }
}
