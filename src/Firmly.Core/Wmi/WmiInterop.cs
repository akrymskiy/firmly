using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Firmly.Core.Wmi;

// WMI's COM API (wbemcli.h), declared for source-generated COM so it works trimmed and ahead-of-time compiled.
// Only the methods Firmly calls have real signatures. The others are placeholders that keep each method
// in its vtable slot, so they must stay, in this order.

[GeneratedComInterface(StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(BStrStringMarshaller))]
[Guid("dc12a687-737f-11cf-884d-00aa004b2e24")]
internal partial interface IWbemLocator
{
    [PreserveSig]
    int ConnectServer(
        string networkResource, string? user, string? password, string? locale, int securityFlags, string? authority,
        nint context, out IWbemServices? services);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(BStrStringMarshaller))]
[Guid("9556dc99-828c-11cf-a37e-00aa003240c7")]
internal partial interface IWbemServices
{
    void OpenNamespace();
    void CancelAsyncCall();
    void QueryObjectSink();

    [PreserveSig]
    int GetObject(string objectPath, int flags, nint context, out IWbemClassObject? result, nint callResult);

    void GetObjectAsync();
    void PutClass();
    void PutClassAsync();
    void DeleteClass();
    void DeleteClassAsync();
    void CreateClassEnum();
    void CreateClassEnumAsync();
    void PutInstance();
    void PutInstanceAsync();
    void DeleteInstance();
    void DeleteInstanceAsync();
    void CreateInstanceEnum();
    void CreateInstanceEnumAsync();

    [PreserveSig]
    int ExecQuery(string queryLanguage, string query, int flags, nint context, out IEnumWbemClassObject? results);

    void ExecQueryAsync();
    void ExecNotificationQuery();
    void ExecNotificationQueryAsync();

    [PreserveSig]
    int ExecMethod(
        string objectPath, string methodName, int flags, nint context, IWbemClassObject? inParameters,
        out IWbemClassObject? outParameters, nint callResult);
}

[GeneratedComInterface]
[Guid("027947e1-d731-11ce-a357-000000000001")]
internal partial interface IEnumWbemClassObject
{
    void Reset();

    /// <returns>WBEM_S_NO_ERROR (0) with an object; WBEM_S_FALSE (1) at the end.</returns>
    [PreserveSig]
    int Next(int timeout, uint count, out IWbemClassObject? result, out uint returned);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("dc12a681-737f-11cf-884d-00aa004b2e24")]
internal unsafe partial interface IWbemClassObject
{
    void GetQualifierSet();

    [PreserveSig]
    int Get(string name, int flags, Variant* value, nint type, nint flavor);

    [PreserveSig]
    int Put(string name, int flags, Variant* value, int type);

    void Delete();
    void GetNames();
    void BeginEnumeration();
    void Next();
    void EndEnumeration();
    void GetPropertyQualifierSet();
    void Clone();
    void GetObjectText();
    void SpawnDerivedClass();

    [PreserveSig]
    int SpawnInstance(int flags, out IWbemClassObject? instance);

    void CompareTo();
    void GetPropertyOrigin();
    void InheritsFrom();

    [PreserveSig]
    int GetMethod(string name, int flags, out IWbemClassObject? inSignature, out IWbemClassObject? outSignature);
}

/// <summary>An OLE VARIANT: a type tag and 8 or 16 bytes of data, depending on the pointer size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Variant
{
    public ushort Type;
    private readonly ushort _reserved1, _reserved2, _reserved3;
    public nint Value;
    private readonly nint _record;

    public const ushort VT_EMPTY = 0, VT_NULL = 1, VT_I2 = 2, VT_I4 = 3, VT_BSTR = 8, VT_BOOL = 11, VT_UI1 = 17, VT_ARRAY = 0x2000;
}

internal static unsafe partial class WmiNative
{
    public static readonly Guid CLSID_WbemLocator = new("4590f811-1d3a-11d0-891f-00aa004b2e24");

    public const uint CLSCTX_INPROC_SERVER = 1;
    public const uint COINIT_MULTITHREADED = 0;
    public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    public const uint RPC_C_AUTHN_WINNT = 10, RPC_C_AUTHZ_NONE = 0, RPC_C_AUTHN_LEVEL_CALL = 3, RPC_C_IMP_LEVEL_IMPERSONATE = 3, EOAC_NONE = 0;

    public const int WBEM_FLAG_RETURN_IMMEDIATELY = 0x10, WBEM_FLAG_FORWARD_ONLY = 0x20, WBEM_INFINITE = -1;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    public static partial int CoSetProxyBlanket(
        void* proxy, uint authenticationService, uint authorizationService, nint serverPrincipalName,
        uint authenticationLevel, uint impersonationLevel, nint authInfo, uint capabilities);

    [LibraryImport("oleaut32.dll")]
    public static partial int VariantClear(Variant* value);

    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayGetLBound(nint array, uint dimension, out int bound);

    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayGetUBound(nint array, uint dimension, out int bound);

    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayAccessData(nint array, out nint data);

    [LibraryImport("oleaut32.dll")]
    public static partial int SafeArrayUnaccessData(nint array);
}
