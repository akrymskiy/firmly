using Firmly.Core;
using Firmly.Core.Vendors.Hp;
using Firmly.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Firmly");

// Vendor providers, in detection order.
builder.Services.AddSingleton<IFirmwareProvider, HpBiosProvider>();
builder.Services.AddSingleton<FirmwareProviderResolver>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
