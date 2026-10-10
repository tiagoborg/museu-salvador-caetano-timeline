using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates and validates the timeline scaling dataset: 100 events, ten per existing category, each
/// with ten images of its own - 1000 distinct PNGs under <c>StreamingAssets/assets/scaling/</c> - and
/// a content file beside them in the shipped appData.json schema.
/// </summary>
/// <remarks>
/// Deterministic: the same index always produces the same pixels, so a re-run writes only files whose
/// bytes differ and reports the rest unchanged. It never deletes anything, and writes only inside its own
/// folder plus one file outside it: config/appData.json, which becomes the generated content (by
/// request - it replaces the old 30-event sample). Languages, categories and every other data key are
/// read from that file and kept; only <c>timeline</c> is generated, so a re-run reproduces the same bytes.
/// The same content is also written as the baseline fixture beside the images.
/// Must not name App types (this is Assembly-CSharp-Editor), so content is built as JSON tokens.
/// </remarks>
public static class TimelineScalingDataset
{
    public const int CategoryCount = 10;
    public const int ItemsPerCategory = 10;
    public const int ImagesPerItem = 10;
    public const int ItemCount = CategoryCount * ItemsPerCategory;
    public const int ImageCount = ItemCount * ImagesPerItem;
    public const int MaxDimension = 512;
    public const int FirstYear = 1920;
    public const int LastYear = 2026;
    public const string Folder = "scaling";
    public const string ContentFile = "appData.scaling.json";

    public static string FolderPath { get { return Path.Combine(Application.streamingAssetsPath, "assets", Folder); } }
    public static string ContentPath { get { return Path.Combine(FolderPath, ContentFile); } }
    public static string ConfigContentPath { get { return Path.Combine(Application.streamingAssetsPath, "config", "appData.json"); } }

    public static string Url(int item, int image) { return Folder + "/" + FileName(item, image); }
    private static string FileName(int item, int image) { return $"c{item % CategoryCount}_e{item:000}_i{image}.png"; }

    [MenuItem("Gema/Timeline Scaling/Generate Dataset")]
    private static void GenerateMenu() { Debug.Log(Generate()); }

    [MenuItem("Gema/Timeline Scaling/Validate Dataset")]
    private static void ValidateMenu() { Debug.Log(Validate()); }

    /// <summary>
    /// Writes images <paramref name="first"/> .. first+count-1 (global index = item * 10 + image) and the
    /// content file. Chunk it from eval, e.g. Generate(0, 100) .. Generate(900, 100), to stay inside the
    /// pipeline's 5 s main-thread limit. Does not refresh the AssetDatabase.
    /// </summary>
    public static string Generate(int first = 0, int count = ImageCount)
    {
        first = Mathf.Clamp(first, 0, ImageCount);
        int end = Mathf.Clamp(first + count, first, ImageCount);
        Directory.CreateDirectory(FolderPath);

        JObject content = BuildContent();
        int written = 0, unchanged = 0;
        byte[] json = new UTF8Encoding(false).GetBytes(content.ToString(Formatting.Indented));
        if (WriteIfChanged(ContentPath, json)) written++; else unchanged++;
        if (WriteIfChanged(ConfigContentPath, json)) written++; else unchanged++;

        string[] labels = CategoryCodes(content);
        for (int g = first; g < end; g++)
        {
            byte[] png = RenderPng(g / ImagesPerItem, g % ImagesPerItem, labels);
            if (WriteIfChanged(Path.Combine(FolderPath, FileName(g / ImagesPerItem, g % ImagesPerItem)), png))
                written++;
            else
                unchanged++;
        }

        return $"TimelineScalingDataset generate images {first}..{end - 1} + 2 content files: written {written}, "
            + $"unchanged {unchanged}, remaining after this chunk {ImageCount - end}. Folder: {FolderPath}";
    }

    private static bool WriteIfChanged(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
            return false;
        File.WriteAllBytes(path, bytes);
        return true;
    }

    // ---------------------------------------------------------------- content

    private static JObject BuildContent()
    {
        JObject shipped = JObject.Parse(File.ReadAllText(ConfigContentPath, Encoding.UTF8));
        JObject data = (JObject)shipped["data"]?.DeepClone();
        JArray categories = data?["categories"] as JArray;
        if (categories == null || categories.Count != CategoryCount)
            throw new InvalidOperationException($"TimelineScalingDataset: {ConfigContentPath} must have exactly "
                + $"{CategoryCount} categories; found {categories?.Count ?? 0}.");

        JArray timeline = new JArray();
        for (int i = 0; i < ItemCount; i++)
        {
            int categoryId = categories[i % CategoryCount].Value<int>("id");
            JObject name = categories[i % CategoryCount]["name"] as JObject;
            JArray images = new JArray();
            for (int k = 0; k < ImagesPerItem; k++)
                images.Add(new JObject { ["url"] = Url(i, k) });

            timeline.Add(new JObject
            {
                ["id"] = i,
                ["categoryId"] = categoryId,
                // 1920 .. 2026 inclusive, spread evenly, so every decade has several categories.
                ["year"] = FirstYear + (int)Math.Round(i * (LastYear - FirstYear) / (double)(ItemCount - 1)),
                ["content"] = new JObject
                {
                    ["pt"] = Text(i, true, name?.Value<string>("pt") ?? ""),
                    ["en"] = Text(i, false, name?.Value<string>("en") ?? ""),
                },
                ["images"] = images,
            });
        }

        data["timeline"] = timeline;
        return new JObject { ["code"] = shipped["code"] ?? new JValue(0), ["data"] = data };
    }

    /// <summary>Short, medium and long texts in turn; sample text, labelled as such.</summary>
    private static string Text(int i, bool pt, string category)
    {
        switch (i % 3)
        {
            case 0:
                return pt ? $"Exemplo {i:000}: evento curto." : $"Sample {i:000}: short event.";
            case 1:
                return pt
                    ? $"Exemplo {i:000}: inauguração de uma oficina na área de {category}, com apresentação ao público."
                    : $"Sample {i:000}: opening of a workshop in the {category} area, presented to the public.";
            default:
                return pt
                    ? $"Exemplo {i:000}: um texto longo de teste de escala em {category}. Descreve a expansão da "
                      + "atividade, a contratação de novos colaboradores e a participação em exposições nacionais."
                    : $"Sample {i:000}: a long scaling test text in {category}. It describes the growth of the "
                      + "business, the hiring of new staff and the presence at national trade exhibitions.";
        }
    }

    private static string[] CategoryCodes(JObject content)
    {
        return ((JArray)content["data"]["categories"]).Select(c => "C" + c.Value<int>("id")).ToArray();
    }

    // ---------------------------------------------------------------- pixels

    /// <summary>
    /// One opaque image. Shape cycles landscape / portrait / square; the pattern, its colours and
    /// parameters come from a per-image seed; the label <c>C&lt;cat&gt;-E&lt;item&gt;-I&lt;image&gt;</c> and a
    /// ten-bit index strip make every file visibly and byte-wise unique.
    /// </summary>
    private static byte[] RenderPng(int item, int image, string[] categoryCodes)
    {
        int g = item * ImagesPerItem + image;
        System.Random rng = new System.Random(1000003 + g * 7919);
        int category = item % CategoryCount;

        int longSide = MaxDimension - rng.Next(0, 3) * 64;          // 512, 448 or 384
        int shortSide = 256 + rng.Next(0, 5) * 32;                   // 256 .. 384
        int shape = g % 3;
        int w = shape == 0 ? longSide : shape == 1 ? shortSide : longSide;
        int h = shape == 0 ? shortSide : shape == 1 ? longSide : longSide;

        float hue = (category * 0.1f + (float)rng.NextDouble() * 0.06f) % 1f;
        Color32 a = Color.HSVToRGB(hue, 0.55f + (float)rng.NextDouble() * 0.4f, 0.55f + (float)rng.NextDouble() * 0.4f);
        Color32 b = Color.HSVToRGB((hue + 0.35f + (float)rng.NextDouble() * 0.3f) % 1f, 0.6f, 0.35f + (float)rng.NextDouble() * 0.5f);
        int pattern = (g / 3 + item) % 5;
        float period = 12f + rng.Next(0, 40);
        float angle = (float)rng.NextDouble() * Mathf.PI;
        float cx = (float)rng.NextDouble() * w, cy = (float)rng.NextDouble() * h;
        float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);

        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = Mathf.Repeat(x, period) - period * 0.5f;
                float v = Mathf.Repeat(y, period) - period * 0.5f;
                bool on;
                switch (pattern)
                {
                    case 0: on = Mathf.Repeat((x * dx + y * dy) / period, 1f) < 0.5f; break;           // stripes
                    case 1: on = ((int)(x / period) + (int)(y / period)) % 2 == 0; break;              // checker
                    case 2: on = Mathf.Repeat(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / period, 1f) < 0.5f; break; // rings
                    case 3: on = u * u + v * v < period * period * 0.12f; break;                       // dots
                    default: on = Mathf.Repeat((x + y * 0.5f) / period + Mathf.Sin(y / period), 1f) < 0.5f; break; // waves
                }
                float shade = 0.75f + 0.25f * (x + y) / (float)(w + h);
                Color32 c = on ? a : b;
                px[y * w + x] = new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255);
            }
        }

        // Texture row 0 is the bottom. Category colour bar along the top, index bits along the bottom.
        Color32 bar = Color.HSVToRGB(category * 0.1f, 1f, 1f);
        Fill(px, w, 0, h - 14, w, 14, bar);
        for (int bit = 0; bit < 10; bit++)
            Fill(px, w, 6 + bit * 18, 6, 14, 14, ((g >> bit) & 1) == 1 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255));

        string label = $"{categoryCodes[category]}-E{item:000}-I{image}";
        int scale = Mathf.Max(3, (w - 24) / (label.Length * 4 + 4));
        Fill(px, w, 6, h - 30 - scale * 7, label.Length * 4 * scale + scale * 2, scale * 7, new Color32(0, 0, 0, 255));
        DrawText(px, w, 6 + scale, h - 30 - scale, scale, label, new Color32(255, 255, 255, 255));

        Texture2D texture = new Texture2D(w, h, TextureFormat.RGB24, false);
        try
        {
            texture.SetPixels32(px);
            texture.Apply(false);
            return texture.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void Fill(Color32[] px, int w, int x0, int y0, int fw, int fh, Color32 c)
    {
        int h = px.Length / w;
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y0 + fh); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + fw); x++)
                px[y * w + x] = c;
    }

    // 3x5 glyphs, top row first.
    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" }, ['1'] = new[] { ".#.", "##.", ".#.", ".#.", "###" },
        ['2'] = new[] { "###", "..#", "###", "#..", "###" }, ['3'] = new[] { "###", "..#", "###", "..#", "###" },
        ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" }, ['5'] = new[] { "###", "#..", "###", "..#", "###" },
        ['6'] = new[] { "###", "#..", "###", "#.#", "###" }, ['7'] = new[] { "###", "..#", "..#", "..#", "..#" },
        ['8'] = new[] { "###", "#.#", "###", "#.#", "###" }, ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
        ['C'] = new[] { "###", "#..", "#..", "#..", "###" }, ['E'] = new[] { "###", "#..", "##.", "#..", "###" },
        ['I'] = new[] { "###", ".#.", ".#.", ".#.", "###" }, ['-'] = new[] { "...", "...", "###", "...", "..." },
    };

    /// <summary>Draws <paramref name="text"/> with its top-left at (x, top) in texture space (y up).</summary>
    private static void DrawText(Color32[] px, int w, int x, int top, int scale, string text, Color32 c)
    {
        foreach (char ch in text)
        {
            if (Glyphs.TryGetValue(ch, out string[] rows))
            {
                for (int r = 0; r < 5; r++)
                    for (int col = 0; col < 3; col++)
                        if (rows[r][col] == '#')
                            Fill(px, w, x + col * scale, top - (r + 1) * scale, scale, scale, c);
            }
            x += 4 * scale;
        }
    }

    // ---------------------------------------------------------------- validation

    /// <summary>
    /// Checks the dataset end to end and throws listing every failure. Reads files only; writes nothing.
    /// </summary>
    public static string Validate()
    {
        List<string> fail = new List<string>();
        if (!File.Exists(ContentPath))
            throw new Exception($"TimelineScalingDataset validate FAILED: {ContentPath} does not exist. Run Generate first.");

        JObject content = JObject.Parse(File.ReadAllText(ContentPath, Encoding.UTF8));
        JToken data = content["data"];

        // The app loads config/appData.json; the fixture beside the images must be the same content.
        if (!File.Exists(ConfigContentPath) || !File.ReadAllBytes(ConfigContentPath).SequenceEqual(File.ReadAllBytes(ContentPath)))
            fail.Add($"{ConfigContentPath} is not byte-identical to {ContentFile}; run Generate");
        if ((data?["languages"] as JArray)?.Count != 2)
            fail.Add("expected the two languages pt and en");
        if ((data?["categories"] as JArray)?.Count != CategoryCount)
            fail.Add($"expected {CategoryCount} categories");

        HashSet<int> categoryIds = new HashSet<int>((data?["categories"] as JArray ?? new JArray()).Select(c => c.Value<int>("id")));
        JArray timeline = data?["timeline"] as JArray ?? new JArray();
        if (timeline.Count != ItemCount)
            fail.Add($"timeline has {timeline.Count} items, expected {ItemCount}");

        HashSet<int> ids = new HashSet<int>();
        Dictionary<int, int> perCategory = new Dictionary<int, int>();
        List<string> urls = new List<string>();
        int minYear = int.MaxValue, maxYear = int.MinValue, minText = int.MaxValue, maxText = 0;

        foreach (JToken item in timeline)
        {
            int id = item.Value<int>("id"), cat = item.Value<int>("categoryId"), year = item.Value<int>("year");
            if (!ids.Add(id)) fail.Add($"duplicate event id {id}");
            if (!categoryIds.Contains(cat)) fail.Add($"event {id}: categoryId {cat} is not a category");
            perCategory[cat] = perCategory.TryGetValue(cat, out int n) ? n + 1 : 1;
            if (year < FirstYear || year > LastYear) fail.Add($"event {id}: year {year} outside {FirstYear}..{LastYear}");
            minYear = Math.Min(minYear, year);
            maxYear = Math.Max(maxYear, year);

            foreach (string lang in new[] { "pt", "en" })
            {
                string text = item["content"]?.Value<string>(lang);
                if (string.IsNullOrEmpty(text)) { fail.Add($"event {id}: empty {lang} text"); continue; }
                minText = Math.Min(minText, text.Length);
                maxText = Math.Max(maxText, text.Length);
            }

            JArray images = item["images"] as JArray ?? new JArray();
            if (images.Count != ImagesPerItem) fail.Add($"event {id}: {images.Count} images, expected {ImagesPerItem}");
            urls.AddRange(images.Select(img => img.Value<string>("url")));
        }

        foreach (int cat in categoryIds)
            if (!perCategory.TryGetValue(cat, out int n) || n != ItemsPerCategory)
                fail.Add($"category {cat} has {(perCategory.TryGetValue(cat, out n) ? n : 0)} events, expected {ItemsPerCategory}");
        if (minYear != FirstYear || maxYear != LastYear) fail.Add($"years span {minYear}..{maxYear}, expected {FirstYear}..{LastYear}");
        if (minText >= 50 || maxText <= 150) fail.Add($"text lengths {minText}..{maxText} do not mix short (<50) and long (>150)");

        int distinctUrls = urls.Distinct(StringComparer.Ordinal).Count();
        if (urls.Count != ImageCount || distinctUrls != ImageCount)
            fail.Add($"{urls.Count} urls, {distinctUrls} distinct, expected {ImageCount} of each");

        HashSet<string> hashes = new HashSet<string>();
        int landscape = 0, portrait = 0, square = 0, maxDim = 0, missing = 0;
        long bytes = 0;
        using (SHA256 sha = SHA256.Create())
        {
            foreach (string url in urls.Distinct(StringComparer.Ordinal))
            {
                string path = Path.Combine(Application.streamingAssetsPath, "assets", url);
                if (url == null || !url.StartsWith(Folder + "/", StringComparison.Ordinal) || !File.Exists(path))
                {
                    if (++missing <= 5) fail.Add($"missing or outside {Folder}/: {url}");
                    continue;
                }

                byte[] file = File.ReadAllBytes(path);
                bytes += file.Length;
                hashes.Add(Convert.ToBase64String(sha.ComputeHash(file)));
                if (!TryPngSize(file, out int w, out int h)) { fail.Add($"not a PNG: {url}"); continue; }
                maxDim = Math.Max(maxDim, Math.Max(w, h));
                if (w > MaxDimension || h > MaxDimension) fail.Add($"{url} is {w}x{h}, over {MaxDimension}");
                if (w > h) landscape++; else if (h > w) portrait++; else square++;
            }
        }
        if (missing > 5) fail.Add($"... {missing} missing files in total");
        if (hashes.Count != ImageCount) fail.Add($"{hashes.Count} distinct file contents, expected {ImageCount}");
        if (landscape == 0 || portrait == 0 || square == 0) fail.Add($"shapes L{landscape}/P{portrait}/S{square}: one is missing");

        // Only this tool writes here, so anything else is reported rather than removed.
        HashSet<string> expected = new HashSet<string>(urls.Select(u => u.Substring(Folder.Length + 1)), StringComparer.OrdinalIgnoreCase) { ContentFile };
        string[] stray = Directory.GetFiles(FolderPath)
            .Select(f => Path.GetFileName(f))
            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !expected.Contains(f))
            .ToArray();
        if (stray.Length > 0) fail.Add($"{stray.Length} unreferenced file(s) in {Folder}/: {string.Join(", ", stray.Take(5))}");

        string summary = $"items={timeline.Count} categories={categoryIds.Count} perCategory={ItemsPerCategory} urls={urls.Count} "
            + $"distinctUrls={distinctUrls} distinctBytes={hashes.Count} landscape={landscape} portrait={portrait} "
            + $"square={square} maxDim={maxDim} pngBytes={bytes} years={minYear}..{maxYear} textLen={minText}..{maxText}";
        if (fail.Count > 0)
            throw new Exception("TimelineScalingDataset validate FAILED (" + fail.Count + "): " + string.Join("; ", fail) + " | " + summary);
        return "TimelineScalingDataset validate PASS: " + summary;
    }

    /// <summary>Width and height from the IHDR chunk, without decoding.</summary>
    private static bool TryPngSize(byte[] file, out int w, out int h)
    {
        w = h = 0;
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (file.Length < 24 || !file.Take(8).SequenceEqual(signature) || Encoding.ASCII.GetString(file, 12, 4) != "IHDR")
            return false;
        w = (file[16] << 24) | (file[17] << 16) | (file[18] << 8) | file[19];
        h = (file[20] << 24) | (file[21] << 16) | (file[22] << 8) | file[23];
        return w > 0 && h > 0;
    }
}
