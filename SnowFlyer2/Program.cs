using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ConsoleHotKey;
using System.Windows.Forms;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sources;
using System.Threading.Tasks;
using System.Numerics;

namespace SnowFlyer2
{

    class Program
    {
        /// <summary>
        /// Original code
        /// 
        /// Devmode checks:
        /// 7FF6B945000A - 80 B8 C8000000 00     - cmp byte ptr [rax+000000C8],00
        /// dev toggle also accessed via the following, but this isn't required for us here
        /// SnowRunner.exe+860CBF - 80 B8 C9000000 00     - cmp byte ptr [rax+000000C9],00
        /// 
        /// Freecam checks:
        /// SnowRunner.exe+860EA0 - 83 3D CD3E5C02 00     - cmp dword ptr [SnowRunner.exe+2E24D74],00
        /// SnowRunner.exe+88E9A6 - 83 3D C7635902 00     - cmp dword ptr [SnowRunner.exe+2E24D74],00
        /// 
        /// The original code checks a dev flag to enable free camera, 
        /// and checks a second flag to determine which camera should be used. 
        /// We flip those checks to toggle free camera at will
        /// A pattern search is used to hopefully make this compatible with future patches
        /// </summary>


        private static readonly string DevCheckPattern = "80 B8 C8 00 00 00 00 75"; //cmp byte ptr [rax+000000C8],00   75 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] DevCheckPatch = { 0x80, 0xB8, 0xC8, 0x00, 0x00, 0x00, 0X01 }; //cmp byte ptr [rax+000000C8],01

        private static readonly string DevCheckRevertPattern = "80 B8 C8 00 00 00 01 75"; //cmp byte ptr [rax+000000C8],01   75 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] DevCheckRevertPatch = { 0x80, 0xB8, 0xC8, 0x00, 0x00, 0x00, 0X00 }; //cmp byte ptr [rax+000000C8],00


        private static readonly string DevCheckPatternB = "80 B8 C8 00 00 00 00 0F 85"; //cmp byte ptr [rax+000000C8],00   0F 85 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] DevCheckPatchB = { 0x80, 0xB8, 0xC8, 0x00, 0x00, 0x00, 0X01 }; //cmp byte ptr [rax+000000C8],01

        private static readonly string DevCheckRevertPatternB = "80 B8 C8 00 00 00 01 0F 85"; //cmp byte ptr [rax+000000C8],01   0F 85 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] DevCheckRevertPatchB = { 0x80, 0xB8, 0xC8, 0x00, 0x00, 0x00, 0X00 }; //cmp byte ptr [rax+000000C8],00

        // To find the fly mode offset after patches, use Cheat Engine in a mod map, toggling the free camera button and scanning for a 4 byte boolean.
        // There will be two - one is the highlight state of the free camera button, the other is the actual freecam state.
        // NB- this mod can't be tested on mod maps, since those already have devmode flag set they will become unable to use freecam when we swizzle the devmode check
        private static readonly int FlyModeFlagOffset = 0x2E87138;
        private static readonly byte[] FlyModeOnPatch = { 0x01 };
        private static readonly byte[] FlyModeRevertPatch = { 0x00 };

        // The following are intended to patch the flymode *check* rather than change the value, which would be more resilient to game patches.
        // Not yet working as intended, and the fly mode flag address is trivial to find again so this isn't a priority to fix
        private static readonly string FlyModeCheckPatternA = "83 3D C7 63 59 02 00 48"; //cmp dword ptr[SnowRunner.exe + 2E24D74],00  48 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] FlyModeCheckPatchA = { 0x83, 0x3D, 0xC7, 0x63, 0x59, 0x02, 0X01 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],01

        private static readonly string FlyModeCheckRevertPatternA = "83 3D C7 63 59 02 01 48"; //cmp dword ptr[SnowRunner.exe + 2E24D74],01  48 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] FlyModeCheckRevertPatchA = { 0x83, 0x3D, 0xC7, 0x63, 0x59, 0x02, 0X00 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],00

        private static readonly string FlyModeCheckPatternB = "83 3D CD 3E 5C 02 00 0F"; //cmp dword ptr [SnowRunner.exe+2E24D74],00    0F    ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] FlyModeCheckPatchB = { 0x83, 0x3D, 0xCD, 0x3E, 0x5C, 0x02, 0X01 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],01

        private static readonly byte[] FlyModeCheckRevertPatchB = { 0x83, 0x3D, 0xCD, 0x3E, 0x5C, 0x02, 0X00 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],00
        private static readonly string FlyModeCheckRevertPatternB = "83 3D CD 3E 5C 02 01 0F"; //cmp dword ptr [SnowRunner.exe+2E24D74],01    0F    ON NEXT BYTE FOR UNIQUENESS

        // Time Of Day    
        private static readonly string TODTickDisablePattern = "F3 41 0F 11 95 38 01 00 00"; //movss[r13 + 00000138],xmm2   optional F3 0F ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] TODTickDisablePatch = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }; // 9 NOPS, prevent timer from advancing

        private static readonly string TODTickRevertPattern = "90 90 90 90 90 90 90 90 90 F3 0F 10 05"; //our NOPS, plus next instruction since there are definitely dupes of NOP 
        private static readonly byte[] TODTickRevertPatch = { 0xF3, 0x41, 0x0F, 0x11, 0x95, 0x38, 0x01, 0x00, 0x00 }; //original ticker code

        // To find the TOD pointer - use Cheat Engine with above TODTickDisablePattern, which should be editing one address (r13+ 000000138).
        // Memory view-> search for AOB, go to same address in the ASM section at top, ->'find out what addresses this instruction accesses'
        // Add it to addresslist, ->generate pointermap for it with offsets ending in 138 as below
        // ->'Pointerscan for this address' using the pointermap. Should be 3 top-level pointers available.
        // Can do this twice and 'compare results with other saved pointermaps', but so far the 3 top level ones are always valid and there's no need.
        private static readonly int TODOffset = 0x138;
        private static readonly int TODPointer = 0x02E90620;

        // Player location
        private static string PatternPlayerCoords = "0F B6 5C 24 70 84 DB 75 43";

        private static bool IsFreeCamActive = false;
        private static bool ShouldLoopTime = false;
        private static bool IsTimePassing = true;

        [STAThread]
        static void Main(string[] args)
        {
            KeybindingManager keybindingManager = new KeybindingManager();
            KeybindingConfig config = keybindingManager.ReadKeybindingConfig();

            Process snowRunnerProcess = attachToSnowRunnerProcess();
            HotKeyManager.RegisterHotKey(config.ToggleFlyMode, KeyModifiers.Control);
            HotKeyManager.RegisterHotKey(config.ToggleFreezeTime, KeyModifiers.Control);
            HotKeyManager.RegisterHotKey(config.ToggleTimeLapseMode, KeyModifiers.Control);
            HotKeyManager.RegisterHotKey(config.GetPlayerLocation, KeyModifiers.Control);
            HotKeyManager.HotKeyPressed += (sender2, e2) => Hotkey_Pressed(sender2, e2, config,snowRunnerProcess);

            bool showMenu = true;
            while (showMenu)
            {
                showMenu = MainMenu(snowRunnerProcess, config);
            }

        }
        private static bool MainMenu(Process snowRunnerProcess, KeybindingConfig config)
        {
            string logo = @"   _____                     ______ _                   ___  
  / ____|                   |  ____| |                 |__ \ 
 | (___  _ __   _____      _| |__  | |_   _  ___ _ __     ) |
  \___ \| '_ \ / _ \ \ /\ / /  __| | | | | |/ _ \ '__|   / / 
  ____) | | | | (_) \ V  V /| |    | | |_| |  __/ |     / /_ 
 |_____/|_| |_|\___/ \_/\_/ |_|    |_|\__, |\___|_|    |____|
                                       __/ |                 
                                      |___/                  ";
            Console.Clear();

            Console.WriteLine(logo);



            DisplayHotkeyInstructions(config);
            
            //Prevent app from closing
            Console.ReadKey();
            return true;
        }

        private static void StopTODTicker(Process snowRunnerProcess)
        {
            ShouldLoopTime = false;
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            try
            {
                SearchAndApplyPatch(snowRunnerProcess, scanner, TODTickDisablePattern, TODTickDisablePatch, "TODTick");

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\n\n Timer Stopped!");
                Console.ResetColor();
                IsTimePassing = false;

                float currentTime = GetCurrentTOD(snowRunnerProcess);

                Console.WriteLine("Current time is:{0}h", currentTime.ToString("0.00"));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void ResumeTODTicker(Process snowRunnerProcess)
        {
            ShouldLoopTime = false;
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            try
            {
                SearchAndApplyPatch(snowRunnerProcess, scanner, TODTickRevertPattern, TODTickRevertPatch, "TODTick");

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\n\n Timer Resumed!");
                Console.ResetColor();
                IsTimePassing = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }


        private static void GetPlayerCoords(Process snowRunnerProcess)
        {
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            var coordsPtr = scanner.CompiledFindPattern(PatternPlayerCoords);

            IntPtr PlayerCoordsBasePtr = IntPtr.Zero;
            int[] PlayerCoordsOffsets = { 0x28, 0x18, 0x1A8 };
            var snowRunnerBase = snowRunnerProcess.MainModule.BaseAddress;

            StructPointerDescription<Vector3> PlayerCoord;
            if (coordsPtr.Found)
            {
                var memory = new ExternalMemory(snowRunnerProcess);
                PlayerCoordsBasePtr = ReadOffsetFromCallToLeaSingleton(memory, snowRunnerBase + coordsPtr.Offset + GetOpcodeLengthOfPattern(PatternPlayerCoords));
                if (PlayerCoordsBasePtr == IntPtr.Zero)
                    return;
                PlayerCoord = new StructPointerDescription<Vector3>(memory, PlayerCoordsBasePtr, PlayerCoordsOffsets);

                var playerVec = PlayerCoord.Read();
                Console.WriteLine($"Coord: {playerVec.X:F2} {playerVec.Y:F2} {playerVec.Z:F2}");
            }
        }

        public static IntPtr ReadOffsetFromOpcode(IMemory memory, IntPtr opcodeBegin, int offsetUntilAddress)
        {
            var opcodeOffset = memory.Read<int>(opcodeBegin + offsetUntilAddress);
            var effectiveAddress = opcodeBegin + offsetUntilAddress + opcodeOffset;
            return effectiveAddress + sizeof(int);
        }
        public static IntPtr ReadOffsetFromCallToLeaSingleton(IMemory memory, IntPtr opcodeBegin) //qqtas opcodebegin is first byte after our player pattern
        {
            var callToGetPtr = ReadOffsetFromOpcode(memory, opcodeBegin, 1);//call offset 1
            callToGetPtr = ReadOffsetFromOpcode(memory, callToGetPtr, 3); //lea offset 3
            return callToGetPtr;
        }

        public static IntPtr GetLastPtrOfPtrChain(IMemory memory, IntPtr baseAddress, params int[] offsets)
        {
            if (baseAddress == IntPtr.Zero)
            {
                return baseAddress;
            }
            if (offsets.Length == 0)
            {
                return baseAddress;
            }

            var basePtr = memory.Read<IntPtr>(baseAddress);
            if (basePtr == IntPtr.Zero)
            {
                return default;
            }
            for (var i = 0; i < offsets.Length - 1; i++)
            {
                basePtr = memory.Read<IntPtr>(basePtr + offsets[i]);
                if (basePtr == IntPtr.Zero)
                {
                    return default;
                }
            }
            if (basePtr == IntPtr.Zero)
            {
                return default;
            }
            //return basePtr + offsets[^1];
            return basePtr + offsets[offsets.Length-1];
        }

        private static int GetOpcodeLengthOfPattern(string pattern)
        {
            //2 characters represent a byte
            return pattern.Replace(" ", "").Length / 2;
        }


        private static void EnableFreeCam(Process snowRunnerProcess)
        {
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            try
            {
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckPattern, DevCheckPatch, "DevMode");
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckPatternB, DevCheckPatchB, "DevModeB");
                ForceApplyPatchAtOffset(snowRunnerProcess, FlyModeFlagOffset, FlyModeOnPatch, "FlyMode");

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\n\n FreeCam Enabled!");
                Console.ResetColor();
                IsFreeCamActive = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void DisableFreeCam(Process snowRunnerProcess)
        {
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            try
            {
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckRevertPattern, DevCheckRevertPatch, "DevMode");
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckRevertPatternB, DevCheckRevertPatchB, "DevModeB");
                ForceApplyPatchAtOffset(snowRunnerProcess, FlyModeFlagOffset, FlyModeRevertPatch, "FlyMode");

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\n\n FreeCam Disabled!");
                Console.ResetColor();
                IsFreeCamActive = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static Process attachToSnowRunnerProcess()
        {
            Console.WriteLine("Looking for SnowRunner.exe");
            var p = Process.GetProcessesByName("SnowRunner");
            while (p.Length == 0)
            {
                Console.WriteLine("Waiting for SnowRunner.exe");
                Thread.Sleep(1000);
                p = Process.GetProcessesByName("SnowRunner");
            }
            var snowRunnerProcess = p.First();
            return snowRunnerProcess;
        }

        private static void SearchAndApplyPatch(Process snowRunnerProcess, Scanner scanner, String searchPattern, byte[] patchBytes, String label)
        {
            var offset = scanner.CompiledFindPattern(searchPattern);
            Console.WriteLine("Searching for {0} patch location", label);
            if (offset.Found)
            {
                Console.WriteLine("Found {0} patch location at {1:X}. Patching game in memory...", label, offset.Offset);
                try
                {
                    var memory = new ExternalMemory(snowRunnerProcess);
                    var baseAddress = snowRunnerProcess.MainModule.BaseAddress + offset.Offset;
                    memory.WriteRaw(baseAddress, patchBytes);
                    Console.WriteLine("Patch in memory successful!");
                }
                catch (Exception)
                {
                    throw new Exception(String.Format("Patching in memory failed for {0} \n Try running again as Administrator", label));
                }

            }
            else
            {
                throw new Exception(String.Format("Could not find patch location for {0}", label));
            }
        }


        private static void ForceApplyPatchAtOffset(Process snowRunnerProcess, int offset, byte[] patchBytes, String label)
        {
            Console.WriteLine("Using known {0} patch location {1:X}. Patching game in memory...", label, offset);
            try
            {
                var memory = new ExternalMemory(snowRunnerProcess);
                var baseAddress = snowRunnerProcess.MainModule.BaseAddress + offset;
                memory.WriteRaw(baseAddress, patchBytes);

                Console.WriteLine("Patch in memory successful!");
            }
            catch (Exception)
            {
                throw new Exception(String.Format("Patching in memory failed for {0} \n Try running again as Administrator", label));
            }
        }


        private static float GetCurrentTOD(Process snowRunnerProcess)
        {
            try
            {
                var memory = new ExternalMemory(snowRunnerProcess);
                var valueAddress = GetTODMemoryAddress(snowRunnerProcess, memory);

                float outBytes;
                memory.Read<float>(valueAddress, out outBytes);
                return outBytes;
            }
            catch (Exception)
            {
                throw new Exception(String.Format("Could not read current time from memory\n Try running again as Administrator"));
            }
        }


        private static void SetCurrentTOD(Process snowRunnerProcess, float newTOD)
        {
            try
            {
                var memory = new ExternalMemory(snowRunnerProcess);
                var valueAddress = GetTODMemoryAddress(snowRunnerProcess, memory);

                memory.Write<float>(valueAddress, newTOD);
            }
            catch (Exception)
            {
                throw new Exception(String.Format("eek"));
            }
        }


        private static void CycleTOD(Process snowRunnerProcess)
        {
            try
            {
                // Spawn thread for time loop so we can interrupt it via hotkey
                Task worker = Task.Run(() => LoopTimeThread(snowRunnerProcess));
                ConsoleLogWithColour("Timelapse running!", ConsoleColor.White, ConsoleColor.Red);
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Could not start timelapse, {0}", ex.Message));
            }
        }

        private static void StopCyclingTOD(Process snowRunnerProcess)
        {
            ShouldLoopTime = false;

            ConsoleLogWithColour("Timelapse stopped", ConsoleColor.White, ConsoleColor.Red);
            float currentTime = GetCurrentTOD(snowRunnerProcess);

            Console.WriteLine("Current time is:{0}h", currentTime.ToString("0.00"));
        }

        static void LoopTimeThread(Process snowRunnerProcess)
        {
            var memory = new ExternalMemory(snowRunnerProcess);
            var valueAddress = GetTODMemoryAddress(snowRunnerProcess, memory);

            var now = GetCurrentTOD(snowRunnerProcess);
            ShouldLoopTime = true;
            while (ShouldLoopTime)
            {
                memory.Write<float>(valueAddress, now);
                Thread.Sleep(10);
                now = now + 0.03f;
                if (now > 24f) now = now - 24f;
            }
        }

        private static IntPtr GetTODMemoryAddress(Process snowRunnerProcess, ExternalMemory memory)
        {
            var baseAddress = snowRunnerProcess.MainModule.BaseAddress + TODPointer;
            byte[] readBytes;
            memory.ReadRaw(baseAddress, out readBytes, 8);

            var addressInPointer = BitConverter.ToInt64(readBytes, 0);

            //This gives us 1E19DACD310, which is the right address pre-offset!!
            // So check the value there:
            var valueAddress = addressInPointer + TODOffset;
            return (IntPtr)valueAddress;
        }

        private static void DisplayHotkeyInstructions(KeybindingConfig config)
        {
            Console.WriteLine("\n \nHotkeys: \n");
            Console.WriteLine("Ctrl + "+ config.ToggleFlyMode +": \t Toggle free camera mode\n");
            Console.WriteLine("Ctrl + "+ config.ToggleFreezeTime +": \t Toggle in-game time\n");
            Console.WriteLine("Ctrl + "+ config.ToggleTimeLapseMode +": \t Toggle timelapse\n");
            Console.WriteLine("Ctrl + "+ config.GetPlayerLocation +": \t Get player location\n");
        }

        private static void ConsoleLogWithColour(string content, ConsoleColor foreground, ConsoleColor background)
        {
            Console.BackgroundColor = background;
            Console.ForegroundColor = foreground;
            Console.WriteLine(content);
            Console.ResetColor();
        }

        private static void Hotkey_Pressed(object sender2, HotKeyEventArgs e2, KeybindingConfig config, Process snowRunnerProcess)
        {
            Console.Clear();

            if (e2.Key == config.ToggleFlyMode)
            {
                Console.WriteLine("Toggling freecam...");
                if (IsFreeCamActive)
                {
                    DisableFreeCam(snowRunnerProcess);
                }
                else
                {
                    // Try disabling in case someone quits while in fly mode and then can't revert
                    DisableFreeCam(snowRunnerProcess);
                    EnableFreeCam(snowRunnerProcess);
                }
            }
            else if (e2.Key == config.ToggleFreezeTime)
            {
                if (IsTimePassing)
                {
                    Console.WriteLine("Disabling timer...");
                    StopTODTicker(snowRunnerProcess);
                }
                else
                {
                    // Try disabling in case someone quits while time is stopped and then can't revert
                    Console.WriteLine("Check timer was running...");
                    StopTODTicker(snowRunnerProcess);
                    Console.WriteLine("Resuming timer...");
                    ResumeTODTicker(snowRunnerProcess);
                }
            }
            else if (e2.Key == config.ToggleTimeLapseMode)
            {
                if (ShouldLoopTime)
                {
                    Console.WriteLine("Stopping timelapse");
                    StopCyclingTOD(snowRunnerProcess);
                }
                else
                {
                    Console.WriteLine("Starting timelapse");
                    CycleTOD(snowRunnerProcess);
                }
            }
            else if (e2.Key == config.GetPlayerLocation)
            {
                GetPlayerCoords(snowRunnerProcess);
            }
            else
            {
                Console.WriteLine("Unknown hotkey!");
            }

            DisplayHotkeyInstructions(config);
        }




        internal class StructPointerDescription<T>
            where T : struct
        {
            private readonly IMemory _memory;
            private readonly IntPtr _basePtr;
            private readonly int[] _offsets;
            private T _value;
            public StructPointerDescription(IMemory memory, IntPtr basePtr, params int[] offsets)
            {
                _memory = memory;
                _basePtr = basePtr;
                _offsets = offsets;
            }

            public T Read()
            {
                var ptr = GetLastPtrOfPtrChain(_memory, _basePtr, _offsets);
                if (ptr == IntPtr.Zero)
                    return default;
                //Console.WriteLine($"Player pointer is: {ptr}");
                _memory.Read<T>(ptr, out _value, false);
                return _value;
            }

            public void Write(T value)
            {
                var ptr = GetLastPtrOfPtrChain(_memory, _basePtr, _offsets);
                if (ptr == IntPtr.Zero)
                    return;
                _memory.Write<T>(ptr, value, false);
            }
        }



    }
}
