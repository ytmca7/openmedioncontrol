using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using OpenMedionControl.Hardware;

namespace OpenMedionControl;

public sealed class ZoneSettings
{
    public LightEffect Effect { get; set; } = LightEffect.Static;
    public byte Brightness { get; set; } = 128;
    public byte Speed { get; set; } = 2;
    public bool Reverse { get; set; }
    public string Color { get; set; } = "#00A6FF";

    public ZoneSettings Clone() => (ZoneSettings)MemberwiseClone();

    [JsonIgnore]
    public (byte R, byte G, byte B) Rgb
    {
        get
        {
            int v = int.Parse(Color.TrimStart('#'), NumberStyles.HexNumber);
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
    }

    public void ApplyTo(LightZone zone)
    {
        var (r, g, b) = Rgb;
        Lighting.Apply(zone, Effect, Brightness, Speed, Reverse, r, g, b);
    }
}

public sealed class AppSettings
{
    public const string DefaultVendorPath = @"C:\Program Files (x86)\MedionOSD";

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenMedionControl");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string VendorPath { get; set; } = DefaultVendorPath;
    public bool LightsOn { get; set; } = true;
    public bool WinKeyLocked { get; set; }
    public bool DedicatedGpu { get; set; }
    public bool ApplyAllZones { get; set; }
    public Dictionary<LightZone, ZoneSettings> Zones { get; set; } = new()
    {
        [LightZone.Keyboard] = new(),
        [LightZone.Logo] = new(),
        [LightZone.LeftSide] = new(),
        [LightZone.RightSide] = new(),
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json));
        }
        catch (Exception) { }

        var imported = ImportFromVendor(DefaultVendorPath);
        imported.Save();
        return imported;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    static AppSettings Normalize(AppSettings? s)
    {
        s ??= new AppSettings();
        foreach (LightZone zone in Enum.GetValues<LightZone>())
            s.Zones.TryAdd(zone, new ZoneSettings());
        return s;
    }

    static AppSettings ImportFromVendor(string vendorPath)
    {
        var s = new AppSettings();
        try
        {
            var props = XDocument.Load(Path.Combine(vendorPath, "CConly.config.xml")).Root?.Element("properties");
            if (props == null) return s;

            s.LightsOn = props.Element("isGodSyncLightOn")?.Value != "0";
            s.WinKeyLocked = props.Element("winLockStatus")?.Value == "1";
            s.DedicatedGpu = props.Element("dedicatedcardStatus")?.Value == "1";

            var zoneNames = new (LightZone Zone, string Name)[]
            {
                (LightZone.Keyboard, "kbdzone"), (LightZone.Logo, "logozone"),
                (LightZone.LeftSide, "leftsidezone"), (LightZone.RightSide, "rightsidezone"),
            };
            foreach (var (zone, name) in zoneNames)
            {
                var el = props.Element(name);
                if (el == null) continue;
                byte Attr(string a) => byte.TryParse(el.Attribute(a)?.Value, out var v) ? v : (byte)0;
                s.Zones[zone] = new ZoneSettings
                {
                    Effect = ParseVendorEffect(el.Value.Trim()),
                    Brightness = Attr("brightness"),
                    Speed = Attr("speed"),
                    Reverse = el.Attribute("orientation")?.Value == "1",
                    Color = $"#{Attr("r"):X2}{Attr("g"):X2}{Attr("b"):X2}",
                };
            }
        }
        catch (Exception) { }
        return Normalize(s);
    }

    static LightEffect ParseVendorEffect(string name) =>
        name == "AlwaysOn" ? LightEffect.Static
        : Enum.TryParse<LightEffect>(name, out var e) ? e
        : LightEffect.Static;
}
