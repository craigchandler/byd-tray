using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tmds.DBus.Protocol;
using Byd.Client;

namespace Byd.Tray;

internal sealed record Account(string Username, string Password)
{
    public override string ToString() => "BYD account (redacted)";
}
internal interface ISecretStore
{
    Task<Account?> Read();
    Task Save(Account account);
    Task Delete();
}
internal static class SecretStore
{
    internal static ISecretStore Create(BydMarket market) => OperatingSystem.IsWindows() ? new WindowsStore(market) : new KWalletStore(market);
    internal static string WalletEntry(BydMarket market) => market.CountryCode == "AU" ? "Australian account" : "Account/" + market.CountryCode;
    internal static string WindowsTarget(BydMarket market) => market.CountryCode == "AU" ? "BydTray/Australia/Account" : "BydTray/" + market.CountryCode + "/Account";
}
internal sealed class KWalletStore(BydMarket market) : ISecretStore
{
    private const string AppId = "BYD Tray", Folder = "BYD Tray";
    private string Entry => SecretStore.WalletEntry(market);
    private async Task<T> WithWallet<T>(Func<DBus.KWallet, int, Task<T>> action)
    {
        using var connection = new DBusConnection(DBusAddress.Session ?? throw new InvalidOperationException("KWallet is unavailable."));
        await connection.ConnectAsync();
        var wallet = new DBus.KWallet(connection, "org.kde.kwalletd6", "/modules/kwalletd6");
        var handle = await wallet.OpenAsync(await wallet.LocalWalletAsync(), 0, AppId);
        if (handle < 0) throw new InvalidOperationException("KWallet was not unlocked.");
        try { return await action(wallet, handle); }
        finally { await wallet.CloseAsync(handle, false, AppId); }
    }
    public Task<Account?> Read() => WithWallet(async (wallet, handle) =>
    {
        if (!await wallet.HasFolderAsync(handle, Folder, AppId)) return null;
        var value = await wallet.ReadPasswordAsync(handle, Folder, Entry, AppId);
        return string.IsNullOrEmpty(value) ? null : JsonSerializer.Deserialize<Account>(value);
    });
    public Task Save(Account account) => WithWallet(async (wallet, handle) =>
    {
        if (!await wallet.HasFolderAsync(handle, Folder, AppId) && !await wallet.CreateFolderAsync(handle, Folder, AppId)) throw new InvalidOperationException("KWallet could not create the application folder.");
        if (await wallet.WritePasswordAsync(handle, Folder, Entry, JsonSerializer.Serialize(account), AppId) != 0) throw new InvalidOperationException("KWallet could not save the account.");
        return true;
    });
    public Task Delete() => WithWallet(async (wallet, handle) =>
    {
        if (await wallet.HasFolderAsync(handle, Folder, AppId) && await wallet.RemoveEntryAsync(handle, Folder, Entry, AppId) != 0) throw new InvalidOperationException("KWallet could not remove the saved account.");
        return true;
    });
}
internal sealed class WindowsStore(BydMarket market) : ISecretStore
{
    private string Target => SecretStore.WindowsTarget(market);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type; public string? TargetName, Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist, AttributeCount; public IntPtr Attributes; public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadNative(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteNative(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
    public Task<Account?> Read()
    {
        if (!ReadNative(Target, 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return Task.FromResult<Account?>(null);
            throw new InvalidOperationException("Windows Credential Manager is unavailable.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var json = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return Task.FromResult(json == null ? null : JsonSerializer.Deserialize<Account>(json));
        }
        finally { CredFree(pointer); }
    }
    public Task Save(Account account)
    {
        var bytes = Encoding.Unicode.GetBytes(JsonSerializer.Serialize(account));
        if (bytes.Length > 2560) throw new InvalidOperationException("Account entry is too large.");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new Credential { Type = 1, TargetName = Target, CredentialBlob = pointer, CredentialBlobSize = (uint)bytes.Length, Persist = 2, UserName = "BYD account" };
            if (!Write(ref credential, 0)) throw new InvalidOperationException("Windows Credential Manager could not save the account.");
        }
        finally
        {
            Array.Clear(bytes); Marshal.Copy(bytes, 0, pointer, bytes.Length); Marshal.FreeHGlobal(pointer);
        }
        return Task.CompletedTask;
    }
    public Task Delete()
    {
        if (!DeleteNative(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new InvalidOperationException("Windows Credential Manager could not remove the account.");
        return Task.CompletedTask;
    }
}
