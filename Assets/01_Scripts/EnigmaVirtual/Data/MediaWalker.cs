using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace EnigmaVirtual
{
    /// <summary>
    /// Walks a deserialized appData graph and reports every media path it finds, deciding what
    /// counts as media by member name via <c>AppSettingsBase.mediaPaths</c>.
    ///
    /// Replaces two near-identical copies in <see cref="AssetManager"/> and
    /// <c>TextureCacheManager</c> that had already drifted apart (only one filtered by file
    /// extension), and fixes what both got wrong:
    ///
    /// * **Fields as well as properties.** The old walk used <c>GetProperties()</c> only. Every
    ///   settings and content DTO in this project uses public *fields* — that is the documented
    ///   convention, because JsonUtility and Unity's serializer ignore properties. So a correctly
    ///   written model was invisible: with `AppData` holding `public MenuVO[] menus;`,
    ///   `GetProperties()` returned zero and nothing was ever collected.
    /// * **Cycle detection.** No visited-set meant a back-reference (a child pointing at its
    ///   parent — ordinary in CMS models) recursed until StackOverflowException, which .NET cannot
    ///   catch.
    /// * **One read per member.** The old code called GetValue up to three times for the same
    ///   member and re-looked-up the PropertyInfo it already held.
    /// * **Cached member lists** instead of reflecting the same type repeatedly.
    /// </summary>
    internal static class MediaWalker
    {
        /// <summary>Backstop for a graph that is deep rather than cyclic.</summary>
        private const int MaxDepth = 32;

        private static readonly Dictionary<Type, MemberInfo[]> MemberCache = new Dictionary<Type, MemberInfo[]>();

        /// <summary>
        /// Reports every media path in <paramref name="root"/> to <paramref name="onMediaFound"/>.
        /// Callers decide what to do with each one (dedup, extension filter, logging).
        /// </summary>
        /// <param name="isMediaName">Whether a member or dictionary key names a media path.</param>
        internal static void Collect(object root, Func<string, bool> isMediaName, Action<string> onMediaFound)
        {
            if (isMediaName == null || onMediaFound == null)
            {
                Development.LogError("MediaWalker.Collect called without a predicate or callback.");
                return;
            }

            // Reference equality: value equality would wrongly treat two equal-but-distinct nodes
            // as already visited and skip the second one's media.
            Walk(root, isMediaName, onMediaFound, new HashSet<object>(ReferenceComparer.Instance), 0);
        }

        private static void Walk(object node, Func<string, bool> isMediaName, Action<string> onMediaFound,
            HashSet<object> visited, int depth)
        {
            if (node == null || IsLeaf(node))
                return;

            if (depth > MaxDepth)
            {
                Development.LogError($"MediaWalker: giving up below depth {MaxDepth} at [{node.GetType().Name}] - "
                    + "the graph is deeper than expected or cyclic in a way the visited-set missed.");
                return;
            }

            if (!visited.Add(node))
                return;

            // A collection reached directly (an array element that is itself a list, say).
            if (node is IEnumerable)
            {
                WalkEnumerable(node, null, isMediaName, onMediaFound, visited, depth);
                return;
            }

            foreach (MemberInfo member in MembersOf(node.GetType()))
            {
                object value = ReadValue(member, node);
                if (value == null)
                    continue;

                string name = member.Name;

                if (value is string text)
                {
                    if (isMediaName(name) && text.Length > 0)
                        onMediaFound(text);

                    continue;
                }

                if (value is IEnumerable)
                {
                    WalkEnumerable(value, name, isMediaName, onMediaFound, visited, depth + 1);
                    continue;
                }

                if (!IsLeaf(value))
                    Walk(value, isMediaName, onMediaFound, visited, depth + 1);
            }
        }

        /// <summary>
        /// Handles dictionaries and lists. <paramref name="memberName"/> is the name the collection
        /// was reached through, or null when it was reached directly - a collection whose *member*
        /// is a media name means its values are media paths.
        /// </summary>
        private static void WalkEnumerable(object collection, string memberName, Func<string, bool> isMediaName,
            Action<string> onMediaFound, HashSet<object> visited, int depth)
        {
            bool memberIsMedia = memberName != null && isMediaName(memberName);

            if (collection is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value is string dictText)
                    {
                        // Either the whole member is media (a dictionary of paths), or the key names
                        // one - which is how a localised media path keyed by language works.
                        string key = entry.Key as string;
                        if ((memberIsMedia || (key != null && isMediaName(key))) && dictText.Length > 0)
                            onMediaFound(dictText);
                    }
                    else
                    {
                        Walk(entry.Value, isMediaName, onMediaFound, visited, depth + 1);
                    }
                }

                return;
            }

            // IEnumerable, not IList: the old code cast to IList, which throws on any other
            // collection type (a HashSet, or a LINQ result).
            foreach (object item in (IEnumerable)collection)
            {
                if (item is string itemText)
                {
                    if (memberIsMedia && itemText.Length > 0)
                        onMediaFound(itemText);
                }
                else
                {
                    Walk(item, isMediaName, onMediaFound, visited, depth + 1);
                }
            }
        }

        /// <summary>Public instance fields and readable, non-indexed instance properties.</summary>
        private static MemberInfo[] MembersOf(Type type)
        {
            if (MemberCache.TryGetValue(type, out MemberInfo[] cached))
                return cached;

            List<MemberInfo> members = new List<MemberInfo>();

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                members.Add(field);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Indexers would need an argument; Dictionary exposes several properties that are
                // either indexed or would re-walk the same collection.
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    members.Add(property);
            }

            MemberInfo[] result = members.ToArray();
            MemberCache[type] = result;
            return result;
        }

        private static object ReadValue(MemberInfo member, object target)
        {
            try
            {
                FieldInfo field = member as FieldInfo;
                if (field != null)
                    return field.GetValue(target);

                return ((PropertyInfo)member).GetValue(target);
            }
            catch (Exception e)
            {
                // A throwing getter must not abort the whole walk - it would silently truncate the
                // media list, and for the cleanup path a short list means deleted files.
                Development.LogError($"MediaWalker: could not read [{member.DeclaringType?.Name}.{member.Name}]: {e.Message}");
                return null;
            }
        }

        /// <summary>Types with no members worth walking into.</summary>
        private static bool IsLeaf(object value)
        {
            Type type = value.GetType();
            return type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is DateTime;
        }

        /// <summary>Identity comparer, so the visited-set tracks instances rather than equal values.</summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
