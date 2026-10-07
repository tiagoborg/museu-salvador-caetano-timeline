using EnigmaVirtual;
using System;
using System.Collections.Generic;
using System.Linq;

public static class CollectionExtensions
{
    /// <summary>
    /// Swap two elements in array
    /// </summary>
    public static void Swap<T>(this T[] array, int a, int b) => (array[a], array[b]) = (array[b], array[a]);

    /// <summary>
    /// Shuffles the array.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="array"></param>
    /// <returns></returns>
    public static T[] Shuffle<T>(this T[] array)
    {
        int length = array.Length;
        for (int i = 0; i < length; i++)
        {
            int randomIndex = UnityEngine.Random.Range(i, length);
            T temp = array[i];
            array[i] = array[randomIndex];
            array[randomIndex] = temp;
        }

        return array;
    }

    /// <summary>
    /// Shuffles a collection in place using the Knuth algorithm.
    /// </summary>
    public static IList<T> Shuffle<T>(this IList<T> source)
    {
        Random random = new Random();

        for (int i = source.Count - 1; i > 0; --i)
        {
            int indexToSwap = random.Next(0, i + 1);
            source.SwapInPlace(i, indexToSwap);
        }

        return source;
    }

    /// <summary>
    /// Chooses a set of items without repetition.
    /// </summary>
    /// <typeparam name="T">Type of object in array.</typeparam>
    /// <param name="array">Input array.</param>
    /// <param name="size">Size of set.</param>
    /// <returns></returns>
    public static T[] ChooseSet<T>(this T[] array, int size)
    {
        if (size <= 0 || size > array.Length)
        {
            Development.LogError("Invalid size for choosing a set.");
            return null;
        }

        T[] result = new T[size];

        List<T> remainingItems = new List<T>(array);

        for (int i = 0; i < size; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, remainingItems.Count);
            result[i] = remainingItems[randomIndex];
            remainingItems.RemoveAt(randomIndex);
        }

        return result;
    }

    public static T ChooseWithProbability<T>(this T[] array, float[] probs)
    {
        if (array == null || probs == null || array.Length != probs.Length || probs.Length == 0)
        {
            Development.LogError("Invalid input for choosing with probability.");
            return default(T);
        }

        float totalProbability = probs.Sum();

        if (totalProbability <= 0f)
        {
            Development.LogError("Total probability should be greater than 0.");
            return default(T);
        }

        float randomPoint = UnityEngine.Random.value * totalProbability;

        for (int i = 0; i < probs.Length; i++)
        {
            if (randomPoint < probs[i])
                return array[i];
            else
                randomPoint -= probs[i];
        }

        return array[probs.Length - 1];
    }

    /// <summary>
    /// Returns string with ToString array elements
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="array"></param>
    /// <returns></returns>
    public static string ElementsToString<T>(this T[] array, string joiner = ",") => System.String.Join(joiner, array.Select(p => p.ToString()).ToArray());

    /// <summary>
    /// Is array null or empty
    /// </summary>
    public static bool IsNullOrEmpty<T>(this T[] collection) => collection == null || collection.Length == 0;

    /// <summary>
    /// Collection is not null or empty
    /// </summary>
    public static bool NotNullOrEmpty<T>(this T[] collection) => !collection.IsNullOrEmpty();

    /// <summary>
    /// Get next index for circular array. <br />
    /// -1 will result with last element index, Length + 1 is 0. <br />
    /// If step is more that 1, you will get correct offset <br />
    /// 
    /// <code>
    /// Example (infinite loop first->last->first):
    /// i = myArray.NextIndex(i++);
    /// var nextItem = myArray[i];
    /// </code>
    /// </summary>
    public static int NextIndexInCircle<T>(this T[] array, int desiredPosition)
    {
        if (array.IsNullOrEmpty())
        {
            Development.LogError("Input array is null or empty");
            return -1;
        }

        int length = array.Length;
        return (desiredPosition % length + length) % length;
    }

    /// <summary>
    /// Returns new array with inserted empty element at index
    /// </summary>
    public static T[] InsertAt<T>(this T[] array, int index)
    {
        if (index < 0 || index > array.Length)
        {
            Development.LogError("Index is out of range. Array is not modified");
            return array;
        }

        T[] newArray = new T[array.Length + 1];
        for (int i = 0, j = 0; i < newArray.Length; ++i)
        {
            if (i == index)
                newArray[i] = default(T); // Initialize the new element with default value
            else
                newArray[i] = array[j++];
        }

        return newArray;
    }

    /// <summary>
    /// Returns new array without element at index
    /// </summary>
    public static T[] RemoveAt<T>(this T[] array, int index)
    {
        if (index < 0 || index >= array.Length)
        {
            Development.LogError("Index is out of range. Array is not modified");
            return array;
        }

        T[] newArray = new T[array.Length - 1];

        for (int i = 0, j = 0; i < array.Length; ++i)
        {
            if (i != index)
            {
                newArray[j++] = array[i];
            }
        }

        return newArray;
    }

    /// <summary>
    /// Returns random element from collection
    /// </summary>
    public static T GetRandom<T>(this T[] collection) => collection[UnityEngine.Random.Range(0, collection.Length)];

    /// <summary>
    /// Returns random element from collection
    /// </summary>
    public static T GetRandom<T>(this IList<T> collection) => collection[UnityEngine.Random.Range(0, collection.Count)];

    /// <summary>
    /// Returns random element from collection
    /// </summary>
    // ReSharper disable PossibleMultipleEnumeration
    public static T GetRandom<T>(this IEnumerable<T> collection) => collection.ElementAt(UnityEngine.Random.Range(0, collection.Count()));
    // ReSharper restore PossibleMultipleEnumeration

    /// <summary>
    /// Is list null or empty
    /// </summary>
    public static bool IsNullOrEmpty<T>(this IList<T> collection) => collection == null || collection.Count == 0;

    /// <summary>
    /// Is collection null or empty. IEnumerable is relatively slow. Use Array or List implementation if possible
    /// </summary>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T> collection) => collection == null || !collection.Any();

    /// <summary>
    /// Collection is not null or empty
    /// </summary>
    public static bool NotNullOrEmpty<T>(this IList<T> collection) => !collection.IsNullOrEmpty();

    /// <summary>
    /// Collection is not null or empty
    /// </summary>
    public static bool NotNullOrEmpty<T>(this IEnumerable<T> collection) => !collection.IsNullOrEmpty();

    /// <returns>
    /// Returns -1 if none found
    /// </returns>
    public static int IndexOfItem<T>(this IEnumerable<T> collection, T item)
    {
        if (collection == null)
        {
            Development.LogError("IndexOfItem Caused: source collection is null");
            return -1;
        }

        var index = 0;
        foreach (var i in collection)
        {
            if (EqualityComparer<T>.Default.Equals(i, item))
                return index;

            ++index;
        }

        return -1;
    }

    /// <summary>
    /// Is Elements in two collections are the same
    /// </summary>
    public static bool ContentsMatch<T>(this IEnumerable<T> first, IEnumerable<T> second)
    {
        if (first.IsNullOrEmpty() && second.IsNullOrEmpty()) return true;
        if (first.IsNullOrEmpty() || second.IsNullOrEmpty()) return false;

        HashSet<T> firstHashSet = new HashSet<T>(first);
        HashSet<T> secondHashSet = new HashSet<T>(second);

        return firstHashSet.SetEquals(secondHashSet);
    }

    /// <summary>
    /// Is Keys in MyDictionary is the same as some collection
    /// </summary>
    public static bool ContentsMatchKeys<T1, T2>(this IDictionary<T1, T2> source, IEnumerable<T1> check)
    {
        if (source.IsNullOrEmpty() && check.IsNullOrEmpty()) return true;
        if (source.IsNullOrEmpty() || check.IsNullOrEmpty()) return false;

        return source.Keys.ContentsMatch(check);
    }

    /// <summary>
    /// Is Values in MyDictionary is the same as some collection
    /// </summary>
    public static bool ContentsMatchValues<T1, T2>(this IDictionary<T1, T2> source, IEnumerable<T2> check)
    {
        if (source.IsNullOrEmpty() && check.IsNullOrEmpty()) return true;
        if (source.IsNullOrEmpty() || check.IsNullOrEmpty()) return false;

        return source.Values.ContentsMatch(check);
    }

    /// <summary>
    /// Adds a key/value pair to the IDictionary&lt;TKey,TValue&gt; if the
    /// key does not already exist. Returns the new value, or the existing
    /// value if the key exists.
    /// </summary>
    public static TValue GetOrAddDefault<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key) where TValue : new()
    {
        if (!source.ContainsKey(key)) source[key] = new TValue();
        return source[key];
    }
    /// <summary>
    /// Adds a key/value pair to the IDictionary&lt;TKey,TValue&gt; if the
    /// key does not already exist. Returns the new value, or the existing
    /// value if the key exists.
    /// </summary>
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> source,
        TKey key, TValue value)
    {
        if (!source.ContainsKey(key)) source[key] = value;
        return source[key];
    }

    /// <summary>
    /// Adds a key/value pair to the IDictionary&lt;TKey,TValue&gt; by using
    /// the specified function if the key does not already exist. Returns
    /// the new value, or the existing value if the key exists.
    /// </summary>
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> source,
        TKey key, Func<TValue> valueFactory)
    {
        if (!source.ContainsKey(key)) source[key] = valueFactory();
        return source[key];
    }

    /// <summary>
    /// Adds a key/value pair to the IDictionary&lt;TKey,TValue&gt; by using
    /// the specified function if the key does not already exist. Returns
    /// the new value, or the existing value if the key exists.
    /// </summary>
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> source,
        TKey key, Func<TKey, TValue> valueFactory)
    {
        if (!source.ContainsKey(key)) source[key] = valueFactory(key);
        return source[key];
    }

    /// <summary>
    /// Adds a key/value pair to the IDictionary&lt;TKey,TValue&gt; by using
    /// the specified function and an argument if the key does not already
    /// exist, or returns the existing value if the key exists.
    /// </summary>
    public static TValue GetOrAdd<TKey, TValue, TArg>(this IDictionary<TKey, TValue> source,
        TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
    {
        if (!source.ContainsKey(key)) source[key] = valueFactory(key, factoryArgument);
        return source[key];
    }

    /// <summary>
    /// Performs an action on each element of a collection.
    /// </summary>
    public static IEnumerable<T> ForEach<T>(this IEnumerable<T> source, System.Action<T> action)
    {
        foreach (T element in source) action(element);
        return source;
    }

    /// <summary>
    /// Performs a function on each element of a collection.
    /// </summary>
    public static IEnumerable<T> ForEach<T, R>(this IEnumerable<T> source, Func<T, R> func)
    {
        foreach (T element in source) func(element);
        return source;
    }

    /// <summary>
    /// Performs an action on each element of a collection with its index
    /// passed along.
    /// </summary>
    public static IEnumerable<T> ForEach<T>(this IEnumerable<T> source,
        System.Action<T, int> action)
    {
        int index = 0;
        foreach (T element in source) { action(element, index); ++index; }
        return source;
    }

    /// <summary>
    /// Performs an action on each element of a collection with its index
    /// passed along.
    /// </summary>
    public static IEnumerable<T> ForEach<T, R>(this IEnumerable<T> source,
        Func<T, int, R> func)
    {
        int index = 0;
        foreach (T element in source) { func(element, index); ++index; }
        return source;
    }

    /// <summary>
    /// Find the element of a collection that has the highest selected value.
    /// </summary>
    public static T MaxBy<T, S>(this IEnumerable<T> source, Func<T, S> selector)
        where S : IComparable<S>
    {
        if (source.IsNullOrEmpty())
        {
            Development.LogError("MaxBy Caused: source collection is null or empty");
            return default(T);
        }
        return source.Aggregate((e, n) => selector(e).CompareTo(selector(n)) > 0 ? e : n);
    }

    /// <summary>
    /// Find the element of a collection that has the lowest selected value.
    /// </summary>
    public static T MinBy<T, S>(this IEnumerable<T> source, Func<T, S> selector)
        where S : IComparable<S>
    {
        if (source.IsNullOrEmpty())
        {
            Development.LogError("Input collection is null or empty");
            return default(T);
        }
        return source.Aggregate((e, n) => selector(e).CompareTo(selector(n)) < 0 ? e : n);
    }

    /// <summary>
    /// Convert a single element into an enumerable with the source as the
    /// single element.
    /// </summary>
    public static IEnumerable<T> SingleToEnumerable<T>(this T source) =>
        Enumerable.Empty<T>().Append(source);

    /// <summary>
    /// First index of an item that matches a predicate.
    /// </summary>
    public static int FirstIndex<T>(this IList<T> source, Predicate<T> predicate)
    {
        for (int i = 0; i < source.Count; ++i)
            if (predicate(source[i]))
                return i;

        return -1;
    }


    /// <summary>
    /// First index of an item that matches a predicate.
    /// </summary>
    public static int FirstIndex<T>(this IEnumerable<T> source, Predicate<T> predicate)
    {
        int index = 0;
        foreach (T e in source)
        {
            if (predicate(e)) return index;
            ++index;
        }
        return -1;
    }

    /// <summary>
    /// Last index of an item that matches a predicate.
    /// </summary>
    public static int LastIndex<T>(this IList<T> source, Predicate<T> predicate)
    {
        for (int i = source.Count - 1; i >= 0; --i)
        {
            if (predicate(source[i])) return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns random index from collection with weighted probabilities.
    /// </summary>
    public static int GetWeightedRandomIndex<T>(this IEnumerable<T> source, Func<T, double> weightSelector)
    {
        var weights = source.Select(weightSelector).Select(w => w < 0 ? 0 : w);
        var weightStages = weights.Select((w, i) => weights.Take(i + 1).Sum());
        var random = new System.Random();
        var roll = random.NextDouble() * weights.Sum();

        return weightStages.FirstIndex(ws => ws > roll);
    }

    /// <summary>
    /// Returns random element from collection with weighted probabilities.
    /// </summary>
    public static T GetWeightedRandom<T>(this IList<T> source, Func<T, double> weightSelector)
        => source[source.GetWeightedRandomIndex(weightSelector)];

    /// <summary>
    /// Returns random element from collection with weighted probabilities.
    /// </summary>
    public static T GetWeightedRandom<T>(this IEnumerable<T> source, Func<T, double> weightSelector)
        => source.ElementAt(source.GetWeightedRandomIndex(weightSelector));


    /// <summary>
    /// Fills a collection with values generated using a factory function that
    /// passes along their index numbers.
    /// </summary>
    public static IList<T> FillBy<T>(this IList<T> source, Func<int, T> valueFactory)
    {
        for (int i = 0; i < source.Count; ++i) source[i] = valueFactory(i);
        return source;
    }

    /// <summary>
    /// Fills an array with values generated using a factory function that
    /// passes along their index numbers.
    /// </summary>
    public static T[] FillBy<T>(this T[] source, Func<int, T> valueFactory)
    {
        for (int i = 0; i < source.Length; ++i) source[i] = valueFactory(i);
        return source;
    }

    /// <summary>
    /// Randomly samples a non-repeating number of elements from the source
    /// collection.
    /// </summary>
    public static T[] ExclusiveSample<T>(this IList<T> source, int sampleNumber)
    {
        if (sampleNumber > source.Count)
            throw new ArgumentOutOfRangeException("Cannot sample more elements than what the source collection contains");

        var results = new T[sampleNumber];
        int resultIndex = 0;
        var random = new System.Random();

        for (int i = 0; i < source.Count && resultIndex < sampleNumber; ++i)
        {
            var probability = (double)(sampleNumber - resultIndex) / (source.Count - i);

            if (random.NextDouble() < probability)
            {
                results[resultIndex] = source[i]; 
                ++resultIndex;
            }
        }

        return results;
    }

    /// <summary>
    /// Swaps 2 elements at the specified index positions in place.
    /// </summary>
    public static IList<T> SwapInPlace<T>(this IList<T> source, int index1, int index2)
    {
        if (index1 < 0 || index1 >= source.Count || index2 < 0 || index2 >= source.Count)
            throw new ArgumentOutOfRangeException("Invalid indices for swapping");

        (source[index1], source[index2]) = (source[index2], source[index1]);
        return source;
    } 
}
