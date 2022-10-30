using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sources;

namespace SnowRunnerStutterPatcher
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
        private static readonly byte[] DevCheckRevertPatch = { 0x80, 0xB8, 0xC8, 0x00, 0x00, 0x00, 0X00 }; //cmp byte ptr [rax+000000C8],01


        private static readonly string FlyModeFlagPatternA = "83 3D C7 63 59 02 00 48"; //cmp dword ptr[SnowRunner.exe + 2E24D74],00  48 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] FlyModeFlagPatchA = { 0x83, 0x3D, 0xC7, 0x63, 0x59, 0x02, 0X01 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],01

        private static readonly string FlyModeRevertPatternA = "83 3D C7 63 59 02 01 48"; //cmp dword ptr[SnowRunner.exe + 2E24D74],01  48 ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] FlyModeRevertPatchA = { 0x83, 0x3D, 0xC7, 0x63, 0x59, 0x02, 0X00 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],00


        private static readonly string FlyModeFlagPatternB = "83 3D CD 3E 5C 02 00 0F"; //cmp dword ptr [SnowRunner.exe+2E24D74],00    0F    ON NEXT BYTE FOR UNIQUENESS
        private static readonly byte[] FlyModeFlagPatchB = { 0x83, 0x3D, 0xCD, 0x3E, 0x5C, 0x02, 0X01 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],01

        private static readonly byte[] FlyModeRevertPatchB = { 0x83, 0x3D, 0xCD, 0x3E, 0x5C, 0x02, 0X00 }; //cmp dword ptr[SnowRunner.exe + 2E24D74],00
        private static readonly string FlyModeRevertPatternB = "83 3D CD 3E 5C 02 01 0F"; //cmp dword ptr [SnowRunner.exe+2E24D74],01    0F    ON NEXT BYTE FOR UNIQUENESS


        [STAThread]
        static void Main(string[] args)
        {
            bool showMenu = true;
            while (showMenu)
            {
                showMenu = MainMenu();
            }

        }
        private static bool MainMenu()
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

            Console.WriteLine("\n \nPlease select an option:");
            Console.WriteLine("1) enable fly mode");
            Console.WriteLine("2) disable fly mode");
            Console.WriteLine("3) exit");
            Console.Write("# ");

            int input;
            int.TryParse(Console.ReadKey().KeyChar.ToString(), out input);

            Console.WriteLine();
            if (input == 1)
            {
                Console.Clear();
                EnableFreeCam();
                Console.ReadKey();
                return true;
            }
            if (input == 2)
            {
                Console.Clear();
                DisableFreeCam();
                Console.ReadKey();

                return true;
            }
            if (input == 3)
            {
                Console.Clear();
                Console.WriteLine("option 3 picked");

                return false;
            }
            else
            {
                return true;
            }
        }

        private static void EnableFreeCam()
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
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);
            try
            {
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckPattern, DevCheckPatch, "DevFlag");
                SearchAndApplyPatch(snowRunnerProcess, scanner, FlyModeFlagPatternA, FlyModeFlagPatchA, "Fly1");
                SearchAndApplyPatch(snowRunnerProcess, scanner, FlyModeFlagPatternB, FlyModeFlagPatchB, "Fly2");

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\n\n FreeCam Enabled!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void DisableFreeCam()
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
            var scanner = new Scanner(snowRunnerProcess, snowRunnerProcess.MainModule);

            try
            {
                SearchAndApplyPatch(snowRunnerProcess, scanner, DevCheckRevertPattern, DevCheckRevertPatch, "DevFlag");
                SearchAndApplyPatch(snowRunnerProcess, scanner, FlyModeRevertPatternA, FlyModeRevertPatchA, "Fly1");
                SearchAndApplyPatch(snowRunnerProcess, scanner, FlyModeRevertPatternB, FlyModeRevertPatchB, "Fly2");

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\n\n FreeCam Disabled!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void SearchAndApplyPatch(Process snowRunnerProcess, Scanner scanner, String searchPattern, byte[] patchBytes, String label)
        {
            var offset = scanner.CompiledFindPattern(searchPattern);
            Console.WriteLine("Searching for {0} patch location", label);
            if (offset.Found)
            {
                Console.WriteLine(offset.Offset);
                Console.WriteLine("Found {0} patch location. Patching game in memory...", label);
                try
                {
                    var memory = new ExternalMemory(snowRunnerProcess);
                    var baseAddress = snowRunnerProcess.MainModule.BaseAddress + offset.Offset;
                    memory.WriteRaw(baseAddress, patchBytes);
                    Console.WriteLine("Patch in memory successful!");
                }
                catch (Exception)
                {
                    throw new Exception(String.Format("Patching in memory failed for {0} \n Try running SnowRunnerStutterPatcher as Administrator", label));
                }

            }
            else
            {
                throw new Exception(String.Format("Could not find patch location for {0}", label));
            }
        }
    }
}
