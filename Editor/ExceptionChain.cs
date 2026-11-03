using System;

namespace VRVlog.LilToonExporter
{
    // Build tools wrap callback failures in their own exceptions, so a failure
    // is searched for through inner and aggregated exceptions.
    internal static class ExceptionChain
    {
        // The first of error and its inner exceptions that matches, also inside
        // aggregated ones, at most 16 levels down each chain.
        internal static Exception Find(Exception error, Func<Exception, bool> match)
        {
            for (var depth = 0; error != null && depth < 16; depth++)
            {
                if (match(error)) return error;
                if (error is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions)
                        if (Find(inner, match) is Exception found) return found;
                error = error.InnerException;
            }
            return null;
        }

        internal static bool Any(Exception error, Func<Exception, bool> match) => Find(error, match) != null;
    }
}
