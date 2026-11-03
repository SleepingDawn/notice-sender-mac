using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NoticeSender.Windows;

/// <summary>Own one real database window for the current Windows user/session.</summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;
    private bool disposed;
    public string DatabasePath { get; }
    public bool Acquired { get; }
    public SingleInstanceGuard(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        string identity = Environment.UserDomainName + "\\" + Environment.UserName + "\n" + DatabasePath.ToUpperInvariant();
        string name = "Local\\NoticeSender-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        mutex = new Mutex(true, name, out bool createdNew);
        Acquired = createdNew;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Acquired) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
