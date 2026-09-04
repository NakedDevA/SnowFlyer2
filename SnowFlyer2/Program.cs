using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ConsoleHotKey;
using System.Windows.Forms;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sigscan.Structs;
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

        // The fly mode flag is referenced by two RIP-relative CMP instructions, four-byte
        // displacement changes with patches
        private static readonly string[] FlyModeFlagReferencePatterns =
        {
            "83 3D ?? ?? ?? ?? 00 48 8B F9 0F 85 ?? ?? ?? ?? 48 8B 41 28 49 89 5B 08 4D 89 73 18",
            "83 3D ?? ?? ?? ?? 00 0F 84 ?? ?? ?? ?? F3 44 0F 10 4E 28 F3 44 0F 10 56 18 F3 44 0F 10 5E 08"
        };
        private const int FlyModeFlagReferenceLength = 7;
        private const int FlyModeFlagDisplacementOffset = 2;
        private static IntPtr FlyModeFlagAddress = IntPtr.Zero;
        private static int FlyModeFlagProcessId = -1;

        // Time Of Day
        private static readonly string TODTickDisablePattern = "F3 41 0F 11 95 ?? ?? ?? ?? F3 0F 10 05";
        private static readonly byte[] TODTickDisablePatch = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }; // 9 NOPS, prevent timer from advancing

        private static readonly string TODTickRevertPattern = "90 90 90 90 90 90 90 90 90 F3 0F 10 05"; //our NOPS, plus next instruction since there are definitely dupes of NOP 
        private static readonly string TODFieldReferencePattern = "F3 41 0F 11 95 ?? ?? ?? ?? 45 32 F6";
        private static readonly string TODAccessorCallPattern = "E8 ?? ?? ?? ?? 48 8B F8 48 8D 8B ?? ?? ?? ?? B8 ?? ?? ?? ?? 48 85 DB 48 0F 44 C8 C6 44 24 28 01 48 89 4C 24 20 41 0F 28 D8 0F 28 D7 48 8B D6 48 8B 0F E8 ?? ?? ?? ??";
        private const int TODTickInstructionLength = 9;
        private const int TODFieldDisplacementOffset = 5;
        private const int TODUpdateCallOffset = 0x32;
        private static byte[] TODTickOriginalBytes;
        private static IntPtr TODOwnerPointerAddress = IntPtr.Zero;
        private static IntPtr TODTickInstructionAddress = IntPtr.Zero;
        private static int TODFieldOffset;
        private static int TODProcessId = -1;

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
            try
            {
                // cache the live pointer and original instruction before noping
                GetTODMemoryAddress(snowRunnerProcess, new ExternalMemory(snowRunnerProcess));
                var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
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
            try
            {
                ResolveTODMemoryLayout(snowRunnerProcess, new ExternalMemory(snowRunnerProcess));
                var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
                SearchAndApplyPatch(snowRunnerProcess, scanner, TODTickRevertPattern, TODTickOriginalBytes, "TODTick");

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
                var flyModeFlagAddress = GetFlyModeFlagAddress(snowRunnerProcess);
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckPattern, DevCheckPatch, "DevMode");
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckPatternB, DevCheckPatchB, "DevModeB");
                SetFlyModeFlag(snowRunnerProcess, flyModeFlagAddress, true);

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
                var flyModeFlagAddress = GetFlyModeFlagAddress(snowRunnerProcess);
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckRevertPattern, DevCheckRevertPatch, "DevMode");
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckRevertPatternB, DevCheckRevertPatchB, "DevModeB");
                SetFlyModeFlag(snowRunnerProcess, flyModeFlagAddress, false);

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
                    throw new Exception(String.Format("Patching in memory failed for {0} \n ", label));
                }

            }
            else
            {
                throw new Exception(String.Format("Could not find patch location for {0}", label));
            }
        }


        private static IntPtr GetFlyModeFlagAddress(Process snowRunnerProcess)
        {
            if (FlyModeFlagAddress != IntPtr.Zero && FlyModeFlagProcessId == snowRunnerProcess.Id)
            {
                return FlyModeFlagAddress;
            }

            Console.WriteLine("Searching for FlyMode flag references");

            var module = snowRunnerProcess.MainModule;
            var scanner = new Scanner(snowRunnerProcess, module);
            var memory = new ExternalMemory(snowRunnerProcess);
            HashSet<long> sharedTargets = null;

            foreach (var pattern in FlyModeFlagReferencePatterns)
            {
                var targets = FindRipRelativeTargets(scanner, memory, module, pattern);
                if (targets.Count == 0)
                {
                    throw new Exception(String.Format("Could not find a FlyMode flag reference matching {0}", pattern));
                }

                if (sharedTargets == null)
                {
                    sharedTargets = targets;
                }
                else
                {
                    sharedTargets.IntersectWith(targets);
                }
            }

            if (sharedTargets == null || sharedTargets.Count != 1)
            {
                var candidateCount = sharedTargets == null ? 0 : sharedTargets.Count;
                throw new Exception(String.Format("Could not uniquely resolve the FlyMode flag ({0} shared candidates)", candidateCount));
            }

            var resolvedAddress = new IntPtr(sharedTargets.Single());
            var currentValue = memory.Read<int>(resolvedAddress);
            if (currentValue != 0 && currentValue != 1)
            {
                throw new Exception(String.Format("Resolved FlyMode flag contained unexpected value {0}", currentValue));
            }

            FlyModeFlagAddress = resolvedAddress;
            FlyModeFlagProcessId = snowRunnerProcess.Id;

            var relativeAddress = resolvedAddress.ToInt64() - module.BaseAddress.ToInt64();
            Console.WriteLine("Found FlyMode flag at SnowRunner.exe+{0:X}", relativeAddress);
            return resolvedAddress;
        }

        private static HashSet<long> FindRipRelativeTargets(Scanner scanner, ExternalMemory memory, ProcessModule module, string pattern)
        {
            var targets = new HashSet<long>();
            var compiledPattern = new CompiledScanPattern(pattern);
            var startingIndex = 0;
            var moduleStart = module.BaseAddress.ToInt64();
            var moduleEnd = moduleStart + module.ModuleMemorySize;

            while (startingIndex < module.ModuleMemorySize)
            {
                var match = scanner.CompiledFindPattern(compiledPattern, startingIndex);
                if (!match.Found)
                {
                    break;
                }

                var instructionAddress = module.BaseAddress + match.Offset;
                var displacement = memory.Read<int>(instructionAddress + FlyModeFlagDisplacementOffset);
                var targetAddress = instructionAddress.ToInt64() + FlyModeFlagReferenceLength + displacement;

                if (targetAddress >= moduleStart && targetAddress <= moduleEnd - sizeof(int))
                {
                    targets.Add(targetAddress);
                }

                startingIndex = match.Offset + 1;
            }

            return targets;
        }

        private static void SetFlyModeFlag(Process snowRunnerProcess, IntPtr flagAddress, bool enabled)
        {
            Console.WriteLine("Setting FlyMode flag to {0}", enabled ? "on" : "off");
            try
            {
                var memory = new ExternalMemory(snowRunnerProcess);
                var currentValue = memory.Read<int>(flagAddress);
                if (currentValue != 0 && currentValue != 1)
                {
                    throw new Exception(String.Format("FlyMode flag contained unexpected value {0}", currentValue));
                }

                var newValue = enabled ? 1 : 0;
                memory.Write<int>(flagAddress, newValue);

                if (memory.Read<int>(flagAddress) != newValue)
                {
                    throw new Exception("FlyMode flag verification failed");
                }

                Console.WriteLine("FlyMode flag updated successfully!");
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Updating the FlyMode flag failed: {0}\n", ex.Message));
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
                throw new Exception(String.Format("Could not read current time from memory\n"));
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
            ResolveTODMemoryLayout(snowRunnerProcess, memory);

            var ownerAddress = memory.Read<IntPtr>(TODOwnerPointerAddress);
            if (ownerAddress == IntPtr.Zero)
            {
                throw new Exception("The time-of-day owner is not available; load into a map and try again");
            }

            var valueAddress = ownerAddress + TODFieldOffset;
            var currentValue = memory.Read<float>(valueAddress);
            if (float.IsNaN(currentValue) || float.IsInfinity(currentValue) || currentValue < 0f || currentValue > 24f)
            {
                throw new Exception(String.Format("Resolved time-of-day value was outside the expected range: {0}", currentValue));
            }

            return valueAddress;
        }

        private static void ResolveTODMemoryLayout(Process snowRunnerProcess, ExternalMemory memory)
        {
            if (TODOwnerPointerAddress != IntPtr.Zero && TODProcessId == snowRunnerProcess.Id)
            {
                return;
            }

            Console.WriteLine("Searching for time-of-day memory references");

            var module = snowRunnerProcess.MainModule;
            var scanner = new Scanner(snowRunnerProcess, module);
            var activeTick = scanner.CompiledFindPattern(TODTickDisablePattern);

            IntPtr tickInstructionAddress;
            if (activeTick.Found)
            {
                tickInstructionAddress = FindUniquePatternAddress(scanner, module, TODTickDisablePattern, "active TOD tick");
                memory.ReadRaw(tickInstructionAddress, out TODTickOriginalBytes, TODTickInstructionLength);
            }
            else
            {
                // If SnowFlyer was restarted while the game was frozen, the first store is
                // already NOPed. A second store in the same routine supplies identical original
                // instruction bytes, including the current field displacement.
                tickInstructionAddress = FindUniquePatternAddress(scanner, module, TODTickRevertPattern, "disabled TOD tick");
                var fieldReferenceAddress = FindUniquePatternAddress(scanner, module, TODFieldReferencePattern, "TOD field reference");
                memory.ReadRaw(fieldReferenceAddress, out TODTickOriginalBytes, TODTickInstructionLength);
            }

            TODFieldOffset = BitConverter.ToInt32(TODTickOriginalBytes, TODFieldDisplacementOffset);
            if (TODFieldOffset <= 0 || TODFieldOffset > 0x10000)
            {
                throw new Exception(String.Format("Resolved an invalid time-of-day field offset: {0:X}", TODFieldOffset));
            }

            var callSiteAddress = FindUniquePatternAddress(scanner, module, TODAccessorCallPattern, "TOD owner accessor call");
            var updateFunctionAddress = ReadOffsetFromOpcode(memory, callSiteAddress + TODUpdateCallOffset, 1);
            var tickDistance = tickInstructionAddress.ToInt64() - updateFunctionAddress.ToInt64();
            if (tickDistance < 0 || tickDistance > 0x10000)
            {
                throw new Exception("The resolved TOD call site did not target the routine containing the tick instruction");
            }

            var accessorAddress = ReadOffsetFromOpcode(memory, callSiteAddress, 1);
            byte[] accessorBytes;
            memory.ReadRaw(accessorAddress, out accessorBytes, 8);
            if (accessorBytes[0] != 0x48 || accessorBytes[1] != 0x8D || accessorBytes[2] != 0x05 || accessorBytes[7] != 0xC3)
            {
                throw new Exception("The resolved TOD owner accessor did not have the expected LEA/RET form");
            }

            var ownerPointerAddress = ReadOffsetFromOpcode(memory, accessorAddress, 3);
            var moduleStart = module.BaseAddress.ToInt64();
            var moduleEnd = moduleStart + module.ModuleMemorySize;
            if (ownerPointerAddress.ToInt64() < moduleStart || ownerPointerAddress.ToInt64() > moduleEnd - IntPtr.Size)
            {
                throw new Exception("The resolved TOD owner pointer was outside SnowRunner.exe");
            }

            TODOwnerPointerAddress = ownerPointerAddress;
            TODTickInstructionAddress = tickInstructionAddress;
            TODProcessId = snowRunnerProcess.Id;

            Console.WriteLine(
                "Found TOD owner pointer at SnowRunner.exe+{0:X}, field offset +{1:X}, tick at +{2:X}",
                TODOwnerPointerAddress.ToInt64() - moduleStart,
                TODFieldOffset,
                TODTickInstructionAddress.ToInt64() - moduleStart);
        }

        private static IntPtr FindUniquePatternAddress(Scanner scanner, ProcessModule module, string pattern, string label)
        {
            var compiledPattern = new CompiledScanPattern(pattern);
            var firstMatch = scanner.CompiledFindPattern(compiledPattern, 0);
            if (!firstMatch.Found)
            {
                throw new Exception(String.Format("Could not find {0}", label));
            }

            if (firstMatch.Offset + 1 < module.ModuleMemorySize)
            {
                var secondMatch = scanner.CompiledFindPattern(compiledPattern, firstMatch.Offset + 1);
                if (secondMatch.Found)
                {
                    throw new Exception(String.Format("Could not uniquely find {0}", label));
                }
            }

            return module.BaseAddress + firstMatch.Offset;
        }

        private static bool IsTODTickerRunning(Process snowRunnerProcess)
        {
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            var activeTickFound = scanner.CompiledFindPattern(TODTickDisablePattern).Found;
            var disabledTickFound = scanner.CompiledFindPattern(TODTickRevertPattern).Found;

            if (activeTickFound == disabledTickFound)
            {
                throw new Exception("Could not determine whether the time-of-day ticker is running");
            }

            return activeTickFound;
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
                try
                {
                    // Read the actual patch state so restarting SnowFlyer while time is frozen
                    // does not leave the local state out of sync with the game.
                    IsTimePassing = IsTODTickerRunning(snowRunnerProcess);
                    if (IsTimePassing)
                    {
                        Console.WriteLine("Disabling timer...");
                        StopTODTicker(snowRunnerProcess);
                    }
                    else
                    {
                        Console.WriteLine("Resuming timer...");
                        ResumeTODTicker(snowRunnerProcess);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
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
