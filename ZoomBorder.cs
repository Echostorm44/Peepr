using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Reactive.Linq;

namespace Peepr;

public class ZoomBorder : Border
{
	private Control BorderChild = null;
	private Point OriginPoint;
	private Point StartPoint;

	private TranslateTransform GetTranslateTransform(Control element)
	{
		return (TranslateTransform)((TransformGroup)element.RenderTransform)
		  .Children.First(tr => tr is TranslateTransform);
	}

	private ScaleTransform GetScaleTransform(Control element)
	{
		return (ScaleTransform)((TransformGroup)element.RenderTransform)
		  .Children.First(tr => tr is ScaleTransform);
	}

	public override void ApplyTemplate()
	{
		base.ApplyTemplate();
		if(BorderChild == null)
		{
			var children = this.GetVisualChildren();
			if(children != null && children.Count() > 0)
			{
				BorderChild = (Control)children.First();
				Initialize(BorderChild);
			}
		}
	}

	public void Initialize(Control element)
	{
		BorderChild = element;
		if(BorderChild != null)
		{
			TransformGroup group = new TransformGroup();
			ScaleTransform st = new ScaleTransform();
			group.Children.Add(st);
			TranslateTransform tt = new TranslateTransform();
			group.Children.Add(tt);
			BorderChild.RenderTransform = group;
			BorderChild.RenderTransformOrigin = new RelativePoint(0.0, 0.0, RelativeUnit.Relative);
			PointerWheelChanged += ZoomBorder_PointerWheelChanged;
			PointerPressed += ZoomBorder_PointerPressed;
			PointerReleased += ZoomBorder_PointerReleased;
			PointerMoved += ZoomBorder_PointerMoved;
		}
	}

	private void ZoomBorder_PointerMoved(object? sender, PointerEventArgs e)
	{
		if(BorderChild != null)
		{
			if(e.Pointer.Captured != null)
			{
				var tt = GetTranslateTransform(BorderChild);
				Vector v = StartPoint - e.GetPosition(this);
				tt.X = OriginPoint.X - v.X;
				tt.Y = OriginPoint.Y - v.Y;
			}
		}
	}

	private void ZoomBorder_PointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if(BorderChild != null)
		{
			e.Pointer.Capture(null);
			Cursor = new Cursor(StandardCursorType.Arrow);
		}
	}

	private void ZoomBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if(BorderChild != null)
		{
			var tt = GetTranslateTransform(BorderChild);
			StartPoint = e.GetPosition(this);
			OriginPoint = new Point(tt.X, tt.Y);
			Cursor = new Cursor(StandardCursorType.Hand);
			e.Pointer.Capture(BorderChild);
			if(e.GetCurrentPoint(sender as Control).Properties.IsRightButtonPressed == true)
			{
				Reset();
			}
		}
	}

	private void ZoomBorder_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
	{
		if(BorderChild != null)
		{
			var st = GetScaleTransform(BorderChild);
			var tt = GetTranslateTransform(BorderChild);

			double zoom = e.Delta.Y > 0 ? .2 : -.2;
			if(!(e.Delta.Y > 0) && (st.ScaleX < .4 || st.ScaleY < .4))
			{
				return;
			}

			Point relative = e.GetPosition(BorderChild);
			double absoluteY = (relative.Y * st.ScaleY) + tt.Y;
			double absoluteX = (relative.X * st.ScaleX) + tt.X;

			st.ScaleX += zoom;
			st.ScaleY += zoom;

			tt.X = absoluteX - (relative.X * st.ScaleX);
			tt.Y = absoluteY - (relative.Y * st.ScaleY);
		}
	}

	public void Reset()
	{
		if(BorderChild != null)
		{
			// reset zoom
			var st = GetScaleTransform(BorderChild);
			st.ScaleX = 1.0;
			st.ScaleY = 1.0;

			// reset pan
			var tt = GetTranslateTransform(BorderChild);
			tt.X = 0.0;
			tt.Y = 0.0;
		}
	}
}