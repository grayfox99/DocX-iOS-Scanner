using System;
using System.Drawing.Imaging;
using System.Linq;
using AVFoundation;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using UIKit;

namespace iosDocumentScanner {
	/// <summary>
	/// An object that receives frames for querying (i.e., `RectangleScanner`, `iosDocumentScanner`)
	/// </summary>
	interface IRectangleViewer {
		void OnFrameCaptured (CVPixelBuffer buffer);
	}

	/// <summary>
	/// Controller object for app: responsible for UX, coordinates video processing and querying
	/// </summary>
	public partial class ViewController : UIViewController
	{
		VideoCapture captureController;
		VideoCaptureDelegate captureDelegate;
		RectangleScanner scanner;
		ObjectTracker tracker;
		IRectangleViewer activeViewer;
		
		AVCaptureVideoPreviewLayer previewLayer;
		Overlay overlay;
		UIView topBlurView;
		UIView bottomBlurView;
		UIView previewView;
		UIImageView bufferImageView = new UIImageView();
		UIImageView capturedImageView = new UIImageView();
		UIButton captureButton;
		UIButton resetButton;
		UIButton clearButton;

		public CGRect croppingBounds { get; set; }

		public CMVideoDimensions CMVideoDimensions { get; set; }
		public CIAffineTransform rotationTransform { get; set; }
		public static ViewController Instance { get; private set; }
		
		protected ViewController (IntPtr handle) : base (handle)
		{
			Instance = this;
			// Note: this .ctor should not contain any initialization logic.
		}

		public override void ViewDidLoad ()
		{
			base.ViewDidLoad ();

			previewView = new UIView ();
			previewView.Frame = View.Bounds;
			View.AddSubview (previewView);
			
			ConfigureBlurViews(View);
			View.AddSubview (ConfigureResetButton ());
			
			View.AddSubview (ConfigureOverlay (topBlurView, bottomBlurView));
			View.AddSubview (ConfigureBufferImageView ());

			ConfigureInitialVisionTask ();

			previewLayer = new AVCaptureVideoPreviewLayer (captureController.Session);
			previewView.Layer.AddSublayer (previewLayer);
			
			View.AddSubview (ConfigureCaptureButton());
			// Add the UIImageView to the view
			View.AddSubview(ConfigureCapturedImageView());
		}

		private UIView ConfigureCapturedImageView()
		{
			// Set the frame (position and size) of the UIImageView
			capturedImageView.Frame = new CGRect(View.Frame.Left, View.Frame.Bottom + 5, View.Frame.Right,
				View.Frame.Top - View.Frame.Bottom - 10);
			capturedImageView.BackgroundColor = UIColor.Black;
			//capturedImageView.Frame = View.Bounds;
			capturedImageView.UserInteractionEnabled = true;

			capturedImageView.Hidden = true;
			capturedImageView.Opaque = true;
			capturedImageView.AddSubview(ConfigureClearButton());
			
			clearButton.TranslatesAutoresizingMaskIntoConstraints = false;

			// Center horizontally in imageView
			clearButton.CenterXAnchor.ConstraintEqualTo(capturedImageView.CenterXAnchor).Active = true;

			// Align to bottom of imageView
			clearButton.BottomAnchor.ConstraintEqualTo(capturedImageView.BottomAnchor).Active = true;

			// Set width and height
			clearButton.WidthAnchor.ConstraintEqualTo(200).Active = true;
			clearButton.HeightAnchor.ConstraintEqualTo(100).Active = true;
			
			// Align to bottom of imageView, subtracting a constant to move it up
			clearButton.BottomAnchor.ConstraintEqualTo(capturedImageView.BottomAnchor, -100).Active = true;
			
			return capturedImageView;
		}

		private UIView ConfigureBufferImageView ()
		{
			bufferImageView.Frame = new CGRect (10, 30, 108, 115);
			return bufferImageView;
		}

		private UIView ConfigureOverlay (UIView tbv, UIView bbv)
		{
			//Configure layer on which we do our graphics
			overlay = new Overlay {
				Frame = new CGRect (tbv.Frame.Left, tbv.Frame.Bottom + 5, tbv.Frame.Right, bbv.Frame.Top - tbv.Frame.Bottom - 10),
				BackgroundColor = UIColor.Clear
			};
			return overlay;
		}
		
		private UIView ConfigureResetButton ()
		{
			resetButton = new UIButton ();
			resetButton.SetTitle ("Reset", UIControlState.Normal);
			resetButton.Hidden = true;
			resetButton.TouchDown += ResetTracking;
			resetButton.TranslatesAutoresizingMaskIntoConstraints = false;
			resetButton.Frame =  new CGRect(20, 20, 100, 44);
			return resetButton;
		}

		private UIView ConfigureClearButton()
		{
			clearButton = new UIButton(UIButtonType.RoundedRect);
			clearButton.TranslatesAutoresizingMaskIntoConstraints = false;
			clearButton.SetTitle("Clear", UIControlState.Normal);
			clearButton.SetTitleColor(UIColor.White, UIControlState.Normal);
			clearButton.BackgroundColor = UIColor.Blue;
			
			// Add an event handler for the button's TouchUpInside event
			clearButton.TouchUpInside += (sender, e) =>
			{
				capturedImageView.Hidden = true;
				capturedImageView.Image = null;
			};

			return clearButton;
		}
		
		private UIView ConfigureCaptureButton()
		{
			captureButton = new UIButton(UIButtonType.RoundedRect);
			captureButton.TranslatesAutoresizingMaskIntoConstraints = false;
			captureButton.SetTitle("Capture", UIControlState.Normal);
			captureButton.SetTitleColor(UIColor.White, UIControlState.Normal);
			captureButton.BackgroundColor = UIColor.Blue;
			View.AddSubview(captureButton);

			captureButton.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor).Active = true;
			captureButton.BottomAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.BottomAnchor).Active = true;
			captureButton.WidthAnchor.ConstraintEqualTo(100).Active = true;
			captureButton.HeightAnchor.ConstraintEqualTo(100).Active = true;
			
			// Add an event handler for the button's TouchUpInside event
			captureButton.TouchUpInside += (sender, e) => {
				captureController.CaptureImage();
			};

			return captureButton;
		}

		void ConfigureInitialVisionTask ()
		{
			// Assert overlay initialized
			scanner = new RectangleScanner (overlay);
			tracker = new ObjectTracker (overlay);

			activeViewer = scanner;

			captureDelegate = new VideoCaptureDelegate (OnFrameCaptured);
			captureDelegate.viewController = this;
			captureController = new VideoCapture (captureDelegate);
		}

		void ResetTracking (Object sender, EventArgs e)
		{
			overlay.Message = "Scanning";
			activeViewer = scanner;
			resetButton.Hidden = true;
		}

		private void ConfigureBlurViews (UIView mainView)
		{
			var blur = UIBlurEffect.FromStyle (UIBlurEffectStyle.Regular);
			topBlurView = new UIVisualEffectView (blur);
			mainView.AddSubview (topBlurView);
			bottomBlurView = new UIVisualEffectView (blur);
			mainView.AddSubview (bottomBlurView);
		}

		public override void ViewDidLayoutSubviews ()
		{
			base.ViewDidLayoutSubviews ();
			previewLayer.Frame = previewView.Bounds;

			var oneFifthHeight = previewLayer.Frame.Height / 5;
			topBlurView.Frame = new CGRect (previewLayer.Frame.Left, previewLayer.Frame.Top, previewLayer.Frame.Right, oneFifthHeight);
			bottomBlurView.Frame = new CGRect (previewLayer.Frame.Left, previewLayer.Frame.Bottom - oneFifthHeight, previewLayer.Frame.Right, oneFifthHeight);
			overlay.Frame = new CGRect (topBlurView.Frame.Left, topBlurView.Frame.Bottom, topBlurView.Frame.Right, bottomBlurView.Frame.Top - topBlurView.Frame.Bottom);
			resetButton.Frame = new CGRect (View.Frame.Right - 180, 40, 150, 50);
		}

		public override void DidReceiveMemoryWarning ()
		{
			base.DidReceiveMemoryWarning ();
			// Release any cached data, images, etc that aren't in use.
		}

		/// <summary>
		/// Sees if the touch is inside a detected rectangle. If so, switches to "Tracking" mode
		/// </summary>
		/// <param name="touches">Touches.</param>
		/// <param name="evt">Evt.</param>
		public override void TouchesBegan (NSSet touches, UIEvent evt)
		{
			base.TouchesBegan (touches, evt);

			var touch = touches.First () as UITouch;
			var pt = touch.LocationInView (overlay);
			var normalizedPoint = new CGPoint (pt.X / overlay.Frame.Width, pt.Y / overlay.Frame.Height);
			if (activeViewer == scanner) {
				var trackedRectangle = scanner.Containing (normalizedPoint);
				if (trackedRectangle != null) {
					tracker.Track (trackedRectangle);
					overlay.Message = "Target acquired";
					activeViewer = tracker;
					resetButton.Hidden = false;
				}
			}
		}

		/// <summary>
		/// Handles frame captured event: forwards frame to active viewer, displays frame
		/// </summary>
		/// <param name="sender">The `VideoCaptureDelegate` delegate-object</param>
		/// <param name="args">EventArgsT containing the (processed) frame</param>
		void OnFrameCaptured (Object sender, EventArgsT<CVPixelBuffer> args)
		{
			var buffer = args.Value;
			activeViewer.OnFrameCaptured (buffer);

			// Display it
			var img = VideoCapture.ImageBufferToUIImage (buffer);
			overlay.BeginInvokeOnMainThread (() => {
				var oldImg = bufferImageView.Image;
				if (oldImg != null) {
					oldImg.Dispose ();
				}
				bufferImageView.Image = img;
				bufferImageView.SetNeedsDisplay ();
			});
		}

		private UIImage RotateImage(UIImage image)
		{
			var imageSize = image.Size;
			UIGraphics.BeginImageContext(imageSize);
			var context = UIGraphics.GetCurrentContext();
			context.TranslateCTM(imageSize.Width / 2, imageSize.Height / 2);
			//context.RotateCTM(angle);
			image.Draw(new CGRect(-imageSize.Width / 2, -imageSize.Height / 2, imageSize.Width, imageSize.Height));
			var rotatedImage = UIGraphics.GetImageFromCurrentImageContext();
			UIGraphics.EndImageContext();
			return rotatedImage;
		}
		
		private UIImage ConvertToGrayscale(UIImage originalImage)
		{
			// Create a Core Image version of the image.
			var ciImage = new CIImage(originalImage);

			// Apply a grayscale filter
			var grayscaleFilter = new CIColorControls()
			{
				Saturation = 0.0f,
				InputImage = ciImage
			};

			// Create a new UIImage from the grayscale image
			var grayscaleImage = new UIImage(grayscaleFilter.OutputImage);

			return grayscaleImage;
		}
		
		public void DisplayCapturedImage(UIImage image)
		{
            BeginInvokeOnMainThread(() =>
            {
	            var rotatedImage = RotateImage(image); // Rotate 90 degrees
	            var croppedImage = rotatedImage.CGImage.WithImageInRect(croppingBounds);
	            var finalImage = ConvertToGrayscale(new UIImage(croppedImage));
	            capturedImageView.Image = finalImage;
	            capturedImageView.ContentMode = UIViewContentMode.ScaleAspectFit;
				capturedImageView.Hidden = false;
			});
		}
	}
}
