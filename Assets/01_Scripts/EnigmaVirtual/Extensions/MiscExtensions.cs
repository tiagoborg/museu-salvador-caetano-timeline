using EnigmaVirtual;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class MiscExtensions
{
    /// <summary>
    /// Retrives last N elements on an IEnumerable. 
    /// Ex: collection.TakeLast(5);
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="N"></param>
    /// <returns></returns>
    public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> source, int N)
    {
        return source.Skip(Math.Max(0, source.Count() - N));
    }

    /// <summary>
    /// Formats a collection of strings to a single concatenated string.
    /// </summary>
    /// <param name="input">
    /// A <see cref="List<System.String>"/>
    /// </param>
    /// <returns>
    /// A <see cref="System.String"/>
    /// </returns>
    public static string ToListString(this List<string> input)
    {
        string output = "";

        foreach (string value in input)
            output += value;

        return output;
    }

    /// <summary>
    /// Splits a List into several smaller lists.
    /// </summary>
    /// <typeparam name="T">The type of the array.</typeparam>
    /// <param name="self">The list to split.</param>
    /// <param name="chunkSize">The size of the smaller lists.</param>
    /// <returns>A List containing smaller Lists. To convert to List List T use nestedEnumerable.Select(innerEnumerable => innerEnumerable.ToList()).ToList()</T></returns>
    /// <remarks>
    /// <para><b>One pass over the source, not one <c>Skip</c> per chunk. This is not a
    /// speed-up.</b> The old body yielded
    /// <c>source.Skip(start).Take(size)</c>, and the claim was that this is O(n²/chunkSize) on large
    /// asset sets. Measured in the editor at n=20000, it is not: <c>Enumerable.Skip</c> type-tests
    /// its argument at runtime, so a <c>List&lt;T&gt;</c> gets a <c>ListPartition</c> that indexes
    /// straight to <c>start</c> - 0.6ms flat at every chunk size - and both callers pass
    /// <c>List&lt;string&gt;</c>. The quadratic mode is real but needs a source with <b>no
    /// indexer</b>: a <c>HashSet&lt;T&gt;</c> gets an <c>EnumerablePartition</c> that walks, costing
    /// 1.9ms / 2.5ms / 12.9ms at chunk sizes 20000 / 2500 / 313. It also needs
    /// <c>downloadThreads &gt; 1</c>, since the shipped <c>1</c> makes chunkSize the whole list and
    /// <c>Skip(0)</c> walks nothing.</para>
    ///
    /// <para>So this costs the shipped path about <b>1.1ms once per boot</b> (0.6ms to 1.7ms) and
    /// buys a uniform O(n) for every <c>ICollection&lt;T&gt;</c>. That trade is not worth making for
    /// performance - it is worth making because this is a <c>public</c> method on
    /// <c>ICollection&lt;T&gt;</c> in a framework every project branches from, and its complexity
    /// should not depend on the caller's concrete type and on a LINQ optimisation surviving whatever
    /// scripting profile a branch builds with. <b>Do not "restore" the <c>Skip</c> version for the
    /// 1.1ms, and do not add an <c>IList&lt;T&gt;</c> fast path</b> - that re-implements the type
    /// test LINQ already does.</para>
    ///
    /// <para>The chunks are <b>materialised</b> now rather than lazy views over the source. Both
    /// callers already did <c>.ToList()</c> on each chunk immediately, so nothing here changes; a
    /// branched project that mutated the source mid-enumeration would see the old lazy chunks pick
    /// the mutation up and these not. That is the safer of the two behaviours.</para>
    /// </remarks>
    public static IEnumerable<IEnumerable<T>> Split<T>(this ICollection<T> source, int chunkSize)
    {
        if (chunkSize <= 0)
        {
            // Clamped, not thrown. Both callers are coroutines whose completion counter is the last
            // statement, so an exception here kills the chunk and hangs the boot chain. A
            // non-positive chunkSize used to yield zero chunks, which every caller
            // read as "finished, nothing to do" - so the app carried on having downloaded nothing,
            // silently. Loud and wrong-but-working beats silent and stopped.
            Development.LogError($"Split called with chunkSize {chunkSize}; clamping to 1. "
                + "This usually means downloadThreads is 0 in settings.json.");
            chunkSize = 1;
        }

        // Capped by the source: chunkSize is a caller argument and `Split(list, int.MaxValue)` is a
        // normal way to ask for one chunk. Preallocating chunkSize directly would turn that into an
        // immediate OutOfMemoryException on a call the Skip version handled fine, and a modest
        // Split(tenItems, 1000) into a wasted 8KB.
        int capacity = Math.Min(chunkSize, source.Count);

        List<T> chunk = new List<T>(capacity);

        foreach (T item in source)
        {
            chunk.Add(item);

            if (chunk.Count == chunkSize)
            {
                yield return chunk;

                // A new list rather than Clear(): the one just yielded belongs to the caller now,
                // and reusing the buffer would rewrite a chunk it is still holding.
                chunk = new List<T>(capacity);
            }
        }

        if (chunk.Count > 0)
            yield return chunk;
    }

    public static bool IsNullOrEmpty<T>(this List<T> list)
    {
        return list == null || list.Count == 0;
    }
}