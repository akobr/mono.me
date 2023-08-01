using System.Collections.Generic;

namespace _42.CLI.Toolkit.Output
{
    public static class ListExtensions
    {
        public static void Merge<TItem>(this IList<TItem> list, IEnumerable<TItem>? items)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                list.Add(item);
            }
        }
    }
}
