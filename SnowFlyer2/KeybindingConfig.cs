using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SnowFlyer2
{
    public class KeybindingConfig
    {

        public string Instructions { get; set; } // not used as part of the keybinds, just there to make the json file have an explanation

        [JsonConverter(typeof(HexConverter))]
        public Keys ToggleFlyMode { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public Keys ToggleFreezeTime { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public Keys ToggleTimeLapseMode { get; set; }

        [JsonConverter(typeof(HexConverter))]
        public Keys GetPlayerLocation { get; set; }
    }
}
