using Avalonia;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace Peepr;

class Program
{
	public static readonly string DataFolderPath =
		Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
			+ "\\Peepr\\";

	public static readonly string LogFolderPath =
		Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
			+ "\\Peepr\\Logs\\";
	public static List<string> ImageFileExtensions { get; set; }
	public static List<string> VideoFileExtensions { get; set; }
	public static HashSet<string> ValidExtensions { get; set; }
	public static PeeprSettings Settings { get; set; }

	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	[STAThread]
	public static void Main(string[] args)
	{
		if(!Directory.Exists(DataFolderPath))
		{
			Directory.CreateDirectory(DataFolderPath);
		}
		if(!Directory.Exists(LogFolderPath))
		{
			Directory.CreateDirectory(LogFolderPath);
		}
		Settings = SettingsHelpers.ReadSettings();
		ImageFileExtensions = new List<string>() { ".webp", ".jpg", ".jpeg", ".png", ".gif" };
		VideoFileExtensions = new List<string>() { ".mp4", ".webm", ".mkv", ".avi", ".flv" };
		ValidExtensions = new HashSet<string>() { ".webp", ".jpg", ".jpeg", ".png", ".gif",
			".mp4", ".webm", ".mkv", ".avi", ".flv" };

		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.WithInterFont()
				.LogToTrace();
	}
}