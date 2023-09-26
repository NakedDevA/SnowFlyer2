using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowFlyer2
{
    using Newtonsoft.Json;
    using System;
    using System.IO;
    using System.Windows.Forms;

    public class KeybindingManager
    {
        private const string ConfigFileName = "keybinds.json";
        private static readonly string ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        public KeybindingConfig ReadKeybindingConfig()
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                return JsonConvert.DeserializeObject<KeybindingConfig>(json);
            }
            else
            {
                // Default keybinding if the file doesn't exist
                KeybindingConfig defaultBinds = new KeybindingConfig { Instructions= "The keybinds are hex key modifiers - see microsoft docs for a full list. https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes", ToggleFlyMode = Keys.F1, ToggleFreezeTime=Keys.F2, ToggleTimeLapseMode=Keys.F3, GetPlayerLocation=Keys.F4};
                SaveKeybindingConfig(defaultBinds);
                return defaultBinds;
            }
        }

        public void SaveKeybindingConfig(KeybindingConfig config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigFilePath, json);
        }
    }

}
