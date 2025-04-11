using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.VisualTree;
using LibVLCSharp.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Peepr;

public static class Helpers
{
	public static object lockLog = true;

	public static void OpenLogFile()
	{
		var fileName = $"log-{DateTime.Now:yyyy-MM-dd}.txt";
		var path = System.IO.Path.Combine(Program.LogFolderPath, fileName);
		Process.Start("notepad.exe", path);
	}

	public static void WriteLogEntry(string entry)
	{
		// Todo send this to the server too && logic to cleanup old logs
		lock(lockLog)
		{
			var fileName = $"log-{DateTime.Now:yyyy-MM-dd}.txt";
			using(TextWriter tw = new StreamWriter(System.IO.Path.Combine(Program.LogFolderPath, fileName), true))
			{
				tw.WriteLine(entry);
			}
		}
	}

	public static string FormatFileSize(long bytes)
	{
		var unit = 1024;
		if(bytes < unit)
		{
			return $"{bytes} B";
		}

		var exp = (int)(Math.Log(bytes) / Math.Log(unit));
		return $"{bytes / Math.Pow(unit, exp):F2} {"KMGTPE"[exp - 1]}B";
	}
}

public static class Debouncer
{
	static ConcurrentDictionary<string, CancellationTokenSource> tokens = new();

	public static void Debounce(string uniqueKey, Action action, int milliSeconds)
	{
		var token = tokens.AddOrUpdate(uniqueKey,
			(key) => //key not found - create new
			{
				return new CancellationTokenSource();
			},
			(key, existingToken) => //key found - cancel task and recreate
			{
				existingToken.Cancel(); //cancel previous
				return new CancellationTokenSource();
			});

		//schedule execution after pause
		Task.Delay(milliSeconds, token.Token).ContinueWith(task =>
		{
			if(!task.IsCanceled)
			{
				action(); //run
				if(tokens.TryRemove(uniqueKey, out var cts))
				{
					cts.Dispose(); //cleanup
				}
			}
		}, token.Token);
	}
}

[JsonSerializable(typeof(Asset))]
public class Asset
{
	public string browser_download_url { get; set; }
}

[JsonSerializable(typeof(Root))]
public class Root
{
	public bool draft { get; set; }
	public bool prerelease { get; set; }
	public DateTime created_at { get; set; }
	public DateTime published_at { get; set; }
	public List<Asset> assets { get; set; }
	public string name { get; set; }
}

[JsonSerializable(typeof(SetupSettings))]
public class SetupSettings
{
	public string Title { get; set; }
	public string DefaultInstallFolderName { get; set; }
	public string ExeFileName { get; set; }
	public string IconFileName { get; set; }
	public bool UseLauncher { get; set; }
	public string GithubOwner { get; set; }
	public string GithubRepo { get; set; }
	public string ZipName { get; set; }
	public DateTime? LatestDownloadedUpdateDate { get; set; }
	public string DoNotDeleteTheseFiles { get; set; }
}
