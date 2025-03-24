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
using System.Threading;
using System.Threading.Tasks;

namespace Peepr;

public static class Helpers
{
	public static object lockLog = true;

	// TODO Make update check ref Program.CurrentSoftwareVersion
	// TODO a function to uninstall the application
	//public static async Task<ClientInstructions> ApiGetInstructions()
	//{
	//	try
	//	{
	//		string url = $@"{Program.Settings.TelemetryBaseURL}Telem/GetInstructions/{Program.Settings.SourceID}";
	//		using (HttpClient client = new HttpClient())
	//		{
	//			var resp = await client.GetAsync(url);
	//			var result = await resp.Content.ReadFromJsonAsync<ClientInstructions>();
	//			return result;
	//		}
	//	}
	//	catch (Exception ex)
	//	{
	//		WriteLogEntry(ex.ToString());
	//		return null;
	//	}
	//}

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
			Height = 180,
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

		// Create custom title bar
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

		// Create message text block
		MessageTextBlock = new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(20),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};

		// Create buttons
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

		// Button container
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

		// Main layout
		var mainPanel = new Grid();
		mainPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Title bar
		mainPanel.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star)); // Message
		mainPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Buttons

		Grid.SetRow(titleBar, 0);
		Grid.SetRow(MessageTextBlock, 1);
		Grid.SetRow(buttonPanel, 2);

		mainPanel.Children.Add(titleBar);
		mainPanel.Children.Add(MessageTextBlock);
		mainPanel.Children.Add(buttonPanel);

		// Add a border around the window
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

	private new async Task<bool> ShowDialogAsync()
	{
		await ShowDialog(((IClassicDesktopStyleApplicationLifetime)Application
			.Current!.ApplicationLifetime!).MainWindow!);
		return await ResultTcs.Task;
	}
}
