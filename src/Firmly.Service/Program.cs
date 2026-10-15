using Firmly.Core;
using Firmly.Core.Vendors.Hp;
using Firmly.Service;

#if DEBUG
// Developer commands for testing against real hardware from an elevated terminal:
//   Firmly.Service --dev-get
//   Firmly.Service --dev-set "<setting>" "<value>"
if (args is ["--dev-get"] or ["--dev-set", _, _])
    return DevCommands.Run(args, new FirmwareProviderResolver([new HpBiosProvider()]));
#endif

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Firmly");

// Vendor providers, in detection order.
builder.Services.AddSingleton<IFirmwareProvider, HpBiosProvider>();
builder.Services.AddSingleton<FirmwareProviderResolver>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new RequestHandler(
    sp.GetRequiredService<ILogger<RequestHandler>>(),
    sp.GetRequiredService<FirmwareProviderResolver>(),
    MachinePolicy.RequireAdminForAllChanges,
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
host.Run();
return 0;
