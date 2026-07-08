<img src="https://raw.githubusercontent.com/LenovoLegionToolkit-Team/PresentMonFps/v2/src/Favicon.png" width="80">

> [!NOTE]
> This is a modified fork of the original [PresentMonFps](https://github.com/lemutec/PresentMonFps) library maintained for the [Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) project.

[![NuGet](https://img.shields.io/nuget/v/LLT.PresentMonFps.svg)](https://nuget.org/packages/LLT.PresentMonFps) [![Actions](https://github.com/LenovoLegionToolkit-Team/PresentMonFps/actions/workflows/build.yml/badge.svg)](https://github.com/LenovoLegionToolkit-Team/PresentMonFps/actions) [![Platform](https://img.shields.io/badge/platform-Windows-blue?logo=windowsxp&color=1E9BFA)](https://dotnet.microsoft.com/en-us/download/dotnet/latest/runtime)

# PresentMonFps

The PresentMon .NET Wrapper for calculating FPS.

## Installation

**Nuget**：https://www.nuget.org/packages/LLT.PresentMonFps

## Demo

```c#
// Check Available.
if (!FpsInspector.IsAvailable)
{
    Console.WriteLine("This library is only available on Windows.");
    return;
}

// Simple method to get PID.
// Fullname is unnecessary.
uint pid = await FpsInspector.GetProcessIdByNameAsync("YourApp.exe");

// Calculate FPS Once.
FpsResult result = await FpsInspector.StartOnceAsync(new FpsRequest(pid));
Console.WriteLine(result);

// Calculate FPS Forever.
await FpsInspector.StartForeverAsync(new FpsRequest(pid), Console.WriteLine, null!);
```

See more from [PresentMon.SampleWPF](https://github.com/LenovoLegionToolkit-Team/PresentMonFps/tree/v2/demo/PresentMon.SampleWPF) and [PresentMon.SampleConsole](https://github.com/LenovoLegionToolkit-Team/PresentMonFps/tree/v2/demo/PresentMon.SampleConsole).

## Thanks to

- https://github.com/GameTechDev/PresentMon

## Licenses

[MIT](https://github.com/LenovoLegionToolkit-Team/PresentMonFps/blob/v2/LICENSE)

