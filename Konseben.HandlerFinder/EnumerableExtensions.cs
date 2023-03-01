using System.Collections.Generic;
using System.Threading.Tasks;

namespace Konseben.HandlerFinder
{
    public static class EnumerableExtensions
    {
        public static async Task<IEnumerable<T>> WhenAllAsync<T>(this IEnumerable<Task<T>> tasks)
        {
            return await Task.WhenAll(tasks);
        }
    }
}
