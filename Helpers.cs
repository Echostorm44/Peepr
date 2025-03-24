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

public class MessageBox : Window
{
	Button OKButton;
	Button CancelButton;
	TextBlock MessageTextBlock;
	TaskCompletionSource<bool> ResultTcs;

	public static async Task<bool> ShowAsync(string title, string message, string okText = "OK", 
		string cancelText = "Cancel", bool showCancel = false)
	{
		var messageBox = new MessageBox
		{
			Title = title,
			Width = 350,
			Height = 190,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
			ExtendClientAreaToDecorationsHint = true,
			Background = (ImmutableSolidColorBrush)new BrushConverter().ConvertFromString("#252627")
		};

		messageBox.InitializeComponents(title, message, okText, cancelText, showCancel);
		return await messageBox.ShowDialogAsync();
	}

	private void InitializeComponents(string title, string message, string okText, string cancelText, bool showCancel)
	{
		ResultTcs = new TaskCompletionSource<bool>();

		var titleBarBackground = (ImmutableSolidColorBrush)new BrushConverter().ConvertFromString("#007ACC");

		var titleBlock = new TextBlock
		{
			Text = title,
			Foreground = Brushes.White,
			FontWeight = FontWeight.SemiBold,
			Margin = new Thickness(10, 8, 0, 8),
			VerticalAlignment = VerticalAlignment.Center
		};

		var closeButton = new Button
		{
			Content = "✕",
			Width = 30,
			Height = 30,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			Padding = new Thickness(0),
			Margin = new Thickness(0),
			Background = Brushes.Transparent,
			Foreground = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Right,
			TabIndex = 2			
		};
		closeButton.Click += (s, e) =>
		{
			ResultTcs.SetResult(false);
			Close();
		};

		var titleBar = new Grid
		{
			Background = titleBarBackground,
			Height = 30
		};

		titleBar.Children.Add(titleBlock);
		titleBar.Children.Add(closeButton);

		MessageTextBlock = new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(20),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};

		ScrollViewer messageScrollViewer = new ScrollViewer();
		messageScrollViewer.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
		messageScrollViewer.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
		messageScrollViewer.Content = MessageTextBlock;

		OKButton = new Button
		{
			TabIndex = 0,
			Content = okText,
			Width = 80,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(5),
			HorizontalAlignment = HorizontalAlignment.Right
		};
		OKButton.Click += (s, e) =>
		{
			ResultTcs.SetResult(true);
			Close();
		};

		CancelButton = new Button
		{
			Content = cancelText,
			Width = 80,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(5),
			HorizontalAlignment = HorizontalAlignment.Right,
			TabIndex = 2
		};
		CancelButton.Click += (s, e) =>
		{
			ResultTcs.SetResult(false);
			Close();
		};

		var buttonPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(10)
		};

		if(showCancel)
		{
			buttonPanel.Children.Add(CancelButton);
		}
		buttonPanel.Children.Add(OKButton);

		var mainPanel = new Grid();
		mainPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Title bar
		mainPanel.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star)); // Message
		mainPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Buttons

		Grid.SetRow(titleBar, 0);
		Grid.SetRow(messageScrollViewer, 1);
		Grid.SetRow(buttonPanel, 2);

		mainPanel.Children.Add(titleBar);
		mainPanel.Children.Add(messageScrollViewer);
		mainPanel.Children.Add(buttonPanel);

		var border = new Border
		{
			BorderBrush = (ImmutableSolidColorBrush)new BrushConverter().ConvertFromString("#CCCCCC"),
			BorderThickness = new Thickness(1),
			Child = mainPanel
		};

		Content = border;
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);

		// In case the window is closed without clicking buttons
		if(!ResultTcs.Task.IsCompleted)
		{
			ResultTcs.SetResult(false);
		}
	}

	private async Task<bool> ShowDialogAsync()
	{
		await ShowDialog(((IClassicDesktopStyleApplicationLifetime)Application
			.Current!.ApplicationLifetime!).MainWindow!);
		return await ResultTcs.Task;
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