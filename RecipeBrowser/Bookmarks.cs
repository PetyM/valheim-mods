using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RecipeBrowser;

internal sealed class Bookmarks(string path)
{
    private readonly List<string> keys = File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToList() : new List<string>();

    public IReadOnlyList<string> Keys => keys;
    public bool Contains(string key) => keys.Contains(key);

    public void Add(string key)
    {
        if (keys.Contains(key)) return;
        keys.Add(key);
        File.WriteAllLines(path, keys);
    }

    public void Remove(string key)
    {
        if (keys.Remove(key)) File.WriteAllLines(path, keys);
    }

    /// <summary>Moves a bookmark to the target's place (null = to the end).</summary>
    public void Move(string key, string target)
    {
        if (key == target || !keys.Contains(key)) return;
        int to = target == null ? keys.Count - 1 : keys.IndexOf(target);
        if (to < 0) return;
        keys.Remove(key);
        keys.Insert(System.Math.Min(to, keys.Count), key);
        File.WriteAllLines(path, keys);
    }
}
