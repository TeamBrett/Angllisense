using System.Collections.Generic;
using System.Linq;

namespace Tests {
    public static class Extensions {
        public static T Second<T>(this IEnumerable<T> enumerable) {
            return enumerable.Skip(1).First();
        }
    }
}
