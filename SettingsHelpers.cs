using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace Peepr;

public static class SettingsHelpers
{
	public static async Task SaveSettingsAsync()
	{
		try
		{
			var serializedSettings = JsonSerializer.Serialize(Program.Settings,
				SourceGenerationContext.Default.PeeprSettings);
			var filePath = Path.Combine(Program.DataFolderPath, "settings.json");
			await File.WriteAllTextAsync(filePath, serializedSettings);
		}
		catch(Exception ex)
		{
			Helpers.WriteLogEntry(ex.ToString());
		}
	}

	public static PeeprSettings ReadSettings()
	{
		var result = new PeeprSettings();
		try
		{
			var filePath = Path.Combine(Program.DataFolderPath, "settings.json");
			if(File.Exists(filePath))
			{
				var fileText = File.ReadAllText(filePath);
				result = JsonSerializer.Deserialize<PeeprSettings>(fileText,
					SourceGenerationContext.Default.PeeprSettings) ?? new PeeprSettings();
			}
		}
		catch(Exception ex)
		{
			Helpers.WriteLogEntry(ex.ToString());
		}
		return result;
	}
}

[JsonSerializable(typeof(PeeprSettings))]
public class PeeprSettings
{
	public bool FirstRun { get; set; }
	public int VideoVolume { get; set; }
	public bool WarnBeforeDelete { get; set; }
	public int LastX { get; set; }
	public int LastY { get; set; }
	public int SlideShowDelaySeconds { get; set; }
	public DateTime LastestUpdate { get; set; }

	public PeeprSettings()
	{
		FirstRun = true;
		VideoVolume = 50;
		WarnBeforeDelete = true;
		LastX = 0;
		LastY = 0;
		SlideShowDelaySeconds = 3;
	}
}

// We need this for AOT && tree shaking to work correctly
[JsonSerializable(typeof(PeeprSettings))]
[JsonSerializable(typeof(SetupSettings))]
[JsonSerializable(typeof(Asset))]
[JsonSerializable(typeof(Root[]))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
