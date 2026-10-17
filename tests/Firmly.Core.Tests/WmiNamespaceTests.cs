using Firmly.Core.Wmi;
using Microsoft.Win32;

namespace Firmly.Core.Tests;

// Against WMI classes every Windows PC has, so the COM declarations are checked without HP hardware.
public class WmiNamespaceTests
{
    [Fact]
    public void Query_ReadsStringsNumbersAndArrays()
    {
        using var wmi = WmiNamespace.Connect(@"root\cimv2");
        var results = wmi.Query("SELECT Caption, NumberOfProcesses, MUILanguages FROM Win32_OperatingSystem");
        try
        {
            var os = Assert.Single(results);
            Assert.Equal("Win32_OperatingSystem", os.ClassName);
            Assert.StartsWith("Microsoft Windows", os.GetString("Caption"));
            Assert.True(os.GetInt32("NumberOfProcesses") > 0);
            Assert.NotEmpty(os.GetStrings("MUILanguages")!);
        }
        finally
        {
            foreach (var result in results)
                result.Dispose();
        }
    }

    [Fact]
    public void Get_WrongType_Throws()
    {
        using var wmi = WmiNamespace.Connect(@"root\cimv2");
        var results = wmi.Query("SELECT Caption FROM Win32_OperatingSystem");
        using var os = Assert.Single(results);
        Assert.Throws<InvalidCastException>(() => os.GetInt32("Caption"));
    }

    [Fact]
    public void Connect_MissingNamespace_ReportsInvalidNamespace()
    {
        var ex = Assert.Throws<WmiException>(() => WmiNamespace.Connect(@"root\firmly\nonexistent"));
        Assert.Equal(WmiException.InvalidNamespace, ex.HResult);
    }

    [Fact]
    public void InvokeMethod_PassesParametersAndReturnsResults()
    {
        // StdRegProv.GetStringValue is a static method; its hDefKey defaults to HKEY_LOCAL_MACHINE.
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        using var wmi = WmiNamespace.Connect(@"root\default");
        var classes = wmi.Query("SELECT * FROM meta_class WHERE __CLASS = 'StdRegProv'");
        using var registry = Assert.Single(classes);

        using var result = wmi.InvokeMethod(registry, "GetStringValue", new Dictionary<string, string>
        {
            ["sSubKeyName"] = key,
            ["sValueName"] = "ProductName",
        });

        Assert.Equal(0, result.GetInt32("ReturnValue"));
        Assert.Equal(Registry.LocalMachine.OpenSubKey(key)?.GetValue("ProductName"), result.GetString("sValue"));
    }
}
