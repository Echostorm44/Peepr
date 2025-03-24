/* Hi there.  Just a quick note to say that yes, I realize that I could have created a view model &&   
    Bound everything to the UI && used Commands instead of event handlers && I could have used DI to inject
	the logging && some of the other stuff && on some larger projects some || all of those things might make sense
	in this case I am trying to create a small simple project that runs quickly && many of those things would have
	created more overhead than they were worth.  I don't believe every class needs an interface, that every method
	needs unit tests && that using new instead of DI can give you more control over the lifetime of objects && make
	your code easier to understand.  One of the great things about software engineering is that you can get to the 
	same place in so many different ways.  Hugs && kisses, -Adam
 */
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Peepr;

public partial class MainWindow : Window
{
	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	IntPtr PeeprWindowHandle;
	List<string> AllFiles = new List<string>();
	private int VisibleFileCounter = 0;
	public string CurrentFileDeetz { get; set; }
	private readonly LibVLC libVLC;
	private MediaPlayer CurrentMediaPlayer;
	private Media? CurrentMovie;
	private bool VideoIsSeeking = false;
	private bool VideoIsMuted = false;
	private bool VideoJustStoleFocus = false;
	private int PreviousVolume = 50;
	private DispatcherTimer VideoPlayerSeekBarUpdateTimer = new DispatcherTimer();
	bool InitialLoading = true;
	bool SlideShowRunning = false;
	public bool WarnBeforeDelete { get; set; }

	public MainWindow()
	{
		InitializeComponent();
		this.DataContext = this;
		try
		{
			this.Position = new PixelPoint(Program.Settings.LastX, Program.Settings.LastY);
			imageViewer.IsVisible = true;
			string[] args = Environment.GetCommandLineArgs();
			var imageToLoaad = args.Skip(1).FirstOrDefault();
			PeeprWindowHandle = (this.TryGetPlatformHandle()?.Handle) ?? IntPtr.Zero;
			Core.Initialize();// Gotta call this for VLC
			libVLC = new LibVLC();
			CurrentMediaPlayer = new MediaPlayer(libVLC);

			CurrentMediaPlayer.EndReached += (_, _) =>
			{// this is because of a weird quirk where the callback happens on a different thread
				ThreadPool.QueueUserWorkItem((a) =>
				{
					videoViewer.MediaPlayer?.Play(CurrentMovie);
					VideoIsSeeking = true;
					Dispatcher.UIThread.Invoke(() =>
					{
						VideoSeekSlider.Value = CurrentMediaPlayer.Time / (double)CurrentMediaPlayer.Media.Duration * 100;
					});

					VideoIsSeeking = false;
				});
			};
			// Set initial volume
			chkWarnBeforeDelete.IsChecked = Program.Settings.WarnBeforeDelete;
			CurrentMediaPlayer.Volume = Program.Settings.VideoVolume;
			VideoVolumeSlider.Value = Program.Settings.VideoVolume;
			txtSlideShowSeconds.Value = Program.Settings.SlideShowDelaySeconds;

			VideoPlayerSeekBarUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
			VideoPlayerSeekBarUpdateTimer.Tick += (_, _) => UpdateSeekSlider();

			// Use the forward && back buttons on mouse to navigate
			this.AddHandler(PointerPressedEvent, (me, e) =>
			{
				var ptr = e.GetCurrentPoint(this);
				if(ptr.Properties.IsXButton1Pressed)
				{
					ShowPreviousFile();
				}
				else if(ptr.Properties.IsXButton2Pressed)
				{
					ShowNextFile();
				}
			}, RoutingStrategies.Tunnel, true);

			this.KeyDown += (_, e) =>
			{
				switch(e.Key)
				{
					case Avalonia.Input.Key.Right:
						ShowNextFile();
						break;
					case Avalonia.Input.Key.Left:
						ShowPreviousFile();
						break;
					case Avalonia.Input.Key.Escape:
						Environment.Exit(0);
						break;
					case Avalonia.Input.Key.Delete:
						DeleteCurrentFile();
						break;
				}
			};

			this.Deactivated += (_, _) =>
			{
				// When the video player appears && plays it steals focus off the app to the native vlc player
				// this makes it so we can't use the arrow hotkeys to navigate so we have to steal it back
				// we use the VideoJustStoleFocus so that you can still use other apps without Peepr constantly
				// stealing focus back as that would be pretty shitty.
				if(!VideoJustStoleFocus)
				{
					return;
				}
				Dispatcher.UIThread.Post(async () =>
				{
					await Task.Delay(10);
					SetForegroundWindow(PeeprWindowHandle);
					ShowWindow(PeeprWindowHandle, 9);
					VideoJustStoleFocus = false;
				});
			};

			videoViewer.AttachedToVisualTree += (_, _) =>
			{
				// This is because the MediaPlayer is not attached to the visual tree if we set it before this && 
				// videos will play in a weird external window. So we wait until it's attached to set it.
				// We also don't try to load anything because if it is a video mediaplayer would be null
				// I don't love this but I don't see a better option right now && I don't want to do a double type check
				// here && hope that it loads if a video comes up. This way we're positive everything is ready on
				// the first && subsequent loads.			
				videoViewer.MediaPlayer = CurrentMediaPlayer;
				videoViewer.IsVisible = false;
				//imageToLoaad = @"D:\Projects\Images\AnimatedWebp.webp";
				//imageToLoaad = @"D:\Projects\Videos\WebMTest.webm";
				//imageToLoaad = @"D:\Pix\Tablet.jpg";		
				ScanOpeningFolderAndOpenFile(imageToLoaad);
			};

			InitialLoading = false;
		}
		catch(Exception ex)
		{
			MessageBox.ShowAsync("Error", ex.ToString(), "OK");
		}
	}

	private void ScanOpeningFolderAndOpenFile(string imageToLoaad)
	{
		if(string.IsNullOrEmpty(imageToLoaad))
		{
			return;
		}

		var allRawFiles = Directory.GetFiles(Path.GetDirectoryName(imageToLoaad)!).ToList();
		foreach(var item in allRawFiles)
		{
			if(Program.ValidExtensions.Contains(Path.GetExtension(item).ToLower()))
			{
				AllFiles.Add(item);
			}
		}

		VisibleFileCounter = AllFiles.IndexOf(imageToLoaad);
		LoadImageOrVideo(imageToLoaad);
	}

	public void ShowNextFile()
	{
		VisibleFileCounter = (VisibleFileCounter + 1) % AllFiles.Count;
		LoadImageOrVideo(AllFiles[VisibleFileCounter]);
	}

	public void ShowPreviousFile()
	{
		VisibleFileCounter = (VisibleFileCounter - 1 + AllFiles.Count) % AllFiles.Count;
		LoadImageOrVideo(AllFiles[VisibleFileCounter]);
	}

	private void OnTogglePlayPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if(CurrentMediaPlayer.IsPlaying)
		{
			CurrentMediaPlayer.Pause();
			PauseIcon.Text = "▶";
		}
		else
		{
			CurrentMediaPlayer.Play();
			PauseIcon.Text = "⏸";
		}
	}

	private void UpdateSeekSlider()
	{
		if(CurrentMediaPlayer.Media != null && CurrentMediaPlayer.IsPlaying)
		{
			VideoIsSeeking = true;
			VideoSeekSlider.Value = CurrentMediaPlayer.Time / (double)CurrentMediaPlayer.Media.Duration * 100;
			VideoIsSeeking = false;
		}
	}

	private async void VideoVolumeSlider_ValueChanged(object? sender, 
		Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		CurrentMediaPlayer.Volume = (int)e.NewValue;
		if(!InitialLoading)
		{
			Program.Settings.VideoVolume = (int)e.NewValue;
			// debounce this save, otherwise if they are moving the slider it will save every time they move it 
			// potentially hundreds of times && causing a lot of unnecessary IO.
			Debouncer.Debounce("SaveSettings", async () => 
				await SettingsHelpers.SaveSettingsAsync(), 1000);
		}
	}

	private void VideoSeekSlider_ValueChanged(object? sender, 
		Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if(VideoIsSeeking)
		{
			return; // Prevent feedback loop
		}

		if(CurrentMediaPlayer.Media != null && CurrentMediaPlayer.IsPlaying)
		{
			double percentage = e.NewValue / 100.0;
			CurrentMediaPlayer.Time = (long)(CurrentMediaPlayer.Media.Duration * percentage);
		}
	}

	private void VideoMute_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if(VideoIsMuted)
		{
			CurrentMediaPlayer.Volume = PreviousVolume;
			MuteIcon.Text = "🔊";
			VideoVolumeSlider.Value = PreviousVolume;
		}
		else
		{
			PreviousVolume = CurrentMediaPlayer.Volume;
			CurrentMediaPlayer.Volume = 0;
			MuteIcon.Text = "🔇";
			VideoVolumeSlider.Value = 0;
		}
		VideoIsMuted = !VideoIsMuted;
	}

	void LoadImageOrVideo(string fileToLoad)
	{
		// Stop current video if playing so we are not wasting resources || possibly playing sound in 
		// the background if we're loading a pic now.
		if(videoViewer.MediaPlayer.IsPlaying)
		{
			VideoPlayerSeekBarUpdateTimer.Stop();
			videoViewer.MediaPlayer.Stop();
			CurrentMovie?.Dispose();
		}
		var fileInfo = new FileInfo(fileToLoad);
		var fileSizeReadable = Helpers.FormatFileSize(fileInfo.Length);

		this.Title = fileInfo.Name + " | " + (VisibleFileCounter + 1) + "/" +
			AllFiles.Count + " | " + fileSizeReadable;
		var extension = Path.GetExtension(fileToLoad).ToLower();
		if(Program.VideoFileExtensions.Contains(extension))
		{
			PlayVideo(fileToLoad);
		}
		else if(Program.ImageFileExtensions.Contains(extension))
		{
			videoViewer.IsVisible = false;
			imageViewer.IsVisible = true;
			imageViewer.LoadImage(fileToLoad, this);
		}
	}

	void PlayVideo(string videoToLoad)
	{
		VideoJustStoleFocus = true;
		videoViewer.IsVisible = true;
		imageViewer.IsVisible = false;
		videoViewer.MediaPlayer.Stop();
		VideoPlayerSeekBarUpdateTimer.Stop();
		CurrentMovie?.Dispose();
		CurrentMovie = new Media(libVLC, new Uri(videoToLoad));
		videoViewer.MediaPlayer.Play(CurrentMovie);
		VideoPlayerSeekBarUpdateTimer.Start();
		// get the resolution of the video but it's not available immediately so we wait a bit
		Thread.Sleep(50);
		uint px = 0;
		uint py = 0;
		var videoResolution = CurrentMediaPlayer.Size(0, ref px, ref py);
		this.Title += " | " + px + "x" + py;
	}

	private void PrevButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		ShowPreviousFile();
	}
	private void NextButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		ShowNextFile();
	}

	private void RegisterImageExtensions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		AppRegistrationHelper.RegisterExtensionsAndApp(ExtensionTypeToRegister.Image);
		Title = "Extensions Registered!";
	}

	private void RegisterVideoExtensions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		AppRegistrationHelper.RegisterExtensionsAndApp(ExtensionTypeToRegister.Video);
		Title = "Extensions Registered!";
	}

	private async void Window_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
	{
		Program.Settings.LastX = this.Position.X;
		Program.Settings.LastY = this.Position.Y;
		await SettingsHelpers.SaveSettingsAsync();
	}

	private async void OpenFileButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var dialog = TopLevel.GetTopLevel(this);
		var dialogResult = await dialog!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			AllowMultiple = false,
			Title = "Select a file to open",
			FileTypeFilter = new List<FilePickerFileType>() 
			{ 
				new FilePickerFileType("Image/Videos") 
				{
					Patterns = Program.ValidExtensions.Select(a => "*" + a).ToList()
				}
			}
		});
		if(dialogResult != null && dialogResult.Count > 0)
		{
			ScanOpeningFolderAndOpenFile(dialogResult.First().Path.LocalPath);
		}
	}

	private void SettingButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		settingPanel.IsPaneOpen = !settingPanel.IsPaneOpen;
	}

	private void DeleteButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		DeleteCurrentFile();
	}

	private async void DeleteCurrentFile()
	{
		if(AllFiles.Count == 0)
		{
			return;
		}
		if(Program.Settings.WarnBeforeDelete)
		{
			var dialogResult = await MessageBox.ShowAsync("You Sure Dawg?",
			"Do you really want to delete this cute innocent file you monster?", "Yeah", "Nah",
			showCancel: true);
			if(!dialogResult)
			{
				return;
			}
		}
		videoViewer.MediaPlayer.Stop();
		VideoPlayerSeekBarUpdateTimer.Stop();
		CurrentMovie?.Dispose();
		var fileToDelete = AllFiles[VisibleFileCounter];
		AllFiles.RemoveAt(VisibleFileCounter);
		File.Delete(fileToDelete);
		if(AllFiles.Count == 0)
		{
			imageViewer.IsVisible = false;
			videoViewer.IsVisible = false;
			return;
		}
		ShowNextFile();
	}

	private void UnregisterAllExtensions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		AppRegistrationHelper.DeRegisterExtensionsAndApp();
		Title = "Extensions Unregistered!";
	}

	private async void CheckForUpdatesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		//var moo = await MessageBox.ShowAsync("Information", "Your changes have been saved.", "OK", 
		//	showCancel: true);
		//var loo = moo;
		MessageBox.ShowAsync("Information", "Your changes have been saved.", "OK");
	}

	private void VideoView_PointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
	{
		pnlVideoControls.IsVisible = true;
	}

	private void VideoView_PointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
	{
		pnlVideoControls.IsVisible = false;
	}

	private void WarnBeforeDeleteChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		Program.Settings.WarnBeforeDelete = chkWarnBeforeDelete.IsChecked ?? true;
		Debouncer.Debounce("SaveSettings", async () =>
			await SettingsHelpers.SaveSettingsAsync(), 1000);
	}

	private void AboutButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
	}

	private async void SlideShowButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if(AllFiles.Count < 2)
		{
			return;
		}
		SlideShowRunning = !SlideShowRunning;
		while(SlideShowRunning && AllFiles.Count > 1)
		{
			btnSlideshow.Background = new SolidColorBrush(Colors.Red);
			ShowNextFile();
			await Task.Delay(Program.Settings.SlideShowDelaySeconds * 1000);
		}
		btnSlideshow.Background = (ImmutableSolidColorBrush)new BrushConverter().ConvertFromString("#33ffffff");
	}

	private void SlideShowSecondsChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
	{
		Program.Settings.SlideShowDelaySeconds = (int)(e.NewValue ?? 1);
		Debouncer.Debounce("SaveSettings", async () =>
			await SettingsHelpers.SaveSettingsAsync(), 1000);
	}
}

