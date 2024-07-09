using System.Collections.Generic;

namespace Pool
{
	public static class ListExtensions
	{
		public static void FillWithDefault<T>(this IList<T> list) => list.Fill(default);

		public static void Fill<T>(this IList<T> list, T value)
		{
			for (int i = 0; i < list.Count; i++)
			{
				list[i] = value;
			}
		}

		public static void SwapAt<T>(this IList<T> list, int index1, int index2)
		{
			(list[index1], list[index2]) = (list[index2], list[index1]);
		}
	}
}
