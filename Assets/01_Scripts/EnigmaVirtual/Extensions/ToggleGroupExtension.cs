using System.Collections.Generic;
using UnityEngine.UI;

// Extracted from RegisterField.cs in v0.54.6, and it stays in the GLOBAL namespace deliberately.
// An extension method is only in scope where its namespace is, so namespacing this would break
// every `someToggleGroup.GetActive()` call site in every branched project with a CS1061 that does
// not even name a namespace. It was the reason RegisterField.cs could not simply be wrapped, so
// the holder left the file instead and the two ordinary types it shared with are now namespaced.

public static class ToggleGroupExtension
{
    public static Toggle GetActive(this ToggleGroup aGroup)
    {
        IEnumerator<Toggle> toggleEnum = aGroup.ActiveToggles().GetEnumerator();
        toggleEnum.MoveNext();
        return toggleEnum.Current;
    }
}
