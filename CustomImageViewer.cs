using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Peepr;

public class CustomImageViewer : Image
{
	private static SemaphoreSlim semaphore = new SemaphoreSlim(1);
	public CancellationTokenSource cts { get; set; }
	public CancellationToken ctoken { get; set; }

	public async void LoadImage(string imgPath, Window parentWindow)
	{
		// If there is already an animation running we stop it gracefully by setting RunAnimationLoop to false
		// && waiting for the lock to be released so we don't have multiple threads trying to load images in 
		// case the animation has a long delay between frames || anything else that might cause a delay

		cts?.Cancel();
		await semaphore.WaitAsync();
		cts = new CancellationTokenSource();
		ctoken = cts.Token;

		Task.Factory.StartNew(() =>
		{
			// We're going to take this off the UI thread to prevent blocking the UI as there could be a lot
			// of frames to load && later for the animation loop which we could do on a timer but this is
			// simpler && safer I think, with a little bit less overhead
			try
			{
				using var stream = File.OpenRead(imgPath);
				using var codec = SKCodec.Create(stream);

				Dispatcher.UIThread.Invoke(() => parentWindow.Title += $" | {codec.Info.Width}x{codec.Info.Height}");
				var frameCount = codec.FrameCount;
				if(frameCount > 1)
				{
					// if we got here that means this is an animated image, so we need to pull the frames out
					// && store them so they can be displayed in a loop						
					Stopwatch sw = new Stopwatch();
					sw.Start();
					while(!ctoken.IsCancellationRequested)
					{
						for(int i = 0; i < frameCount && !ctoken.IsCancellationRequested; i++)
						{
							sw.Restart();
							using var skBitmap = new SKBitmap(codec.Info.Width, codec.Info.Height);
							var frameInfo = new SKCodecOptions(i);
							codec.GetPixels(skBitmap.Info, skBitmap.GetPixels(), frameInfo);
							using var writableBitmap = ConvertToWriteableBitmap(skBitmap);
							Dispatcher.UIThread.Invoke(() => Source = writableBitmap);
							sw.Stop();
							var elapsed = codec.FrameInfo[i].Duration - sw.ElapsedMilliseconds;
							var delay = Math.Max((int)elapsed, 1);
							Thread.Sleep(delay);
						}
					}
					Dispatcher.UIThread.Invoke(() => Source = null);
					//GC.Collect(); // This shouldn't be necessary but if we have memory issues we can try a forced collection maybe do it for all gens
				}
				else
				{
					var frame = new Bitmap(imgPath);
					Dispatcher.UIThread.Invoke(() => Source = frame);
				}
			}
			catch(Exception ex)
			{
				Helpers.WriteLogEntry(ex.ToString());
			}
		}, TaskCreationOptions.LongRunning).ContinueWith((task) =>
		{
			semaphore.Release();
		});
	}

	private WriteableBitmap ConvertToWriteableBitmap(SKBitmap skBitmap)
	{
		var info = new PixelSize(skBitmap.Width, skBitmap.Height);
		var dpi = new Vector(96, 96);
		var wb = new WriteableBitmap(info, dpi, Avalonia.Platform.PixelFormat.Bgra8888, AlphaFormat.Unpremul);

		using(var fb = wb.Lock())
		{
			Marshal.Copy(skBitmap.Bytes, 0, fb.Address, skBitmap.Bytes.Length);
		}

		return wb;
	}
}
