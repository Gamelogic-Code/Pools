using System.Threading;

namespace Pool
{
	/// <summary>
	/// Represents an ID for an object which is unique for objects of the given type.
	/// </summary>
	/// <typeparam name="T">The type for which to create IDs.</typeparam>
	// The type parameter is so that we get distinct ID sets for different types.
	// ReSharper disable once UnusedTypeParameter
	public sealed record Id<T>
	{
		// ReSharper disable once StaticMemberInGenericType
		private static int counter = 0;

		/// <summary>
		/// Gets the unique value of the ID.
		/// </summary>
		public readonly int value;
		
		/// <summary>
		/// Initializes a new instance of the <see cref="Id{T}"/> class.
		/// </summary>
		public Id() => value = Interlocked.Increment(ref counter);
		
		/// <inheritdoc />
		public override string ToString() => value.ToString();
		
		/// <inheritdoc />
		public override int GetHashCode() => value.GetHashCode();
	}
}
