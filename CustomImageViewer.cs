using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Peepr;

public class CustomImageViewer : Image
{
	public bool RunAnimationLoop { get; set; }
	private readonly Lock LoadImageLock = new();

	public void LoadImage(string imgPath, Window parentWindow)
	{
		// If there is already an animation running we stop it gracefully by setting RunAnimationLoop to false
		// && waiting for the lock to be released so we don't have multiple threads trying to load images in 
		// case the animation has a long delay between frames || anything else that might cause a delay

		RunAnimationLoop = false;
		lock(LoadImageLock)
		{
			Task.Run(() =>
			{
				// We're going to take this off the UI thread to prevent blocking the UI as there could be a lot
				// of frames to load && later for the animation loop which we could do on a timer but this is
				// simpler && safer I think with a little bit less overhead
				try
				{
					using var stream = File.OpenRead(imgPath);
					using var codec = SKCodec.Create(stream);

					Dispatcher.UIThread.Invoke(() => parentWindow.Title += " | " + 
						codec.Info.Width + "x" + codec.Info.Height);
					var frameCount = codec.FrameCount;
					if(frameCount > 1)
					{
						// if we got here that means this is an animated image, so we need to pull the frames out
						// && store them so they can be displayed in a loop
						List<(Bitmap Frame, int Delay)> frames = new();
						for(int i = 0; i < frameCount; i++)
						{
							using var skBitmap = new SKBitmap(codec.Info.Width, codec.Info.Height);
							var frameInfo = new SKCodecOptions(i);
							codec.GetPixels(skBitmap.Info, skBitmap.GetPixels(), frameInfo);

							using var skImage = SKImage.FromBitmap(skBitmap);
							using var skData = skImage.Encode();
							using var ms = new MemoryStream(skData.ToArray());
							var frame = new Bitmap(ms);

							frames.Add((frame, codec.FrameInfo[i].Duration));
						}
						RunAnimationLoop = true;
						int frameIndex = 0;
						while(RunAnimationLoop)
						{
							Dispatcher.UIThread.Invoke(() => Source = frames[frameIndex].Frame);
							Thread.Sleep(frames[frameIndex].Delay);
							frameIndex = (frameIndex + 1) % frameCount;
						}
						foreach(var frame in frames)
						{
							frame.Frame.Dispose();
						}
						frames.Clear();
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
					// TODO logging && notification
					Helpers.WriteLogEntry(ex.ToString());
				}
			});
		}
	}
}
