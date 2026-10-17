using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using static Firmly.Core.Wmi.WmiNative;

namespace Firmly.Core.Wmi;

/// <summary>A WMI call failed; <see cref="Exception.HResult"/> holds WMI's error code.</summary>
public sealed class WmiException : Exception
{
    public const int InvalidNamespace = unchecked((int)0x8004100E);
    public const int AccessDenied = unchecked((int)0x80041003);
    public const int GeneralAccessDenied = unchecked((int)0x80070005);

    public WmiException(string message, int hresult) : base($"{message} (WMI error 0x{hresult:X8})") => HResult = hresult;

    public bool IsAccessDenied => HResult is AccessDenied or GeneralAccessDenied;

    internal static void ThrowIfFailed(int hresult, string message)
    {
        if (hresult < 0)
            throw new WmiException(message, hresult);
    }
}

/// <summary>
/// A connection to one WMI namespace on this PC, through WMI's own COM API. Unlike System.Management this works
/// in a trimmed or ahead-of-time compiled program.
/// Use it on one thread, and dispose the objects it returns before disposing it.
/// </summary>
internal sealed unsafe class WmiNamespace : IDisposable
{
    private readonly IWbemServices _services;
    private readonly bool _uninitialize;

    private WmiNamespace(IWbemServices services, bool uninitialize)
    {
        _services = services;
        _uninitialize = uninitialize;
    }

    /// <param name="path">E.g. <c>root\cimv2</c>.</param>
    /// <exception cref="WmiException">E.g. <see cref="WmiException.InvalidNamespace"/>.</exception>
    public static WmiNamespace Connect(string path)
    {
        // Joins the thread to the multithreaded apartment, unless it's in a single-threaded one already;
        // WMI works from either. Each successful call needs a matching CoUninitialize.
        var uninitialize = CoInitializeEx(0, COINIT_MULTITHREADED) >= 0;
        IWbemLocator? locator = null;
        try
        {
            var hr = CoCreateInstance(CLSID_WbemLocator, 0, CLSCTX_INPROC_SERVER, typeof(IWbemLocator).GUID, out var pointer);
            WmiException.ThrowIfFailed(hr, "Couldn't start WMI");
            locator = ComInterfaceMarshaller<IWbemLocator>.ConvertToManaged((void*)pointer)!;
            Marshal.Release(pointer);

            hr = locator.ConnectServer(path, null, null, null, 0, null, 0, out var services);
            WmiException.ThrowIfFailed(hr, $@"Couldn't connect to WMI namespace {path}");
            AllowImpersonation(services!);
            return new WmiNamespace(services!, uninitialize);
        }
        catch
        {
            if (uninitialize)
                CoUninitialize();
            throw;
        }
        finally
        {
            Release(locator);
        }
    }

    /// <summary>Runs a WQL query, e.g. <c>SELECT * FROM Win32_OperatingSystem</c>. The caller disposes the results.</summary>
    public List<WmiObject> Query(string wql)
    {
        var hr = _services.ExecQuery("WQL", wql, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, 0, out var enumerator);
        WmiException.ThrowIfFailed(hr, $"WMI query failed: {wql}");
        AllowImpersonation(enumerator!);

        List<WmiObject> results = [];
        try
        {
            while (true)
            {
                // Errors such as access denied surface here, since the query returns immediately.
                hr = enumerator!.Next(WBEM_INFINITE, 1, out var item, out var returned);
                WmiException.ThrowIfFailed(hr, $"WMI query failed: {wql}");
                if (returned == 0 || item is null)
                    break;
                results.Add(new WmiObject(item));
            }
            return results;
        }
        catch
        {
            foreach (var result in results)
                result.Dispose();
            throw;
        }
        finally
        {
            Release(enumerator);
        }
    }

    /// <summary>
    /// Calls <paramref name="method"/> on <paramref name="target"/> (an instance or, for a static method, its class),
    /// with the given string parameters. The caller disposes the returned out parameters.
    /// </summary>
    public WmiObject InvokeMethod(WmiObject target, string method, IReadOnlyDictionary<string, string> parameters)
    {
        var className = target.GetString("__CLASS") ?? throw new WmiException("WMI object has no class", WmiException.InvalidNamespace);
        var path = target.GetString("__RELPATH") ?? className;

        var hr = _services.GetObject(className, 0, 0, out var classObject, 0);
        WmiException.ThrowIfFailed(hr, $"Couldn't read WMI class {className}");
        IWbemClassObject? signature = null, outSignature = null, inParameters = null;
        try
        {
            hr = classObject!.GetMethod(method, 0, out signature, out outSignature);
            WmiException.ThrowIfFailed(hr, $"WMI class {className} has no method {method}");
            if (signature is not null)
            {
                hr = signature.SpawnInstance(0, out inParameters);
                WmiException.ThrowIfFailed(hr, $"Couldn't prepare the parameters for {className}.{method}");
                foreach (var (name, value) in parameters)
                    WmiObject.Put(inParameters!, name, value);
            }

            hr = _services.ExecMethod(path, method, 0, 0, inParameters, out var outParameters, 0);
            WmiException.ThrowIfFailed(hr, $"WMI method {className}.{method} failed");
            return new WmiObject(outParameters!);
        }
        finally
        {
            Release(inParameters);
            Release(outSignature);
            Release(signature);
            Release(classObject);
        }
    }

    public void Dispose()
    {
        Release(_services);
        if (_uninitialize)
            CoUninitialize();
    }

    /// <summary>Releases the COM object now rather than when the garbage collector gets to it.</summary>
    internal static void Release(object? comObject) => (comObject as ComObject)?.FinalRelease();

    // WMI needs to impersonate the caller; COM's default level only lets it identify them. Set per proxy.
    private static void AllowImpersonation<T>(T proxy)
    {
        var pointer = ComInterfaceMarshaller<T>.ConvertToUnmanaged(proxy);
        try
        {
            var hr = CoSetProxyBlanket(
                pointer, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, 0, RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, 0, EOAC_NONE);
            WmiException.ThrowIfFailed(hr, "Couldn't set WMI security");
        }
        finally
        {
            ComInterfaceMarshaller<T>.Free(pointer);
        }
    }
}

/// <summary>A WMI object (an instance, or a method's out parameters) and its properties.</summary>
internal sealed unsafe class WmiObject(IWbemClassObject inner) : IDisposable
{
    /// <summary>E.g. <c>HPBIOS_BIOSEnumeration</c>.</summary>
    public string ClassName => GetString("__CLASS") ?? "";

    /// <summary>A string property; null when it's null or missing.</summary>
    /// <exception cref="InvalidCastException">The property isn't a string.</exception>
    public string? GetString(string name) =>
        Read(name, v => v.Type == Variant.VT_BSTR ? Marshal.PtrToStringBSTR(v.Value) : throw WrongType(name, v));

    /// <summary>A string array property; null when it's null or missing.</summary>
    public string[]? GetStrings(string name) => Read(name, v =>
    {
        if (v.Type != (Variant.VT_ARRAY | Variant.VT_BSTR))
            throw WrongType(name, v);

        SafeArrayGetLBound(v.Value, 1, out var lower);
        SafeArrayGetUBound(v.Value, 1, out var upper);
        WmiException.ThrowIfFailed(SafeArrayAccessData(v.Value, out var data), $"Couldn't read WMI property {name}");
        try
        {
            var items = new string[upper - lower + 1];
            for (var i = 0; i < items.Length; i++)
                items[i] = Marshal.PtrToStringBSTR(((nint*)data)[i]) ?? "";
            return items;
        }
        finally
        {
            SafeArrayUnaccessData(v.Value);
        }
    });

    /// <summary>
    /// An integer property (WMI's uint8, sint16, uint16, sint32, uint32 or boolean); null when it's null or missing.
    /// uint32 values arrive as signed 32-bit numbers, as WMI sends them.
    /// </summary>
    public int? GetInt32(string name) => Read<int?>(name, v => v.Type switch
    {
        Variant.VT_I4 => (int)v.Value,
        Variant.VT_I2 => (short)v.Value,
        Variant.VT_UI1 => (byte)v.Value,
        Variant.VT_BOOL => (short)v.Value != 0 ? 1 : 0,
        _ => throw WrongType(name, v),
    });

    public void Dispose() => WmiNamespace.Release(inner);

    internal static void Put(IWbemClassObject target, string name, string value)
    {
        var variant = new Variant { Type = Variant.VT_BSTR, Value = Marshal.StringToBSTR(value) };
        try
        {
            WmiException.ThrowIfFailed(target.Put(name, 0, &variant, 0), $"Couldn't set WMI parameter {name}");
        }
        finally
        {
            VariantClear(&variant);
        }
    }

    // Reads a property and converts it while the VARIANT is alive; null and missing (empty) both give default.
    private T? Read<T>(string name, Func<Variant, T> convert)
    {
        Variant variant = default;
        WmiException.ThrowIfFailed(inner.Get(name, 0, &variant, 0, 0), $"Couldn't read WMI property {name}");
        try
        {
            return variant.Type is Variant.VT_EMPTY or Variant.VT_NULL ? default : convert(variant);
        }
        finally
        {
            VariantClear(&variant);
        }
    }

    private static InvalidCastException WrongType(string name, Variant value) =>
        new($"WMI property {name} has variant type {value.Type}.");
}
