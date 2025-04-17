using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
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
		cts?.Cancel();
		await semaphore.WaitAsync();
		cts = new CancellationTokenSource();
		ctoken = cts.Token;

		Task.Factory.StartNew(() =>
		{
			try
			{
				using var stream = File.OpenRead(imgPath);
				using var codec = SKCodec.Create(stream);

				Dispatcher.UIThread.Invoke(() => parentWindow.Title += $" | {codec.Info.Width}x{codec.Info.Height}");
				var frameCount = codec.FrameCount;
				if(frameCount > 1)
				{
					codec.Dispose();
					stream.Dispose();
					var frameQueue = new BlockingCollection<(WriteableBitmap Frame, int Delay)>(5);

					var producerTask = Task.Run(() =>
					{
						var ext = System.IO.Path.GetExtension(imgPath).ToLowerInvariant();
						IAnimatedImageDecoder _decoder = ext switch
						{
							".gif" => new GifDecoder(imgPath),
							".webp" => new WebPDecoder(imgPath),
							_ => throw new NotSupportedException("Unsupported format")
						};

						while(!ctoken.IsCancellationRequested)
						{
							for(int i = 0; i < frameCount && !ctoken.IsCancellationRequested; i++)
							{
								var (frame, delay) = _decoder.DecodeFrameAsync(i, ctoken).Result;
								frameQueue.Add((frame, delay), ctoken);
							}
						}
					});
					var consumerTask = Task.Run(async () =>
					{
						// Wait a sec for the producer to get a head start
						await Task.Delay(200, ctoken);
						while(!ctoken.IsCancellationRequested)
						{
							if(frameQueue.TryTake(out var item, Timeout.Infinite, ctoken))
							{
								await Dispatcher.UIThread.InvokeAsync(() => Source = item.Frame);
								await Task.Delay(item.Delay, ctoken);
								// Maybe put in a check to see if the queue is ever at zero && then
								// give it a bit to fill back up?
							}
						}

						// Clean up any remaining frames
						while(frameQueue.TryTake(out var leftover))
						{
							leftover.Frame?.Dispose();
						}

						await Dispatcher.UIThread.InvokeAsync(() => Source = null);
					});
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
}

// SkiaSharp doesn't handle pulling frames very well so we have to do it ourselves for now.

public class GifFrameMetadata
{
	public int X { get; set; }
	public int Y { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public int DelayTimeMs { get; set; }
	public int DisposalMethod { get; set; }
	public bool HasTransparency { get; set; }
	public byte TransparencyIndex { get; set; }
}

public static class GifFrameParser
{
	public static List<GifFrameMetadata> Parse(string filePath)
	{
		using var fs = File.OpenRead(filePath);
		using var br = new BinaryReader(fs);

		var frames = new List<GifFrameMetadata>();
		GifFrameMetadata? pendingMetadata = null;

		// Skip GIF Header (6 bytes) and Logical Screen Descriptor (7 bytes)
		br.BaseStream.Seek(6, SeekOrigin.Begin);
		byte[] lsd = br.ReadBytes(7);
		byte packed = lsd[4];

		if(HasGlobalColorTable(packed))
		{
			int gctSize = GetColorTableSize(packed);
			br.BaseStream.Seek(gctSize * 3, SeekOrigin.Current);
		}

		while(br.BaseStream.Position < br.BaseStream.Length)
		{
			byte b = br.ReadByte();

			switch(b)
			{
				case 0x21:
				{
					// Extension Introducer
					byte label = br.ReadByte();
					if(label == 0xF9) // Graphic Control Extension
					{
						br.ReadByte(); // Block size (always 4)
						packed = br.ReadByte();
						int delay = br.ReadUInt16();
						byte transIndex = br.ReadByte();
						br.ReadByte(); // Block terminator

						pendingMetadata = new GifFrameMetadata
						{
							DisposalMethod = (packed >> 2) & 0b111,
							DelayTimeMs = delay * 10,
							HasTransparency = (packed & 0b1) != 0,
							TransparencyIndex = transIndex
						};
					}
					else
					{
						// Skip unknown extension
						SkipSubBlocks(br);
					}
				}
					break;

				case 0x2C:
				{
					// Image Descriptor
					int x = br.ReadUInt16();
					int y = br.ReadUInt16();
					int w = br.ReadUInt16();
					int h = br.ReadUInt16();
					byte packedFields = br.ReadByte();

					if((packedFields & 0b10000000) != 0)
					{
						int lctSize = GetColorTableSize(packedFields);
						br.BaseStream.Seek(lctSize * 3, SeekOrigin.Current);
					}

					br.ReadByte(); // LZW Minimum Code Size

					// Now skip the actual image data
					SkipSubBlocks(br);

					if(pendingMetadata == null)
					{
						pendingMetadata = new GifFrameMetadata();
					}

					pendingMetadata.X = x;
					pendingMetadata.Y = y;
					pendingMetadata.Width = w;
					pendingMetadata.Height = h;

					frames.Add(pendingMetadata);
					pendingMetadata = null;
				}
					break;

				case 0x3B: // Trailer
					return frames;

				case 0x00: // This is likely a block terminator					
					break;

				default:
					throw new InvalidDataException($"Unknown block: 0x{b:X2}");
			}
		}

		return frames;
	}

	private static bool HasGlobalColorTable(byte packed)
	{
		return (packed & 0b10000000) != 0;
	}

	private static int GetColorTableSize(byte packed)
	{
		return 1 << ((packed & 0b00000111) + 1);
	}

	private static void SkipSubBlocks(BinaryReader br)
	{
		byte size;
		while((size = br.ReadByte()) != 0)
		{
			br.BaseStream.Seek(size, SeekOrigin.Current);
		}
	}
}

public class WebPFrameMetadata
{
	public int X { get; set; }
	public int Y { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }

	public int DurationMs { get; set; }
	public bool BlendWithPrevious { get; set; }
	public bool DisposeToBackground { get; set; }
}

public static class WebPFrameParser
{
	public static List<WebPFrameMetadata> Parse(string filePath)
	{
		using var fs = File.OpenRead(filePath);
		using var br = new BinaryReader(fs);
		var frames = new List<WebPFrameMetadata>();

		if(br.ReadUInt32() != 0x46464952) // "RIFF"
		{
			throw new InvalidDataException("Not a RIFF file");
		}

		br.ReadUInt32(); // file size
		if(br.ReadUInt32() != 0x50424557) // "WEBP"
		{
			throw new InvalidDataException("Not a WebP file");
		}

		while(br.BaseStream.Position + 8 < br.BaseStream.Length)
		{
			uint chunkId = br.ReadUInt32();
			uint chunkSize = br.ReadUInt32();
			long chunkStart = br.BaseStream.Position;

			if(chunkId == 0x464D4E41) // "ANMF"
			{
				var frame = new WebPFrameMetadata
				{
					X = br.ReadUInt24(),
					Y = br.ReadUInt24(),
					Width = br.ReadUInt24() + 1,
					Height = br.ReadUInt24() + 1
				};

				uint durationAndFlags = br.ReadUInt32();
				frame.DurationMs = (int)(durationAndFlags & 0xFFFFFF);
				byte flags = (byte)(durationAndFlags >> 24);

				frame.DisposeToBackground = (flags & 0b00000001) != 0;
				frame.BlendWithPrevious = (flags & 0b00000010) == 0; // 0 = blend, 1 = no blend

				frames.Add(frame);
			}

			br.BaseStream.Seek(chunkStart + chunkSize + (chunkSize % 2), SeekOrigin.Begin); // even padding
		}

		return frames;
	}

	// Reads a 24-bit little-endian int
	private static int ReadUInt24(this BinaryReader br)
	{
		int b1 = br.ReadByte();
		int b2 = br.ReadByte();
		int b3 = br.ReadByte();
		return b1 | (b2 << 8) | (b3 << 16);
	}
}

public interface IAnimatedImageDecoder : IDisposable
{
	int FrameCount { get; }
	SKSizeI Size { get; }

	Task<(WriteableBitmap Frame, int DelayMs)> DecodeFrameAsync(int index, CancellationToken ct);
}

public class GifDecoder : IAnimatedImageDecoder
{
	private readonly SKCodec Codec;
	private readonly List<GifFrameMetadata> AllFrames;
	public int FrameCount => Codec.FrameCount;
	public SKSizeI Size => Codec.Info.Size;
	private SKBitmap? CompositedBitmap;
	private SKBitmap? PreviousCompositedBitmap;

	//Stopwatch sw = new Stopwatch();

	public GifDecoder(string path)
	{
		Codec = SKCodec.Create(File.OpenRead(path)) ?? throw new Exception("Invalid GIF");
		AllFrames = GifFrameParser.Parse(path);
		CompositedBitmap = new SKBitmap(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		CompositedBitmap.Erase(SKColors.Transparent);
		//sw.Start();
	}

	public async Task<(WriteableBitmap, int)> DecodeFrameAsync(int index, CancellationToken ct)
	{
		//sw.Restart();
		var info = AllFrames[index];
		var frameInfo = Codec.FrameInfo[index];

		// Handle disposal from previous frame
		if(index > 0)
		{
			var prevMeta = AllFrames[index - 1];
			var prevFrameInfo = Codec.FrameInfo[index - 1];

			if(prevMeta.DisposalMethod == 2) // Restore to background
			{
				using var canvas = new SKCanvas(CompositedBitmap);
				canvas.DrawRect(new SKRectI(prevMeta.X, prevMeta.Y, prevMeta.X + prevMeta.Width, prevMeta.Y + prevMeta.Height),
					new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src });
			}
			else if(prevMeta.DisposalMethod == 3 && PreviousCompositedBitmap != null)
			{// Restore to previous -- I'm yet to see this actually happen
				CompositedBitmap?.Dispose();
				CompositedBitmap = PreviousCompositedBitmap.Copy();
			}
		}

		// Backup before disposal if needed
		if(info.DisposalMethod == 3)
		{// Still have not seen this happen
			PreviousCompositedBitmap?.Dispose();
			PreviousCompositedBitmap = CompositedBitmap?.Copy();
		}

		// Decode frame into raw bitmap
		using var rawFrame = new SKBitmap(Size.Width, Size.Height);
		var options = new SKCodecOptions(index, frameInfo.RequiredFrame);
		Codec.GetPixels(rawFrame.Info, rawFrame.GetPixels(), options);

		// Draw raw frame onto composited canvas at offset
		using(var canvas = new SKCanvas(CompositedBitmap))
		{
			var dstRect = new SKRectI(info.X, info.Y, info.X + info.Width, info.Y + info.Height);
			var srcRect = new SKRectI(info.X, info.Y, info.X + info.Width, info.Y + info.Height);
			canvas.DrawBitmap(rawFrame, srcRect, dstRect);
		}

		var wb = ConvertToWriteableBitmap(CompositedBitmap);
		//sw.Stop();
		//Trace.WriteLine("Gif frame took: " + sw.ElapsedMilliseconds + "ms Delay is:" + info.DelayTimeMs);
		// fyi this is coming in at 0 for small gifs && single digits for large ones
		return (wb, info.DelayTimeMs);
	}

	public void Dispose()
	{
		Codec.Dispose();
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

public class WebPDecoder : IAnimatedImageDecoder
{
	private readonly SKCodec Codec;
	private readonly SKCodecFrameInfo[] AllFrameInfos;
	private SKBitmap? CompositedBitmap;
	private SKBitmap? PrevCompositedBitmap;
	public int FrameCount => Codec.FrameCount;
	public SKSizeI Size => Codec.Info.Size;

	public WebPDecoder(string path)
	{
		Codec = SKCodec.Create(File.OpenRead(path)) ?? throw new Exception("Invalid WebP");
		AllFrameInfos = Codec.FrameInfo;
		CompositedBitmap = new SKBitmap(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		CompositedBitmap.Erase(SKColors.Transparent);
	}

	public async Task<(WriteableBitmap, int)> DecodeFrameAsync(int index, CancellationToken ct)
	{
		var frameInfo = AllFrameInfos[index];

		// Handle disposal from the previous frame
		if(index > 0)
		{
			var prevFrameInfo = AllFrameInfos[index - 1];
			if(prevFrameInfo.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious && 
				PrevCompositedBitmap != null)
			{// Yet to see this hit
				CompositedBitmap?.Dispose();
				CompositedBitmap = PrevCompositedBitmap.Copy();  // Restore the previous frame
			}
			else if(prevFrameInfo.DisposalMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
			{
				// Clear the area that was modified in the previous frame (Transparent background)
				// Still yet to see this happen
				using var canvas = new SKCanvas(CompositedBitmap);
				canvas.Clear(SKColors.Transparent);
			}
		}

		// Just in case we ever run into an image that uses this
		if(frameInfo.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
		{
			PrevCompositedBitmap?.Dispose();
			PrevCompositedBitmap = CompositedBitmap.Copy();
		}

		// Get current frame
		using var rawFrame = new SKBitmap(Size.Width, Size.Height);
		var options = new SKCodecOptions(index, frameInfo.RequiredFrame);
		Codec.GetPixels(rawFrame.Info, rawFrame.GetPixels(), options);

		// Draw the frame onto the composited bitmap
		using(var canvas = new SKCanvas(CompositedBitmap))
		{
			var frameRect = new SKRectI(0, 0, rawFrame.Width, rawFrame.Height);
			canvas.DrawBitmap(rawFrame, frameRect, frameRect);
		}

		var wb = ConvertToWriteableBitmap(CompositedBitmap);

		return (wb, frameInfo.Duration);
	}

	public void Dispose()
	{
		Codec.Dispose();
		CompositedBitmap?.Dispose();
		PrevCompositedBitmap?.Dispose();
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
