# OpenMedionControl

I got tired of the Medion Control Center on my Erazer Major X10, so I wrote my own. Turns out the original is just a rebranded Machenike tool, and all it really does is send a few bytes to the embedded controller.

This one does the same stuff without the bloat:

- Office / Gaming / Turbo mode
- CPU and GPU temps and fan speeds
- max fan speed, USB power when off, Windows key lock
- MUX switch (dGPU only or hybrid)
- RGB for keyboard, logo and side lights
- tray icon, autostart, and a small CLI (`--status`, `--mode gaming`, `--fan on`)

Only tested on the Major X10 (board N68630). Other Medion/Machenike laptops with the same control center might work, but no promises.

## Driver

The tool needs Medion's port I/O driver (`WTIOportDrv.sys`) and three DLLs from their software: `rwport.dll`, `mcucontrol.dll` and `SysInfo.dll`. They're not mine, so they're not in this repo. Copy them from `C:\Program Files (x86)\MedionOSD` into `src/vendor/` before building.

On first start the app installs the driver as its own service, so you can uninstall the Medion software afterwards. If the Medion software is still installed, its driver gets used instead. Under Settings you can take it over.

## Build

You need the .NET 8 SDK. The Medion DLLs are 32-bit, so the app is x86 and needs admin rights.

```
dotnet publish src/OpenMedionControl.csproj -c Release -o dist
```

Then run `dist\OpenMedionControl.exe`.

## License

MIT
