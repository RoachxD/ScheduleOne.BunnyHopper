using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(true)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("78217591-6296-4A47-9657-F90E4322F66F")]

// MelonLoader attributes
[assembly: MelonInfo(
    typeof(BunnyHopper.Main),
    "Bunny Hopper",
    "1.0.0",
    "Roach_ (Adrian Nicolae)",
    "https://github.com/RoachxD/ScheduleOne.BunnyHopper/releases/latest"
)]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonColor(255, 212, 172, 45)]
[assembly: AssemblyMetadata("NexusModID", "1033")]
