using System;
using System.Linq;
using CoreGraphics;
using CoreVideo;
using Foundation;
using UIKit;
using Vision;

namespace iosDocumentScanner {
	/// <summary>
	/// Makes Vision requests in "scanning" mode -- looks for rectangles
	/// </summary>
	internal class RectangleScanner : NSObject, IRectangleViewer {
		
		/// <summary>
		/// Connection to the Vision subsystem
		/// </summary>
		VNDetectRectanglesRequest rectangleRequest;

		/// <summary>
		/// Connection to the Vision subsystem for documents
		/// </summary>
		VNDetectDocumentSegmentationRequest documentRequest;

		/// <summary>
		/// The set of detected rectangles
		/// </summary> 
		VNRectangleObservation [] observations;

		/// <summary>
		/// Display overlay
		/// </summary>
		Overlay overlay;

		internal RectangleScanner (Overlay overlay)
		{
			this.overlay = overlay;

			rectangleRequest = new VNDetectRectanglesRequest (RectanglesDetected);
			rectangleRequest.MaximumObservations = 10;
		}

		/// <summary>
		/// Called by `ViewController.OnFrameCaptured` once per frame with the buffer processed by the image-processing pipeline in 
		/// `VideoCaptureDelegate.DidOutputSampleBuffer`
		/// </summary>
		/// <param name="buffer">The captured video frame.</param>
		public void OnFrameCaptured (CVPixelBuffer buffer)
		{
			BeginInvokeOnMainThread (() => overlay.Message = $"Scanning...");

			// Create a document segmentation request
			documentRequest = new VNDetectDocumentSegmentationRequest(RectanglesDetected);

			// Run the document segmentation request
			var handler = new VNImageRequestHandler (buffer, new NSDictionary ());
			NSError error;
			handler.Perform (new VNRequest [] { documentRequest }, out error);
			if (error != null) {
				Console.Error.WriteLine (error);
				BeginInvokeOnMainThread (() => overlay.Message = error.ToString ());
			}
		}

		/// <summary>
		/// Asynchronously called by the Vision subsystem subsequent to `Perform` in `OnFrameCaptured` 
		/// </summary>
		/// <param name="request">The request sent to the Vision subsystem.</param>
		/// <param name="err">If not null, describes an error in Vision.</param>
		private void RectanglesDetected (VNRequest request, NSError err)
		{
			if (err != null) {
				overlay.Message = err.ToString ();
				Console.Error.WriteLine (err);
				return;
			}
			overlay.Clear ();

			observations = request.GetResults<VNRectangleObservation> ();
			overlay.StrokeColor = UIColor.Yellow.CGColor;

			// Draw all detected rectangles in blue
			foreach (var o in observations)
			{
				// Get the original corner points
				var topLeft = o.TopLeft;
				var topRight = o.TopRight;
				var bottomRight = o.BottomRight;
				var bottomLeft = o.BottomLeft;

				// Adjust the y-coordinate of the top points to reduce the height
				var adjustmentFactor = 0.05; //experimentally chose this value
				nfloat adjustment = new nfloat(adjustmentFactor); 
				topLeft.Y -= adjustment;
				topRight.Y -= adjustment;
				bottomLeft.Y += adjustment;
				bottomRight.Y += adjustment;

				// Create a new quadrilateral with the adjusted points
				var quad = new[] { topLeft, topRight, bottomRight, bottomLeft };
        
				RectangleDetected(quad);
			}
		}

		private void RectangleDetected (CGPoint [] normalizedQuadrilateral)
		{
			var oWidth = ViewController.Instance.CMVideoDimensions.Height;
			var oHeight = ViewController.Instance.CMVideoDimensions.Width;
			var viewWidth = 1080;
			var viewHeight = 1920;
			var hOffset = oWidth * 0.035;
			var vOffset = oHeight * 0.035;
			
			overlay.InvokeOnMainThread (() => 
			{
				// Note conversion from inverted coordinate system!
				var rotatedQuadrilateral = normalizedQuadrilateral.Select (pt => new CGPoint (pt.X, 1.0 - pt.Y)).ToArray ();
				overlay.AddQuad (rotatedQuadrilateral);
				
				// Convert normalized coordinates to pixel coordinates
				var pixelQuadrilateral = rotatedQuadrilateral.Select(pt => new CGPoint(pt.X * viewWidth, pt.Y * viewHeight)).ToArray();

				// Scale pixel coordinates to match output image size
				var scaledQuadrilateral = pixelQuadrilateral.Select(pt => new CGPoint(pt.X * oWidth / viewWidth, pt.Y * oHeight / viewHeight)).ToArray();

				// Create a rectangle from the scaled quadrilateral points
				var rectangle = new CGRect(scaledQuadrilateral[0].X - hOffset * 1.3, scaledQuadrilateral[0].Y + vOffset / 1.5,
					(scaledQuadrilateral[2].X - scaledQuadrilateral[0].X) + hOffset * 2,
					(scaledQuadrilateral[2].Y - scaledQuadrilateral[0].Y) - vOffset * 2);

				ViewController.Instance.croppingBounds = rectangle;
			});
		}
		
		private static bool ObservationContainsPoint (VNRectangleObservation o, CGPoint normalizedPoint)
		{
			// Enhancement: This is actually wrong, since the touch could be within the bounding box but outside the quadrilateral. 
			// For better accuracy, implement the Winding Rule algorithm 
			return o.BoundingBox.Contains (normalizedPoint);
		}

		internal VNRectangleObservation Containing (CGPoint normalizedPoint) => observations.FirstOrDefault (o => ObservationContainsPoint (o, normalizedPoint));
	}
}
