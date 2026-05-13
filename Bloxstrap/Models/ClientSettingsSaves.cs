using System;
using System.IO;
using System.Text.Json;
using Voidstrap;

public static class NullCoreRobloxSettingsManager
{
    public class NullCoreRobloxSettings
    {
        public int MemoryCleanerIntervalSeconds { get; set; }
    }

    private static readonly string FolderPath = Paths.Base;

    private static readonly string FilePath =
        Path.Combine(FolderPath, "NullCoreRobloxSaves.json");

    public static NullCoreRobloxSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new NullCoreRobloxSettings();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<NullCoreRobloxSettings>(json)
                   ?? new NullCoreRobloxSettings();
        }
        catch
        {
            return new NullCoreRobloxSettings();
        }
    }

    public static void Save(NullCoreRobloxSettings settings)
    {
        try
        {
            if (!Directory.Exists(FolderPath))
                Directory.CreateDirectory(FolderPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
        }
    }
}
