using System;

namespace iosDocumentScanner {
	/// <summary>
	/// Strongly typed EventArgs class
	/// </summary>
	public class EventArgsT<T> : EventArgs {
		public T Value { get; }
		public EventArgsT (T val) => Value = val;
	}
}
