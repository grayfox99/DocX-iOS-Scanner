using System;
using System.Linq;
using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreVideo;
using Foundation;
using UIKit;

namespace iosDocumentScanner {
	/// <summary>
	/// Handles the capturing of video from the device's rear camera
	/// </summary>
	public class VideoCapture : NSObject, IAVCapturePhotoCaptureDelegate {
		/// <summary>
		/// The camera
		/// </summary>
		private AVCaptureDevice captureDevice;
		private AVCapturePhotoOutput photoOutput = new AVCapturePhotoOutput();

		/// <summary>
		/// Perform the capture and image-processing pipeline on a background queue (note that 
		/// this is different than the Vision request
		/// </summary>
		DispatchQueue queue = new DispatchQueue ("videoQueue");

		/// <summary>
		/// Used by `AVCaptureVideoPreviewLayer` in `ViewController`
		/// </summary>
		/// <value>The session.</value>
		public AVCaptureSession Session { get; }


		AVCaptureVideoDataOutput videoOutput = new AVCaptureVideoDataOutput ();

		public IAVCaptureVideoDataOutputSampleBufferDelegate Delegate { get; }

		public VideoCapture (IAVCaptureVideoDataOutputSampleBufferDelegate delegateObject)
		{
			Delegate = delegateObject;
			Session = new AVCaptureSession ();
			SetupCamera ();
		}

		/// <summary>
		/// Typical video-processing code. More advanced would allow user selection of camera, resolution, etc.
		/// </summary>
		private void SetupCamera ()
		{
			var deviceDiscovery = AVCaptureDeviceDiscoverySession.Create (
				new AVCaptureDeviceType [] { AVCaptureDeviceType.BuiltInWideAngleCamera }, AVMediaType.Video, AVCaptureDevicePosition.Back);

			var device = deviceDiscovery.Devices.Last ();
			if (device != null) {
				captureDevice = device;
				BeginSession ();
			}
		}

		private void BeginSession ()
		{
			try {
				var settings = new CVPixelBufferAttributes {
					PixelFormatType = CVPixelFormatType.CV32BGRA
				};
				videoOutput.WeakVideoSettings = settings.Dictionary;
				videoOutput.AlwaysDiscardsLateVideoFrames = true;
				videoOutput.SetSampleBufferDelegateQueue (Delegate, queue);
				
				photoOutput.IsHighResolutionCaptureEnabled = true;

				Session.SessionPreset = AVCaptureSession.Preset1920x1080;
				Session.AddOutput (videoOutput);
				Session.AddOutput (photoOutput);

				var input = new AVCaptureDeviceInput (captureDevice, out var err);
				if (err != null) {
					Console.Error.WriteLine ("AVCapture error: " + err);
				}
				Session.AddInput (input);

				Session.StartRunning ();
				Console.WriteLine ("started AV capture session");
			} catch (Exception exception){
				Console.Error.WriteLine (exception);
			}
		}

		/// <summary>
		/// This is an expensive call. Used by preview thumbnail.
		/// </summary>
		/// <returns>The (processed) video frame, as a UIImage.</returns>
		/// <param name="imageBuffer">The (processed) video frame.</param>
		public static UIImage ImageBufferToUIImage (CVPixelBuffer imageBuffer)
		{
			imageBuffer.Lock (CVPixelBufferLock.None);

			var baseAddress = imageBuffer.BaseAddress;
			var bytesPerRow = imageBuffer.BytesPerRow;

			var width = imageBuffer.Width;
			var height = imageBuffer.Height;

			var colorSpace = CGColorSpace.CreateDeviceRGB ();
			var bitmapInfo = (uint) CGImageAlphaInfo.NoneSkipFirst | (uint) CGBitmapFlags.ByteOrder32Little;

			using (var context = new CGBitmapContext (baseAddress, width, height, 8, bytesPerRow, colorSpace, (CGImageAlphaInfo) bitmapInfo)) {
				var quartzImage = context?.ToImage ();
				imageBuffer.Unlock (CVPixelBufferLock.None);

				var image = new UIImage (quartzImage, 1.0f, UIImageOrientation.Up);

				return image;
			}
		}
		
		public void CaptureImage()
		{
			// Create an AVCapturePhotoSettings object
			var photoSettings = AVCapturePhotoSettings.Create();

			// Set the pixel format
			var previewPixelType = photoSettings.AvailablePreviewPhotoPixelFormatTypes.First();
			photoSettings.PreviewPhotoFormat = new NSDictionary<NSString, NSObject>(CVPixelBuffer.PixelFormatTypeKey, previewPixelType);
			photoSettings.IsHighResolutionPhotoEnabled = true;
			
			var resolution = captureDevice.ActiveFormat.HighResolutionStillImageDimensions;
			ViewController.Instance.CMVideoDimensions = resolution;
			
			// Capture the photo
			photoOutput.CapturePhoto(photoSettings, this);
		}
		
		// This is a delegate method that gets called when the photo is captured
		[Export("captureOutput:didFinishProcessingPhoto:error:")]
		public void DidFinishProcessingPhoto(AVCapturePhotoOutput output, AVCapturePhoto photo, NSError error)
		{
			if (error != null)
			{
				Console.Error.WriteLine(error);
				return;
			}

			// Get the image data
			var imageData = photo.FileDataRepresentation;
			
			// Convert NSData to UIImage
			var image = UIImage.LoadFromData(imageData);
			
			// Display the image in a UIImageView (replace "imageView" with your UIImageView instance)
			BeginInvokeOnMainThread(() =>
			{
				ViewController.Instance.DisplayCapturedImage(image);
			});
			// You can now do something with the image data, like save it to the Photos library
		}
	}
}
