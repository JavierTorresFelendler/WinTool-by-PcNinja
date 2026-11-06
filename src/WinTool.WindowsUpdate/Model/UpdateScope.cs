namespace WinTool.WindowsUpdate.Model
{
    /// <summary>Which kinds of updates a scan or run includes (WU-07).</summary>
    public sealed class UpdateScope
    {
        public UpdateScope(bool windows, bool optional, bool drivers, bool firmware)
        {
            Windows = windows;
            Optional = optional;
            Drivers = drivers;
            Firmware = firmware;
        }

        public bool Windows { get; }
        public bool Optional { get; }
        public bool Drivers { get; }
        public bool Firmware { get; }

        /// <summary>Without an explicit choice: everything except firmware.</summary>
        public static UpdateScope Default(bool includeFirmware = false) => new UpdateScope(true, true, true, includeFirmware);

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Windows) parts.Add("Windows");
            if (Optional) parts.Add("Optional");
            if (Drivers) parts.Add("Drivers");
            if (Firmware) parts.Add("Firmware/BIOS");
            return parts.Count == 0 ? "None" : string.Join(", ", parts);
        }
    }
}
